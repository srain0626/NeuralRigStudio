import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseForRequest } from "@/lib/supabase";

export async function GET(req: NextRequest) {
  const db = getSupabaseForRequest(req);
  if (!db) {
    return NextResponse.json({ ok: true, items: [], source: "unconfigured" });
  }

  const category = (req.nextUrl.searchParams.get("category") || "all").trim().toLowerCase();
  const query = (req.nextUrl.searchParams.get("q") || "").trim().toLowerCase();
  const sort = (req.nextUrl.searchParams.get("sort") || "downloads").trim().toLowerCase();

  let builder = db
    .from("hub_gears")
    .select("*")
    .order(sort === "recent" ? "created_at" : "downloads", { ascending: false })
    .limit(80);

  if (category !== "all" && ["drive", "amp", "cab", "preset"].includes(category)) {
    builder = builder.eq("category", category);
  }

  const { data, error } = await builder;
  if (error) {
    return NextResponse.json({ error: error.message }, { status: 500 });
  }

  const filtered = (data || []).filter((row) => {
    if (!query) return true;
    const hay = [
      row.name,
      row.short_name,
      row.original_name,
      row.summary,
      ...(row.aliases || []),
      ...(row.tags || []),
    ]
      .join(" ")
      .toLowerCase();
    return hay.includes(query);
  });

  return NextResponse.json({
    ok: true,
    items: filtered.map((r) => ({
      id: r.id,
      ownerId: r.owner_id,
      ownerName: r.owner_name,
      slug: r.slug,
      name: r.name,
      shortName: r.short_name,
      originalName: r.original_name,
      aliases: r.aliases || [],
      category: r.category,
      summary: r.summary,
      schematicSourceUrl: r.schematic_source_url,
      tags: r.tags || [],
      downloads: r.downloads,
      likes: r.likes,
      createdAt: r.created_at,
      spec: r.spec_json,
    })),
  });
}

export async function POST(req: NextRequest) {
  const auth = await authenticateRequest(req);
  if (!auth) {
    return NextResponse.json({ error: "Google login required to publish gear." }, { status: 401 });
  }

  const db = getSupabaseForRequest(req);
  if (!db) {
    return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
  }

  const body = (await req.json()) as {
    name?: string;
    shortName?: string;
    originalName?: string;
    aliases?: string[];
    category?: string;
    summary?: string;
    schematicSourceUrl?: string;
    tags?: string[];
    spec?: Record<string, unknown>;
  };

  const name = (body.name || "").trim();
  const category = (body.category || "drive").trim().toLowerCase();
  if (!name || !body.spec || !["drive", "amp", "cab", "preset"].includes(category)) {
    return NextResponse.json({ error: "Valid name, category, and spec JSON are required." }, { status: 400 });
  }

  const slugBase = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "gear";
  const slug = `${slugBase}-${Date.now().toString(36)}`;

  const { data, error } = await db
    .from("hub_gears")
    .insert({
      owner_id: auth.userId,
      owner_name: auth.displayName,
      slug,
      name,
      short_name: (body.shortName || name).slice(0, 32),
      original_name: body.originalName || name,
      aliases: body.aliases || [],
      category,
      summary: body.summary || "",
      schematic_source_url: body.schematicSourceUrl || "",
      tags: body.tags || [],
      spec_json: body.spec,
    })
    .select("*")
    .single();

  if (error) {
    return NextResponse.json({ error: error.message }, { status: 500 });
  }

  return NextResponse.json({ ok: true, item: data }, { status: 201 });
}
