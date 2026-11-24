import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseForRequest } from "@/lib/supabase";

export async function GET(req: NextRequest) {
  const auth = await authenticateRequest(req);
  if (!auth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const db = getSupabaseForRequest(req);
  if (!db) {
    return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
  }

  const { data, error } = await db
    .from("user_settings_sync")
    .select("*")
    .eq("user_id", auth.userId)
    .maybeSingle();

  if (error) {
    return NextResponse.json({ error: error.message }, { status: 500 });
  }

  return NextResponse.json({
    ok: true,
    user: auth,
    snapshot: data || null,
  });
}

export async function PUT(req: NextRequest) {
  const auth = await authenticateRequest(req);
  if (!auth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const db = getSupabaseForRequest(req);
  if (!db) {
    return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
  }

  const body = (await req.json()) as {
    settings?: unknown;
    presets?: unknown;
    guitarProfiles?: unknown;
    customDevices?: unknown;
  };

  const now = new Date().toISOString();
  const { error } = await db.from("user_settings_sync").upsert({
    user_id: auth.userId,
    settings_json: body.settings ?? {},
    presets_json: body.presets ?? [],
    guitar_profiles_json: body.guitarProfiles ?? [],
    custom_devices_json: body.customDevices ?? [],
    updated_at: now,
  });

  if (error) {
    return NextResponse.json({ error: error.message }, { status: 500 });
  }

  await db.from("telemetry_events").insert({
    user_id: auth.userId,
    event_type: "settings_sync",
    app_version: "3.0.0",
    platform: "win-x64",
    metadata: { hasCustomDevices: Array.isArray(body.customDevices) && body.customDevices.length > 0 },
  });

  return NextResponse.json({ ok: true, updatedAt: now, user: auth });
}
