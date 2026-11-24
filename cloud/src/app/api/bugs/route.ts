import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseAnon, getSupabaseForRequest, scrubText } from "@/lib/supabase";

export async function POST(req: NextRequest) {
  const auth = await authenticateRequest(req);
  const db = auth ? getSupabaseForRequest(req) : getSupabaseAnon();

  const body = (await req.json()) as {
    title?: string;
    category?: string;
    severity?: string;
    appVersion?: string;
    description?: string;
    sanitizedLogs?: string;
    diagnostics?: Record<string, unknown>;
  };

  const title = scrubText((body.title || "").trim()).slice(0, 200);
  if (!title) {
    return NextResponse.json({ error: "Bug report title is required." }, { status: 400 });
  }

  const scrubbedLogs = scrubText(body.sanitizedLogs || "").slice(0, 48000);
  const scrubbedDesc = scrubText(body.description || "").slice(0, 8000);

  if (!db) {
    return NextResponse.json({
      ok: true,
      queuedOfflineFallback: true,
      id: `local_${Date.now()}`,
      message: "Supabase not configured; validated scrubbed payload cleanly.",
    });
  }

  const id = crypto.randomUUID();
  const createdAt = new Date().toISOString();

  const { error } = await db
    .from("bug_reports")
    .insert({
      id,
      user_id: auth?.userId || null,
      user_email: auth?.email || "guest",
      title,
      category: (body.category || "general").slice(0, 48),
      severity: (body.severity || "normal").slice(0, 24),
      status: "open",
      app_version: (body.appVersion || "3.0.0").slice(0, 32),
      description: scrubbedDesc,
      sanitized_logs: scrubbedLogs,
      diagnostics_json: body.diagnostics || {},
      created_at: createdAt,
    });

  if (error) {
    return NextResponse.json({ error: error.message }, { status: 500 });
  }

  await db.from("telemetry_events").insert({
    user_id: auth?.userId || null,
    event_type: "bug_report_submitted",
    app_version: body.appVersion || "3.0.0",
    platform: "win-x64",
    metadata: { category: body.category || "general", severity: body.severity || "normal" },
  });

  return NextResponse.json({ ok: true, id, createdAt }, { status: 201 });
}
