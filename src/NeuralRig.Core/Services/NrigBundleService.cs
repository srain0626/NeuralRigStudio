using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Serialization;

namespace NeuralRigStudio.Services;

public sealed class NrigNamEntryDto
{
    public string OriginalModelId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Architecture { get; set; } = "WaveNet";
    public string ArchPreset { get; set; } = "custom";
    public int SampleRate { get; set; } = 44100;
    public double Esr { get; set; }
    public string EntryPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int[] BoundSlots { get; set; } = Array.Empty<int>();
}

public sealed class NrigCabIrEntryDto
{
    public string Name { get; set; } = "";
    public int SampleRate { get; set; } = 48000;
    public int Taps { get; set; }
    public string EntryPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class NrigManifestDto
{
    public string FormatVersion { get; set; } = "2.0";
    public string BundleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "NeuralRig User";
    public string Description { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string CreatedAt { get; set; } = "";
    public int SampleRate { get; set; } = 48000;
    public SignalChainConfigDto SignalChain { get; set; } = new();
    public List<NrigNamEntryDto> NamModels { get; set; } = new();
    public NrigCabIrEntryDto? CustomCabIr { get; set; }
    public GuitarProfileDto? GuitarProfile { get; set; }
}

public sealed record NrigImportResultDto(
    NrigManifestDto Manifest,
    SignalChainConfigDto SignalChain,
    List<ToneProfile> ImportedModels,
    string? CustomCabIrPath,
    string CustomCabIrName,
    GuitarProfileDto? GuitarProfile);

/// <summary>
/// NeuralRig 통합 패키지 포맷(.nrig) 서비스 (Milestone 5 / v2.0).
/// 프리셋(16슬롯 + 8씬 + 듀얼 가상 마이킹) + 바인딩된 모든 .nam 가중치 + 커스텀 Cab IR WAV + 기타 프로필을
/// SHA-256 무결성 검증이 포함된 단일 ZIP 아카이브(.nrig)로 내보내고 임포트한다.
/// </summary>
public static class NrigBundleService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultIrsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NeuralRigStudio", "irs");

    public static NrigManifestDto ExportBundle(
        string destinationNrigPath,
        SignalChainConfigDto chain,
        ToneProcessor? toneProc = null,
        IEnumerable<ToneProfile>? modelLibrary = null,
        GuitarProfileDto? guitarProfile = null,
        string? author = null,
        string? description = null,
        string[]? tags = null,
        int sampleRate = 48000)
    {
        if (string.IsNullOrWhiteSpace(destinationNrigPath))
            throw new ArgumentException("저장할 .nrig 경로가 비어 있습니다.", nameof(destinationNrigPath));

        string? dir = Path.GetDirectoryName(destinationNrigPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var (bytes, manifest) = ExportBundleToBytes(
            chain, toneProc, modelLibrary, guitarProfile, author, description, tags, sampleRate);
        File.WriteAllBytes(destinationNrigPath, bytes);
        return manifest;
    }

    public static (byte[] bytes, NrigManifestDto manifest) ExportBundleToBytes(
        SignalChainConfigDto chain,
        ToneProcessor? toneProc = null,
        IEnumerable<ToneProfile>? modelLibrary = null,
        GuitarProfileDto? guitarProfile = null,
        string? author = null,
        string? description = null,
        string[]? tags = null,
        int sampleRate = 48000)
    {
        var cleanChain = (chain ?? new SignalChainConfigDto()).Clone();
        var lookup = BuildModelLookup(toneProc, modelLibrary);

        // 슬롯별 nam_capture 바인딩 수집
        var modelSlotMap = new Dictionary<ToneProfile, List<int>>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < cleanChain.Slots.Count; i++)
        {
            var sl = cleanChain.Slots[i];
            if (!string.Equals(sl.ModelId, "nam_capture", StringComparison.OrdinalIgnoreCase))
                continue;

            ToneProfile? prof = null;
            if (!string.IsNullOrWhiteSpace(sl.NamModelId))
                lookup.TryGetValue(sl.NamModelId, out prof);
            prof ??= toneProc?.ActiveProfile;
            if (prof is null || prof.Weights.Length == 0)
                continue;

            if (!modelSlotMap.TryGetValue(prof, out var slots))
            {
                slots = new List<int>();
                modelSlotMap[prof] = slots;
            }
            slots.Add(i);
        }

        using var ms = new MemoryStream(65536);
        NrigManifestDto manifest;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var namEntries = new List<NrigNamEntryDto>();
            int modelIdx = 0;
            foreach (var kv in modelSlotMap)
            {
                var prof = kv.Key;
                var boundSlots = kv.Value.ToArray();
                string namJson = ToneProfileStore.SerializeNamJson(prof);
                byte[] namBytes = Encoding.UTF8.GetBytes(namJson);
                string hash = ComputeSha256Hex(namBytes);
                string safeName = SanitizeFileName(prof.Name, $"Model_{modelIdx}");
                string entryPath = $"models/{modelIdx:D2}_{safeName}.nam";

                WriteZipEntryBytes(zip, entryPath, namBytes);
                namEntries.Add(new NrigNamEntryDto
                {
                    OriginalModelId = prof.Id,
                    Name = prof.Name,
                    Architecture = prof.Architecture,
                    ArchPreset = prof.ArchPreset,
                    SampleRate = prof.SampleRate > 0 ? prof.SampleRate : 44100,
                    Esr = prof.Esr,
                    EntryPath = entryPath,
                    Sha256 = hash,
                    BoundSlots = boundSlots,
                });
                modelIdx++;
            }

            NrigCabIrEntryDto? cabEntry = null;
            if (toneProc != null && toneProc.HasCustomCabIr)
            {
                byte[]? irWavBytes = null;
                int irSr = sampleRate;
                int irTaps = 0;
                string irName = string.IsNullOrWhiteSpace(toneProc.CustomCabIrName) ? "CustomCab.wav" : toneProc.CustomCabIrName;

                string? existingPath = toneProc.CustomCabIrPath;
                if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
                {
                    irWavBytes = File.ReadAllBytes(existingPath);
                    var (_, _, loadedName) = toneProc.GetCustomCabIrSnapshot();
                    var snapSamples = toneProc.GetCustomCabIrSnapshot();
                    irSr = snapSamples.sampleRate;
                    irTaps = snapSamples.samples?.Length ?? 0;
                    if (!string.IsNullOrWhiteSpace(loadedName)) irName = loadedName;
                }
                else
                {
                    var (samples, sr, name) = toneProc.GetCustomCabIrSnapshot();
                    if (samples is { Length: >= 8 })
                    {
                        irWavBytes = EncodeMonoFloatWavBytes(samples, sr);
                        irSr = sr;
                        irTaps = samples.Length;
                        if (!string.IsNullOrWhiteSpace(name)) irName = name;
                    }
                }

                if (irWavBytes is { Length: > 44 })
                {
                    string safeIr = SanitizeFileName(Path.GetFileNameWithoutExtension(irName), "CustomCab") + ".wav";
                    string entryPath = $"ir/{safeIr}";
                    WriteZipEntryBytes(zip, entryPath, irWavBytes);
                    cabEntry = new NrigCabIrEntryDto
                    {
                        Name = Path.GetFileName(safeIr),
                        SampleRate = irSr,
                        Taps = irTaps,
                        EntryPath = entryPath,
                        Sha256 = ComputeSha256Hex(irWavBytes),
                    };
                }
            }

            manifest = new NrigManifestDto
            {
                FormatVersion = "2.0",
                BundleId = $"nrig_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}",
                Name = string.IsNullOrWhiteSpace(cleanChain.PresetName) ? "NeuralRig Bundle" : cleanChain.PresetName.Trim(),
                Author = string.IsNullOrWhiteSpace(author) ? Environment.UserName : author.Trim(),
                Description = description ?? "",
                Tags = tags ?? Array.Empty<string>(),
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                SampleRate = Math.Max(8000, sampleRate),
                SignalChain = cleanChain,
                NamModels = namEntries,
                CustomCabIr = cabEntry,
                GuitarProfile = guitarProfile?.Clone(),
            };

            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, NeuralRigIndentedJsonContext.Default.NrigManifestDto));
            WriteZipEntryBytes(zip, "manifest.json", manifestBytes);
        }

        return (ms.ToArray(), manifest);
    }

    public static NrigManifestDto InspectBundle(string nrigPath)
    {
        if (string.IsNullOrWhiteSpace(nrigPath) || !File.Exists(nrigPath))
            throw new FileNotFoundException(".nrig 번들 파일을 찾을 수 없습니다.", nrigPath);
        using var fs = File.OpenRead(nrigPath);
        return InspectBundle(fs);
    }

    public static NrigManifestDto InspectBundle(Stream nrigStream)
    {
        using var zip = new ZipArchive(nrigStream, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException(".nrig 아카이브에 'manifest.json'이 없습니다.");

        NrigManifestDto manifest;
        using (var s = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize(s, NeuralRigJsonContext.Default.NrigManifestDto)
                ?? throw new InvalidDataException(".nrig manifest.json 파싱에 실패했습니다.");
        }

        if (manifest.SignalChain is null)
            throw new InvalidDataException(".nrig 매니페스트에 시그널 체인 정보가 없습니다.");

        if (manifest.NamModels != null)
        {
            foreach (var nm in manifest.NamModels)
            {
                byte[] data = ReadZipEntryBytes(zip, nm.EntryPath, $"NAM 모델 '{nm.Name}'");
                string actual = ComputeSha256Hex(data);
                if (!string.Equals(actual, nm.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"무결성 오류(SHA-256 불일치): '{nm.Name}' ({nm.EntryPath}) 파일이 손상되었거나 변조되었습니다.");
            }
        }

        if (manifest.CustomCabIr != null && !string.IsNullOrWhiteSpace(manifest.CustomCabIr.EntryPath))
        {
            byte[] irData = ReadZipEntryBytes(zip, manifest.CustomCabIr.EntryPath, $"Cab IR '{manifest.CustomCabIr.Name}'");
            string actual = ComputeSha256Hex(irData);
            if (!string.Equals(actual, manifest.CustomCabIr.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"무결성 오류(SHA-256 불일치): '{manifest.CustomCabIr.Name}' ({manifest.CustomCabIr.EntryPath}) IR이 변조되었습니다.");
        }

        return manifest;
    }

    public static NrigImportResultDto ImportBundle(
        string nrigPath,
        ToneProcessor? toneProc = null,
        string? modelsRootOverride = null,
        string? irsRootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(nrigPath) || !File.Exists(nrigPath))
            throw new FileNotFoundException(".nrig 번들 파일을 찾을 수 없습니다.", nrigPath);
        using var fs = File.OpenRead(nrigPath);
        return ImportBundle(fs, toneProc, modelsRootOverride, irsRootOverride);
    }

    public static NrigImportResultDto ImportBundle(
        Stream nrigStream,
        ToneProcessor? toneProc = null,
        string? modelsRootOverride = null,
        string? irsRootOverride = null)
    {
        byte[] rawArchive;
        using (var copy = new MemoryStream())
        {
            nrigStream.CopyTo(copy);
            rawArchive = copy.ToArray();
        }

        using var msVerify = new MemoryStream(rawArchive, writable: false);
        var manifest = InspectBundle(msVerify);

        using var msRead = new MemoryStream(rawArchive, writable: false);
        using var zip = new ZipArchive(msRead, ZipArchiveMode.Read, leaveOpen: false);

        var existingModels = ToneProfileStore.LoadAll(modelsRootOverride);
        var existingByWeightHash = new Dictionary<string, ToneProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var em in existingModels)
        {
            string wKey = ComputeModelWeightSignature(em);
            existingByWeightHash.TryAdd(wKey, em);
        }

        var importedModels = new List<ToneProfile>();
        var chain = manifest.SignalChain.Clone();
        var idRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (manifest.NamModels != null)
        {
            foreach (var nm in manifest.NamModels)
            {
                byte[] namBytes = ReadZipEntryBytes(zip, nm.EntryPath, nm.Name);
                string namJson = Encoding.UTF8.GetString(namBytes);

                // 임시 메모리/임포트로 파싱 후 중복 검사 또는 저장
                var parsed = ParseNamWeightKeyOnly(namJson, nm.Architecture);
                ToneProfile resolved;
                if (parsed != null && existingByWeightHash.TryGetValue(parsed, out var dup))
                {
                    resolved = dup;
                }
                else
                {
                    resolved = ToneProfileStore.ImportNamJson(namJson, nm.Name, nm.EntryPath, modelsRootOverride);
                    existingByWeightHash[ComputeModelWeightSignature(resolved)] = resolved;
                }

                importedModels.Add(resolved);
                if (!string.IsNullOrWhiteSpace(nm.OriginalModelId))
                    idRemap[nm.OriginalModelId] = resolved.Id;

                if (nm.BoundSlots != null)
                {
                    foreach (int slotIdx in nm.BoundSlots)
                    {
                        if (slotIdx >= 0 && slotIdx < chain.Slots.Count &&
                            string.Equals(chain.Slots[slotIdx].ModelId, "nam_capture", StringComparison.OrdinalIgnoreCase))
                        {
                            chain.Slots[slotIdx].NamModelId = resolved.Id;
                        }
                    }
                }
            }
        }

        // 나머지 nam_capture 슬롯도 OriginalModelId 리매핑 보장
        for (int i = 0; i < chain.Slots.Count; i++)
        {
            var sl = chain.Slots[i];
            if (string.Equals(sl.ModelId, "nam_capture", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(sl.NamModelId) &&
                idRemap.TryGetValue(sl.NamModelId, out string? mapped))
            {
                sl.NamModelId = mapped;
            }
        }

        string? irDestPath = null;
        string irDestName = "";
        if (manifest.CustomCabIr != null && !string.IsNullOrWhiteSpace(manifest.CustomCabIr.EntryPath))
        {
            byte[] irBytes = ReadZipEntryBytes(zip, manifest.CustomCabIr.EntryPath, manifest.CustomCabIr.Name);
            string irDir = string.IsNullOrWhiteSpace(irsRootOverride) ? DefaultIrsRoot : irsRootOverride;
            Directory.CreateDirectory(irDir);
            string safeIr = SanitizeFileName(Path.GetFileNameWithoutExtension(manifest.CustomCabIr.Name), "BundleIR");
            string shortHash = manifest.CustomCabIr.Sha256.Length >= 8 ? manifest.CustomCabIr.Sha256[..8] : "00000000";
            irDestPath = Path.Combine(irDir, $"{shortHash}_{safeIr}.wav");
            File.WriteAllBytes(irDestPath, irBytes);
            irDestName = Path.GetFileName(irDestPath);
            if (toneProc != null)
            {
                var (loadedName, _) = toneProc.LoadCustomCabIr(irDestPath);
                irDestName = loadedName;
            }
        }

        if (toneProc != null)
        {
            var refreshedLib = ToneProfileStore.LoadAll(modelsRootOverride);
            toneProc.SetModelLibrary(refreshedLib);
            if (importedModels.Count > 0)
                toneProc.ActiveProfile = importedModels[0];
            toneProc.Chain.UpdateChain(chain);
            toneProc.Mode = ToneMode.SignalChain;
        }

        return new NrigImportResultDto(
            manifest,
            chain,
            importedModels,
            irDestPath,
            irDestName,
            manifest.GuitarProfile?.Clone());
    }

    public static byte[] EncodeMonoFloatWavBytes(float[] samples, int sampleRate)
        => WavFileCodec.EncodeMonoFloatWavBytes(samples, sampleRate);

    private static Dictionary<string, ToneProfile> BuildModelLookup(
        ToneProcessor? toneProc,
        IEnumerable<ToneProfile>? library)
    {
        var dict = new Dictionary<string, ToneProfile>(StringComparer.OrdinalIgnoreCase);
        if (library != null)
        {
            foreach (var m in library)
                if (m != null && !string.IsNullOrWhiteSpace(m.Id))
                    dict[m.Id] = m;
        }
        if (toneProc != null)
        {
            foreach (var m in toneProc.GetModelLibrarySnapshot())
                if (m != null && !string.IsNullOrWhiteSpace(m.Id))
                    dict.TryAdd(m.Id, m);
            if (toneProc.ActiveProfile != null && !string.IsNullOrWhiteSpace(toneProc.ActiveProfile.Id))
                dict.TryAdd(toneProc.ActiveProfile.Id, toneProc.ActiveProfile);
        }
        return dict;
    }

    private static string? ParseNamWeightKeyOnly(string namJson, string archHint)
    {
        try
        {
            using var doc = JsonDocument.Parse(namJson);
            var root = doc.RootElement;
            string arch = root.TryGetProperty("architecture", out var aEl) ? (aEl.GetString() ?? archHint) : archHint;
            if (!root.TryGetProperty("weights", out var wEl) || wEl.ValueKind != JsonValueKind.Array)
                return null;
            var arr = new float[wEl.GetArrayLength()];
            int i = 0;
            foreach (var item in wEl.EnumerateArray()) arr[i++] = item.GetSingle();
            return ComputeFloatArraySignature(arch, arr);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeModelWeightSignature(ToneProfile p)
        => ComputeFloatArraySignature(p.Architecture, p.Weights);

    private static string ComputeFloatArraySignature(string arch, float[] weights)
    {
        byte[] bytes = new byte[weights.Length * sizeof(float)];
        Buffer.BlockCopy(weights, 0, bytes, 0, bytes.Length);
        return $"{arch.ToLowerInvariant()}:{ComputeSha256Hex(bytes)}";
    }

    private static void WriteZipEntryBytes(ZipArchive zip, string entryPath, byte[] data)
    {
        var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(data, 0, data.Length);
    }

    private static byte[] ReadZipEntryBytes(ZipArchive zip, string entryPath, string label)
    {
        var entry = zip.GetEntry(entryPath)
            ?? throw new InvalidDataException($".nrig 아카이브에서 필수 항목을 찾을 수 없습니다: {label} ({entryPath})");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SanitizeFileName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string((name ?? "").Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
}
