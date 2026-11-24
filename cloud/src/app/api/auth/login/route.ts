import { NextRequest, NextResponse } from "next/server";
import { getSupabaseAnon } from "@/lib/supabase";

export async function GET(req: NextRequest) {
  const redirectPort = Number.parseInt(req.nextUrl.searchParams.get("redirect_port") || "0", 10);
  if (!Number.isInteger(redirectPort) || redirectPort < 1024 || redirectPort > 65535) {
    return NextResponse.json({ error: "Invalid loopback redirect_port" }, { status: 400 });
  }

  const supabase = getSupabaseAnon();
  if (!supabase) {
    return NextResponse.json(
      { error: "Supabase credentials are not configured on this cloud deployment." },
      { status: 503 }
    );
  }

  const callbackUrl = new URL("/api/auth/callback", req.nextUrl.origin);
  callbackUrl.searchParams.set("redirect_port", String(redirectPort));

  const { data, error } = await supabase.auth.signInWithOAuth({
    provider: "google",
    options: {
      redirectTo: callbackUrl.toString(),
      queryParams: { access_type: "offline", prompt: "select_account" },
    },
  });

  if (error || !data?.url) {
    return NextResponse.json(
      { error: error?.message || "Failed to construct Google OAuth URL" },
      { status: 500 }
    );
  }

  return NextResponse.redirect(data.url);
}
