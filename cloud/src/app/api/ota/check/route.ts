import { NextRequest, NextResponse } from "next/server";
import { compareSemVer, getSupabaseAdmin } from "@/lib/supabase";

export async function GET(req: NextRequest) {
  const currentVersion = (req.nextUrl.searchParams.get("currentVersion") || "3.0.0").trim();
  const repo = process.env.GITHUB_REPO || "srain0626/NeuralRigStudio";
  const db = getSupabaseAdmin();

  if (db) {
    const { data } = await db
      .from("app_releases")
      .select("*")
      .eq("is_active", true)
      .order("created_at", { ascending: false })
      .limit(10);

    if (data && data.length > 0) {
      const sorted = [...data].sort((a, b) => compareSemVer(b.version, a.version));
      const latest = sorted[0];
      const updateAvailable = compareSemVer(latest.version, currentVersion) > 0;
      const belowMin = compareSemVer(currentVersion, latest.min_supported_version || "3.0.0") < 0;
      const forceUpdate = updateAvailable && (Boolean(latest.force_update) || belowMin);

      return NextResponse.json({
        checked: true,
        currentVersion,
        latestVersion: latest.version,
        minSupportedVersion: latest.min_supported_version || latest.version,
        updateAvailable,
        forceUpdate,
        releaseNotesKo: latest.release_notes_ko || "",
        releaseNotesEn: latest.release_notes_en || "",
        installerUrl: latest.installer_url || "",
        standaloneExeUrl: latest.standalone_exe_url || "",
        sha256Installer: latest.sha256_installer || "",
        releasePageUrl: `https://github.com/${repo}/releases/latest`,
        source: "supabase",
      });
    }
  }

  // Fallback to GitHub Releases API
  try {
    const ghRes = await fetch(`https://api.github.com/repos/${repo}/releases/latest`, {
      headers: { Accept: "application/vnd.github+json", "User-Agent": "NeuralRigCloud/3.0" },
      next: { revalidate: 120 },
    });
    if (ghRes.ok) {
      const gh = (await ghRes.json()) as {
        tag_name?: string;
        body?: string;
        html_url?: string;
        assets?: { name: string; browser_download_url: string }[];
      };
      const ver = (gh.tag_name || "3.0.0").replace(/^v/i, "");
      const body = gh.body || "";
      const minMatch = /min_supported_version:\s*v?([0-9]+\.[0-9]+\.[0-9]+)/i.exec(body);
      const minVer = minMatch ? minMatch[1] : ver;
      const forceMarker = /\[FORCE_UPDATE\]/i.test(body);
      const updateAvailable = compareSemVer(ver, currentVersion) > 0;
      const forceUpdate = updateAvailable && (forceMarker || compareSemVer(currentVersion, minVer) < 0);

      const assets = gh.assets || [];
      const setupAsset = assets.find((a) => /setup.*\.exe$/i.test(a.name));
      const standaloneAsset = assets.find((a) => /standalone.*\.exe$/i.test(a.name));

      return NextResponse.json({
        checked: true,
        currentVersion,
        latestVersion: ver,
        minSupportedVersion: minVer,
        updateAvailable,
        forceUpdate,
        releaseNotesKo: body.slice(0, 1500),
        releaseNotesEn: body.slice(0, 1500),
        installerUrl: setupAsset?.browser_download_url || "",
        standaloneExeUrl: standaloneAsset?.browser_download_url || "",
        sha256Installer: "",
        releasePageUrl: gh.html_url || `https://github.com/${repo}/releases/latest`,
        source: "github-fallback",
      });
    }
  } catch {
    // Fall through to baseline response
  }

  return NextResponse.json({
    checked: true,
    currentVersion,
    latestVersion: currentVersion,
    minSupportedVersion: currentVersion,
    updateAvailable: false,
    forceUpdate: false,
    releaseNotesKo: "현재 최신 버전입니다.",
    releaseNotesEn: "You are running the latest version.",
    installerUrl: "",
    standaloneExeUrl: "",
    sha256Installer: "",
    releasePageUrl: `https://github.com/${repo}/releases`,
    source: "baseline",
  });
}
