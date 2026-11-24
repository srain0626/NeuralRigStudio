import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseAdmin } from "@/lib/supabase";

export async function POST(req: NextRequest) {
  const auth = await authenticateRequest(req);
  const db = getSupabaseAdmin();

  const body = (await req.json()) as {
    eventType?: string;
    appVersion?: string;
    platform?: string;
    metadata?: Record<string, unknown>;
  };

  const eventType = (body.eventType || "").trim().slice(0, 64);
  if (!eventType) {
    return NextResponse.json({ error: "eventType is required" }, { status: 400 });
  }

  if (!db) {
    return NextResponse.json({ ok: true, stored: false });
  }

  const { error } = await db.from("telemetry_events").insert({
    user_id: auth?.userId || null,
    event_type: eventType,
    app_version: (body.appVersion || "3.0.0").slice(0, 32),
    platform: (body.platform || "win-x64").slice(0, 32),
    metadata: body.metadata || {},
  });

  if (error) {
    return NextResponse.json({ error: error.message }, { status: 500 });
  }

  return NextResponse.json({ ok: true, stored: true });
}
