using UnityEngine;
using System;
using System.Collections.Generic;

public class SquadronController : MonoBehaviour
{
    public enum Formation { Vee, Line, Circle }

    public string Id { get; private set; } = Guid.NewGuid().ToString("N");

    public List<DroneController> Drones = new List<DroneController>();
    public Formation             CurrentFormation = Formation.Vee;
    public CommandShipController ParentShip;  // assign when you instantiate
    private Vector3 _destination;

    /// <summary>Call once at creation to override the generated ID.</summary>
    public void InitializeId(string id)
    {
        Id = id;
    }

    /// <summary>Adds a drone under this squadron.</summary>
    public void AddDrone(DroneController drone)
    {
        drone.ParentShip = ParentShip;
        Drones.Add(drone);
        RefreshFormation();
    }

    /// <summary>Removes (and destroys) a drone.</summary>
    public void RemoveDrone(string droneId)
    {
        var dr = Drones.Find(d => d.Id == droneId);
        if (dr != null)
        {
            Drones.Remove(dr);
            dr.StartObserving();
            RefreshFormation();
        }
    }

    public void RefreshFormation()
    {
        // just call the same logic as SetDestination,
        // but without changing _destination
        var target = _destination != default
            ? _destination
            : ParentShip.transform.position;

        Vector3[] offsets = ComputeOffsets(Drones.Count, CurrentFormation);
        for (int i = 0; i < Drones.Count; i++)
        {
            Drones[i].ObserveAt(target + offsets[i]);
        }
    }

    /// <summary>Switches the formation shape.</summary>
    public void SetFormation(Formation f)
    {
        CurrentFormation = f;
        RefreshFormation();
    }

    /// <summary>Sends the entire squadron to target, using formation offsets.</summary>
    public void SetDestination(Vector3 worldTarget)
    {
        _destination = worldTarget;
        RefreshFormation();
    }

    /// <summary>Recall all drones, then destroy this squadron.</summary>
    public void ReturnToShip()
    {
        foreach (var dr in Drones)
            dr.ReturnToShip();
        Destroy(gameObject, 5f);
    }

    /// <summary>Compute local offsets for each slot.</summary>
    private static Vector3[] ComputeOffsets(int count, Formation f)
    {
        Vector3[] offs = new Vector3[count];
        float spacing = 3f;

        switch (f)
        {
            case Formation.Vee:
                for (int i = 0; i < count; i++)
                {
                    int rank = (i + 1) / 2;
                    int side = (i % 2 == 0) ? 1 : -1;
                    offs[i] = new Vector3(side * spacing * rank, 0, -spacing * rank);
                }
                break;

            case Formation.Line:
                for (int i = 0; i < count; i++)
                    offs[i] = new Vector3(0, 0, -spacing * i);
                break;

            case Formation.Circle:
                for (int i = 0; i < count; i++)
                {
                    float angle = i * (360f / count) * Mathf.Deg2Rad;
                    offs[i] = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * spacing;
                }
                break;
        }

        return offs;
    }

    void LateUpdate()
    {
        if (Drones == null || Drones.Count == 0) return;

        // compute the centroid of all member drones
        Vector3 sum = Vector3.zero;
        foreach (var dr in Drones)
        {
            sum += dr.transform.position;
        }
        Vector3 center = sum / Drones.Count;

        // move the squadron GameObject there
        transform.position = center;
    }
    void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (Drones == null || Drones.Count == 0) return;

        Vector3[] offs = ComputeOffsets(Drones.Count, CurrentFormation);
        for (int i = 0; i < offs.Length; i++)
        {
            Vector3 worldPos = transform.position + offs[i];
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(worldPos, 0.5f);

            // label each slot with its 1-based index
            UnityEditor.Handles.Label(
              worldPos + Vector3.up * 0.5f,
              (i+1).ToString()
            );
        }
#endif
    }
}
