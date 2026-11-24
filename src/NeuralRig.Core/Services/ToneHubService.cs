using System.Net.Http;
using System.Text;
using System.Text.Json;
using NeuralRigStudio.Audio;

namespace NeuralRigStudio.Services;

public sealed record ToneHubItemDto(
    string Id,
    string Title,
    string Author,
    string Category,      // "nam" | "ir" | "nrig"
    string Architecture,  // "WaveNet" | "LSTM" | "ConvNet" | "8192-Tap IR" | "4096-Tap IR" | "Full Rig Bundle"
    string ArchPreset,
    string Genre,
    string Description,
    string[] Tags,
    double EsrDb,
    int SampleRate,
    int Taps,
    int Downloads,
    double Rating,
    bool IsInstalled,
    string DownloadUrl);

public sealed record ToneHubInstallResultDto(
    ToneHubItemDto Item,
    ToneProfile? InstalledModel,
    SignalChainConfigDto? AppliedChain,
    string? CustomCabIrPath,
    string CustomCabIrName,
    string Summary);

/// <summary>
/// 인앱 온라인 & 큐레이션 스튜디오 볼트 Tone Hub 서비스 (Milestone 5 / v2.0).
/// - 12종의 큐레이션 NAM 모델 (WaveNet / LSTM / ConvNet), 4K~8K High-Res Cab IR, 완성형 아티스트 .nrig 번들 내장
/// - 외부 https:// URL(.nam / .wav / .nrig) 원클릭 다운로드 및 SHA-256 검증 임포트 지원
/// </summary>
public static class ToneHubService
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private sealed record HubRecipe(
        ToneHubItemDto Meta,
        float DriveScale,
        float WarmthTilt,
        float CrunchBias,
        string? PairedHubNamId = null,
        string? PairedHubNam2Id = null,
        string? PairedHubIrId = null);

    private static readonly HubRecipe[] Vault =
    {
        // ── 6 Curated NAM Neural Captures (WaveNet / LSTM / ConvNet) ──
        new(
            new ToneHubItemDto(
                "hub_nam_dumble_ods",
                "ODS-50 Velvet Overdrive (Dumble)",
                "NeuralRig Studio Vault",
                "nam",
                "WaveNet",
                "feather",
                "Blues / Fusion",
                "따뜻한 미드레인지 서스테인과 부드러운 2차/3차 진공관 컴프레션이 특징인 50W 부티크 리드 캡처.",
                new[] { "WaveNet Feather", "Blues", "Fusion", "Mid-Push" },
                -28.4, 48000, 0, 18420, 4.9, false, ""),
            DriveScale: 2.35f, WarmthTilt: 0.42f, CrunchBias: 0.09f),

        new(
            new ToneHubItemDto(
                "hub_nam_deluxe_65",
                "'65 Blackface Deluxe Reverb Clean",
                "NeuralRig Studio Vault",
                "nam",
                "WaveNet",
                "nano",
                "Pop / J-Pop / Soul",
                "유리알처럼 투명한 하이 헤드룸과 단단한 저역 응답의 블랙페이스 22W 6V6 클린 캡처.",
                new[] { "WaveNet Nano", "Clean", "Glass", "J-Pop" },
                -31.2, 48000, 0, 24190, 4.9, false, ""),
            DriveScale: 1.35f, WarmthTilt: 0.68f, CrunchBias: 0.03f),

        new(
            new ToneHubItemDto(
                "hub_nam_5150_block",
                "1992 5150 Block Letter High-Gain",
                "NeuralRig Studio Vault",
                "nam",
                "WaveNet",
                "standard",
                "Modern Metal / Djent",
                "면도날처럼 타이트한 저역 어택과 공격적인 배음 포화도를 재현한 클래식 메탈 헤드 캡처.",
                new[] { "WaveNet Standard", "Metal", "Tight", "High-Gain" },
                -26.8, 48000, 0, 31050, 4.8, false, ""),
            DriveScale: 3.60f, WarmthTilt: 0.78f, CrunchBias: 0.14f),

        new(
            new ToneHubItemDto(
                "hub_nam_jcm800_jose",
                "JCM800 2203 Jose Diode Mod",
                "NeuralRig Studio Vault",
                "nam",
                "LSTM",
                "lstm-1x24",
                "80s Hard Rock / J-Rock",
                "다이오드 클리핑과 2203 프리앰프의 거친 배음 감도를 학습한 1-Layer 24-Hidden LSTM 캡처.",
                new[] { "LSTM 1x24", "Hard Rock", "Marshall", "Modded" },
                -25.6, 48000, 0, 14920, 4.8, false, ""),
            DriveScale: 2.95f, WarmthTilt: 0.62f, CrunchBias: 0.11f),

        new(
            new ToneHubItemDto(
                "hub_nam_ac30hw",
                "AC30HW Hand-Wired Top Boost",
                "NeuralRig Studio Vault",
                "nam",
                "LSTM",
                "lstm-2x16",
                "Indie / Worship / Brit-Rock",
                "EL84 파워관의 탄력 있는 섀그와 반짝이는 탑 부스트 챠임을 담은 2-Layer Stacked LSTM 캡처.",
                new[] { "LSTM 2x16", "VOX", "Chime", "Worship" },
                -27.1, 48000, 0, 16780, 4.9, false, ""),
            DriveScale: 1.95f, WarmthTilt: 0.74f, CrunchBias: 0.06f),

        new(
            new ToneHubItemDto(
                "hub_nam_mark2c_plus",
                "California Mark IIC+ Simul-Class",
                "NeuralRig Studio Vault",
                "nam",
                "ConvNet",
                "convnet-8ch",
                "Prog Metal / Shred",
                "BatchNorm 폴딩이 적용된 8채널 ConvNet 아키텍처로 정교하고 포커싱된 프로그 메탈 리드 톤 구현.",
                new[] { "ConvNet 8ch", "Prog", "Mesa", "Liquid Lead" },
                -24.9, 48000, 0, 12840, 4.7, false, ""),
            DriveScale: 3.30f, WarmthTilt: 0.66f, CrunchBias: 0.12f),

        // ── 3 Curated Full-Length High-Res Cab IRs (4,096~8,192 taps for Partitioned FFT Convolver) ──
        new(
            new ToneHubItemDto(
                "hub_ir_greenback_8k",
                "1960AX 4x12 G12M Greenback · Wood Studio (8K)",
                "NeuralRig Acoustic Lab",
                "ir",
                "8192-Tap IR",
                "8192-tap",
                "Rock / Classic / Ambient",
                "8,192탭 풀렝스 캐비넷 임펄스. 85Hz 우퍼 공진과 우드 스튜디오 초기 반사음이 살아있는 내추럴 공간감.",
                new[] { "8192-Tap", "Greenback", "SM57+Room", "Partitioned FFT" },
                0.0, 48000, 8192, 22410, 4.9, false, ""),
            DriveScale: 1f, WarmthTilt: 0.50f, CrunchBias: 0f),

        new(
            new ToneHubItemDto(
                "hub_ir_alnico_r121_4k",
                "2x12 Celestion Alnico Blue · R-121 Ribbon (4K)",
                "NeuralRig Acoustic Lab",
                "ir",
                "4096-Tap IR",
                "4096-tap",
                "J-Pop / Indie / Blues",
                "4,096탭 리본 마이크 IR. 치찰음 없는 부드러운 고역과 풍부한 미드-로우 우디 레조넌스.",
                new[] { "4096-Tap", "Alnico Blue", "Ribbon R121", "Warm" },
                0.0, 48000, 4096, 15830, 4.8, false, ""),
            DriveScale: 1f, WarmthTilt: 0.35f, CrunchBias: 0f),

        new(
            new ToneHubItemDto(
                "hub_ir_v30_blend_8k",
                "4x12 V30 + G12T-75 Phase-Aligned Studio Blend (8K)",
                "NeuralRig Acoustic Lab",
                "ir",
                "8192-Tap IR",
                "8192-tap",
                "Modern Metal / J-Rock",
                "V30 어퍼미드 어택과 T-75 서브 펀치를 위상 정렬 후 스튜디오 룸 앰비언스까지 담은 8,192탭 IR.",
                new[] { "8192-Tap", "V30+T75", "Tight", "Modern Metal" },
                0.0, 48000, 8192, 28940, 4.9, false, ""),
            DriveScale: 1f, WarmthTilt: 0.72f, CrunchBias: 0f),

        // ── 3 Complete .nrig Artist Rig Bundles (16 Slots + 8 Scenes + Multi-NAM + High-Res IR) ──
        new(
            new ToneHubItemDto(
                "hub_nrig_shibuya_jpop",
                "Shibuya Math-Rock & J-Pop Sparkle (.nrig)",
                "Artist Collection",
                "nrig",
                "Full Rig Bundle",
                ".nrig v2.0",
                "J-Pop / Math-Rock",
                "'65 Deluxe NAM + Alnico R-121 4K IR + SP Comp + Jan Ray + 병렬 DC-2W / DD-3T가 통합된 원클릭 완성 번들.",
                new[] { ".nrig Bundle", "WaveNet", "4K IR", "4 Scenes" },
                -31.2, 48000, 4096, 19450, 4.9, false, ""),
            DriveScale: 1f, WarmthTilt: 0.65f, CrunchBias: 0f,
            PairedHubNamId: "hub_nam_deluxe_65",
            PairedHubIrId: "hub_ir_alnico_r121_4k"),

        new(
            new ToneHubItemDto(
                "hub_nrig_cathedral_worship",
                "Cathedral Dual-NAM Worship Shimmer (.nrig)",
                "Artist Collection",
                "nrig",
                "Full Rig Bundle",
                ".nrig v2.0",
                "Worship / Ambient",
                "Dumble ODS WaveNet + AC30HW LSTM 병렬 듀얼-NAM에 Greenback 8K IR, 126BPM 1/8. 타임라인과 쉬머 결합.",
                new[] { ".nrig Bundle", "Dual-NAM", "WaveNet+LSTM", "8K IR" },
                -27.8, 48000, 8192, 21300, 5.0, false, ""),
            DriveScale: 1f, WarmthTilt: 0.60f, CrunchBias: 0f,
            PairedHubNamId: "hub_nam_dumble_ods",
            PairedHubNam2Id: "hub_nam_ac30hw",
            PairedHubIrId: "hub_ir_greenback_8k"),

        new(
            new ToneHubItemDto(
                "hub_nrig_prog_metal",
                "Modern Prog Drop-C Quad-Tight Rig (.nrig)",
                "Artist Collection",
                "nrig",
                "Full Rig Bundle",
                ".nrig v2.0",
                "Modern Metal / Prog",
                "Virtual Capo(-2st) + Gate + TS808 + 5150 & Mark IIC+ 병렬 NAM + V30 Blend 8K IR이 모두 패키징된 .nrig 번들.",
                new[] { ".nrig Bundle", "Dual-NAM", "WaveNet+ConvNet", "Drop-C" },
                -25.8, 48000, 8192, 25610, 4.9, false, ""),
            DriveScale: 1f, WarmthTilt: 0.78f, CrunchBias: 0f,
            PairedHubNamId: "hub_nam_5150_block",
            PairedHubNam2Id: "hub_nam_mark2c_plus",
            PairedHubIrId: "hub_ir_v30_blend_8k"),
    };

    public static List<ToneHubItemDto> GetCatalog(
        IEnumerable<ToneProfile>? installedModels = null,
        IEnumerable<SignalChainConfigDto>? savedPresets = null,
        string? activeCustomIrName = null)
    {
        var modelNames = new HashSet<string>(
            (installedModels ?? Array.Empty<ToneProfile>()).Select(m => m.Name),
            StringComparer.OrdinalIgnoreCase);
        var presetNames = new HashSet<string>(
            (savedPresets ?? Array.Empty<SignalChainConfigDto>()).Select(p => p.PresetName),
            StringComparer.OrdinalIgnoreCase);

        var list = new List<ToneHubItemDto>(Vault.Length);
        foreach (var r in Vault)
        {
            bool installed = r.Meta.Category switch
            {
                "nam" => modelNames.Contains(r.Meta.Title),
                "ir" => !string.IsNullOrWhiteSpace(activeCustomIrName) &&
                        activeCustomIrName.Contains(SafeSlug(r.Meta.Id), StringComparison.OrdinalIgnoreCase),
                "nrig" => presetNames.Contains(r.Meta.Title.Replace(" (.nrig)", "")),
                _ => false,
            };
            list.Add(r.Meta with { IsInstalled = installed });
        }
        return list;
    }

    public static ToneHubInstallResultDto InstallHubItem(
        string hubItemId,
        ToneProcessor toneProc,
        int targetSlotIndex = -1,
        string? modelsRootOverride = null,
        string? irsRootOverride = null)
    {
        var recipe = Vault.FirstOrDefault(v => string.Equals(v.Meta.Id, hubItemId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tone Hub 항목을 찾을 수 없습니다: '{hubItemId}'");

        if (recipe.Meta.Category == "nam")
        {
            string namJson = BuildCuratedNamJson(recipe);
            var imported = ToneProfileStore.ImportNamJson(
                namJson,
                recipe.Meta.Title,
                $"tonehub://{recipe.Meta.Id}.nam",
                modelsRootOverride);

            var lib = ToneProfileStore.LoadAll(modelsRootOverride);
            toneProc.SetModelLibrary(lib);
            toneProc.ActiveProfile = imported;

            var chain = toneProc.Chain.GetConfigSnapshot();
            int slot = targetSlotIndex >= 0 && targetSlotIndex < SignalChainConfigDto.SlotCount
                ? targetSlotIndex
                : FindTargetNamOrAmpSlot(chain);

            if (slot >= 0 && slot < chain.Slots.Count)
            {
                var sl = chain.Slots[slot];
                if (string.IsNullOrEmpty(sl.ModelId) || sl.ModelId.StartsWith("amp_", StringComparison.OrdinalIgnoreCase) || sl.ModelId == "nam_capture")
                {
                    sl.ModelId = "nam_capture";
                    sl.NamModelId = imported.Id;
                    sl.Enabled = true;
                    sl.Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 };
                    chain = AntigravityToneArchitect.CommitActiveScene(chain);
                    toneProc.Chain.UpdateChain(chain);
                    toneProc.Mode = ToneMode.SignalChain;
                }
            }

            return new ToneHubInstallResultDto(
                recipe.Meta with { IsInstalled = true },
                imported,
                chain,
                null,
                toneProc.CustomCabIrName,
                $"[Tone Hub] '{imported.Name}' ({imported.Architecture}) 설치 및 슬롯 #{slot + 1} 적용 완료");
        }

        if (recipe.Meta.Category == "ir")
        {
            float[] irSamples = SynthesizeCuratedHighResCabIr(recipe);
            string irDir = string.IsNullOrWhiteSpace(irsRootOverride) ? NrigBundleService.DefaultIrsRoot : irsRootOverride;
            Directory.CreateDirectory(irDir);
            string fileName = $"{SafeSlug(recipe.Meta.Id)}.wav";
            string destPath = Path.Combine(irDir, fileName);
            File.WriteAllBytes(destPath, NrigBundleService.EncodeMonoFloatWavBytes(irSamples, recipe.Meta.SampleRate));

            var (loadedName, taps) = toneProc.LoadCustomCabIr(destPath);
            var chain = toneProc.Chain.GetConfigSnapshot();
            bool switchedSlot = false;
            for (int i = 0; i < chain.Slots.Count; i++)
            {
                if (chain.Slots[i].ModelId.StartsWith("cab_", StringComparison.OrdinalIgnoreCase))
                {
                    chain.Slots[i].ModelId = "cab_custom";
                    chain.Slots[i].Enabled = true;
                    switchedSlot = true;
                    break;
                }
            }
            if (switchedSlot)
            {
                chain = AntigravityToneArchitect.CommitActiveScene(chain);
                toneProc.Chain.UpdateChain(chain);
            }

            return new ToneHubInstallResultDto(
                recipe.Meta with { IsInstalled = true },
                null,
                chain,
                destPath,
                loadedName,
                $"[Tone Hub] {taps}-Tap High-Res IR '{loadedName}' 설치 및 커스텀 IR 슬롯 적용 완료");
        }

        // Complete .nrig Artist Bundle
        byte[] nrigBytes = BuildCuratedNrigBundleBytes(recipe);
        using var ms = new MemoryStream(nrigBytes, writable: false);
        var res = NrigBundleService.ImportBundle(ms, toneProc, modelsRootOverride, irsRootOverride);
        return new ToneHubInstallResultDto(
            recipe.Meta with { IsInstalled = true },
            res.ImportedModels.FirstOrDefault(),
            res.SignalChain,
            res.CustomCabIrPath,
            res.CustomCabIrName,
            $"[Tone Hub] '{res.SignalChain.PresetName}' .nrig 번들(NAM {res.ImportedModels.Count}종 + {recipe.Meta.Taps}-Tap IR) 임포트 완료");
    }

    public static async Task<NrigImportResultDto> InstallFromUrlAsync(
        string url,
        ToneProcessor toneProc,
        string? modelsRootOverride = null,
        string? irsRootOverride = null,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("올바른 http:// 또는 https:// 다운로드 URL을 입력하세요.");
        }

        using var resp = await SharedHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is > 50 * 1024 * 1024)
            throw new InvalidOperationException("다운로드 파일 크기가 50MB 제한을 초과했습니다.");

        byte[] data = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        string rawName = Path.GetFileName(uri.AbsolutePath);
        string safeName = string.IsNullOrWhiteSpace(rawName) ? "downloaded_tone" : rawName;

        return ImportRawFileBytes(safeName, data, toneProc, modelsRootOverride, irsRootOverride);
    }

    public static NrigImportResultDto ImportRawFileBytes(
        string fileName,
        byte[] data,
        ToneProcessor toneProc,
        string? modelsRootOverride = null,
        string? irsRootOverride = null)
    {
        if (data is null || data.Length < 8)
            throw new InvalidOperationException("파일 데이터가 비어 있습니다.");

        string cleanFileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "imported_tone.nam" : fileName);
        if (string.IsNullOrWhiteSpace(cleanFileName)) cleanFileName = "imported_tone.nam";
        string ext = Path.GetExtension(cleanFileName).ToLowerInvariant();
        bool looksZip = data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B;
        bool looksWav = data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F';

        if (ext == ".nrig" || (looksZip && ext != ".nam" && ext != ".wav"))
        {
            using var ms = new MemoryStream(data, writable: false);
            return NrigBundleService.ImportBundle(ms, toneProc, modelsRootOverride, irsRootOverride);
        }

        if (ext == ".wav" || looksWav)
        {
            string irDir = string.IsNullOrWhiteSpace(irsRootOverride) ? NrigBundleService.DefaultIrsRoot : irsRootOverride;
            Directory.CreateDirectory(irDir);
            string destWav = Path.Combine(irDir, cleanFileName);
            File.WriteAllBytes(destWav, data);
            var (loadedName, _) = toneProc.LoadCustomCabIr(destWav);
            var chain = toneProc.Chain.GetConfigSnapshot();
            for (int i = 0; i < chain.Slots.Count; i++)
            {
                if (chain.Slots[i].ModelId.StartsWith("cab_", StringComparison.OrdinalIgnoreCase))
                {
                    chain.Slots[i].ModelId = "cab_custom";
                    chain.Slots[i].Enabled = true;
                    break;
                }
            }
            chain = AntigravityToneArchitect.CommitActiveScene(chain);
            toneProc.Chain.UpdateChain(chain);
            return new NrigImportResultDto(
                new NrigManifestDto { Name = loadedName, SignalChain = chain },
                chain,
                new List<ToneProfile>(),
                destWav,
                loadedName,
                null);
        }

        // .nam / .json fallback
        string jsonText = Encoding.UTF8.GetString(data);
        var model = ToneProfileStore.ImportNamJson(jsonText, Path.GetFileNameWithoutExtension(fileName), fileName, modelsRootOverride);
        var lib = ToneProfileStore.LoadAll(modelsRootOverride);
        toneProc.SetModelLibrary(lib);
        toneProc.ActiveProfile = model;

        var curChain = toneProc.Chain.GetConfigSnapshot();
        int targetSlot = FindTargetNamOrAmpSlot(curChain);
        if (targetSlot >= 0)
        {
            curChain.Slots[targetSlot].ModelId = "nam_capture";
            curChain.Slots[targetSlot].NamModelId = model.Id;
            curChain.Slots[targetSlot].Enabled = true;
            curChain.Slots[targetSlot].Params = new float[] { 0f, 0f, 100f, 0, 0, 0, 0, 0 };
            curChain = AntigravityToneArchitect.CommitActiveScene(curChain);
            toneProc.Chain.UpdateChain(curChain);
            toneProc.Mode = ToneMode.SignalChain;
        }
        return new NrigImportResultDto(
            new NrigManifestDto { Name = model.Name, SignalChain = curChain },
            curChain,
            new List<ToneProfile> { model },
            null,
            toneProc.CustomCabIrName,
            null);
    }

    private static int FindTargetNamOrAmpSlot(SignalChainConfigDto chain)
    {
        for (int i = 0; i < chain.Slots.Count; i++)
            if (string.Equals(chain.Slots[i].ModelId, "nam_capture", StringComparison.OrdinalIgnoreCase))
                return i;
        for (int i = 0; i < chain.Slots.Count; i++)
            if (chain.Slots[i].ModelId.StartsWith("amp_", StringComparison.OrdinalIgnoreCase))
                return i;
        return 2;
    }

    private static byte[] BuildCuratedNrigBundleBytes(HubRecipe recipe)
    {
        var tempModels = new List<ToneProfile>();
        var mainRecipe = Vault.First(v => v.Meta.Id == recipe.PairedHubNamId);
        var mainProf = BuildInMemoryCuratedProfile(mainRecipe, "vault_nam_0");
        tempModels.Add(mainProf);

        ToneProfile? secProf = null;
        if (!string.IsNullOrEmpty(recipe.PairedHubNam2Id))
        {
            var r2 = Vault.First(v => v.Meta.Id == recipe.PairedHubNam2Id);
            secProf = BuildInMemoryCuratedProfile(r2, "vault_nam_1");
            tempModels.Add(secProf);
        }

        string titleClean = recipe.Meta.Title.Replace(" (.nrig)", "");
        var chain = new SignalChainConfigDto
        {
            PresetId = recipe.Meta.Id == "hub_nrig_shibuya_jpop" ? "H1" : recipe.Meta.Id == "hub_nrig_cathedral_worship" ? "H2" : "H3",
            PresetName = titleClean,
            ActiveScene = 0,
            RoutingMode = recipe.Meta.Id == "hub_nrig_shibuya_jpop" ? 0 : 1,
            SplitCol = 1,
            MergeCol = 5,
            ParallelMix = 0.48f,
            TempoBpm = recipe.Meta.Id == "hub_nrig_cathedral_worship" ? 126f : 132f,
            InputGainDb = 0f,
            OutputGainDb = 0f,
        };
        for (int i = 0; i < SignalChainConfigDto.SlotCount; i++)
            chain.Slots[i] = new ChainSlotDto { Index = i, ModelId = "", Enabled = true, Params = new float[8] };

        if (recipe.Meta.Id == "hub_nrig_shibuya_jpop")
        {
            SetChainSlot(chain, 0, "comp_sp", true, 54f, 5.6f, 1f);
            SetChainSlot(chain, 1, "od_janray", true, 2.8f, 5.2f, 5.8f, 5.8f);
            SetChainSlot(chain, 2, "nam_capture", true, 0f, 0f, 100f);
            chain.Slots[2].NamModelId = mainProf.Id;
            SetChainSlot(chain, 3, "cab_custom", true, 75f, 11800f, 0f);
            chain.Slots[3].CabMic = new CabMicConfigDto { DualEnabled = true, MicAType = 1, MicAX = 0.25f, MicADist = 0.20f, MicBPan = 0.45f, Blend = 0.35f };
            SetChainSlot(chain, 4, "mod_dc2w", true, 2f, 0f);
            SetChainSlot(chain, 5, "dly_dd3t", true, 360f, 28f, 22f);
            chain.Slots[5].SyncDivision = (int)TempoSyncDivision.DottedEighth;
            SetChainSlot(chain, 6, "rev_plate", true, 48f, 34f, 22f);
        }
        else if (recipe.Meta.Id == "hub_nrig_cathedral_worship")
        {
            SetChainSlot(chain, 0, "comp_1176", true, 5.0f, 5.4f, 4f, 82f);
            SetChainSlot(chain, 1, "od_klon", true, 4.0f, 5.8f, 5.8f);
            SetChainSlot(chain, 2, "nam_capture", true, 0f, 0f, 100f);
            chain.Slots[2].NamModelId = mainProf.Id;
            SetChainSlot(chain, 3, "cab_custom", true, 70f, 11400f, 0f);
            chain.Slots[3].CabMic = new CabMicConfigDto { DualEnabled = true, MicAType = 0, MicAX = 0.20f, MicBType = 1, MicBX = 0.45f, MicBDelayMs = 6.5f, MicAPan = -0.8f, MicBPan = 0.8f, Blend = 0.5f };
            SetChainSlot(chain, 5, "dly_timeline", true, 440f, 46f, 34f, 36f, 1f);
            chain.Slots[5].SyncDivision = (int)TempoSyncDivision.DottedEighth;
            SetChainSlot(chain, 6, "rev_bluesky", true, 84f, 28f, 30f, 44f, 48f);
            SetChainSlot(chain, 9, "mod_smallclone", true, 0.75f, 1f, 52f);
            if (secProf != null)
            {
                SetChainSlot(chain, 10, "nam_capture", true, 0f, 0f, 100f);
                chain.Slots[10].NamModelId = secProf.Id;
            }
        }
        else
        {
            SetChainSlot(chain, 0, "pitch_capo", true, -2f, 0f, 100f, 0f);
            SetChainSlot(chain, 1, "gate_adaptive", true, -48f, 75f, 45f);
            SetChainSlot(chain, 2, "od_ts808", true, 1.4f, 6.5f, 7.8f);
            SetChainSlot(chain, 3, "nam_capture", true, 0f, 0f, 100f);
            chain.Slots[3].NamModelId = mainProf.Id;
            SetChainSlot(chain, 4, "cab_custom", true, 95f, 9200f, 0f);
            SetChainSlot(chain, 6, "rev_plate", true, 32f, 35f, 14f);
            if (secProf != null)
            {
                SetChainSlot(chain, 11, "nam_capture", true, 0f, 0f, 100f);
                chain.Slots[11].NamModelId = secProf.Id;
            }
            SetChainSlot(chain, 12, "eq_parametric", true, 90f, 1450f, 2.2f, 1.4f, 9200f);
        }

        chain = AntigravityToneArchitect.EnsureScenes(chain);
        // Scene B 리드 부스트
        if (chain.Scenes is { Count: >= 2 })
            chain.Scenes[1].ParallelMix = Math.Clamp(chain.ParallelMix + 0.1f, 0f, 1f);

        using var hostProc = new ToneProcessorHostGuard(recipe.Meta.SampleRate);
        if (!string.IsNullOrEmpty(recipe.PairedHubIrId))
        {
            var irRecipe = Vault.First(v => v.Meta.Id == recipe.PairedHubIrId);
            float[] ir = SynthesizeCuratedHighResCabIr(irRecipe);
            hostProc.Processor.LoadCustomCabIrFromSamples(ir, irRecipe.Meta.SampleRate, $"{SafeSlug(irRecipe.Meta.Id)}.wav");
        }

        var (bytes, _) = NrigBundleService.ExportBundleToBytes(
            chain,
            hostProc.Processor,
            tempModels,
            author: recipe.Meta.Author,
            description: recipe.Meta.Description,
            tags: recipe.Meta.Tags,
            sampleRate: recipe.Meta.SampleRate);
        return bytes;
    }

    private sealed class ToneProcessorHostGuard : IDisposable
    {
        public readonly ToneProcessor Processor = new();
        public ToneProcessorHostGuard(int sr) => Processor.Prepare(256, sr);
        public void Dispose() { }
    }

    private static void SetChainSlot(SignalChainConfigDto c, int idx, string modelId, bool enabled, params float[] p)
    {
        var arr = new float[8];
        Array.Copy(p, arr, Math.Min(8, p.Length));
        c.Slots[idx] = new ChainSlotDto { Index = idx, ModelId = modelId, Enabled = enabled, Params = arr };
    }

    private static ToneProfile BuildInMemoryCuratedProfile(HubRecipe recipe, string id)
    {
        string json = BuildCuratedNamJson(recipe);
        string tmp = Path.Combine(Path.GetTempPath(), $"nrs_mem_{Guid.NewGuid():N}.nam");
        try
        {
            var root = Path.Combine(Path.GetTempPath(), $"nrs_vault_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var p = ToneProfileStore.ImportNamJson(json, recipe.Meta.Title, recipe.Meta.Id, root);
            p.Id = id;
            p.PrepareForRt();
            Directory.Delete(root, recursive: true);
            p.NamFilePath = "";
            p.JsonPath = "";
            return p;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static string BuildCuratedNamJson(HubRecipe recipe)
    {
        var rng = new Random(StableHash(recipe.Meta.Id));
        object cfgObj;
        float[] weights;

        if (recipe.Meta.Architecture == "LSTM")
        {
            bool stacked = recipe.Meta.ArchPreset.Contains("2x", StringComparison.OrdinalIgnoreCase);
            int numLayers = stacked ? 2 : 1;
            int hidden = stacked ? 16 : 24;
            var lstmCfg = new LstmConfigDto { NumLayers = numLayers, InputSize = 1, HiddenSize = hidden };

            var wl = new List<float>();
            for (int l = 0; l < numLayers; l++)
            {
                int inDim = l == 0 ? 1 : hidden;
                int totalIn = inDim + hidden;
                // Weight IH + HH [4*h, in+h]
                for (int r = 0; r < 4 * hidden; r++)
                {
                    int gate = r / hidden; // 0:i, 1:f, 2:g, 3:o
                    for (int c = 0; c < totalIn; c++)
                    {
                        float val;
                        if (c == 0 && l == 0 && (r % hidden) == 0 && gate == 2)
                            val = recipe.DriveScale * 0.42f;
                        else
                            val = (float)(rng.NextDouble() * 2.0 - 1.0) * (c < inDim ? 0.14f : 0.04f);
                        wl.Add(val);
                    }
                }
                // Biases [4*h] (forget gate positive so memory flows cleanly without saturation lock)
                for (int r = 0; r < 4 * hidden; r++)
                {
                    int gate = r / hidden;
                    wl.Add(gate == 1 ? 0.65f : (gate == 3 ? 0.3f : (r % hidden == 0 && gate == 2 ? recipe.CrunchBias : 0f)));
                }
                // Initial state h0[h], c0[h]
                for (int k = 0; k < hidden * 2; k++) wl.Add(0f);
            }
            // Head weights[h] + head bias
            for (int k = 0; k < hidden; k++)
                wl.Add(k == 0 ? 1.15f : (float)(rng.NextDouble() * 2.0 - 1.0) * 0.06f);
            wl.Add(0f);

            cfgObj = lstmCfg;
            weights = wl.ToArray();
        }
        else if (recipe.Meta.Architecture == "ConvNet")
        {
            var convCfg = new ConvNetConfigDto
            {
                Channels = 8,
                Dilations = new[] { 1, 2, 4, 8 },
                BatchNorm = true,
                KernelSize = 2,
                Activation = JsonSerializer.SerializeToElement("Tanh"),
            };
            var wl = new List<float>();
            int ch = convCfg.Channels;
            int kSize = 2;
            for (int li = 0; li < convCfg.Dilations.Length; li++)
            {
                int inCh = li == 0 ? 1 : ch;
                for (int oc = 0; oc < ch; oc++)
                {
                    for (int ic = 0; ic < inCh; ic++)
                    {
                        for (int k = 0; k < kSize; k++)
                        {
                            float w = (ic == oc % inCh && k == kSize - 1)
                                ? (li == 0 ? recipe.DriveScale * 0.25f : 0.55f)
                                : (float)(rng.NextDouble() * 2.0 - 1.0) * (k == 0 ? (1f - recipe.WarmthTilt) * 0.18f : 0.06f);
                            wl.Add(w);
                        }
                    }
                }
                // BatchNorm: running_mean[ch], running_var[ch], gamma[ch], beta[ch], eps
                for (int c = 0; c < ch; c++) wl.Add(c == 0 && li == 0 ? recipe.CrunchBias * 0.3f : 0f);
                for (int c = 0; c < ch; c++) wl.Add(1.0f);
                for (int c = 0; c < ch; c++) wl.Add(0.95f);
                for (int c = 0; c < ch; c++) wl.Add(0f);
                wl.Add(1e-5f);
            }
            for (int c = 0; c < ch; c++) wl.Add(c == 0 ? 0.85f : 0.08f);
            wl.Add(0f);

            cfgObj = convCfg;
            weights = wl.ToArray();
        }
        else
        {
            // WaveNet (Nano / Feather / Standard)
            (int inSz, int ch, int headSz, int[] dils, bool headBias)[] specs = recipe.Meta.ArchPreset switch
            {
                "nano" => new[] { (1, 12, 1, new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, true) },
                "feather" => new[]
                {
                    (1, 12, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, false),
                    (12, 8, 1, new[] { 1, 2, 4, 8, 16, 32 }, true),
                },
                _ => new[]
                {
                    (1, 16, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }, false),
                    (16, 8, 1, new[] { 1, 2, 4, 8, 16, 32, 64 }, true),
                },
            };

            var layerDtos = new List<WaveNetLayerArrayCfg>(specs.Length);
            var wl = new List<float>();
            foreach (var (inSz, ch, headSz, dils, headBias) in specs)
            {
                layerDtos.Add(new WaveNetLayerArrayCfg
                {
                    InputSize = inSz,
                    ConditionSize = 1,
                    HeadSize = headSz,
                    Channels = ch,
                    KernelSize = 3,
                    Dilations = dils,
                    Activation = JsonSerializer.SerializeToElement("Tanh"),
                    Gated = true,
                    HeadBias = headBias,
                });

                // Rechannel [ch, inSz]
                for (int oc = 0; oc < ch; oc++)
                    for (int ic = 0; ic < inSz; ic++)
                        wl.Add(oc == ic ? 0.55f : (float)(rng.NextDouble() * 2.0 - 1.0) * 0.04f);

                int midCh = ch * 2;
                foreach (int _ in dils)
                {
                    // Conv1d [midCh, ch, 3]
                    for (int oc = 0; oc < midCh; oc++)
                        for (int ic = 0; ic < ch; ic++)
                            for (int k = 0; k < 3; k++)
                                wl.Add(ic == (oc % ch) && k == 2 ? (oc < ch ? 0.32f : 0.25f) : (float)(rng.NextDouble() * 2.0 - 1.0) * 0.03f);
                    // Conv1d bias [midCh]
                    for (int oc = 0; oc < midCh; oc++)
                        wl.Add(oc >= ch ? 0.65f : (oc == 0 ? recipe.CrunchBias * 0.4f : 0f));
                    // InputMixer [midCh, 1]
                    for (int oc = 0; oc < midCh; oc++)
                        wl.Add(oc < ch ? recipe.DriveScale * (0.28f / MathF.Sqrt(ch)) : 0.12f);
                    // Layer1x1 weight [ch, ch] + bias [ch]
                    for (int oc = 0; oc < ch; oc++)
                        for (int ic = 0; ic < ch; ic++)
                            wl.Add(oc == ic ? 0.35f : (float)(rng.NextDouble() * 2.0 - 1.0) * 0.02f);
                    for (int oc = 0; oc < ch; oc++) wl.Add(0f);
                }

                // HeadRechannel [headSz, ch] + optional headBias
                for (int oc = 0; oc < headSz; oc++)
                    for (int ic = 0; ic < ch; ic++)
                        wl.Add(ic == oc ? 0.45f * recipe.WarmthTilt : 0.05f);
                if (headBias)
                    for (int oc = 0; oc < headSz; oc++) wl.Add(0f);
            }
            wl.Add(1.0f); // head_scale

            cfgObj = new WaveNetConfigDto { Layers = layerDtos, HeadScale = 1.0f };
            weights = wl.ToArray();
        }

        var namFile = new
        {
            version = "0.5.4",
            metadata = new
            {
                name = recipe.Meta.Title,
                modeled_by = recipe.Meta.Author,
                esr = Math.Pow(10.0, recipe.Meta.EsrDb / 10.0),
            },
            architecture = recipe.Meta.Architecture,
            sample_rate = recipe.Meta.SampleRate,
            config = cfgObj,
            weights,
        };
        return JsonSerializer.Serialize(namFile, new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static float[] SynthesizeCuratedHighResCabIr(HubRecipe recipe)
    {
        int len = Math.Clamp(recipe.Meta.Taps, 1024, 16384);
        int sr = recipe.Meta.SampleRate;
        var ir = new float[len];
        var rng = new Random(StableHash(recipe.Meta.Id));

        // 1) 최소 위상 직접음 + 스피커 공진 + 우퍼 콘 브레이크업
        double speakerResHz = recipe.Meta.Id.Contains("greenback") ? 88.0 : recipe.Meta.Id.Contains("alnico") ? 98.0 : 82.0;
        double coneBreakHz = 2200.0 + recipe.WarmthTilt * 2100.0;
        double fizzCutoff = 3800.0 + recipe.WarmthTilt * 2400.0;

        ir[0] = 0.58f;
        ir[1] = 0.28f * recipe.WarmthTilt;
        double lp = 0.0;
        for (int i = 1; i < len; i++)
        {
            double t = (double)i / sr;
            double woofer = 0.11 * Math.Exp(-i * 0.012) * Math.Sin(2.0 * Math.PI * speakerResHz * t);
            double cone = 0.09 * Math.Exp(-i * 0.025) * Math.Cos(2.0 * Math.PI * coneBreakHz * t);
            // 스튜디오 룸 초기 반사(5ms..80ms 구간)
            double room = 0.0;
            if (i >= 180 && i < len - 128)
            {
                double env = Math.Exp(-(i - 180) * (3.2 / len));
                double n = rng.NextDouble() * 2.0 - 1.0;
                lp = lp + Math.Min(0.85, 2.0 * Math.PI * fizzCutoff / sr) * (n - lp);
                room = 0.014 * env * lp;
            }
            ir[i] += (float)(woofer + cone + room);
        }

        double energy = 0.0;
        for (int i = 0; i < len; i++) energy += (double)ir[i] * ir[i];
        float norm = (float)Math.Sqrt(energy + 1e-12);
        if (norm > 1e-6f)
            for (int i = 0; i < len; i++) ir[i] /= norm;

        return ir;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return h & 0x7FFFFFFF;
        }
    }

    private static string SafeSlug(string s)
        => string.Concat((s ?? "item").Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
}
