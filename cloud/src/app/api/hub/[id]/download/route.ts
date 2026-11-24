import { NextRequest, NextResponse } from "next/server";
import { getSupabaseForRequest } from "@/lib/supabase";

export async function POST(
  req: NextRequest,
  context: { params: Promise<{ id: string }> }
) {
  const { id } = await context.params;
  const db = getSupabaseForRequest(req);
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

  const { data: updatedCount, error: rpcErr } = await db.rpc(
    "increment_hub_gear_downloads",
    { p_gear_id: id }
  );

  const nextDownloads =
    !rpcErr && typeof updatedCount === "number"
      ? updatedCount
      : Number(existing.downloads || 0) + 1;

  return NextResponse.json({
    ok: true,
    downloads: nextDownloads,
    spec: existing.spec_json,
    category: existing.category,
  });
}
