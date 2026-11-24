using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Services;

namespace NeuralRigStudio;

public sealed class MainForm : Form
{
    private readonly WebView2 _web = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(213, 220, 217),
    };

    private readonly AudioRouter _router = new();
    private readonly VisualizerAnalyzer _visualizer = new();
    private readonly AppSettings _settings;
    private readonly RigWebBridge _bridge;
    private readonly System.Windows.Forms.Timer _telemetryTimer = new() { Interval = 20 }; // ~50fps

    private bool _webReady;

    public MainForm()
    {
        Text = "NeuralRig Studio";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1160, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(213, 220, 217);

        _settings = AppSettings.Load();
        _bridge = new RigWebBridge(
            this,
            _router,
            _visualizer,
            _settings,
            SendWebMessageJson);

        Controls.Add(_web);

        Load += async (_, _) => await InitializeStudioAsync();
        FormClosing += (_, _) => Shutdown();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowTheme("#d5dcd9", "#bbc4c0", "#0b0e10", isDark: false);
    }

    public void ApplyWindowTheme(string bgHex, string borderHex, string textHex, bool isDark)
    {
        try
        {
            Color bgCol = ColorTranslator.FromHtml(bgHex);
            Color borderCol = ColorTranslator.FromHtml(borderHex);
            Color textCol = ColorTranslator.FromHtml(textHex);

            BackColor = bgCol;
            _web.DefaultBackgroundColor = bgCol;

            if (!IsHandleCreated) return;
            int darkFlag = isDark ? 1 : 0;
            int bgrBg = (bgCol.B << 16) | (bgCol.G << 8) | bgCol.R;
            int bgrBorder = (borderCol.B << 16) | (borderCol.G << 8) | borderCol.R;
            int bgrText = (textCol.B << 16) | (textCol.G << 8) | textCol.R;

            DwmSetWindowAttribute(Handle, 20, ref darkFlag, sizeof(int));
            DwmSetWindowAttribute(Handle, 34, ref bgrBorder, sizeof(int));
            DwmSetWindowAttribute(Handle, 35, ref bgrBg, sizeof(int));
            DwmSetWindowAttribute(Handle, 36, ref bgrText, sizeof(int));
        }
        catch
        {
            // Ignore DWM calls on unsupported Windows builds
        }
    }

    private async Task InitializeStudioAsync()
    {
        string userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeuralRigStudio", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            {
                bool allowed =
                    uri.Host.Equals("app.local", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
                if (!allowed)
                    e.Cancel = true;
            }
        };

        core.WebMessageReceived += (_, e) =>
        {
            _bridge.HandleWebMessage(e.WebMessageAsJson);
        };

        string? devUrl = Environment.GetEnvironmentVariable("NEURALRIG_DEV_URL");
        if (!string.IsNullOrWhiteSpace(devUrl))
        {
            _web.Source = new Uri(devUrl);
        }
        else
        {
            string wwwroot = ResolveWwwRoot();
            core.SetVirtualHostNameToFolderMapping(
                "app.local",
                wwwroot,
                CoreWebView2HostResourceAccessKind.Allow);
            _web.Source = new Uri("https://app.local/index.html");
        }

        _webReady = true;
        _telemetryTimer.Tick += (_, _) =>
        {
            if (_webReady && !IsDisposed)
                _bridge.EmitTelemetryTick();
        };
        _telemetryTimer.Start();
    }

    private static string ResolveWwwRoot()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot")),
            Path.Combine(Environment.CurrentDirectory, "wwwroot"),
        };
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "index.html")))
                return c;

        using var bundleStream = typeof(MainForm).Assembly.GetManifestResourceStream("NeuralRigStudio.wwwroot.zip");
        if (bundleStream != null)
        {
            string ver = typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "3.0.0";
            string targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NeuralRigStudio",
                "wwwroot_bundle",
                $"v{ver}_{bundleStream.Length}");

            if (!File.Exists(Path.Combine(targetDir, "index.html")))
            {
                string tmpDir = targetDir + "_tmp_" + Guid.NewGuid().ToString("N")[..8];
                Directory.CreateDirectory(tmpDir);
                ZipFile.ExtractToDirectory(bundleStream, tmpDir, overwriteFiles: true);
                Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
                try
                {
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                    Directory.Move(tmpDir, targetDir);
                }
                catch
                {
                    if (File.Exists(Path.Combine(targetDir, "index.html")))
                    {
                        try { Directory.Delete(tmpDir, true); } catch { }
                    }
                    else
                    {
                        targetDir = tmpDir;
                    }
                }
            }
            if (File.Exists(Path.Combine(targetDir, "index.html")))
                return targetDir;
        }

        Directory.CreateDirectory(candidates[0]);
        return candidates[0];
    }

    private void SendWebMessageJson(string json)
    {
        if (!_webReady || IsDisposed || _web.CoreWebView2 is null) return;
        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch
        {
            // 창 닫힘 레이스 무시
        }
    }

    private void Shutdown()
    {
        _webReady = false;
        _telemetryTimer.Stop();
        try { _router.Stop("앱 종료"); } catch { }
        try { _bridge.Dispose(); } catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
