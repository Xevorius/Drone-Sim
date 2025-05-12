"use client";
import { MapContainer, ImageOverlay, Marker } from "react-leaflet";
import L from "leaflet";
import { useMapEvent } from "react-leaflet";
import type { Entity, SimState, FormationType, SquadState } from "@/types";

type Props = {
  sim:   { host: string; port: number };
  state: SimState;
  onEntityClick?: (e: Entity) => void;
  onSquadClick?:  (s: SquadState) => void;
  onMapClick?:    (x: number, y: number) => void;
};

const icons = {
  drone:   new L.Icon({ iconUrl: "/drone-icon.png",   iconSize: [24,24] }),
  ship:    new L.Icon({ iconUrl: "/ship-icon.png",    iconSize: [24,24] }),
  command: new L.Icon({ iconUrl: "/command-icon.png", iconSize: [32,32] }),
};
const squadIcon = new L.Icon({ iconUrl: "/squad-icon.png", iconSize: [32,32] });

function MapClickHandler({ onMapClick }: { onMapClick: (x:number,y:number)=>void }) {
  useMapEvent("click", (e) => onMapClick(e.latlng.lng, e.latlng.lat));
  return null;
}

export default function MiniMap({ sim, state, onEntityClick, onSquadClick, onMapClick }: Props) {
  const bounds = [
    [state.minY, state.minX],
    [state.maxY, state.maxX],
  ] as [[number,number],[number,number]];
  const mapUrl = `http://${sim.host}:${sim.port}/simulations/map`;

  return (
    <MapContainer
      crs={L.CRS.Simple}
      bounds={bounds}
      style={{ height: 400, width: "100%" }}
      scrollWheelZoom
    >
      <ImageOverlay url={mapUrl} bounds={bounds} />
      {onMapClick && <MapClickHandler onMapClick={onMapClick} />}

      {/* Entities */}
      {state.entities.map((e) => (
        <Marker
          key={e.id}
          position={[e.position.y, e.position.x]}
          icon={icons[e.type]}
          eventHandlers={{ click: () => onEntityClick?.(e) }}
        />
      ))}

      {/* Squadrons */}
      {state.squads.map((s) => (
        <Marker
          key={s.id}
          position={[s.position.y, s.position.x]}
          icon={squadIcon}
          eventHandlers={{ click: () => onSquadClick?.(s) }}
        />
      ))}
    </MapContainer>
  );
}
