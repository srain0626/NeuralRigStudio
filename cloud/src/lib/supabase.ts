import { createClient, type SupabaseClient, type User } from "@supabase/supabase-js";
import type { NextRequest } from "next/server";

export function isSupabaseConfigured(): boolean {
  return Boolean(
    process.env.NEXT_PUBLIC_SUPABASE_URL &&
      process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY
  );
}

export function getSupabaseAnon(): SupabaseClient | null {
  const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const key = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
  if (!url || !key) return null;
  return createClient(url, key, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
}

export function extractBearerToken(req?: NextRequest): string {
  const authHeader = req?.headers.get("authorization") || "";
  return authHeader.toLowerCase().startsWith("bearer ")
    ? authHeader.slice(7).trim()
    : "";
}

export function getSupabaseAdmin(): SupabaseClient | null {
  const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const key =
    process.env.SUPABASE_SERVICE_ROLE_KEY ||
    process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
  if (!url || !key) return null;
  return createClient(url, key, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
}

export function getSupabaseForToken(token?: string): SupabaseClient | null {
  const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const serviceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
  const anonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
  if (!url) return null;

  if (serviceKey) {
    return createClient(url, serviceKey, {
      auth: { persistSession: false, autoRefreshToken: false },
    });
  }
  if (!anonKey) return null;

  const trimmed = (token || "").trim();
  return createClient(url, anonKey, {
    auth: { persistSession: false, autoRefreshToken: false },
    ...(trimmed
      ? { global: { headers: { Authorization: `Bearer ${trimmed}` } } }
      : {}),
  });
}

export function getSupabaseForRequest(req?: NextRequest): SupabaseClient | null {
  return getSupabaseForToken(extractBearerToken(req));
}

export interface AuthContext {
  userId: string;
  email: string;
  displayName: string;
  avatarUrl: string;
  isAdmin: boolean;
}

export function isAdminEmail(email: string | undefined | null): boolean {
  if (!email) return false;
  const raw = process.env.NEURALRIG_ADMIN_EMAILS || "";
  const list = raw
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);
  return list.includes(email.trim().toLowerCase());
}

export async function resolveAuthContextForUser(
  user: User,
  accessToken: string
): Promise<AuthContext> {
  const email = user.email || "";
  const meta = (user.user_metadata || {}) as Record<string, unknown>;
  const displayName =
    String(meta.full_name || meta.name || email.split("@")[0] || "Player");
  const avatarUrl = String(meta.avatar_url || meta.picture || "");

  let dbAdmin = false;
  const db = getSupabaseForToken(accessToken);
  if (db) {
    const { data: prof } = await db
      .from("profiles")
      .select("is_admin")
      .eq("id", user.id)
      .maybeSingle();
    dbAdmin = Boolean(prof?.is_admin);
    await db.from("profiles").upsert({
      id: user.id,
      email,
      display_name: displayName,
      avatar_url: avatarUrl,
      is_admin: dbAdmin || isAdminEmail(email),
      updated_at: new Date().toISOString(),
    });
  }

  return {
    userId: user.id,
    email,
    displayName,
    avatarUrl,
    isAdmin: dbAdmin || isAdminEmail(email),
  };
}

export async function authenticateRequest(
  req: NextRequest
): Promise<AuthContext | null> {
  const token = extractBearerToken(req);
  if (!token) return null;

  const anon = getSupabaseAnon();
  if (!anon) return null;

  const { data, error } = await anon.auth.getUser(token);
  if (error || !data.user) return null;

  return resolveAuthContextForUser(data.user, token);
}

export function scrubText(input: string): string {
  return (input || "")
    .replace(/[A-Za-z]:\\Users\\[^\\\r\n\s"']+/gi, "<WIN_USER_DIR>")
    .replace(/\/Users\/[^/\r\n\s"']+/g, "<MAC_USER_DIR>")
    .replace(/\/home\/[^/\r\n\s"']+/g, "<LINUX_USER_DIR>")
    .replace(/Bearer\s+[A-Za-z0-9._-]+/gi, "Bearer <REDACTED>");
}

export function compareSemVer(a: string, b: string): number {
  const parse = (s: string) =>
    (s || "0.0.0")
      .replace(/^v/i, "")
      .split("-")[0]
      .split(".")
      .map((x) => Number.parseInt(x, 10) || 0);
  const pa = parse(a);
  const pb = parse(b);
  const len = Math.max(pa.length, pb.length, 3);
  for (let i = 0; i < len; i++) {
    const va = pa[i] || 0;
    const vb = pb[i] || 0;
    if (va !== vb) return va - vb;
  }
  return 0;
}
