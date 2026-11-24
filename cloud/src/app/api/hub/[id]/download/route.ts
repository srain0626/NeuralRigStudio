import { NextRequest, NextResponse } from "next/server";
import { getSupabaseAdmin } from "@/lib/supabase";

export async function POST(
  _req: NextRequest,
  context: { params: Promise<{ id: string }> }
) {
  const { id } = await context.params;
  const db = getSupabaseAdmin();
  if (!db) {
    return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
  }

  const { data: existing, error: fetchErr } = await db
    .from("hub_gears")
    .select("*")
    .eq("id", id)
    .maybeSingle();

  if (fetchErr || !existing) {
    return NextResponse.json({ error: "Gear item not found" }, { status: 404 });
  }

  const nextDownloads = Number(existing.downloads || 0) + 1;
  await db
    .from("hub_gears")
    .update({ downloads: nextDownloads })
    .eq("id", id);

  return NextResponse.json({
    ok: true,
    downloads: nextDownloads,
    spec: existing.spec_json,
    category: existing.category,
  });
}
