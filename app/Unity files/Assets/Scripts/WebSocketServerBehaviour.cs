using UnityEngine;
using WebSocketSharp.Server;
using System.Net;

public class WebSocketServerBehaviour : MonoBehaviour
{
    [Tooltip("Drop your ApiServer GameObject here")]
    public ApiServer apiServer;

    [Tooltip("Hz frequency for broadcast (e.g. 10 = 10 times/sec)")]
    public float broadcastInterval = 0.1f;

    private WebSocketServer _server;
    private float _timer;

    void Start()
    {
        // 1) Listen on ws://anyIP:4001/state
        _server = new WebSocketServer(IPAddress.Any, 4001);
        _server.AddWebSocketService<StateService>("/state", () => new StateService(apiServer));
        _server.Start();
    }

    void Update()
    {
        // 2) Periodically broadcast the latest state
        _timer += Time.deltaTime;
        if (_timer >= broadcastInterval)
        {
            _timer = 0f;

            apiServer.PopulateEntities();

            string json = JsonUtility.ToJson(apiServer.GetSimState());
            _server.WebSocketServices["/state"].Sessions.Broadcast(json);
        }
    }

    void OnApplicationQuit()
    {
        _server.Stop();
    }
}

// A minimal service that simply exists to accept connections.
public class StateService : WebSocketBehavior
{
    private readonly ApiServer _api;

    public StateService(ApiServer apiServer) { _api = apiServer; }

    protected override void OnOpen()
    {
        Debug.Log("[WebSocket] Client connected, sending initial state");
        // Optionally send initial snapshot
        Sessions.Broadcast(JsonUtility.ToJson(_api.GetSimState()));
    }
}
