using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace NeuralRigSetup;

internal static class Program
{
    private const string AppName = "NeuralRig Studio";
    private const string AppVersion = "3.0.0";
    private const string Publisher = "Ray Kim (NeuralRig Audio)";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NeuralRigStudio";
    private const string ResourceName = "NeuralRigSetup.payload.zip";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) || a.Equals("/S", StringComparison.OrdinalIgnoreCase));
        bool relaunch = args.Any(a => a.Equals("--relaunch", StringComparison.OrdinalIgnoreCase));
        bool uninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "NeuralRigStudio");

        if (uninstall)
            return RunUninstall(installDir, silent);

        return RunInstall(installDir, silent, relaunch);
    }

    private static int RunInstall(string installDir, bool silent, bool relaunch)
    {
        if (!silent)
        {
            var confirm = MessageBox.Show(
                $"{AppName} v{AppVersion}을(를) 다음 경로에 설치합니다:\n\n{installDir}\n\n" +
                "(NeuralRigStudio.exe, NeuralRigMcp.exe, 시작 메뉴/바탕화면 바로가기 포함)\n\n계속하시겠습니까?",
                $"{AppName} Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (confirm != DialogResult.Yes) return 1;
        }

        try
        {
            // Wait briefly and terminate any active instance so file replacement succeeds cleanly during OTA
            foreach (var name in new[] { "NeuralRigStudio", "NeuralRigMcp" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(3000);
                    }
                    catch { }
                }
            }

            using var stream = typeof(Program).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                if (!silent)
                    MessageBox.Show("설치 패키지 페이로드가 포함되지 않았습니다.", $"{AppName} Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            Directory.CreateDirectory(installDir);
            ZipFile.ExtractToDirectory(stream, installDir, overwriteFiles: true);

            string mainExe = Path.Combine(installDir, "NeuralRigStudio.exe");
            string selfCopy = Path.Combine(installDir, "NeuralRigSetup.exe");
            try
            {
                string? currentProc = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(currentProc) &&
                    File.Exists(currentProc) &&
                    !string.Equals(Path.GetFullPath(currentProc), Path.GetFullPath(selfCopy), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(currentProc, selfCopy, overwrite: true);
                }
            }
            catch { }

            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                AppName);
            Directory.CreateDirectory(startMenuDir);
            CreateShortcut(Path.Combine(startMenuDir, $"{AppName}.lnk"), mainExe, installDir, "", AppName);
            CreateShortcut(Path.Combine(startMenuDir, $"Uninstall {AppName}.lnk"), selfCopy, installDir, "--uninstall", $"Uninstall {AppName}");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk"),
                mainExe,
                installDir,
                "",
                AppName);

            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                key?.SetValue("DisplayName", AppName);
                key?.SetValue("DisplayVersion", AppVersion);
                key?.SetValue("Publisher", Publisher);
                key?.SetValue("InstallLocation", installDir);
                key?.SetValue("DisplayIcon", mainExe);
                key?.SetValue("UninstallString", $"\"{selfCopy}\" --uninstall");
                key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            if (relaunch && File.Exists(mainExe))
            {
                Process.Start(new ProcessStartInfo { FileName = mainExe, UseShellExecute = true });
            }
            else if (!silent && File.Exists(mainExe))
            {
                var launch = MessageBox.Show(
                    $"{AppName} v{AppVersion} 설치가 완료되었습니다!\n\n지금 바로 실행하시겠습니까?",
                    $"{AppName} Setup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (launch == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo { FileName = mainExe, UseShellExecute = true });
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show($"설치 중 오류가 발생했습니다:\n{ex.Message}", $"{AppName} Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 3;
        }
    }

    private static int RunUninstall(string installDir, bool silent)
    {
        if (!silent)
        {
            var confirm = MessageBox.Show(
                $"{AppName}을(를) 제거하시겠습니까?\n(사용자 프리셋 %AppData%\\NeuralRigStudio 는 보존됩니다.)",
                $"{AppName} Uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return 1;
        }

        foreach (var name in new[] { "NeuralRigStudio", "NeuralRigMcp" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }

        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false); } catch { }
        try
        {
            string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");
            if (File.Exists(desk)) File.Delete(desk);
            string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            if (Directory.Exists(sm)) Directory.Delete(sm, recursive: true);
        }
        catch { }

        if (Directory.Exists(installDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{installDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }

        if (!silent)
            MessageBox.Show($"{AppName} 제거가 완료되었습니다.", $"{AppName} Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);

        return 0;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string arguments, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            object? shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut == null) return;
            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        catch
        {
            // Shortcut creation is non-fatal
        }
    }
}
