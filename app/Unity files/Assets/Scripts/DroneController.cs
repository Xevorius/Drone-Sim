using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class DroneController : MonoBehaviour
{
    public enum State { Roaming, Observing, Returning }

    public string Id { get; private set; }

    public CommandShipController ParentShip;

    public Camera snapshotCamera; 
    public int    snapshotSize = 1080;

    private NavMeshAgent _agent;
    private Texture2D    _snapshotTex;
    public State CurrentState { get; private set; } = State.Observing;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        // assign a brand‐new GUID on every instantiation
        Id = Guid.NewGuid().ToString("N");
       // prepare the RenderTexture & Texture2D once
        var rt = new RenderTexture(snapshotSize, snapshotSize, 16);
        snapshotCamera.targetTexture = rt;
        _snapshotTex = new Texture2D(snapshotSize, snapshotSize, TextureFormat.RGB24, false);
    }

    public byte[] CaptureSnapshot()
    {
        // render the camera
        snapshotCamera.Render();

        // read into our texture
        RenderTexture.active = snapshotCamera.targetTexture;
        _snapshotTex.ReadPixels(new Rect(0, 0, snapshotSize, snapshotSize), 0, 0);
        _snapshotTex.Apply();
        RenderTexture.active = null;

        // encode & return
        return _snapshotTex.EncodeToPNG();
    }

    /// <summary>Go to world position.</summary>
    public void ObserveAt(Vector3 worldPos)
    {
        CurrentState = State.Observing;
        _agent.isStopped = false;
        _agent.SetDestination(worldPos);
    }

    /// <summary>Return to parent ship then destroy.</summary>
    public void ReturnToShip()
    {
        if (ParentShip == null) return;
        CurrentState = State.Returning;
        _agent.isStopped = false;

        // kick off a coroutine that chases the ship until close
        StartCoroutine(ReturnAndDestroy());
    }

    private IEnumerator ReturnAndDestroy()
    {
        const float arriveThreshold = 3f;
        while (true)
        {
            // update destination to the ship’s *current* position
            _agent.SetDestination(ParentShip.transform.position);

            // check arrival
            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(ParentShip.transform.position.x, ParentShip.transform.position.z)) <= arriveThreshold)
                break;

            // wait a short time before updating again
            yield return new WaitForSeconds(0.1f);
        }

        // finally destroy
        Destroy(gameObject);
    }
    
    public void StartObserving()
    {
        CurrentState   = State.Observing;
        _agent.isStopped = true;
    }
}
