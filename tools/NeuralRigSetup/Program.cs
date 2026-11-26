using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace NeuralRigSetup;

internal static class Program
{
    private const string AppName = "NeuralRig Studio";
    private const string AppVersion = "3.2.0";
    private const string Publisher = "Ray Kim (NeuralRig Audio)";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NeuralRigStudio";
    private const string InstallerPrefsKeyPath = @"Software\NeuralRigStudio\Installer";
    private const string ResourceName = "NeuralRigSetup.payload.zip";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool silent = args.Any(a =>
            a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase));
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

    private static (bool installVst3, bool installClap) LoadInstallerPrefs()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InstallerPrefsKeyPath);
            if (key == null) return (true, true);
            bool vst3 = Convert.ToInt32(key.GetValue("InstallVst3", 1)) != 0;
            bool clap = Convert.ToInt32(key.GetValue("InstallClap", 1)) != 0;
            return (vst3, clap);
        }
        catch
        {
            return (true, true);
        }
    }

    private static void SaveInstallerPrefs(bool installVst3, bool installClap)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(InstallerPrefsKeyPath);
            key?.SetValue("InstallVst3", installVst3 ? 1 : 0, RegistryValueKind.DWord);
            key?.SetValue("InstallClap", installClap ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Registry preference persistence is non-fatal
        }
    }

    private static bool ShowInstallOptionsDialog(string installDir, ref bool installVst3, ref bool installClap)
    {
        using var form = new Form
        {
            Text = $"{AppName} v{AppVersion} Setup",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(520, 290),
            Font = new Font("Segoe UI", 9.5f),
        };

        var lblHeader = new Label
        {
            Text = $"{AppName} v{AppVersion}\r\nInstall path: {installDir}",
            Location = new Point(20, 16),
            Size = new Size(480, 44),
        };

        var chkApp = new CheckBox
        {
            Text = "Desktop App & MCP Server (NeuralRigStudio.exe, NeuralRigMcp.exe)",
            Checked = true,
            Enabled = false,
            Location = new Point(22, 74),
            Size = new Size(476, 26),
        };

        var chkVst3 = new CheckBox
        {
            Text = @"VST3 Plugin (%LOCALAPPDATA%\Programs\Common\VST3\NeuralRigStudio.vst3)",
            Checked = installVst3,
            Location = new Point(22, 108),
            Size = new Size(476, 26),
        };

        var chkClap = new CheckBox
        {
            Text = @"CLAP Plugin (%LOCALAPPDATA%\Programs\Common\CLAP\NeuralRigStudio)",
            Checked = installClap,
            Location = new Point(22, 142),
            Size = new Size(476, 26),
        };

        var lblNote = new Label
        {
            Text = "Start Menu & Desktop shortcuts and Add/Remove Programs uninstall entry are included.\r\nClose open DAWs before upgrading VST3/CLAP plugins.",
            ForeColor = Color.DimGray,
            Location = new Point(20, 180),
            Size = new Size(480, 38),
        };

        var btnInstall = new Button
        {
            Text = "Install",
            DialogResult = DialogResult.OK,
            Location = new Point(316, 236),
            Size = new Size(88, 32),
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(412, 236),
            Size = new Size(88, 32),
        };

        form.Controls.AddRange(new Control[] { lblHeader, chkApp, chkVst3, chkClap, lblNote, btnInstall, btnCancel });
        form.AcceptButton = btnInstall;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() != DialogResult.OK)
            return false;

        installVst3 = chkVst3.Checked;
        installClap = chkClap.Checked;
        return true;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string targetFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string targetSub = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, targetSub);
        }
    }

    private static int RunInstall(string installDir, bool silent, bool relaunch)
    {
        var (installVst3, installClap) = LoadInstallerPrefs();
        if (!silent)
        {
            if (!ShowInstallOptionsDialog(installDir, ref installVst3, ref installClap))
                return 1;
        }
        SaveInstallerPrefs(installVst3, installClap);

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

            var pluginWarnings = new List<string>();
            string commonProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common");
            if (installVst3)
            {
                string srcVst3 = Path.Combine(installDir, "Plugins", "VST3", "NeuralRigStudio.vst3");
                if (Directory.Exists(srcVst3))
                {
                    try
                    {
                        CopyDirectoryRecursive(srcVst3, Path.Combine(commonProg, "VST3", "NeuralRigStudio.vst3"));
                    }
                    catch (Exception ex)
                    {
                        pluginWarnings.Add($"VST3 복사 보류 (DAW 실행 중 가능): {ex.Message}");
                    }
                }
            }
            if (installClap)
            {
                string srcClap = Path.Combine(installDir, "Plugins", "CLAP", "NeuralRigStudio");
                if (Directory.Exists(srcClap))
                {
                    try
                    {
                        CopyDirectoryRecursive(srcClap, Path.Combine(commonProg, "CLAP", "NeuralRigStudio"));
                    }
                    catch (Exception ex)
                    {
                        pluginWarnings.Add($"CLAP 복사 보류 (DAW 실행 중 가능): {ex.Message}");
                    }
                }
            }

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
                string warnText = pluginWarnings.Count > 0
                    ? "\n\n[참고]\n" + string.Join("\n", pluginWarnings)
                    : "";
                var launch = MessageBox.Show(
                    $"{AppName} v{AppVersion} 설치가 완료되었습니다!{warnText}\n\n지금 바로 실행하시겠습니까?",
                    $"{AppName} Setup",
                    MessageBoxButtons.YesNo,
                    pluginWarnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
        try { Registry.CurrentUser.DeleteSubKeyTree(InstallerPrefsKeyPath, throwOnMissingSubKey: false); } catch { }
        try
        {
            string commonProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common");
            string vst3Dir = Path.Combine(commonProg, "VST3", "NeuralRigStudio.vst3");
            if (Directory.Exists(vst3Dir)) Directory.Delete(vst3Dir, recursive: true);
            string clapDir = Path.Combine(commonProg, "CLAP", "NeuralRigStudio");
            if (Directory.Exists(clapDir)) Directory.Delete(clapDir, recursive: true);
        }
        catch { }
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
