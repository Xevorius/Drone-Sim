// app/simulations/page.tsx
"use client";
import { useEffect, useState } from "react";

type Sim = { id: string; host: string; port: number };

export default function SimsPage() {
  const [sims, setSims] = useState<Sim[]>([]);

  const fetchSims = () => {
    fetch("/api/sims")
      .then((r) => r.json())
      .then(setSims)
      .catch(console.error);
  };

  useEffect(() => {
    fetchSims();                        // initial load
    const interval = setInterval(fetchSims, 5000); // every 5s
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="p-6">
      <h1 className="text-2xl font-bold mb-4">Active Simulations</h1>
      <ul className="space-y-2">
        {sims.map((s) => (
          <li key={s.id}>
            <a
              href={`/simulations/${s.id}`}
              className="text-blue-500 hover:underline"
            >
              Simulation {s.id} @ {s.host}:{s.port}
            </a>
          </li>
        ))}
        {sims.length === 0 && <li>No simulations detected.</li>}
      </ul>
    </div>
  );
}
