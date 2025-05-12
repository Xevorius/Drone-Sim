// app/api/sims/route.ts
import { NextRequest, NextResponse } from "next/server";

type SimRecord = { id: string; host: string; port: number };
const sims = new Map<string, SimRecord>();

export async function POST(req: NextRequest) {
  const { id, host, port } = await req.json() as SimRecord;
  sims.set(id, { id, host, port });
  return NextResponse.json({ ok: true });
}

export async function GET() {
  return NextResponse.json(Array.from(sims.values()));
}

// --- New DELETE handler: ---
export async function DELETE(req: NextRequest) {
  const { id } = await req.json() as { id: string };
  sims.delete(id);
  return NextResponse.json({ ok: true });
}
