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
        DefaultBackgroundColor = Color.FromArgb(9, 9, 11),
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
        BackColor = Color.FromArgb(9, 9, 11);

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
        EnableDarkTitlebar(Handle);
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

    private static void EnableDarkTitlebar(IntPtr hwnd)
    {
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        int bg = 0x000b0909;     // #09090b (BGR)
        int border = 0x002b2626; // #26262b
        int text = 0x00fafafa;   // #fafafa
        DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        DwmSetWindowAttribute(hwnd, 35, ref bg, sizeof(int));
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
    }
}
