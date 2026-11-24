import { NextRequest, NextResponse } from "next/server";
import type { User } from "@supabase/supabase-js";
import { getSupabaseAnon, isAdminEmail } from "@/lib/supabase";

function redirectToDesktopLoopback(
  port: number,
  accessToken: string,
  refreshToken: string,
  user: User
): NextResponse {
  const meta = (user.user_metadata || {}) as Record<string, unknown>;
  const loopback = new URL(`http://127.0.0.1:${port}/callback`);
  loopback.searchParams.set("access_token", accessToken);
  loopback.searchParams.set("refresh_token", refreshToken);
  loopback.searchParams.set("user_id", user.id);
  loopback.searchParams.set("email", user.email || "");
  loopback.searchParams.set(
    "display_name",
    String(meta.full_name || meta.name || user.email || "Player")
  );
  loopback.searchParams.set("avatar_url", String(meta.avatar_url || meta.picture || ""));
  loopback.searchParams.set("is_admin", isAdminEmail(user.email) ? "1" : "0");
  return NextResponse.redirect(loopback.toString());
}

export async function GET(req: NextRequest) {
  const port = Number.parseInt(req.nextUrl.searchParams.get("redirect_port") || "0", 10);
  const code = req.nextUrl.searchParams.get("code");
  const accessToken = req.nextUrl.searchParams.get("access_token");
  const refreshToken = req.nextUrl.searchParams.get("refresh_token") || "";

  if (!Number.isInteger(port) || port < 1024 || port > 65535) {
    return NextResponse.json({ error: "Invalid loopback redirect_port" }, { status: 400 });
  }

  if (code) {
    const supabase = getSupabaseAnon();
    if (!supabase) {
      return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
    }
    const { data, error } = await supabase.auth.exchangeCodeForSession(code);
    if (error || !data.session?.access_token) {
      return NextResponse.json(
        { error: error?.message || "Failed to exchange OAuth code" },
        { status: 400 }
      );
    }

    return redirectToDesktopLoopback(
      port,
      data.session.access_token,
      data.session.refresh_token || "",
      data.session.user
    );
  }

  if (accessToken) {
    const supabase = getSupabaseAnon();
    if (!supabase) {
      return NextResponse.json({ error: "Supabase not configured" }, { status: 503 });
    }
    const { data, error } = await supabase.auth.getUser(accessToken);
    if (error || !data.user) {
      return NextResponse.json(
        { error: error?.message || "Invalid or expired OAuth access token" },
        { status: 401 }
      );
    }

    return redirectToDesktopLoopback(port, accessToken, refreshToken, data.user);
  }

  // Fallback for implicit fragment tokens (#access_token=...) -> re-dispatch to /api/auth/callback so server resolves user profile & admin status
  const safePort = String(port);
  const html = `<!doctype html>
<html>
<head><meta charset="utf-8"><title>NeuralRig Auth Redirect</title></head>
<body style="background:#0c0f14;color:#e6edf3;font-family:sans-serif;padding:32px">
  <h3>Completing NeuralRig Studio sign-in...</h3>
  <p id="status">Verifying account session for NeuralRig Studio desktop.</p>
  <script>
    const hash = window.location.hash ? window.location.hash.slice(1) : "";
    const params = new URLSearchParams(hash);
    const token = params.get("access_token");
    if (token) {
      params.set("redirect_port", "${safePort}");
      window.location.replace("/api/auth/callback?" + params.toString());
    } else {
      const qs = new URLSearchParams(window.location.search);
      const errDesc = qs.get("error_description") || params.get("error_description") || "Missing authentication token.";
      document.getElementById("status").textContent = errDesc + " You may close this tab.";
    }
  </script>
</body>
</html>`;
  return new NextResponse(html, { headers: { "Content-Type": "text/html; charset=utf-8" } });
}
