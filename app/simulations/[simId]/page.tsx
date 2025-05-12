"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import dynamic from "next/dynamic";
import type { Sim, SimState, Entity, SquadState, FormationType } from "@/types";
import SpawnSquadronModal from "@/components/SpawnSquadronModal";

const MiniMap = dynamic(() => import("@/components/MiniMap"), { ssr: false });


export default function SimDetail() {
  const { simId } = useParams();
  const router    = useRouter();
  const [awaitDest, setAwaitDest] = useState(false);
  const [awaitDroneDest, setAwaitDroneDest] = useState(false);
  const [awaitSquadDest,setAwaitSquadDest] = useState(false);
  const [droneSnapshot, setDroneSnapshot] = useState<string|null>(null);
  const [shipState, setShipState] = useState<"Roaming"|"Observing">("Roaming"); 
  const [sim,  setSim]  = useState<Sim|null>(null);
  const [state, setState] = useState<SimState|null>(null);
  const [selEntityId, setSelEntityId] = useState<string|null>(null);
  const [selSquadId,  setSelSquadId]  = useState<string|null>(null);
  const [showSpawnModal, setShowSpawnModal]     = useState(false);

    // 1) load the registry
    useEffect(() => {
      fetch("/api/sims")
        .then(r => r.json())
        .then((all:Sim[]) => setSim(all.find(s=>s.id===simId)||null));
    }, [simId]);
  
  
    // 2) open WS once we know host/port
    useEffect(() => {
      if (!sim) return;
      const host = sim.host.includes(":") ? `[${sim.host}]` : sim.host;
      const ws   = new WebSocket(`ws://${host}:4001/state`);
      ws.onmessage = e => setState(JSON.parse(e.data));
      ws.onclose   = () => router.push("/simulations");
      return () => ws.close();
    }, [sim, router]);

    if (!sim || !state) return <div>Loading…</div>;

    // don’t render the map until we have both sim & state
    const selectedEntity = selEntityId
    ? state.entities.find(e => e.id === selEntityId)! : null;
    const selectedSquad  = selSquadId
    ? state?.squads.find(s=>s.id===selSquadId)    : null;
    const looseDrones = state?.entities
    .filter(e => e.type === "drone")
    .filter(e => !state?.squads.some(sq => sq.droneIds.includes(e.id)));
  
    function reorder(idx: number, dir: -1|1) {
      if (!selectedSquad) return;
      const a = [...selectedSquad.droneIds];
      const j = idx + dir;
      [a[idx], a[j]] = [a[j], a[idx]];
      // PATCH the new order
      fetch(
        `http://${sim?.host}:${sim?.port}/simulations/squadrons/${selectedSquad.id}/reorder`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ droneIds: a }),
        }
      );
    }

    return (
      <div className="p-6">
        <h1 className="text-2xl font-bold mb-4">Simulation {sim.id}</h1>
        <MiniMap
        sim={sim}
        state={state}
        onEntityClick={(e)=>{ setSelEntityId(e.id); setSelSquadId(null); setAwaitDest(false); }}
        onSquadClick={(s)=> { setSelSquadId(s.id); setSelEntityId(null); setAwaitSquadDest(false); }}
        onMapClick={(x,y)=>{
          if (awaitDest    && selectedEntity?.type==="command") {
            fetch(`http://${sim.host}:${sim.port}/simulations/commandShip/${selectedEntity.id}/destination`,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({x,y})})
            .then(()=>setAwaitDest(false));
          } else if (awaitDroneDest && selectedEntity?.type==="drone") {
            fetch(`http://${sim.host}:${sim.port}/simulations/drones/${selectedEntity.id}/destination`,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({x,y})})
            .then(()=>setAwaitDroneDest(false));
          } else if (awaitSquadDest && selectedSquad) {
            fetch(`http://${sim.host}:${sim.port}/simulations/squadrons/${selectedSquad.id}/destination`,{
              method:"PATCH",
              headers:{"Content-Type":"application/json"},
              body:JSON.stringify({x,y})
            }).then(()=>setAwaitSquadDest(false));
          }
        }}
      />
      {/* --- Entity Details --- */}
      {selectedEntity && (
        <div className="mt-4 p-4 border rounded bg-black">
          <h2 className="font-semibold">Entity Details</h2>
          <p>Type: {selectedEntity.type}</p>
          <p>ID:   {selectedEntity.id}</p>
          <p>Pos:  ({selectedEntity.position.x.toFixed(1)}, {selectedEntity.position.y.toFixed(1)})</p>
          {/* …drone buttons as before… */}
        </div>
      )}
          {selectedEntity?.type === "drone" && (
            <div className="mt-4 space-x-2">
              {/* Set Destination */}
              {!awaitDroneDest ? (
                <button
                  className="px-4 py-2 bg-green-600 text-white rounded"
                  onClick={() => setAwaitDroneDest(true)}
                >
                  Set Destination
                </button>
              ) : (
                <span className="text-yellow-700">
                  Click on the map to choose drone destination…
                </span>
              )}

              {/* Return to Ship */}
              <button
                className="px-4 py-2 bg-red-600 text-white rounded"
                onClick={() => {
                  fetch(`http://${sim.host}:${sim.port}/simulations/drones/${selectedEntity.id}/return`, {
                    method: "POST"
                  }).catch(console.error);
                }}
              >
                Return to Ship
              </button>
              {/* Snapshot */}
              <button
                className="px-4 py-2 bg-blue-500 text-white rounded"
                onClick={() => {
                  fetch(
                    `http://${sim.host}:${sim.port}/simulations/drones/${selectedEntity.id}/snapshot`
                  )
                  .then(r => r.blob())
                  .then(blob => {
                    const url = URL.createObjectURL(blob);
                    setDroneSnapshot(url);
                  })
                  .catch(console.error);
                }}
              >
                Camera Snapshot
              </button>
            </div>
          )}
          {/* --- Squadron Details --- */}
          {selectedSquad && (
            <div className="mt-4 p-4 border rounded bg-black">
              <h2 className="font-semibold">Squadron {selectedSquad.id}</h2>

              {/* Formation selector */}
              <label>
                Formation:{" "}
                <select
                  value={selectedSquad.formation}
                  onChange={(e)=>{
                    const f = e.target.value as FormationType;
                    fetch(
                      `http://${sim.host}:${sim.port}/simulations/squadrons/${selectedSquad.id}/formation`,
                      { method:"PATCH", headers:{"Content-Type":"application/json"}, body:JSON.stringify({formation:f}) }
                    );
                  }}
                >
                  <option>Vee</option>
                  <option>Line</option>
                  <option>Circle</option>
                </select>
              </label>

              {/* Set Destination */}
              {!awaitSquadDest ? (
                <button
                  className="ml-2 px-3 py-1 bg-green-600 text-white rounded"
                  onClick={()=>setAwaitSquadDest(true)}
                >
                  Set Destination
                </button>
              ) : (
                <span className="ml-2 text-yellow-700">
                  Click map for squad destination…
                </span>
              )}

              {/* Return Squadron */}
              <button
                className="ml-2 px-3 py-1 bg-red-600 text-white rounded"
                onClick={()=>{
                  fetch(`http://${sim.host}:${sim.port}/simulations/squadrons/${selectedSquad.id}/return`,{method:"POST"});
                }}
              >
                Return Squadron
              </button>

              {/* Drone Roster List */}
              <ul className="mt-4 space-y-1">
                {selectedSquad.droneIds.map((did, idx) => (
                  <li key={did} className="flex items-center space-x-2">
                    <button
                      className="text-blue-500 hover:underline"
                      onClick={()=>setSelEntityId(did)}
                    >
                      {idx+1}. {did}
                    </button>
                    {/* move up */}
                    <button
                      disabled={idx === 0}
                      onClick={() => reorder(idx, -1)}
                    >↑</button>
                    {/* move down */}
                    <button
                      disabled={idx === selectedSquad.droneIds.length-1}
                      onClick={() => reorder(idx, 1)}
                    >↓</button>
                    {/* remove */}
                    <button
                      className="text-red-500"
                      onClick={()=>{
                        fetch(
                          `http://${sim.host}:${sim.port}/simulations/squadrons/${selectedSquad.id}/removeDrone`,
                          {
                            method:"POST",
                            headers:{"Content-Type":"application/json"},
                            body:JSON.stringify({droneId: did})
                          }
                        );
                      }}
                    >
                      ✖
                    </button>
                  </li>
                ))}
              </ul>

              {/* Add existing loose drones to squad */}
              <label className="mt-2 block">
                <span>Add Drone:</span>
                <select
                  className="mt-1 border px-2 py-1"
                  onChange={(e)=>{
                    const did = e.target.value;
                    fetch(
                      `http://${sim.host}:${sim.port}/simulations/squadrons/${selectedSquad.id}/addDrone`,
                      {
                        method:"POST",
                        headers:{"Content-Type":"application/json"},
                        body:JSON.stringify({droneId: did})
                      }
                    );
                  }}
                >
                  <option value="">— select —</option>
                  {state.entities
                    .filter(e=>e.type==="drone")
                    .filter(e=>!state.squads.some(sq=>sq.droneIds.includes(e.id)))
                    .map(e=>(
                      <option key={e.id} value={e.id}>{e.id}</option>
                    ))}
                </select>
              </label>
            </div>
          )}
          {selectedEntity?.type === "command" && (
            <div className="mt-2 space-x-2">
              <button
                className="mt-2 px-4 py-2 bg-blue-600 text-white rounded"
                onClick={() => {
                  console.log("Spawn Drone button clicked for ship", selectedEntity.id);
                  fetch(
                    `http://${sim.host}:${sim.port}/simulations/commandShip/${selectedEntity.id}/spawnDrone`,
                    { method: "POST" }
                  )
                  .then(res => console.log("Spawn response", res.status))
                  .catch(err => console.error("Spawn error", err));
                }}
              >
                Spawn Drone
              </button>
              <button
                className="px-4 py-2 bg-purple-600 text-white rounded"
                onClick={() => setShowSpawnModal(true)}
              >
                Create Squadron
              </button>
            </div>
          )}
          {selectedEntity?.type === "command" && !awaitDest && (
            <div>            <label className="block">
            <span className="font-medium">Ship State:</span>
            <select
              className="mt-1 block w-full border rounded px-2 py-1"
              value={shipState}
              onChange={(e) => {
                const newState = e.target.value as "Roaming" | "Observing";
                setShipState(newState);
                // call the appropriate endpoint in Unity
                fetch(
                  `http://${sim.host}:${sim.port}/simulations/commandShip/${selectedEntity.id}/${newState.toLowerCase()}`,
                  { method: "POST" }
                ).catch(console.error);
              }}
            >
              <option value="Roaming">Roaming</option>
              <option value="Observing">Observing</option>
            </select>
          </label>            
          <button
              className="mt-2 px-4 py-2 bg-green-600 text-white rounded"
              onClick={() => setAwaitDest(true)}
            >
              Set Destination
            </button>
          </div>


          )}
          {awaitDest && (
            <div className="mt-2 text-yellow-700">Click on the map to choose destination…</div>
          )}
          {droneSnapshot && (
            <div
              className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center"
              onClick={() => {
                URL.revokeObjectURL(droneSnapshot);
                setDroneSnapshot(null);
              }}
            >
              <div className="bg-black p-4 rounded shadow-lg">
                <img
                  src={droneSnapshot}
                  alt="Drone Snapshot"
                  className="max-w-full max-h-full"
                />
                <button
                  className="mt-2 px-3 py-1 bg-red-600 text-white rounded"
                  onClick={() => {
                    URL.revokeObjectURL(droneSnapshot);
                    setDroneSnapshot(null);
                  }}
                >
                  Close
                </button>
              </div>
            </div>
          )}
          {/* 4) The Modal */}
          {(showSpawnModal &&  selectedEntity) && (
            <SpawnSquadronModal
              host={sim.host}
              port={sim.port}
              parentShipId={selectedEntity.id}
              looseDrones={looseDrones}
              onClose={() => setShowSpawnModal(false)}
              onCreated={(newSquadId) => {
                // auto‐select the new squadron if you like
                setSelSquadId(newSquadId);
                setShowSpawnModal(false);
              }}
            />
          )}
      </div>
    );
  }
