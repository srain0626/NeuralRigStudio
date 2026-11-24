import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseForRequest } from "@/lib/supabase";

export async function POST(req: NextRequest) {
  const auth = await authenticateRequest(req);
  if (!auth) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  if (!auth.isAdmin) return NextResponse.json({ error: "Forbidden" }, { status: 403 });

  const db = getSupabaseForRequest(req);
  if (!db) return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });

  const body = (await req.json()) as {
    version?: string;
    minSupportedVersion?: string;
    forceUpdate?: boolean;
    releaseNotesKo?: string;
    releaseNotesEn?: string;
    installerUrl?: string;
    standaloneExeUrl?: string;
    sha256Installer?: string;
  };

  const version = (body.version || "").trim().replace(/^v/i, "");
  if (!version) {
    return NextResponse.json({ error: "version is required" }, { status: 400 });
  }

  const now = new Date().toISOString();
  const { error } = await db.from("app_releases").upsert({
    version,
    min_supported_version: (body.minSupportedVersion || version).trim().replace(/^v/i, ""),
    force_update: Boolean(body.forceUpdate),
    release_notes_ko: body.releaseNotesKo || "",
    release_notes_en: body.releaseNotesEn || "",
    installer_url: body.installerUrl || "",
    standalone_exe_url: body.standaloneExeUrl || "",
    sha256_installer: body.sha256Installer || "",
    is_active: true,
    updated_at: now,
  });

  if (error) return NextResponse.json({ error: error.message }, { status: 500 });
  return NextResponse.json({ ok: true, version, forceUpdate: Boolean(body.forceUpdate), updatedAt: now });
}
