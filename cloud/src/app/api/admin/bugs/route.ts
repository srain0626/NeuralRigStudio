import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseAdmin } from "@/lib/supabase";

export async function PATCH(req: NextRequest) {
  const auth = await authenticateRequest(req);
  if (!auth) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  if (!auth.isAdmin) return NextResponse.json({ error: "Forbidden" }, { status: 403 });

  const db = getSupabaseAdmin();
  if (!db) return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });

  const body = (await req.json()) as { id?: string; status?: string };
  const status = (body.status || "").trim().toLowerCase();
  if (!body.id || !["open", "triaged", "resolved", "wontfix"].includes(status)) {
    return NextResponse.json({ error: "Valid id and status required." }, { status: 400 });
  }

  const { error } = await db
    .from("bug_reports")
    .update({ status })
    .eq("id", body.id);

  if (error) return NextResponse.json({ error: error.message }, { status: 500 });
  return NextResponse.json({ ok: true, id: body.id, status });
}
