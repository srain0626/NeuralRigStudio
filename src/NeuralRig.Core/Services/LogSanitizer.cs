using System.Text.RegularExpressions;

namespace NeuralRigStudio.Services;

/// <summary>
/// Scrubs personally identifiable information (OS username, machine name, local profile paths,
/// bearer tokens) from console logs, bug reports, and cloud diagnostics, and buffers recent
/// sanitized entries in memory for automatic bug-report upload.
/// </summary>
public static partial class LogSanitizer
{
    private const int MaxBufferedLines = 400;
    private static readonly object BufferLock = new();
    private static readonly Queue<string> RecentSanitizedLogs = new(MaxBufferedLines + 1);

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\/\r\n\""']+", RegexOptions.IgnoreCase)]
    private static partial Regex WinUserPathRegex();

    [GeneratedRegex(@"/(Users|home)/[^/\r\n\""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UnixHomePathRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)(apikey|api_key|secret|token|password)\s*[:=]\s*[^\s,;\""']+")]
    private static partial Regex KeyValueSecretRegex();

    public static string Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        string res = WinUserPathRegex().Replace(input, "%USERPROFILE%");
        res = UnixHomePathRegex().Replace(res, "~");
        res = BearerTokenRegex().Replace(res, "Bearer [REDACTED]");
        res = KeyValueSecretRegex().Replace(res, m => $"{m.Groups[1].Value}=[REDACTED]");

        string user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length >= 3)
            res = res.Replace(user, "[USER]", StringComparison.OrdinalIgnoreCase);

        string machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine) && machine.Length >= 3)
            res = res.Replace(machine, "[HOST]", StringComparison.OrdinalIgnoreCase);

        return res;
    }

    public static string RecordLog(string? rawLine)
    {
        string scrubbed = Scrub(rawLine);
        string entry = $"[{DateTime.UtcNow:HH:mm:ss}] {scrubbed}";
        lock (BufferLock)
        {
            while (RecentSanitizedLogs.Count >= MaxBufferedLines)
                RecentSanitizedLogs.Dequeue();
            RecentSanitizedLogs.Enqueue(entry);
        }
        return scrubbed;
    }

    public static string GetRecentLogsText(int maxLines = 250)
    {
        lock (BufferLock)
        {
            if (RecentSanitizedLogs.Count == 0) return string.Empty;
            int take = Math.Clamp(maxLines, 1, MaxBufferedLines);
            var arr = RecentSanitizedLogs.ToArray();
            int start = Math.Max(0, arr.Length - take);
            return string.Join(Environment.NewLine, arr[start..]);
        }
    }

    public static void ClearRecentLogs()
    {
        lock (BufferLock)
        {
            RecentSanitizedLogs.Clear();
        }
    }
}
