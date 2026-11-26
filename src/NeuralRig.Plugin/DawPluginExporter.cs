using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Services;

namespace NeuralRigStudio.Plugin;

public sealed record DawPluginPackageResultDto(
    string OutputDirectory,
    string Vst3BundlePath,
    string Vst3ModuleInfoPath,
    string ClapPluginPath,
    string ClapManifestPath,
    string DefaultRigPath,
    int ParameterCount,
    bool NativeAotReady);

/// <summary>
/// Steinberg VST3 번들(.vst3/Contents/x86_64-win/...) 및 CLAP(.clap) DAW 플러그인 패키지 내보내기 엔진.
/// </summary>
public static class DawPluginExporter
{
    public static string DefaultExportRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
        "NeuralRigStudio_Plugins");

    public static Vst3ModuleInfoDto CreateModuleInfo()
    {
        return new Vst3ModuleInfoDto
        {
            Name = Vst3PluginMetadata.Name,
            Version = "3.1.0",
            FactoryInfo = new Vst3FactoryInfoDto
            {
                Vendor = Vst3PluginMetadata.Vendor,
                Url = "https://neuralrig.studio",
                Email = "support@neuralrig.studio",
                Flags = new Vst3FactoryFlagsDto
                {
                    Unicode = true,
                    ClassesDiscardable = false,
                    ComponentNonDiscardable = false,
                },
            },
            Classes = new List<Vst3ClassInfoDto>
            {
                new()
                {
                    Cid = Vst3PluginMetadata.ProcessorCidHex,
                    Category = "Audio Module Class",
                    Name = Vst3PluginMetadata.Name,
                    Vendor = Vst3PluginMetadata.Vendor,
                    Version = "3.1.0",
                    SdkVersion = "VST 3.7.11",
                    SubCategories = new[] { "Fx", "Guitar", "AmpSim" },
                    ClassFlags = 2,
                    Cardinality = 2147483647,
                    Snapshots = Array.Empty<string>(),
                },
            },
        };
    }

    public static DawPluginInspectorDto GetPluginInspectorInfo(string? exportDir = null)
    {
        return new DawPluginInspectorDto
        {
            PluginName = ClapNativeMetadata.PluginName,
            Vendor = ClapNativeMetadata.Vendor,
            Version = "3.1.0",
            ClapId = ClapNativeMetadata.PluginId,
            Vst3ProcessorCid = Vst3PluginMetadata.ProcessorCidHex,
            Vst3ControllerCid = Vst3PluginMetadata.ControllerCidHex,
            SubCategories = Vst3PluginMetadata.SubCategories,
            NativeAotCommand = "powershell -ExecutionPolicy Bypass -File scripts/build-vst3.ps1",
            DefaultOutputDir = string.IsNullOrWhiteSpace(exportDir)
                ? Path.Combine(AppContext.BaseDirectory, "DawPlugins")
                : exportDir,
            Parameters = DawParameterCatalog.All.Select(p => new DawPluginParamSummaryDto
            {
                Id = p.Id,
                Name = p.Name,
                Module = p.Module,
                Unit = p.Unit,
                Min = p.MinValue,
                Max = p.MaxValue,
                DefaultVal = p.DefaultValue,
                Steps = p.StepCount,
            }).ToArray(),
        };
    }

    public static DawPluginPackageResultDto ExportPluginPackage(
        string outputDirectory,
        SignalChainConfigDto? defaultChain = null,
        ToneProcessor? toneProc = null,
        IEnumerable<ToneProfile>? modelLibrary = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            outputDirectory = Path.Combine(AppContext.BaseDirectory, "DawPlugins");

        Directory.CreateDirectory(outputDirectory);
        string pluginDllSrc = Path.Combine(AppContext.BaseDirectory, "NeuralRig.Plugin.dll");
        string coreDllSrc = Path.Combine(AppContext.BaseDirectory, "NeuralRig.Core.dll");

        // 1) Steinberg VST3 Standard Bundle Structure
        string vst3Root = Path.Combine(outputDirectory, "NeuralRigStudio.vst3");
        string vst3BinDir = Path.Combine(vst3Root, "Contents", "x86_64-win");
        string vst3ResDir = Path.Combine(vst3Root, "Contents", "Resources");
        Directory.CreateDirectory(vst3BinDir);
        Directory.CreateDirectory(vst3ResDir);

        string vst3BinaryPath = Path.Combine(vst3BinDir, "NeuralRigStudio.vst3");
        string? nativeRuntimeRoot = ResolveNativeVst3RuntimeRoot();
        bool hasRealNativeVst3 = false;

        if (!string.IsNullOrEmpty(nativeRuntimeRoot))
        {
            string srcVst3 = Path.Combine(nativeRuntimeRoot, "x86_64-win", "NeuralRigStudio.vst3");
            string srcNativeCore = Path.Combine(nativeRuntimeRoot, "x86_64-win", "NeuralRig.NativeCore.dll");
            if (File.Exists(srcVst3) && File.Exists(srcNativeCore))
            {
                File.Copy(srcVst3, vst3BinaryPath, overwrite: true);
                File.Copy(srcNativeCore, Path.Combine(vst3BinDir, "NeuralRig.NativeCore.dll"), overwrite: true);
                hasRealNativeVst3 = true;

                string srcWwwroot = Path.Combine(nativeRuntimeRoot, "Resources", "wwwroot");
                if (Directory.Exists(srcWwwroot))
                    CopyDirectoryRecursive(srcWwwroot, Path.Combine(vst3ResDir, "wwwroot"));
            }
        }

        if (!hasRealNativeVst3)
        {
            if (File.Exists(pluginDllSrc))
            {
                File.Copy(pluginDllSrc, vst3BinaryPath, overwrite: true);
                if (File.Exists(coreDllSrc))
                    File.Copy(coreDllSrc, Path.Combine(vst3BinDir, "NeuralRig.Core.dll"), overwrite: true);
            }
            else
            {
                File.WriteAllText(vst3BinaryPath, "NeuralRig.Plugin NativeAOT Host Target");
            }
        }

        string moduleInfoPath = Path.Combine(vst3Root, "Contents", "moduleinfo.json");
        File.WriteAllText(
            moduleInfoPath,
            JsonSerializer.Serialize(CreateModuleInfo(), Vst3ModuleInfoJsonContext.Default.Vst3ModuleInfoDto),
            Encoding.UTF8);

        string vst3DefaultRigPath = Path.Combine(vst3ResDir, "FactoryDefault.nrig");
        var chain = (defaultChain ?? new SignalChainConfigDto()).Clone();
        NrigBundleService.ExportBundle(
            vst3DefaultRigPath,
            chain,
            toneProc,
            modelLibrary,
            author: Vst3PluginMetadata.Vendor,
            description: "Default Factory .nrig embedded for DAW VST3/CLAP initialization");

        // 2) CLAP v1.2 Plugin Package
        string clapBinaryPath = Path.Combine(outputDirectory, "NeuralRigStudio.clap");
        if (File.Exists(pluginDllSrc))
            File.Copy(pluginDllSrc, clapBinaryPath, overwrite: true);
        else
            File.WriteAllText(clapBinaryPath, "NeuralRig.Plugin CLAP v1.2 NativeAOT Target");

        string clapManifestPath = Path.Combine(outputDirectory, "NeuralRigStudio.clap.json");
        var clapManifest = new ClapManifestDto
        {
            Schema = "clap-v1.2.2",
            PluginId = ClapNativeMetadata.PluginId,
            PluginName = ClapNativeMetadata.PluginName,
            Vendor = ClapNativeMetadata.Vendor,
            Version = ClapNativeMetadata.Version,
            Description = ClapNativeMetadata.Description,
            Features = ClapNativeMetadata.Features,
            DefaultRigFile = Path.GetFileName(vst3DefaultRigPath),
            ParameterCount = DawParameterCatalog.ParameterCount,
        };
        File.WriteAllText(
            clapManifestPath,
            JsonSerializer.Serialize(clapManifest, PluginIndentedJsonContext.Default.ClapManifestDto),
            Encoding.UTF8);

        return new DawPluginPackageResultDto(
            outputDirectory,
            vst3Root,
            moduleInfoPath,
            clapBinaryPath,
            clapManifestPath,
            vst3DefaultRigPath,
            DawParameterCatalog.ParameterCount,
            NativeAotReady: hasRealNativeVst3 || File.Exists(pluginDllSrc));
    }

    private static string? ResolveNativeVst3RuntimeRoot()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "Vst3Runtime", "Contents"),
            Path.Combine(baseDir, "NeuralRigStudio.vst3", "Contents"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "dist-vst3", "NeuralRigStudio.vst3", "Contents")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "dist-vst3", "NeuralRigStudio.vst3", "Contents")),
        };
        foreach (string dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "x86_64-win", "NeuralRigStudio.vst3")) &&
                File.Exists(Path.Combine(dir, "x86_64-win", "NeuralRig.NativeCore.dll")))
            {
                return dir;
            }
        }
        return null;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (string sub in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(sub, Path.Combine(destDir, Path.GetFileName(sub)));
    }
}
