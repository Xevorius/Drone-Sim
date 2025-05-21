using System;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using WebSocketSharp.Server;
using WebSocketSharp;
using UnityEngine;


[Serializable]
public class Position { public float x, y; }

[Serializable]
public class Entity {
    public string id;
    public string type;
    public Position position;
}

[Serializable]
public class SquadState {
    public string   id;
    public string   formation;
    public Position position;   // center of the squadron
    public string[] droneIds;
}

[Serializable]
public class SimState {
    public float          minX, maxX, minY, maxY;
    public List<Entity>   entities;
    public List<SquadState> squads;
}

public class ApiServer : MonoBehaviour
{
    [Serializable]
    private class DestDto { public float x, y; }
    private HttpListener  _listener;
    private Thread        _listenerThread;
    private readonly int  _port    = 4000;
    private readonly string _prefix;
    private readonly string _simId;

    [Serializable] private class SquadDto     { public string parentShipId; public string[] droneIds; public string formation; }
    [Serializable] private class FormDto      { public string formation; }
    [Serializable] private class IdDto        { public string droneId; }
    [Serializable] private class ReorderDto { public string[] droneIds; }

    [Header("Map Capture")]
    public Camera mapCamera;        // assign in Inspector
    public GameObject mapPlane;
    public int    mapSize = 1024;   // resolution of the square snapshot

    private byte[]  _mapImagePng;   // holds the encoded PNG bytes
    private bool    _mapReady = false;
    private const string RegistryUrl = "http://localhost:3000/api/sims";

    public SimState GetSimState() => _simState;

    private readonly SimState _simState = new SimState {
        entities = new List<Entity>(),
        squads   = new List<SquadState>()    // ← initialize here
    };
    // a queue to hold work that must run on Unity’s main thread
    private readonly ConcurrentQueue<Action> _mainThreadActions = 
        new ConcurrentQueue<Action>();

    // helper to enqueue an action
    private void EnqueueOnMainThread(Action a) {
        _mainThreadActions.Enqueue(a);
    }
    
    void Update() {
        while (_mainThreadActions.TryDequeue(out var action)) {
            action();
        }
    }

    async void OnApplicationQuit()
    {
        await DeregisterFromRegistry();
        _listener.Stop();
        _listenerThread.Abort();
    }

    private async System.Threading.Tasks.Task DeregisterFromRegistry()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // Build a DELETE request with JSON body { "id": "<simId>" }
            var req = new HttpRequestMessage(HttpMethod.Delete, RegistryUrl) {
                Content = new StringContent(
                    $"{{\"id\":\"{_simId}\"}}",
                    Encoding.UTF8, "application/json"
                )
            };
            var res = await client.SendAsync(req);
            Debug.Log($"[Unity] Deregister response: {res.StatusCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Unity] Deregistration failed: {e}");
        }
    }

    public ApiServer()
    {
        // 1) generate your unique sim ID
        _simId = Guid.NewGuid().ToString("N");

        // 2) prefix to listen on
        _prefix = $"http://+:{_port}/simulations/";
    }

    async void Start()
    {
        // A) spin up the HTTP listener
        _listener = new HttpListener();
        _listener.Prefixes.Add(_prefix);
        _listener.Start();

        _listenerThread = new Thread(HandleRequests) {
            IsBackground = true
        };
        _listenerThread.Start();

        Debug.Log($"[Unity] API Server running at {_prefix}");
        
        // B) register with Next.js registry
        await RegisterWithRegistry();
        StartCoroutine(CaptureMapAndBounds());
    }

    public void PopulateEntities()
    {
        _simState.entities.Clear();
        _simState.squads.Clear();

    // Command ships
        foreach (var cs in FindObjectsOfType<CommandShipController>()) _simState.entities.Add(new Entity {
            id       = cs.Id,
            type     = "command",
            position = new Position {
                x = cs.transform.position.x,
                y = cs.transform.position.z
            }
        });

        var squadDroneIds = new HashSet<string>( 
            FindObjectsOfType<SquadronController>()
            .SelectMany(sq => sq.Drones)
            .Select(dr => dr.Id)
        );


        // Drones
        foreach (var dr in FindObjectsOfType<DroneController>())
        {
            if (squadDroneIds.Contains(dr.Id)) continue;
            _simState.entities.Add(new Entity {
                id   = dr.Id,
                type = "drone",
                position = new Position {
                    x = dr.transform.position.x,
                    y = dr.transform.position.z
                }
            });
        }
        foreach (var sq in FindObjectsOfType<SquadronController>())
        {
            var wp = sq.transform.position;
            _simState.squads.Add(new SquadState {
                id        = sq.Id,
                formation = sq.CurrentFormation.ToString(),
                position  = new Position { x = wp.x, y = wp.z },
                droneIds  = sq.Drones.Select(d => d.Id).ToArray()
            });
        }
    }

    private IEnumerator CaptureMapAndBounds()
    {
        // wait until end of frame so the scene is rendered
        yield return new WaitForEndOfFrame();

        // 1) RenderTexture setup
        int pxH = mapSize;  
        int pxW = Mathf.RoundToInt(mapSize * mapCamera.aspect);
        var rt = new RenderTexture(pxW, pxH, 24);
        mapCamera.targetTexture = rt;
        mapCamera.Render();

        // 2) Read pixels into Texture2D
        RenderTexture.active = rt;
        var tex = new Texture2D(pxW, pxH, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, pxW, pxH), 0, 0);
        tex.Apply();

        // 3) Encode to PNG
        _mapImagePng = tex.EncodeToPNG();
        _mapReady    = true;

        // cleanup
        mapCamera.targetTexture = null;
        RenderTexture.active    = null;
        Destroy(rt);
        Destroy(tex);

        // 4) Compute world‐space bounds from the camera
        //    (assumes an orthographic camera looking straight down on XY)
        float halfHeight = mapCamera.orthographicSize;
        float halfWidth  = halfHeight * mapCamera.aspect;
        Vector3 cpos     = mapCamera.transform.position;

        var renderer = mapPlane.GetComponent<Renderer>();
        var b = renderer.bounds; 
        _simState.minX = b.min.x;
        _simState.maxX = b.max.x;
        _simState.minY = b.min.z;  // note: z → “map Y”
        _simState.maxY = b.max.z;

        Debug.Log($"[Unity] Map captured; bounds: X[{_simState.minX},{_simState.maxX}] Y[{_simState.minY},{_simState.maxY}]");
    }

    private void HandleRequests()
    {
        while (_listener.IsListening)
        {
            try
            {
                var ctx  = _listener.GetContext();
                var req  = ctx.Request;
                var resp = ctx.Response;

                if (req.HttpMethod == "OPTIONS")
                {
                    resp.AddHeader("Access-Control-Allow-Origin", "*");
                    resp.AddHeader("Access-Control-Allow-Methods", "GET,POST,PATCH,OPTIONS");
                    resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                    resp.StatusCode = 204;
                    resp.OutputStream.Close();
                    continue;
                }

                // PATCH /simulations/squadrons/{id}/reorder
                if (req.HttpMethod == "PATCH"
                && req.Url.AbsolutePath.Contains("/simulations/squadrons/")
                && req.Url.AbsolutePath.EndsWith("/reorder"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin", "*");

                    // 1) Parse squadron ID and incoming new order
                    var parts = req.Url.AbsolutePath.Split('/');
                    var sqId  = parts[3];
                    var dto   = JsonUtility.FromJson<ReorderDto>(
                                new System.IO.StreamReader(req.InputStream).ReadToEnd()
                            );

                    // 2) Enqueue on main thread
                    EnqueueOnMainThread(() =>
                    {
                        var sq = FindObjectsOfType<SquadronController>()
                                .First(s => s.Id == sqId);
                        var allDrones = FindObjectsOfType<DroneController>();

                        // 3) Rebuild the Drones list in the new order
                        //    safely ignore any IDs that don’t match
                        sq.Drones = dto.droneIds
                            .Select(id => allDrones.FirstOrDefault(d => d.Id == id))
                            .Where(d => d != null)
                            .ToList();

                        // 4) Immediately re‐issue the formation move
                        sq.RefreshFormation();
                    });

                    resp.StatusCode = 200;
                    resp.OutputStream.Close();
                    continue;
                }


                if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/simulations/squadrons")
                {
                    // CORS
                    resp.AddHeader("Access-Control-Allow-Origin", "*");
                    resp.AddHeader("Access-Control-Allow-Methods", "GET,POST,PATCH,OPTIONS");
                    resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                    // Body: { "parentShipId": "...", "droneIds": ["..."], "formation":"Vee" }
                    var body = new System.IO.StreamReader(req.InputStream).ReadToEnd();
                    var dto  = JsonUtility.FromJson<SquadDto>(body);

                    string sqId = Guid.NewGuid().ToString("N");
                    EnqueueOnMainThread(() => {
                        // create the Squadron GameObject
                        var go = new GameObject($"Squadron-{sqId}");
                        var sq = go.AddComponent<SquadronController>();
                        sq.InitializeId(sqId); 
                        sq.ParentShip = FindObjectsOfType<CommandShipController>()
                                        .First(c => c.Id == dto.parentShipId);
                        sq.SetFormation(Enum.Parse<SquadronController.Formation>(dto.formation));                        // add each drone
                        foreach (var did in dto.droneIds)
                        {
                            var dr = FindObjectsOfType<DroneController>()
                                    .First(d => d.Id == did);
                            if (dr != null) sq.AddDrone(dr);
                        }
                        sq.SetDestination(sq.ParentShip.transform.position);
                    });

                    resp.StatusCode = 201;
                    var outJson = $"{{\"id\":\"{sqId}\"}}";
                    var data    = Encoding.UTF8.GetBytes(outJson);
                    resp.ContentType     = "application/json";
                    resp.ContentLength64 = data.Length;
                    resp.OutputStream.Write(data, 0, data.Length);
                    resp.OutputStream.Close();
                    continue;
                }

                // 2) PATCH /simulations/squadrons/{id}/formation
                if (req.HttpMethod == "PATCH" && req.Url.AbsolutePath.Contains("/simulations/squadrons/") 
                    && req.Url.AbsolutePath.EndsWith("/formation"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    var parts = req.Url.AbsolutePath.Split('/');
                    // ["","simulations","squadrons","{id}","formation"]
                    var sqId = parts[3];
                    var newForm = JsonUtility.FromJson<FormDto>(
                                    new System.IO.StreamReader(req.InputStream).ReadToEnd()
                                ).formation;

                    EnqueueOnMainThread(() => {
                        var sq = FindObjectsOfType<SquadronController>()
                                .First(s => s.Id == sqId);
                        sq.SetFormation(Enum.Parse<SquadronController.Formation>(newForm));
                    });

                    resp.StatusCode = 200;
                    resp.OutputStream.Close();
                    continue;
                }

                // 3) PATCH /simulations/squadrons/{id}/destination
                if (req.HttpMethod == "PATCH" && req.Url.AbsolutePath.EndsWith("/destination"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    var parts = req.Url.AbsolutePath.Split('/');
                    var sqId = parts[3];
                    var dto  = JsonUtility.FromJson<DestDto>(
                                new System.IO.StreamReader(req.InputStream).ReadToEnd()
                            );
                    EnqueueOnMainThread(() => {
                        var sq = FindObjectsOfType<SquadronController>()
                                .First(s => s.Id == sqId);
                        sq.SetDestination(new Vector3(dto.x, 0, dto.y));
                    });
                    resp.StatusCode = 200;
                    resp.OutputStream.Close();
                    continue;
                }

                // 4) POST /simulations/squadrons/{id}/addDrone  { "droneId":"..." }
                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.EndsWith("/addDrone"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    var parts = req.Url.AbsolutePath.Split('/');
                    var sqId = parts[3];
                    var did  = JsonUtility.FromJson<IdDto>(
                                new System.IO.StreamReader(req.InputStream).ReadToEnd()
                            ).droneId;
                    EnqueueOnMainThread(() => {
                        var sq = FindObjectsOfType<SquadronController>()
                                .First(s => s.Id == sqId);
                        var dr = FindObjectsOfType<DroneController>()
                                .First(d => d.Id == did);
                        if (dr != null) sq.AddDrone(dr);
                    });
                    resp.StatusCode = 200;
                    resp.OutputStream.Close();
                    continue;
                }

                // 5) POST /simulations/squadrons/{id}/removeDrone
                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.EndsWith("/removeDrone"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    var parts = req.Url.AbsolutePath.Split('/');
                    var sqId = parts[3];
                    var did  = JsonUtility.FromJson<IdDto>(
                                new System.IO.StreamReader(req.InputStream).ReadToEnd()
                            ).droneId;
                    EnqueueOnMainThread(() => {
                        var sq = FindObjectsOfType<SquadronController>()
                                .First(s => s.Id == sqId);
                        sq.RemoveDrone(did);
                    });
                    resp.StatusCode = 200;
                    resp.OutputStream.Close();
                    continue;
                }

                if (req.HttpMethod == "GET" && req.Url.AbsolutePath.StartsWith("/simulations/drones/") && req.Url.AbsolutePath.EndsWith("/snapshot"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin", "*");

                    var parts = req.Url.AbsolutePath.Split('/');
                    // ["","simulations","drones","{id}","snapshot"]
                    if (parts.Length == 5 && parts[2] == "drones" && parts[4] == "snapshot")
                    {
                        string droneId = parts[3];
                        byte[] png = null;
                        var mre = new System.Threading.ManualResetEventSlim();

                        // enqueue the capture on the main thread
                        EnqueueOnMainThread(() => {
                            var dr = FindObjectsOfType<DroneController>()
                                        .FirstOrDefault(d => d.Id == droneId);
                            if (dr != null)
                                png = dr.CaptureSnapshot();
                            mre.Set();
                        });

                        // wait (timeout after 2s)
                        if (!mre.Wait(2000))
                        {
                            resp.StatusCode = 504; // timeout
                        }
                        else if (png == null)
                        {
                            resp.StatusCode = 404; // drone not found
                        }
                        else
                        {
                            resp.ContentType     = "image/png";
                            resp.ContentLength64 = png.Length;
                            resp.OutputStream.Write(png, 0, png.Length);
                            resp.StatusCode = 200;
                        }
                    }
                    else
                    {
                        resp.StatusCode = 400;
                    }

                    resp.OutputStream.Close();
                    continue;
                }

                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.StartsWith("/simulations/commandShip/") && req.Url.AbsolutePath.EndsWith("/spawnDrone"))
                {
                    // CORS
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    resp.AddHeader("Access-Control-Allow-Methods","GET,POST,OPTIONS");
                    resp.AddHeader("Access-Control-Allow-Headers","Content-Type");
                    Debug.Log("Atleast reached this");
                    // parse path: ["","simulations","commandShip","<id>","spawnDrone"]
                    var parts = req.Url.AbsolutePath.Split('/');
                    if (parts.Length == 5 && parts[2] == "commandShip" && parts[4] == "spawnDrone")
                    {
                        string shipId = parts[3];
                        // enqueue the spawn on the main thread
                        Debug.Log("Spawn Drone call received");
                        EnqueueOnMainThread(() => {
                            var cs = FindObjectsOfType<CommandShipController>()
                                        .FirstOrDefault(c => c.Id == shipId);
                            cs?.SpawnDrone();
                        });
                        resp.StatusCode = 200;
                    }
                    else
                    {
                        Debug.LogWarning($"[Unity] spawnDrone malformed path: {req.Url.AbsolutePath}");
                        resp.StatusCode = 400; // bad path
                    }

                    resp.OutputStream.Close();
                    continue;
                }

                // only support GET /simulations/
                if (req.HttpMethod == "GET" &&
                    req.Url.AbsolutePath == "/simulations/")
                {
                    // CORS
                    resp.AddHeader("Access-Control-Allow-Origin", "*");

                    // always return a one-element array
                    string json = $"[ {{ \"id\": \"{_simId}\" }} ]";
                    var data = Encoding.UTF8.GetBytes(json);

                    resp.ContentType       = "application/json";
                    resp.ContentLength64   = data.Length;
                    resp.OutputStream.Write(data, 0, data.Length);
                }
                // serve the map image
                if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/simulations/map")
                {
                    if (! _mapReady)
                    {
                        resp.StatusCode = 503; // not yet ready
                    }
                    else
                    {
                        resp.AddHeader("Access-Control-Allow-Origin", "*");
                        resp.ContentType     = "image/png";
                        resp.ContentLength64 = _mapImagePng.Length;
                        Debug.Log(_mapImagePng.Length);
                        resp.OutputStream.Write(_mapImagePng, 0, _mapImagePng.Length);
                    }
                    resp.OutputStream.Close();
                    continue;
                }
                // inside HandleRequests(), before the final `else`
                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.StartsWith("/simulations/commandShip/"))
                {
                    // CORS…
                    resp.AddHeader("Access-Control-Allow-Origin", "*");
                    resp.AddHeader("Access-Control-Allow-Methods", "GET,POST,PATCH,OPTIONS");
                    resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                    var parts = req.Url.AbsolutePath.Split('/');
                    // ["", "simulations","commandShip","{id}","{action}"]
                    if (parts.Length == 5 && parts[1]=="simulations" && parts[2]=="commandShip")
                    {
                        string shipId = parts[3];
                        string action = parts[4];  // now could be "roaming", "observing", or "destination"

                        if (action == "roaming")
                        {
                            EnqueueOnMainThread(() => {
                                var cs = FindObjectsOfType<CommandShipController>()
                                        .FirstOrDefault(c => c.Id == shipId);
                                cs?.StartRoaming();
                            });
                            resp.StatusCode = 200;
                        }
                        else if (action == "observing")
                        {
                            EnqueueOnMainThread(() => {
                                var cs = FindObjectsOfType<CommandShipController>()
                                        .FirstOrDefault(c => c.Id == shipId);
                                if (cs != null)
                                    cs.StartObserving();   // ← uses your new method
                            });
                            resp.StatusCode = 200;
                        }
                        else if (action == "destination")
                        {
                            // read {x,y}…
                            using var sr = new System.IO.StreamReader(req.InputStream);
                            var dto = JsonUtility.FromJson<DestDto>(sr.ReadToEnd());
                            EnqueueOnMainThread(() => {
                                var cs = FindObjectsOfType<CommandShipController>()
                                        .FirstOrDefault(c => c.Id == shipId);
                                cs?.ObserveAt(new Vector3(dto.x, cs.transform.position.y, dto.y));
                            });
                            resp.StatusCode = 200;
                        }
                        else
                        {
                            resp.StatusCode = 400; // unknown action
                        }
                    }
                    else resp.StatusCode = 400; // malformed URL

                    resp.OutputStream.Close();
                    continue;
                }

                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.StartsWith("/simulations/drones/"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    var parts = req.Url.AbsolutePath.Split('/');
                    // ["","simulations","drones","{id}","{action}"]
                    if (parts.Length == 5 && parts[1]=="simulations" && parts[2]=="drones")
                    {
                        string droneId = parts[3];
                        string action  = parts[4]; // "destination" or "return"
                        
                        if (action == "destination")
                        {
                            // parse {x,y} request body
                            using var sr = new System.IO.StreamReader(req.InputStream);
                            var dto = JsonUtility.FromJson<DestDto>(sr.ReadToEnd());
                            
                            EnqueueOnMainThread(() => {
                                var dr = FindObjectsOfType<DroneController>()
                                            .FirstOrDefault(d => d.Id == droneId);
                                if (dr != null)
                                    dr.ObserveAt(new Vector3(dto.x, dr.transform.position.y, dto.y));
                            });
                            resp.StatusCode = 200;
                        }
                        else if (action == "return")
                        {
                            EnqueueOnMainThread(() => {
                                var dr = FindObjectsOfType<DroneController>()
                                            .FirstOrDefault(d => d.Id == droneId);
                                dr?.ReturnToShip();
                            });
                            resp.StatusCode = 200;
                        }
                        else resp.StatusCode = 400;
                    }
                    else resp.StatusCode = 400;

                    resp.OutputStream.Close();
                    continue;
                }
                
                // 6) POST /simulations/squadrons/{id}/return
                if (req.HttpMethod == "POST" && req.Url.AbsolutePath.EndsWith("/return"))
                {
                    resp.AddHeader("Access-Control-Allow-Origin","*");
                    var parts = req.Url.AbsolutePath.Split('/');
                    var sqId = parts[3];
                    EnqueueOnMainThread(() => {
                        var sq = FindObjectsOfType<SquadronController>()
                                .First(s => s.Id == sqId);
                        sq.ReturnToShip();
                    });
                    resp.StatusCode = 200;
                    resp.OutputStream.Close();
                    continue;
                }

                if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/simulations/state")
                {
                    resp.AddHeader("Access-Control-Allow-Origin", "*");
                    string json = JsonUtility.ToJson(_simState);
                    byte[] data = Encoding.UTF8.GetBytes(json);

                    resp.ContentType = "application/json";
                    resp.ContentLength64 = data.Length;
                    resp.OutputStream.Write(data, 0, data.Length);
                    resp.OutputStream.Close();
                    continue;
                }
                resp.OutputStream.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Unity] API error: {e}");
            }
        }
    }

    private async System.Threading.Tasks.Task RegisterWithRegistry()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // tell Next.js “this sim is at localhost:4000”
            string jsonBody = $"{{ " +
                $"\"id\":\"{_simId}\"," +
                $"\"host\":\"localhost\"," +
                $"\"port\":{_port}" +
            $"}}";

            var payload = new StringContent(
                jsonBody,
                Encoding.UTF8,
                "application/json"
            );

            var registryUrl = "http://localhost:3000/api/sims";
            var res = await client.PostAsync(registryUrl, payload);
            Debug.Log($"[Unity] Registry response: {res.StatusCode}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Unity] Failed to register: {ex}");
        }
    }
}
