# 04 — Cloud, Community Hub, Scrubbed Bugs, OTA & Admin

## Desktop Services (`src/NeuralRig.Core/Services/`)
- **`LogSanitizer.cs`**: Strips Windows/Unix profile paths, `Environment.UserName`, `Environment.MachineName`, `Bearer` tokens, and secret key-values; retains last 400 scrubbed lines in thread-safe ring buffer (`GetRecentLogsText`).
- **`OtaUpdateService.cs`**: `CurrentVersion = "3.0.0"`. Queries `${cloudBaseUrl}/api/ota/check` with fallback to GitHub Releases (`srain0626/NeuralRigStudio`). Enforces `forceUpdate = policy.ForceUpdate || CompareSemVer(current, minSupported) < 0`.
- **`CloudPlatformService.cs`**:
  - Google OAuth 2.0 desktop loopback via temporary `HttpListener` on `http://127.0.0.1:<port>/callback/`.
  - Persists session encrypted with AES-256 at `%AppData%\NeuralRigStudio\cloud_auth.dat`.
  - Always allows Guest / Offline mode (`HasChosenAuthMode = true`).
  - Validates Community Gear Hub SPICE/Cab specs via `SpiceAnalyzer` before registering locally.
  - Queues offline bug reports in `%AppData%\NeuralRigStudio\bug_reports_outbox.json` and flushes online.

## Cloud Backend (`cloud/` — Next.js 15 App Router + Supabase)
- Schema: `cloud/supabase/migrations/001_neuralrig_cloud.sql` (`profiles`, `user_settings_sync`, `hub_gears`, `bug_reports`, `telemetry_events`, `app_releases` with RLS).
- Admin authorization: Checked server-side in `cloud/src/lib/supabase.ts` (`NEURALRIG_ADMIN_EMAILS` env or `profiles.is_admin`). Frontend hides `ADMIN` console unless `cloudState.user?.isAdmin === true`.
- Routes: `/api/health`, `/api/auth/login`, `/api/auth/callback`, `/api/sync`, `/api/hub`, `/api/hub/[id]/download`, `/api/bugs`, `/api/telemetry`, `/api/ota/check`, `/api/admin/{overview,bugs,releases}`.
