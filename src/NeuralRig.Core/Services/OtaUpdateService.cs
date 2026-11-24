using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuralRigStudio.Services;

public sealed class OtaCheckResultDto
{
    public bool Checked { get; set; }
    public string CurrentVersion { get; set; } = OtaUpdateService.CurrentVersion;
    public string LatestVersion { get; set; } = OtaUpdateService.CurrentVersion;
    public string MinSupportedVersion { get; set; } = OtaUpdateService.CurrentVersion;
    public bool UpdateAvailable { get; set; }
    public bool ForceUpdate { get; set; }
    public string ReleaseNotesKo { get; set; } = "";
    public string ReleaseNotesEn { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string StandaloneExeUrl { get; set; } = "";
    public string Sha256Installer { get; set; } = "";
    public string ReleasePageUrl { get; set; } = $"https://github.com/{OtaUpdateService.GitHubRepo}/releases/latest";
    public string Source { get; set; } = "baseline";
    public string? Error { get; set; }
}

/// <summary>
/// GitHub Releases + Next.js Cloud OTA policy client supporting both optional (choice)
/// updates and mandatory blocking updates based on minSupportedVersion / forceUpdate flag.
/// </summary>
public static partial class OtaUpdateService
{
    public const string CurrentVersion = "3.0.1";
    public const string GitHubRepo = "srain0626/NeuralRigStudio";

    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [GeneratedRegex(@"min_supported_version:\s*v?([0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MinSupportedRegex();

    static OtaUpdateService()
    {
        if (!SharedHttp.DefaultRequestHeaders.UserAgent.Any())
            SharedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("NeuralRigStudio/3.0.1");
    }

    public static int CompareSemVer(string? a, string? b)
    {
        static int[] ParseParts(string? raw)
        {
            string s = (raw ?? "0.0.0").Trim().TrimStart('v', 'V');
            int dash = s.IndexOf('-');
            if (dash >= 0) s = s[..dash];
            var segs = s.Split('.');
            var res = new int[Math.Max(3, segs.Length)];
            for (int i = 0; i < segs.Length; i++)
                int.TryParse(segs[i], out res[i]);
            return res;
        }

        var pa = ParseParts(a);
        var pb = ParseParts(b);
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int va = i < pa.Length ? pa[i] : 0;
            int vb = i < pb.Length ? pb[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }

    public static (bool UpdateAvailable, bool ForceUpdate) EvaluateReleasePolicy(
        string currentVersion,
        string latestVersion,
        string? minSupportedVersion,
        bool forceFlag)
    {
        bool available = CompareSemVer(latestVersion, currentVersion) > 0;
        if (!available) return (false, false);

        bool belowMin = !string.IsNullOrWhiteSpace(minSupportedVersion) &&
                        CompareSemVer(currentVersion, minSupportedVersion) < 0;
        return (true, forceFlag || belowMin);
    }

    public static async Task<OtaCheckResultDto> CheckForUpdateAsync(
        string? cloudBaseUrl,
        string? currentVersionOverride = null,
        CancellationToken ct = default)
    {
        string curVer = string.IsNullOrWhiteSpace(currentVersionOverride)
            ? CurrentVersion
            : currentVersionOverride.Trim();

        // 1) Try Cloud OTA policy endpoint first when configured
        if (!string.IsNullOrWhiteSpace(cloudBaseUrl) &&
            Uri.TryCreate(cloudBaseUrl.Trim().TrimEnd('/') + $"/api/ota/check?currentVersion={Uri.EscapeDataString(curVer)}",
                UriKind.Absolute, out var cloudUri))
        {
            try
            {
                using var resp = await SharedHttp.GetAsync(cloudUri, ct);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync(ct);
                    var dto = JsonSerializer.Deserialize<OtaCheckResultDto>(json, JsonOpts);
                    if (dto != null)
                    {
                        var (avail, force) = EvaluateReleasePolicy(curVer, dto.LatestVersion, dto.MinSupportedVersion, dto.ForceUpdate);
                        dto.CurrentVersion = curVer;
                        dto.UpdateAvailable = avail;
                        dto.ForceUpdate = force;
                        dto.Checked = true;
                        return dto;
                    }
                }
            }
            catch
            {
                // Fall back to direct GitHub Releases check
            }
        }

        // 2) Direct GitHub Releases fallback
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await SharedHttp.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new OtaCheckResultDto
                {
                    Checked = true,
                    CurrentVersion = curVer,
                    LatestVersion = curVer,
                    Source = "offline",
                    Error = $"HTTP {(int)resp.StatusCode}",
                };
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? curVer) : curVer;
            string latestVer = tag.Trim().TrimStart('v', 'V');
            string body = root.TryGetProperty("body", out var bodyEl) ? (bodyEl.GetString() ?? "") : "";
            string htmlUrl = root.TryGetProperty("html_url", out var htmlEl)
                ? (htmlEl.GetString() ?? $"https://github.com/{GitHubRepo}/releases/latest")
                : $"https://github.com/{GitHubRepo}/releases/latest";

            var minMatch = MinSupportedRegex().Match(body);
            string minVer = minMatch.Success ? minMatch.Groups[1].Value : curVer;
            bool forceMarker = body.Contains("[FORCE_UPDATE]", StringComparison.OrdinalIgnoreCase);
            var (updateAvail, forceUpdate) = EvaluateReleasePolicy(curVer, latestVer, minVer, forceMarker);

            string setupUrl = "";
            string standaloneUrl = "";
            if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assetsEl.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                    string dl = a.TryGetProperty("browser_download_url", out var dEl) ? (dEl.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(dl)) continue;
                    if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        setupUrl = dl;
                    else if (name.Contains("standalone", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        standaloneUrl = dl;
                }
            }

            return new OtaCheckResultDto
            {
                Checked = true,
                CurrentVersion = curVer,
                LatestVersion = latestVer,
                MinSupportedVersion = minVer,
                UpdateAvailable = updateAvail,
                ForceUpdate = forceUpdate,
                ReleaseNotesKo = body.Length > 1400 ? body[..1400] : body,
                ReleaseNotesEn = body.Length > 1400 ? body[..1400] : body,
                InstallerUrl = setupUrl,
                StandaloneExeUrl = standaloneUrl,
                ReleasePageUrl = htmlUrl,
                Source = "github-direct",
            };
        }
        catch (Exception ex)
        {
            return new OtaCheckResultDto
            {
                Checked = true,
                CurrentVersion = curVer,
                LatestVersion = curVer,
                Source = "offline",
                Error = LogSanitizer.Scrub(ex.Message),
            };
        }
    }

    public static async Task<(bool Ok, string Message, string? LaunchedPath)> DownloadAndStageUpdateAsync(
        OtaCheckResultDto info,
        CancellationToken ct = default)
    {
        string url = !string.IsNullOrWhiteSpace(info.InstallerUrl) ? info.InstallerUrl : info.StandaloneExeUrl;
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            // No direct binary asset URL -> open release page safely in browser
            if (Uri.TryCreate(info.ReleasePageUrl, UriKind.Absolute, out var relUri) && relUri.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo { FileName = relUri.ToString(), UseShellExecute = true });
                return (true, "Opened GitHub Releases page in default browser.", relUri.ToString());
            }
            return (false, "No HTTPS download URL found for this release.", null);
        }

        string stageDir = Path.Combine(Path.GetTempPath(), "NeuralRigStudio_Update");
        Directory.CreateDirectory(stageDir);
        string fileName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            fileName = $"NeuralRigStudio-v{info.LatestVersion}-Setup.exe";

        string targetPath = Path.Combine(stageDir, fileName);
        byte[] bytes = await SharedHttp.GetByteArrayAsync(uri, ct);

        if (!string.IsNullOrWhiteSpace(info.Sha256Installer))
        {
            string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(actual, info.Sha256Installer.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Downloaded installer SHA-256 verification failed.");
        }

        await File.WriteAllBytesAsync(targetPath, bytes, ct);

        bool isInstaller = fileName.Contains("Setup", StringComparison.OrdinalIgnoreCase);
        Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            Arguments = isInstaller ? "--silent --relaunch" : "",
            UseShellExecute = true,
        });

        return (true, $"Update downloaded and launched ({fileName}).", targetPath);
    }
}
