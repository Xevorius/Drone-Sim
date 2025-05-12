using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class CommandShipController : MonoBehaviour
{
    public enum State { Roaming, Observing }

    [Header("Wander Settings")]
    public float wanderRadius   = 50f;
    public float wanderInterval = 5f;

    private NavMeshAgent _agent;
    private Coroutine    _wanderCoroutine;

    public State CurrentState { get; private set; } = State.Roaming;
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");

    [Header("Drone Spawning")]
    [Tooltip("Assign your Drone prefab here (with DroneController on it)")]
    public DroneController dronePrefab;

    void Awake() => _agent = GetComponent<NavMeshAgent>();

    void Start()
    {
      // begin roaming
      _wanderCoroutine = StartCoroutine(WanderRoutine());
    }

    public void SpawnDrone()
    {
        // pick a point 2 units in front of the ship
        Vector3 spawnPos = transform.position + transform.forward * 2f;
        // instantiate the prefab
        var go = Instantiate(dronePrefab.gameObject, spawnPos, Quaternion.identity);
        var dr = go.GetComponent<DroneController>();
        // link it back to this ship
        dr.ParentShip = this;
    }


    private IEnumerator WanderRoutine()
    {
      var wait = new WaitForSeconds(wanderInterval);
      while (true)
      {
        if (CurrentState == State.Roaming)
        {
          Vector3 randomPoint = transform.position + UnityEngine.Random.insideUnitSphere * wanderRadius;
          if (NavMesh.SamplePosition(randomPoint, out var hit, wanderRadius, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
        }
        yield return wait;
      }
    }

    public void StartObserving()
    {
        CurrentState = State.Observing;
        _agent.isStopped = true;
    }

    public void StartRoaming()
    {
      CurrentState = State.Roaming;
      _agent.isStopped = false;
    }

    public void ObserveAt(Vector3 worldPos)
    {
      CurrentState = State.Observing;
      _agent.isStopped = false;
      _agent.SetDestination(worldPos);
    }
}
