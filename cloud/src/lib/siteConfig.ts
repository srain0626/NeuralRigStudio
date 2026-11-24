export const PRIMARY_SITE_URL = "https://neuralrigstudio.seltum.xyz";
export const HUB_SITE_URL = "https://seltum.xyz";
export const DEFAULT_GTM_ID = "GTM-5PHL3X34";

export function resolveSiteUrl(rawUrl?: string | null): string {
  const candidate = rawUrl?.trim();
  if (!candidate) return PRIMARY_SITE_URL;
  try {
    return new URL(candidate).origin;
  } catch {
    return PRIMARY_SITE_URL;
  }
}

export const SITE_URL = resolveSiteUrl(process.env.NEXT_PUBLIC_SITE_URL);
