// components/SpawnSquadronModal.tsx
"use client";
import { useState } from "react";
import type { Entity, FormationType } from "@/types";

export default function SpawnSquadronModal({
  host,
  port,
  parentShipId,
  looseDrones,
  onClose,
  onCreated,
}: {
  host: string;
  port: number;
  parentShipId: string;
  looseDrones: Entity[];
  onClose: () => void;
  onCreated: (squadId: string) => void;
}) {
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [formation, setFormation]     = useState<FormationType>("Vee");

  // toggle drone in/out of selection
  function toggleDrone(id: string) {
    setSelectedIds((prev) =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    );
  }

  // move up/down in the selected list
  function move(idx: number, dir: -1|1) {
    setSelectedIds((prev) => {
      const a = [...prev];
      const j = idx + dir;
      [a[idx], a[j]] = [a[j], a[idx]];
      return a;
    });
  }

  // create the squadron via API
  function createSquadron() {
    fetch(`http://${host}:${port}/simulations/squadrons`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        parentShipId,
        droneIds: selectedIds,
        formation,
      }),
    })
      .then((r) => r.json())
      .then((data: { id: string }) => {
        onCreated(data.id);
      })
      .catch(console.error);
  }

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-white p-6 rounded shadow-lg w-96"
        onClick={e => e.stopPropagation()}
      >
        <h3 className="text-xl font-semibold mb-4">
          Spawn Squadron from Ship {parentShipId}
        </h3>

        {/* 1) Available drones */}
        <div className="mb-4">
          <p className="font-medium">Select Drones:</p>
          <ul className="max-h-40 overflow-auto border rounded p-2">
            {looseDrones.map((d) => (
              <li key={d.id}>
                <label className="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    checked={selectedIds.includes(d.id)}
                    onChange={() => toggleDrone(d.id)}
                  />
                  <span className="text-sm">{d.id}</span>
                </label>
              </li>
            ))}
          </ul>
        </div>

        {/* 2) Formation */}
        <div className="mb-4">
          <label className="font-medium">
            Formation:{" "}
            <select
              value={formation}
              onChange={(e) =>
                setFormation(e.target.value as FormationType)
              }
              className="border rounded px-2 py-1 ml-2"
            >
              <option>Vee</option>
              <option>Line</option>
              <option>Circle</option>
            </select>
          </label>
        </div>

        {/* 3) Order/Reorder */}
        <div className="mb-4">
          <p className="font-medium">Order:</p>
          <ul className="space-y-1">
            {selectedIds.map((id, i) => (
              <li key={id} className="flex items-center space-x-2">
                <span className="flex-1 text-sm">{i + 1}. {id}</span>
                <button
                  disabled={i === 0}
                  onClick={() => move(i, -1)}
                  className="px-2"
                >
                  ↑
                </button>
                <button
                  disabled={i === selectedIds.length - 1}
                  onClick={() => move(i, 1)}
                  className="px-2"
                >
                  ↓
                </button>
              </li>
            ))}
          </ul>
        </div>

        {/* 4) Actions */}
        <div className="flex justify-end space-x-2">
          <button
            onClick={onClose}
            className="px-4 py-2 bg-gray-300 rounded"
          >
            Cancel
          </button>
          <button
            disabled={selectedIds.length === 0}
            onClick={createSquadron}
            className="px-4 py-2 bg-blue-600 text-white rounded disabled:opacity-50"
          >
            Create Squadron
          </button>
        </div>
      </div>
    </div>
  );
}
