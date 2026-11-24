using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;

namespace NeuralRigStudio.Services;

public sealed class CloudUserDto
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public bool IsAdmin { get; set; }
}

public sealed class CloudStateDto
{
    public string Mode { get; set; } = "unselected"; // unselected | guest | google
    public bool IsConfigured { get; set; }
    public bool IsAuthenticated { get; set; }
    public CloudUserDto? User { get; set; }
    public string? LastSyncUtc { get; set; }
    public int PendingBugReportsCount { get; set; }
}

public sealed class CommunityGearItemDto
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string Category { get; set; } = "drive";
    public string Summary { get; set; } = "";
    public string SchematicSourceUrl { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public int Downloads { get; set; }
    public int Likes { get; set; }
    public string CreatedAt { get; set; } = "";
    public CustomDeviceSpecDto? Spec { get; set; }
}

public sealed class CloudAuthSession
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public CloudUserDto User { get; set; } = new();
    public string SignedInUtc { get; set; } = "";
}

public sealed class QueuedBugReportDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "general";
    public string Severity { get; set; } = "normal";
    public string AppVersion { get; set; } = OtaUpdateService.CurrentVersion;
    public string Description { get; set; } = "";
    public string SanitizedLogs { get; set; } = "";
    public Dictionary<string, object?> Diagnostics { get; set; } = new();
    public string CreatedAt { get; set; } = "";
}

/// <summary>
/// Public Live Cloud integration service:
/// - Google OAuth 2.0 loopback PKCE desktop sign-in (with Guest / Offline mode always allowed)
/// - Encrypted local token persistence (AES-256)
/// - Cloud settings, presets, guitar profiles, and custom SPICE gear sync
/// - Community Custom Gear Hub upload & validated SPICE/IR install
/// - Automated scrubbed-log bug reporting with offline outbox queuing
/// - Admin analytics & release policy management
/// </summary>
public sealed class CloudPlatformService
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(25),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions PrettyJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private CloudAuthSession? _session;
    private readonly List<QueuedBugReportDto> _bugOutbox = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event Action? SessionStateChanged;

    private enum RefreshOutcome
    {
        Refreshed,
        SessionExpired,
        TransientError,
    }

    private static string AppDataDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeuralRigStudio");

    private static string SessionPath => Path.Combine(AppDataDir, "cloud_auth.dat");
    private static string BugOutboxPath => Path.Combine(AppDataDir, "bug_reports_outbox.json");

    public CloudPlatformService()
    {
        LoadSessionFromDisk();
        LoadBugOutboxFromDisk();
    }

    public const string DefaultCloudBaseUrl = "https://neuralrigstudio.seltum.xyz";

    public static string ResolveCloudBaseUrl(AppSettings _)
        => DefaultCloudBaseUrl;

    public CloudStateDto GetState(AppSettings settings)
    {
        lock (_lock)
        {
            bool authed = _session != null && !string.IsNullOrWhiteSpace(_session.AccessToken);
            string mode = authed
                ? "google"
                : (settings.HasChosenAuthMode ? "guest" : "unselected");

            return new CloudStateDto
            {
                Mode = mode,
                IsConfigured = true,
                IsAuthenticated = authed,
                User = authed ? _session!.User : null,
                LastSyncUtc = settings.LastCloudSyncUtc,
                PendingBugReportsCount = _bugOutbox.Count,
            };
        }
    }

    public CloudStateDto ContinueAsGuest(AppSettings settings)
    {
        settings.HasChosenAuthMode = true;
        settings.Save();
        return GetState(settings);
    }

    public CloudStateDto SignOut(AppSettings settings)
    {
        lock (_lock)
        {
            _session = null;
            try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
        }
        settings.HasChosenAuthMode = true;
        settings.Save();
        return GetState(settings);
    }

    public async Task<CloudStateDto> StartGoogleSignInAsync(AppSettings settings, CancellationToken ct = default)
    {
        string baseUrl = ResolveCloudBaseUrl(settings);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "클라우드 API URL이 설정되지 않았습니다. Vercel 배포 URL(cloud/)을 입력하거나 게스트 모드로 시작하세요.");
        }

        int port = FindFreeTcpPort();
        using var listener = new HttpListener();
        string prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        string loginUrl = $"{baseUrl}/api/auth/login?redirect_port={port}";
        Process.Start(new ProcessStartInfo { FileName = loginUrl, UseShellExecute = true });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(2));

        HttpListenerContext ctx;
        try
        {
            ctx = await listener.GetContextAsync().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Google 로그인 대기 시간이 초과되었습니다(2분). 다시 시도하거나 게스트 모드를 사용하세요.");
        }

        var qs = ctx.Request.QueryString;
        string accessToken = qs["access_token"] ?? "";
        string refreshToken = qs["refresh_token"] ?? "";
        string userId = qs["user_id"] ?? "";
        string email = qs["email"] ?? "";
        string displayName = qs["display_name"] ?? email;
        string avatarUrl = qs["avatar_url"] ?? "";
        bool isAdmin = qs["is_admin"] == "1" || string.Equals(qs["is_admin"], "true", StringComparison.OrdinalIgnoreCase);

        byte[] htmlBytes = Encoding.UTF8.GetBytes(
            "<!doctype html><html><body style='background:#0c0f14;color:#e6edf3;font-family:sans-serif;padding:40px;text-align:center'>" +
            "<h2>NeuralRig Studio Google Sign-In Complete</h2>" +
            "<p>이 탭을 닫고 NeuralRig Studio 데스크톱 앱으로 돌아가세요.</p>" +
            "</body></html>");
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = htmlBytes.Length;
        await ctx.Response.OutputStream.WriteAsync(htmlBytes, ct);
        ctx.Response.Close();

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("Google OAuth 콜백에서 액세스 토큰을 받지 못했습니다.");

        var session = new CloudAuthSession
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            SignedInUtc = DateTime.UtcNow.ToString("O"),
            User = new CloudUserDto
            {
                Id = userId,
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
                AvatarUrl = avatarUrl,
                IsAdmin = isAdmin,
            },
        };

        lock (_lock)
        {
            _session = session;
            SaveSessionToDiskLocked();
        }

        settings.HasChosenAuthMode = true;
        settings.Save();
        return GetState(settings);
    }

    public async Task<CloudStateDto> SyncSettingsAsync(
        AppSettings settings,
        CustomDeviceSpecDto[] localCustomDevices,
        Action<List<SignalChainConfigDto>?, List<GuitarProfileDto>?, List<CustomDeviceSpecDto>?> mergeRemoteData,
        CancellationToken ct = default)
    {
        var (baseUrl, _) = RequireAuthenticatedEndpoint(settings);

        // 1) Push local state first (uploading active settings, presets, guitar profiles, custom SPICE gear)
        var putPayload = new
        {
            settings = new
            {
                settings.InputKind,
                settings.ChannelMode,
                settings.InputChannelOffset,
                settings.AsioInBufferSize,
                settings.OutputKind,
                settings.WasapiExclusive,
                settings.PresetIndex,
                settings.ToneMode,
                settings.ToneDrivePct,
                settings.ToneMixPct,
                settings.ToneGainDb,
                settings.ToneGateDb,
            },
            presets = settings.SavedPresets ?? new List<SignalChainConfigDto>(),
            guitarProfiles = settings.GuitarProfiles ?? new List<GuitarProfileDto>(),
            customDevices = localCustomDevices,
        };

        using (var resp = await SendAuthenticatedAsync(
                   settings,
                   token => BuildAuthRequest(HttpMethod.Put, $"{baseUrl}/api/sync", token, putPayload),
                   ct))
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"클라우드 동기화 실패 ({(int)resp.StatusCode}): {LogSanitizer.Scrub(body)}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("user", out var uEl))
            {
                var updatedUser = JsonSerializer.Deserialize<CloudUserDto>(uEl.GetRawText(), JsonOpts);
                if (updatedUser != null)
                {
                    lock (_lock)
                    {
                        if (_session != null)
                        {
                            _session.User.IsAdmin = updatedUser.IsAdmin;
                            SaveSessionToDiskLocked();
                        }
                    }
                }
            }
        }

        // 2) Pull latest cloud snapshot (merge safely)
        using (var getResp = await SendAuthenticatedAsync(
                   settings,
                   token => BuildAuthRequest(HttpMethod.Get, $"{baseUrl}/api/sync", token),
                   ct))
        {
            if (getResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("snapshot", out var snap) && snap.ValueKind == JsonValueKind.Object)
                {
                    List<SignalChainConfigDto>? remotePresets = null;
                    List<GuitarProfileDto>? remoteGuitars = null;
                    List<CustomDeviceSpecDto>? remoteDevices = null;

                    if (snap.TryGetProperty("presets_json", out var prEl) && prEl.ValueKind == JsonValueKind.Array)
                        remotePresets = JsonSerializer.Deserialize<List<SignalChainConfigDto>>(prEl.GetRawText(), JsonOpts);
                    if (snap.TryGetProperty("guitar_profiles_json", out var gpEl) && gpEl.ValueKind == JsonValueKind.Array)
                        remoteGuitars = JsonSerializer.Deserialize<List<GuitarProfileDto>>(gpEl.GetRawText(), JsonOpts);
                    if (snap.TryGetProperty("custom_devices_json", out var cdEl) && cdEl.ValueKind == JsonValueKind.Array)
                        remoteDevices = JsonSerializer.Deserialize<List<CustomDeviceSpecDto>>(cdEl.GetRawText(), JsonOpts);

                    mergeRemoteData(remotePresets, remoteGuitars, remoteDevices);
                }
            }
        }

        settings.LastCloudSyncUtc = DateTime.UtcNow.ToString("O");
        settings.Save();
        return GetState(settings);
    }

    public async Task<(CommunityGearItemDto[] Items, string Source)> GetCommunityHubAsync(
        AppSettings settings,
        string category = "all",
        string query = "",
        string sort = "downloads",
        CancellationToken ct = default)
    {
        string baseUrl = ResolveCloudBaseUrl(settings);
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            try
            {
                string url = $"{baseUrl}/api/hub?category={Uri.EscapeDataString(category)}&q={Uri.EscapeDataString(query)}&sort={Uri.EscapeDataString(sort)}";
                using var resp = await SharedHttp.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("items", out var itemsEl))
                    {
                        var items = JsonSerializer.Deserialize<CommunityGearItemDto[]>(itemsEl.GetRawText(), JsonOpts) ?? Array.Empty<CommunityGearItemDto>();
                        return (items, "cloud");
                    }
                }
            }
            catch
            {
                // Fall back to curated offline/local showcase below
            }
        }

        // Local/offline fallback: expose user custom gear + built-in SPICE community samples
        var local = CustomDeviceRegistry.GetAll().Select(d => new CommunityGearItemDto
        {
            Id = $"local_{d.Id}",
            OwnerId = "local",
            OwnerName = "Local Studio",
            Slug = d.Id,
            Name = d.Name,
            ShortName = d.ShortName,
            OriginalName = d.OriginalName,
            Aliases = d.Aliases ?? Array.Empty<string>(),
            Category = d.Category,
            Summary = d.SchematicSummary,
            SchematicSourceUrl = d.SchematicSourceUrl,
            Tags = d.ReferencedModels ?? Array.Empty<string>(),
            Downloads = 1,
            Likes = 0,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            Spec = d,
        }).Where(it =>
            (category == "all" || string.Equals(it.Category, category, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query) ||
             it.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             it.OriginalName.Contains(query, StringComparison.OrdinalIgnoreCase))
        ).ToArray();

        return (local, "local-offline");
    }

    public async Task<object> UploadCommunityGearAsync(
        AppSettings settings,
        CustomDeviceSpecDto spec,
        string summary,
        string[] tags,
        CancellationToken ct = default)
    {
        var (baseUrl, _) = RequireAuthenticatedEndpoint(settings);
        ValidateCustomDeviceSpecSafety(spec);

        var payload = new
        {
            name = spec.OriginalName?.Length > 0 ? spec.OriginalName : spec.Name,
            shortName = spec.ShortName,
            originalName = spec.OriginalName,
            aliases = spec.Aliases ?? Array.Empty<string>(),
            category = spec.Category,
            summary = string.IsNullOrWhiteSpace(summary) ? spec.SchematicSummary : summary,
            schematicSourceUrl = spec.SchematicSourceUrl,
            tags,
            spec,
        };

        using var resp = await SendAuthenticatedAsync(
            settings,
            token => BuildAuthRequest(HttpMethod.Post, $"{baseUrl}/api/hub", token, payload),
            ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Community Hub 업로드 실패 ({(int)resp.StatusCode}): {LogSanitizer.Scrub(raw)}");

        return new { ok = true, message = $"'{spec.OriginalName}' 기어가 커뮤니티 허브에 게시되었습니다." };
    }

    public async Task<CustomDeviceSpecDto> InstallCommunityGearAsync(
        AppSettings settings,
        CommunityGearItemDto item,
        CancellationToken ct = default)
    {
        CustomDeviceSpecDto? spec = item.Spec;
        string baseUrl = ResolveCloudBaseUrl(settings);

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out _) && !item.Id.StartsWith("local_", StringComparison.Ordinal))
        {
            try
            {
                using var resp = await SharedHttp.PostAsync($"{baseUrl}/api/hub/{Uri.EscapeDataString(item.Id)}/download", null, ct);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("spec", out var specEl))
                        spec = JsonSerializer.Deserialize<CustomDeviceSpecDto>(specEl.GetRawText(), JsonOpts) ?? spec;
                }
            }
            catch
            {
                // Use bundled item.Spec if online download counter failed
            }
        }

        if (spec == null)
            throw new InvalidOperationException("선택한 커뮤니티 기어에 유효한 회로/캐비넷 스펙이 없습니다.");

        ValidateCustomDeviceSpecSafety(spec);
        return CustomDeviceRegistry.RegisterOrUpdate(spec);
    }

    public static void ValidateCustomDeviceSpecSafety(CustomDeviceSpecDto spec)
    {
        if (spec == null || string.IsNullOrWhiteSpace(spec.Id))
            throw new InvalidOperationException("Invalid custom gear specification.");

        if (spec.SpiceCircuit != null)
        {
            var sim = SpiceAnalyzer.AnalyzeCircuit(spec.SpiceCircuit, sampleRate: 48000);
            if (!sim.Ok || sim.RtAllocBytes != 0)
            {
                string err = sim.Errors is { Length: > 0 } ? string.Join("; ", sim.Errors) : "SPICE stability/RT safety validation failed";
                throw new InvalidOperationException($"커뮤니티 기어 물리 검증 실패: {err}");
            }
        }
        else if (spec.CabAcoustic != null)
        {
            var cabRep = SpiceAnalyzer.AnalyzeCabinet(spec.CabAcoustic, 48000);
            if (!cabRep.Ok)
            {
                string err = cabRep.Errors is { Length: > 0 } ? string.Join("; ", cabRep.Errors) : "Cabinet acoustic validation failed";
                throw new InvalidOperationException($"커뮤니티 캐비넷 IR 검증 실패: {err}");
            }
        }
    }

    public async Task<object> SubmitBugReportAsync(
        AppSettings settings,
        string title,
        string category,
        string severity,
        string description,
        Dictionary<string, object?> diagnostics,
        CancellationToken ct = default)
    {
        string cleanTitle = LogSanitizer.Scrub(title).Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle))
            throw new InvalidOperationException("버그 제목을 입력해 주세요.");

        string cleanDesc = LogSanitizer.Scrub(description);
        string cleanLogs = LogSanitizer.GetRecentLogsText(250);

        var entry = new QueuedBugReportDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = cleanTitle,
            Category = string.IsNullOrWhiteSpace(category) ? "general" : category.Trim(),
            Severity = string.IsNullOrWhiteSpace(severity) ? "normal" : severity.Trim(),
            AppVersion = OtaUpdateService.CurrentVersion,
            Description = cleanDesc,
            SanitizedLogs = cleanLogs,
            Diagnostics = diagnostics,
            CreatedAt = DateTime.UtcNow.ToString("O"),
        };

        bool uploaded = await TrySendBugToCloudAsync(settings, entry, ct);
        if (!uploaded)
        {
            lock (_lock)
            {
                _bugOutbox.Add(entry);
                SaveBugOutboxLocked();
            }
            return new
            {
                ok = true,
                uploaded = false,
                queuedOffline = true,
                pendingCount = _bugOutbox.Count,
                reportId = entry.Id,
                message = "익명화된 로그를 포함하여 로컬 대기열에 저장했습니다. 온라인 연결 시 자동 전송됩니다.",
            };
        }

        return new
        {
            ok = true,
            uploaded = true,
            queuedOffline = false,
            pendingCount = _bugOutbox.Count,
            reportId = entry.Id,
            message = "익명화된 로그 및 진단 데이터와 함께 버그 리포트가 서버로 전송되었습니다.",
        };
    }

    public async Task<int> FlushPendingBugReportsAsync(AppSettings settings, CancellationToken ct = default)
    {
        QueuedBugReportDto[] snapshot;
        lock (_lock)
        {
            if (_bugOutbox.Count == 0) return 0;
            snapshot = _bugOutbox.ToArray();
        }

        int sent = 0;
        foreach (var entry in snapshot)
        {
            if (await TrySendBugToCloudAsync(settings, entry, ct))
            {
                sent++;
                lock (_lock)
                {
                    _bugOutbox.RemoveAll(b => b.Id == entry.Id);
                    SaveBugOutboxLocked();
                }
            }
            else
            {
                break;
            }
        }
        return sent;
    }

    private async Task<bool> TrySendBugToCloudAsync(AppSettings settings, QueuedBugReportDto entry, CancellationToken ct)
    {
        string baseUrl = ResolveCloudBaseUrl(settings);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) return false;

        try
        {
            string? token;
            lock (_lock) { token = _session?.AccessToken; }
            if (!string.IsNullOrWhiteSpace(token) && IsAccessTokenExpiringSoon(token, TimeSpan.FromMinutes(2)))
            {
                var outcome = await TryRefreshSessionAsync(settings, token, ct);
                if (outcome == RefreshOutcome.Refreshed)
                {
                    lock (_lock) { token = _session?.AccessToken; }
                }
                else if (outcome == RefreshOutcome.SessionExpired)
                {
                    token = null;
                }
            }

            using var req = BuildAuthRequest(HttpMethod.Post, $"{baseUrl}/api/bugs", token, new
            {
                title = entry.Title,
                category = entry.Category,
                severity = entry.Severity,
                appVersion = entry.AppVersion,
                description = entry.Description,
                sanitizedLogs = entry.SanitizedLogs,
                diagnostics = entry.Diagnostics,
            });
            using var resp = await SharedHttp.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<JsonElement> GetAdminOverviewAsync(AppSettings settings, CancellationToken ct = default)
    {
        var (baseUrl, session) = RequireAuthenticatedEndpoint(settings);
        if (!session.User.IsAdmin)
            throw new InvalidOperationException("관리자(Admin) 권한이 있는 Google 계정으로만 열람할 수 있습니다.");

        using var resp = await SendAuthenticatedAsync(
            settings,
            token => BuildAuthRequest(HttpMethod.Get, $"{baseUrl}/api/admin/overview", token),
            ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"어드민 통계 조회 실패 ({(int)resp.StatusCode}): {LogSanitizer.Scrub(json)}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<object> UpdateAdminBugStatusAsync(AppSettings settings, string id, string status, CancellationToken ct = default)
    {
        var (baseUrl, session) = RequireAuthenticatedEndpoint(settings);
        if (!session.User.IsAdmin) throw new InvalidOperationException("Admin privilege required.");

        using var resp = await SendAuthenticatedAsync(
            settings,
            token => BuildAuthRequest(HttpMethod.Patch, $"{baseUrl}/api/admin/bugs", token, new { id, status }),
            ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"버그 상태 변경 실패 ({(int)resp.StatusCode}): {LogSanitizer.Scrub(raw)}");

        return new { ok = true, id, status };
    }

    public async Task<object> UpdateAdminReleasePolicyAsync(AppSettings settings, JsonElement payload, CancellationToken ct = default)
    {
        var (baseUrl, session) = RequireAuthenticatedEndpoint(settings);
        if (!session.User.IsAdmin) throw new InvalidOperationException("Admin privilege required.");

        using var resp = await SendAuthenticatedAsync(
            settings,
            token => BuildAuthRequest(HttpMethod.Post, $"{baseUrl}/api/admin/releases", token, payload),
            ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OTA 정책 저장 실패 ({(int)resp.StatusCode}): {LogSanitizer.Scrub(raw)}");

        return new { ok = true };
    }

    public async Task EnsureFreshSessionAsync(AppSettings settings, CancellationToken ct = default)
    {
        try
        {
            string? token;
            lock (_lock) { token = _session?.AccessToken; }
            if (string.IsNullOrWhiteSpace(token) || !IsAccessTokenExpiringSoon(token, TimeSpan.FromMinutes(5)))
                return;

            await TryRefreshSessionAsync(settings, token, ct);
        }
        catch
        {
            // Non-fatal background check
        }
    }

    public static bool IsAccessTokenExpiringSoon(string? jwt, TimeSpan leeway, DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return false;
        string[] parts = jwt.Split('.');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1])) return false;

        try
        {
            string padded = parts[1].Replace('-', '+').Replace('_', '/');
            int mod = padded.Length % 4;
            if (mod == 1) return false;
            if (mod > 0) padded += new string('=', 4 - mod);

            byte[] bytes = Convert.FromBase64String(padded);
            using var doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("exp", out var expEl) || !expEl.TryGetInt64(out long expUnix))
                return false;

            DateTimeOffset now = utcNow ?? DateTimeOffset.UtcNow;
            return DateTimeOffset.FromUnixTimeSeconds(expUnix) <= now + leeway;
        }
        catch
        {
            return false;
        }
    }

    private void ClearSessionAndNotify()
    {
        lock (_lock)
        {
            _session = null;
            try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
        }
        SessionStateChanged?.Invoke();
    }

    private async Task<RefreshOutcome> TryRefreshSessionAsync(AppSettings settings, string staleAccessToken, CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            string refreshToken;
            lock (_lock)
            {
                if (_session == null)
                    return RefreshOutcome.SessionExpired;

                if (!string.Equals(_session.AccessToken, staleAccessToken, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(_session.AccessToken) &&
                    !IsAccessTokenExpiringSoon(_session.AccessToken, TimeSpan.FromMinutes(1)))
                {
                    return RefreshOutcome.Refreshed;
                }

                refreshToken = _session.RefreshToken ?? "";
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                ClearSessionAndNotify();
                return RefreshOutcome.SessionExpired;
            }

            string baseUrl = ResolveCloudBaseUrl(settings);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
                return RefreshOutcome.TransientError;

            HttpResponseMessage resp;
            try
            {
                using var req = BuildAuthRequest(HttpMethod.Post, $"{baseUrl}/api/auth/refresh", null, new { refreshToken });
                resp = await SharedHttp.SendAsync(req, ct);
            }
            catch (HttpRequestException)
            {
                return RefreshOutcome.TransientError;
            }
            catch (TaskCanceledException)
            {
                return RefreshOutcome.TransientError;
            }

            using (resp)
            {
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
                {
                    ClearSessionAndNotify();
                    return RefreshOutcome.SessionExpired;
                }

                if (!resp.IsSuccessStatusCode)
                    return RefreshOutcome.TransientError;

                string body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string newAccess = root.TryGetProperty("accessToken", out var atEl) && atEl.ValueKind == JsonValueKind.String
                    ? atEl.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(newAccess))
                    return RefreshOutcome.TransientError;

                string newRefresh = root.TryGetProperty("refreshToken", out var rtEl) && rtEl.ValueKind == JsonValueKind.String
                    ? rtEl.GetString() ?? ""
                    : "";

                CloudUserDto? updatedUser = null;
                if (root.TryGetProperty("user", out var uEl) && uEl.ValueKind == JsonValueKind.Object)
                    updatedUser = JsonSerializer.Deserialize<CloudUserDto>(uEl.GetRawText(), JsonOpts);

                lock (_lock)
                {
                    if (_session == null)
                        return RefreshOutcome.SessionExpired;

                    _session.AccessToken = newAccess;
                    if (!string.IsNullOrWhiteSpace(newRefresh))
                        _session.RefreshToken = newRefresh;
                    if (updatedUser != null)
                    {
                        if (!string.IsNullOrWhiteSpace(updatedUser.Id)) _session.User.Id = updatedUser.Id;
                        if (!string.IsNullOrWhiteSpace(updatedUser.Email)) _session.User.Email = updatedUser.Email;
                        if (!string.IsNullOrWhiteSpace(updatedUser.DisplayName)) _session.User.DisplayName = updatedUser.DisplayName;
                        if (!string.IsNullOrWhiteSpace(updatedUser.AvatarUrl)) _session.User.AvatarUrl = updatedUser.AvatarUrl;
                        _session.User.IsAdmin = updatedUser.IsAdmin;
                    }
                    SaveSessionToDiskLocked();
                }
            }

            SessionStateChanged?.Invoke();
            return RefreshOutcome.Refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        AppSettings settings,
        Func<string, HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        var (_, session) = RequireAuthenticatedEndpoint(settings);
        string token = session.AccessToken;

        if (IsAccessTokenExpiringSoon(token, TimeSpan.FromMinutes(2)))
        {
            var proactive = await TryRefreshSessionAsync(settings, token, ct);
            if (proactive == RefreshOutcome.SessionExpired)
                throw new InvalidOperationException("Google 로그인 세션이 만료되었습니다. 상단 계정 메뉴에서 다시 로그인해 주세요.");

            if (proactive == RefreshOutcome.Refreshed)
            {
                lock (_lock)
                {
                    if (_session == null || string.IsNullOrWhiteSpace(_session.AccessToken))
                        throw new InvalidOperationException("Google 로그인 세션이 만료되었습니다. 상단 계정 메뉴에서 다시 로그인해 주세요.");
                    token = _session.AccessToken;
                }
            }
        }

        HttpResponseMessage resp;
        using (var req1 = requestFactory(token))
        {
            resp = await SharedHttp.SendAsync(req1, ct);
        }

        if (resp.StatusCode != HttpStatusCode.Unauthorized)
            return resp;

        resp.Dispose();
        var reactive = await TryRefreshSessionAsync(settings, token, ct);
        if (reactive == RefreshOutcome.Refreshed)
        {
            string refreshedToken;
            lock (_lock)
            {
                if (_session == null || string.IsNullOrWhiteSpace(_session.AccessToken))
                    throw new InvalidOperationException("Google 로그인 세션이 만료되었습니다. 상단 계정 메뉴에서 다시 로그인해 주세요.");
                refreshedToken = _session.AccessToken;
            }
            using var req2 = requestFactory(refreshedToken);
            return await SharedHttp.SendAsync(req2, ct);
        }

        if (reactive == RefreshOutcome.SessionExpired)
            throw new InvalidOperationException("Google 로그인 세션이 만료되었습니다. 상단 계정 메뉴에서 다시 로그인해 주세요.");

        throw new InvalidOperationException("인증 세션을 갱신하지 못했습니다. 네트워크 상태를 확인하거나 상단 계정 메뉴에서 다시 로그인해 주세요.");
    }

    private (string BaseUrl, CloudAuthSession Session) RequireAuthenticatedEndpoint(AppSettings settings)
    {
        string baseUrl = ResolveCloudBaseUrl(settings);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("클라우드 API URL이 설정되지 않았습니다.");

        lock (_lock)
        {
            if (_session == null || string.IsNullOrWhiteSpace(_session.AccessToken))
                throw new InvalidOperationException("Google 로그인이 필요합니다.");
            return (baseUrl, _session);
        }
    }

    private static HttpRequestMessage BuildAuthRequest(HttpMethod method, string url, string? bearerToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (body != null)
        {
            string json = JsonSerializer.Serialize(body, JsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return req;
    }

    private static int FindFreeTcpPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    // Encrypted token persistence without requiring Windows-only ProtectedData on net10.0
    private static byte[] DeriveLocalKey()
    {
        string material = $"{Environment.UserDomainName}|{Environment.UserName}|NeuralRigStudio|CloudAuth|v3";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    private void SaveSessionToDiskLocked()
    {
        try
        {
            if (_session == null) return;
            Directory.CreateDirectory(AppDataDir);
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(_session, JsonOpts);
            using var aes = Aes.Create();
            aes.Key = DeriveLocalKey();
            aes.GenerateIV();
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plain, 0, plain.Length);
            }
            File.WriteAllBytes(SessionPath, ms.ToArray());
        }
        catch
        {
            // Non-fatal
        }
    }

    private void LoadSessionFromDisk()
    {
        try
        {
            if (!File.Exists(SessionPath)) return;
            byte[] blob = File.ReadAllBytes(SessionPath);
            if (blob.Length <= 16) return;
            using var aes = Aes.Create();
            aes.Key = DeriveLocalKey();
            byte[] iv = new byte[16];
            Array.Copy(blob, 0, iv, 0, 16);
            aes.IV = iv;
            using var ms = new MemoryStream(blob, 16, blob.Length - 16);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var outMs = new MemoryStream();
            cs.CopyTo(outMs);
            _session = JsonSerializer.Deserialize<CloudAuthSession>(outMs.ToArray(), JsonOpts);
        }
        catch
        {
            _session = null;
        }
    }

    private void SaveBugOutboxLocked()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(BugOutboxPath, JsonSerializer.Serialize(_bugOutbox, PrettyJsonOpts));
        }
        catch { }
    }

    private void LoadBugOutboxFromDisk()
    {
        try
        {
            if (!File.Exists(BugOutboxPath)) return;
            var list = JsonSerializer.Deserialize<List<QueuedBugReportDto>>(File.ReadAllText(BugOutboxPath), JsonOpts);
            if (list != null) _bugOutbox.AddRange(list);
        }
        catch { }
    }
}
