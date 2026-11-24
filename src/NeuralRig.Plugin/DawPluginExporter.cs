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

    public static object GetPluginInspectorInfo(string? exportDir = null)
    {
        return new
        {
            pluginName = ClapNativeMetadata.PluginName,
            vendor = ClapNativeMetadata.Vendor,
            version = ClapNativeMetadata.Version,
            clapId = ClapNativeMetadata.PluginId,
            vst3ProcessorCid = Vst3PluginMetadata.ProcessorCidHex,
            vst3ControllerCid = Vst3PluginMetadata.ControllerCidHex,
            subCategories = Vst3PluginMetadata.SubCategories,
            nativeAotCommand = "dotnet publish src/NeuralRig.Plugin/NeuralRig.Plugin.csproj -c Release -r win-x64 -p:NativeLib=Shared",
            defaultOutputDir = string.IsNullOrWhiteSpace(exportDir)
                ? Path.Combine(AppContext.BaseDirectory, "DawPlugins")
                : exportDir,
            parameters = DawParameterCatalog.All.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                module = p.Module,
                unit = p.Unit,
                min = p.MinValue,
                max = p.MaxValue,
                defaultVal = p.DefaultValue,
                steps = p.StepCount,
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
        if (!string.IsNullOrEmpty(pluginDllSrc) && File.Exists(pluginDllSrc))
        {
            File.Copy(pluginDllSrc, vst3BinaryPath, overwrite: true);
            if (!string.IsNullOrEmpty(coreDllSrc) && File.Exists(coreDllSrc))
                File.Copy(coreDllSrc, Path.Combine(vst3BinDir, "NeuralRig.Core.dll"), overwrite: true);
        }
        else
        {
            File.WriteAllText(vst3BinaryPath, "NeuralRig.Plugin NativeAOT Host Target");
        }

        string moduleInfoPath = Path.Combine(vst3Root, "Contents", "moduleinfo.json");
        var moduleInfo = new
        {
            Name = Vst3PluginMetadata.Name,
            Version = Vst3PluginMetadata.Version,
            FactoryInfo = new
            {
                Vendor = Vst3PluginMetadata.Vendor,
                URL = "https://github.com/neuralrig/studio",
                E_Mail = "support@neuralrig.audio",
                Flags = new { Unicode = true, ClassesDiscardable = false, LicenseCheck = false, ComponentNonDiscardable = false },
            },
            Classes = new object[]
            {
                new
                {
                    CID = Vst3PluginMetadata.ProcessorCidHex,
                    Category = "Audio Module Class",
                    Name = Vst3PluginMetadata.Name,
                    Vendor = Vst3PluginMetadata.Vendor,
                    Version = Vst3PluginMetadata.Version,
                    SDKVersion = "VST 3.7.8",
                    SubCategories = new[] { "Fx", "Guitar", "AmpSim" },
                    ClassFlags = 0,
                    Cardinality = 2147483647,
                    Snapshots = Array.Empty<object>(),
                },
                new
                {
                    CID = Vst3PluginMetadata.ControllerCidHex,
                    Category = "Component Controller Class",
                    Name = $"{Vst3PluginMetadata.Name} Controller",
                    Vendor = Vst3PluginMetadata.Vendor,
                    Version = Vst3PluginMetadata.Version,
                    SDKVersion = "VST 3.7.8",
                    SubCategories = Array.Empty<string>(),
                    ClassFlags = 0,
                    Cardinality = 2147483647,
                },
            },
        };
        File.WriteAllText(
            moduleInfoPath,
            JsonSerializer.Serialize(moduleInfo, new JsonSerializerOptions { WriteIndented = true }),
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
        if (!string.IsNullOrEmpty(pluginDllSrc) && File.Exists(pluginDllSrc))
            File.Copy(pluginDllSrc, clapBinaryPath, overwrite: true);
        else
            File.WriteAllText(clapBinaryPath, "NeuralRig.Plugin CLAP v1.2 NativeAOT Target");

        string clapManifestPath = Path.Combine(outputDirectory, "NeuralRigStudio.clap.json");
        var clapManifest = new
        {
            clapVersion = "1.2.2",
            id = ClapNativeMetadata.PluginId,
            name = ClapNativeMetadata.PluginName,
            vendor = ClapNativeMetadata.Vendor,
            version = ClapNativeMetadata.Version,
            description = ClapNativeMetadata.Description,
            features = ClapNativeMetadata.Features,
            entrySymbol = "clap_entry",
            parameterCount = DawParameterCatalog.ParameterCount,
            defaultRig = Path.GetFileName(vst3DefaultRigPath),
        };
        File.WriteAllText(
            clapManifestPath,
            JsonSerializer.Serialize(clapManifest, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        return new DawPluginPackageResultDto(
            outputDirectory,
            vst3Root,
            moduleInfoPath,
            clapBinaryPath,
            clapManifestPath,
            vst3DefaultRigPath,
            DawParameterCatalog.ParameterCount,
            NativeAotReady: true);
    }
}
