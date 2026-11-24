import { NextRequest, NextResponse } from "next/server";
import { getSupabaseAnon, resolveAuthContextForUser } from "@/lib/supabase";

export const dynamic = "force-dynamic";

export async function POST(req: NextRequest) {
  let body: { refreshToken?: unknown } | null = null;
  try {
    body = (await req.json()) as { refreshToken?: unknown };
  } catch {
    return NextResponse.json({ error: "Invalid JSON body" }, { status: 400 });
  }

  const refreshToken =
    typeof body?.refreshToken === "string" ? body.refreshToken.trim() : "";
  if (!refreshToken) {
    return NextResponse.json(
      { error: "refreshToken is required" },
      { status: 400 }
    );
  }

  const supabase = getSupabaseAnon();
  if (!supabase) {
    return NextResponse.json(
      { error: "Supabase not configured" },
      { status: 503 }
    );
  }

  const { data, error } = await supabase.auth.refreshSession({
    refresh_token: refreshToken,
  });

  if (error || !data.session?.access_token || !data.user) {
    return NextResponse.json(
      { error: error?.message || "Refresh token expired or invalid" },
      { status: 401, headers: { "Cache-Control": "no-store" } }
    );
  }

  const auth = await resolveAuthContextForUser(
    data.user,
    data.session.access_token
  );

  return NextResponse.json(
    {
      ok: true,
      accessToken: data.session.access_token,
      refreshToken: data.session.refresh_token || refreshToken,
      expiresAt: data.session.expires_at ?? null,
      user: {
        id: auth.userId,
        email: auth.email,
        displayName: auth.displayName,
        avatarUrl: auth.avatarUrl,
        isAdmin: auth.isAdmin,
      },
    },
    { headers: { "Cache-Control": "no-store" } }
  );
}
