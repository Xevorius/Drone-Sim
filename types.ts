// types.ts
export type EntityType   = "drone" | "ship" | "command";

export interface Position { x: number; y: number; }
export interface Entity   { id: string; type: EntityType; position: Position; }

export type FormationType = "Vee" | "Line" | "Circle";

export interface SquadState {
  id:        string;
  formation: FormationType;
  position:  Position;    // squadron center on the map
  droneIds:  string[];
}

export interface SimState {
  minX: number; maxX: number; minY: number; maxY: number;
  entities: Entity[];
  squads:   SquadState[];
}

export interface Sim { id: string; host: string; port: number; }
