import { isSupabaseConfigured } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export default function HomePage() {
  const configured = isSupabaseConfigured();
  const repo = process.env.GITHUB_REPO || "srain0626/NeuralRigStudio";

  return (
    <main style={{ maxWidth: 820, margin: "48px auto", padding: "0 24px", lineHeight: 1.6 }}>
      <h1 style={{ fontSize: 28, marginBottom: 8 }}>NeuralRig Studio Public Cloud</h1>
      <p style={{ color: "#9da7b3" }}>
        Backend API gateway for Google OAuth PKCE desktop sign-in, preset &amp; custom SPICE gear sync,
        Community Gear Hub, scrubbed bug log analytics, and choice/mandatory OTA release policies.
      </p>

      <div
        style={{
          marginTop: 24,
          padding: 16,
          borderRadius: 8,
          border: "1px solid #262c36",
          background: "#131820",
        }}
      >
        <div>
          <strong>Supabase status:</strong>{" "}
          <span style={{ color: configured ? "#3fb950" : "#f0883e" }}>
            {configured ? "Configured" : "Awaiting NEXT_PUBLIC_SUPABASE_URL / ANON_KEY env vars"}
          </span>
        </div>
        <div style={{ marginTop: 6 }}>
          <strong>GitHub Release Source:</strong> <code>{repo}</code>
        </div>
      </div>

      <h2 style={{ fontSize: 18, marginTop: 32 }}>Active API Endpoints</h2>
      <ul style={{ color: "#c9d1d9", fontFamily: "ui-monospace, monospace", fontSize: 13 }}>
        <li>GET /api/health</li>
        <li>GET /api/auth/login &amp; GET /api/auth/callback</li>
        <li>GET, PUT /api/sync</li>
        <li>GET, POST /api/hub &amp; POST /api/hub/[id]/download</li>
        <li>POST /api/bugs &amp; POST /api/telemetry</li>
        <li>GET /api/ota/check</li>
        <li>GET /api/admin/overview &amp; PATCH /api/admin/bugs &amp; POST /api/admin/releases</li>
      </ul>
    </main>
  );
}
