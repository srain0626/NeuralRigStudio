import {
  LandingExperience,
  LiveHubGearItem,
  LiveReleaseSnapshot,
} from "@/components/landing/LandingExperience";
import { compareSemVer, getSupabaseAnon, isSupabaseConfigured } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export default async function HomePage() {
  const configured = isSupabaseConfigured();
  const repo = process.env.GITHUB_REPO || "srain0626/NeuralRigStudio";

  let release: LiveReleaseSnapshot = {
    version: "3.2.0",
    installerUrl: "",
    standaloneExeUrl: "",
    releasePageUrl: `https://github.com/${repo}/releases`,
  };

  let liveHubItems: LiveHubGearItem[] = [];

  if (configured) {
    try {
      const db = getSupabaseAnon();
      if (db) {
        const [{ data: relRows }, { data: hubRows }] = await Promise.all([
          db
            .from("app_releases")
            .select("*")
            .eq("is_active", true)
            .order("created_at", { ascending: false })
            .limit(5),
          db
            .from("hub_gears")
            .select("*")
            .order("downloads", { ascending: false })
            .limit(4),
        ]);

        if (relRows && relRows.length > 0) {
          const sorted = [...relRows].sort((a, b) => compareSemVer(b.version, a.version));
          const latest = sorted[0];
          release = {
            version: latest.version || "3.2.0",
            installerUrl: latest.installer_url || "",
            standaloneExeUrl: latest.standalone_exe_url || "",
            releasePageUrl: `https://github.com/${repo}/releases/latest`,
            sha256Installer: latest.sha256_installer || "",
          };
        }

        if (hubRows && hubRows.length > 0) {
          liveHubItems = hubRows.map((r) => ({
            id: String(r.id),
            name: String(r.name || ""),
            originalName: String(r.original_name || r.name || ""),
            category: String(r.category || "drive"),
            ownerName: String(r.owner_name || "Community"),
            summary: String(r.summary || ""),
            downloads: Number(r.downloads || 0),
            tags: Array.isArray(r.tags) ? r.tags.map(String) : [],
          }));
        }
      }
    } catch {
      // Gracefully fall back to static baseline & GitHub release URLs
    }
  }

  return (
    <LandingExperience
      supabaseConfigured={configured}
      repo={repo}
      release={release}
      liveHubItems={liveHubItems}
    />
  );
}
