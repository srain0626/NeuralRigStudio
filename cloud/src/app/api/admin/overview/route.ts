import { NextRequest, NextResponse } from "next/server";
import { authenticateRequest, getSupabaseForRequest } from "@/lib/supabase";

export async function GET(req: NextRequest) {
  const auth = await authenticateRequest(req);
  if (!auth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  if (!auth.isAdmin) {
    return NextResponse.json({ error: "Forbidden: Admin privileges required" }, { status: 403 });
  }

  const db = getSupabaseForRequest(req);
  if (!db) {
    return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
  }

  const since24h = new Date(Date.now() - 24 * 3600 * 1000).toISOString();

  const [
    usersRes,
    syncRes,
    hubRes,
    bugsRes,
    telRes,
    relRes,
  ] = await Promise.all([
    db.from("profiles").select("id, updated_at", { count: "exact" }),
    db.from("user_settings_sync").select("user_id", { count: "exact" }),
    db.from("hub_gears").select("name, category, downloads, owner_name").order("downloads", { ascending: false }).limit(8),
    db.from("bug_reports").select("*").order("created_at", { ascending: false }).limit(25),
    db.from("telemetry_events").select("event_type, app_version, platform, created_at").order("created_at", { ascending: false }).limit(30),
    db.from("app_releases").select("*").eq("is_active", true).order("created_at", { ascending: false }).limit(1),
  ]);

  const active24h = (usersRes.data || []).filter((u) => String(u.updated_at || "") >= since24h).length;
  const totalDownloads = (hubRes.data || []).reduce((acc, g) => acc + Number(g.downloads || 0), 0);
  const bugs = bugsRes.data || [];
  const openBugs = bugs.filter((b) => b.status === "open" || b.status === "triaged").length;
  const latestRel = relRes.data?.[0];

  return NextResponse.json({
    ok: true,
    source: "supabase",
    kpis: {
      totalUsers: usersRes.count || (usersRes.data?.length ?? 0),
      active24hUsers: active24h,
      totalSyncedConfigs: syncRes.count || 0,
      communityGearCount: hubRes.data?.length ?? 0,
      openBugCount: openBugs,
      totalBugCount: bugs.length,
      totalHubDownloads: totalDownloads,
    },
    topGears: (hubRes.data || []).map((g) => ({
      name: g.name,
      category: g.category,
      downloads: g.downloads,
      ownerName: g.owner_name,
    })),
    recentTelemetry: (telRes.data || []).map((t) => ({
      eventType: t.event_type,
      appVersion: t.app_version,
      platform: t.platform,
      createdAt: t.created_at,
    })),
    bugs: bugs.map((b) => ({
      id: b.id,
      title: b.title,
      category: b.category,
      severity: b.severity,
      status: b.status,
      appVersion: b.app_version,
      userEmail: b.user_email,
      description: b.description,
      sanitizedLogs: b.sanitized_logs,
      diagnosticsJson: JSON.stringify(b.diagnostics_json || {}),
      createdAt: b.created_at,
    })),
    releasePolicy: latestRel
      ? {
          version: latestRel.version,
          minSupportedVersion: latestRel.min_supported_version,
          forceUpdate: Boolean(latestRel.force_update),
          releaseNotesKo: latestRel.release_notes_ko || "",
          releaseNotesEn: latestRel.release_notes_en || "",
          installerUrl: latestRel.installer_url || "",
          standaloneExeUrl: latestRel.standalone_exe_url || "",
          updatedAt: latestRel.updated_at,
        }
      : {
          version: "3.0.0",
          minSupportedVersion: "3.0.0",
          forceUpdate: false,
          releaseNotesKo: "",
          releaseNotesEn: "",
        },
  });
}
