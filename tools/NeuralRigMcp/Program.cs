using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NeuralRigStudio.Audio;
using NeuralRigStudio.Audio.Spice;
using NeuralRigStudio.Services;

const string PipeName = "NeuralRigStudio_McpPipe_v1";

var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.InputEncoding = utf8NoBom;
Console.OutputEncoding = utf8NoBom;

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
};
var prettyOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
};

var architect = new AntigravityToneArchitect();

// CLI 1-shot test mode: NeuralRigMcp.exe --call <tool_name> [json_args]
if (args.Length >= 2 && args[0] == "--call")
{
    string tool = args[1];
    string rawArgs = args.Length >= 3 ? args[2] : "{}";
    using var doc = JsonDocument.Parse(rawArgs);
    var res = CallTool(tool, doc.RootElement);
    Console.WriteLine(JsonSerializer.Serialize(res, prettyOpts));
    return;
}

// Stdio MCP JSON-RPC 2.0 loop
using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
bool preferContentLength = false;

while (true)
{
    var frame = ReadNextMessage(stdin, ref preferContentLength);
    if (frame is null) break;
    if (string.IsNullOrWhiteSpace(frame)) continue;

    string? reply = HandleJsonRpcFrame(frame);
    if (reply != null)
        WriteMessage(stdout, reply, preferContentLength, utf8NoBom);
}

string? HandleJsonRpcFrame(string rawJson)
{
    rawJson = rawJson.Trim().TrimStart('\uFEFF');
    if (string.IsNullOrEmpty(rawJson)) return null;
    JsonElement? idEl = null;
    try
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("id", out var foundId))
            idEl = foundId.Clone();

        string method = root.TryGetProperty("method", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? (mEl.GetString() ?? "")
            : "";
        JsonElement paramsEl = root.TryGetProperty("params", out var pEl) ? pEl : default;

        // Notifications have no id -> never emit a response frame
        if (!idEl.HasValue)
            return null;

        object? result = method switch
        {
            "initialize" => HandleInitialize(paramsEl),
            "ping" => new { },
            "tools/list" => new { tools = BuildToolDefinitions() },
            "tools/call" => HandleToolsCall(paramsEl),
            "resources/list" => BuildResourcesList(),
            "resources/read" => HandleResourcesRead(paramsEl),
            "prompts/list" => BuildPromptsList(),
            "prompts/get" => HandlePromptsGet(paramsEl),
            _ => throw new RpcMethodNotFoundException(method),
        };

        var resp = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(idEl.Value.GetRawText()),
            ["result"] = JsonSerializer.SerializeToNode(result, jsonOpts),
        };
        return resp.ToJsonString(jsonOpts);
    }
    catch (RpcMethodNotFoundException ex)
    {
        if (!idEl.HasValue) return null;
        return BuildErrorJson(idEl.Value, -32601, $"Method not found: {ex.Method}");
    }
    catch (Exception ex)
    {
        if (!idEl.HasValue)
        {
            Console.Error.WriteLine($"[NeuralRigMcp] notification error: {ex.Message}");
            return null;
        }
        return BuildErrorJson(idEl.Value, -32603, ex.Message);
    }
}

static string BuildErrorJson(JsonElement idEl, int code, string message)
{
    var errObj = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = JsonNode.Parse(idEl.GetRawText()),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };
    return errObj.ToJsonString();
}

static object HandleInitialize(JsonElement paramsEl)
{
    string ver = "2024-11-05";
    if (paramsEl.ValueKind == JsonValueKind.Object
        && paramsEl.TryGetProperty("protocolVersion", out var vEl)
        && vEl.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(vEl.GetString()))
    {
        ver = vEl.GetString()!;
    }
    return new
    {
        protocolVersion = ver,
        capabilities = new
        {
            tools = new { listChanged = false },
            resources = new { subscribe = false, listChanged = false },
            prompts = new { listChanged = false },
        },
        serverInfo = new
        {
            name = "neuralrig",
            version = "2.0.0",
        },
    };
}

object HandleToolsCall(JsonElement paramsEl)
{
    if (paramsEl.ValueKind != JsonValueKind.Object)
        throw new InvalidOperationException("params object is required.");
    string name = paramsEl.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
    JsonElement toolArgs = paramsEl.TryGetProperty("arguments", out var aEl) ? aEl : default;

    try
    {
        object payload = CallTool(name, toolArgs);
        string text = JsonSerializer.Serialize(payload, prettyOpts);
        return new
        {
            content = new[] { new { type = "text", text } },
            isError = false,
        };
    }
    catch (Exception ex)
    {
        return new
        {
            content = new[] { new { type = "text", text = $"Error executing {name}: {ex.Message}" } },
            isError = true,
        };
    }
}

object CallTool(string name, JsonElement toolArgs) => name switch
{
    "neuralrig_get_catalog" => ToolGetCatalog(toolArgs),
    "neuralrig_get_state" => ToolGetState(),
    "neuralrig_get_guitar_profiles" => ToolGetGuitarProfiles(),
    "neuralrig_generate_tone" => ToolGenerateTone(toolArgs),
    "neuralrig_set_preset" => ToolSetPreset(toolArgs),
    "neuralrig_update_slot" => ToolUpdateSlot(toolArgs),
    "neuralrig_switch_scene" => ToolSwitchScene(toolArgs),
    "neuralrig_get_spice_guide" => SpiceAuthoringGuide.BuildGuidePayload(),
    "neuralrig_list_spice_models" => ToolListSpiceModels(toolArgs),
    "neuralrig_register_spice_model" => ToolRegisterSpiceModel(toolArgs),
    "neuralrig_simulate_spice" => ToolSimulateSpice(toolArgs),
    "neuralrig_commit_spice_device" => ToolCommitSpiceDevice(toolArgs),
    "neuralrig_delete_custom_device" => ToolDeleteCustomDevice(toolArgs),
    _ => throw new InvalidOperationException($"Unknown tool: {name}"),
};

object ToolGetCatalog(JsonElement toolArgs)
{
    string cat = GetStr(toolArgs, "category")?.Trim().ToLowerInvariant() ?? "all";
    string? query = GetStr(toolArgs, "query") ?? GetStr(toolArgs, "search");
    var items = AntigravityToneArchitect.SearchCatalog(cat, query)
        .OrderBy(d => d.Category)
        .ThenBy(d => d.Id)
        .Select(d => new
        {
            id = d.Id,
            name = d.Name,
            originalName = d.OriginalName,
            aliases = d.Aliases ?? Array.Empty<string>(),
            category = d.Category,
            defaults = d.Defaults,
            ranges = d.Ranges.Select(r => new { min = r.Min, max = r.Max }).ToArray(),
            hint = d.Hint,
            isCustomSpice = d.Hint.Contains("[SPICE MNA]", StringComparison.Ordinal),
            isCustomDevice = d.Id.StartsWith("custom_", StringComparison.OrdinalIgnoreCase),
        })
        .ToArray();

    return new
    {
        totalCount = items.Length,
        customCount = items.Count(i => i.isCustomDevice),
        categoryFilter = cat,
        queryFilter = query ?? "",
        layout = "2 rows x 8 slots (Row 1 = slots 0..7, Row 2 = slots 8..15). Built-in and custom MNA SPICE devices run real-time zero-allocation 4x HIIR DSP.",
        devices = items,
    };
}

object ToolGetState()
{
    if (TryCallLivePipe("get_state", new { }, out var data, out _))
        return data;

    var settings = AppSettings.Load();
    var chain = AntigravityToneArchitect.EnsureScenes(settings.SignalChain ?? new SignalChainConfigDto());
    var guitars = settings.GuitarProfiles ?? new List<GuitarProfileDto>();
    var activeGuitar = !string.IsNullOrWhiteSpace(settings.ActiveGuitarProfileId)
        ? guitars.FirstOrDefault(g => string.Equals(g.Id, settings.ActiveGuitarProfileId, StringComparison.OrdinalIgnoreCase))
        : null;

    return new
    {
        liveConnected = false,
        note = "NeuralRig Studio GUI is not currently running; showing saved settings.json state.",
        isAudioRunning = false,
        inputDriver = settings.InputDriver ?? "",
        channelMode = settings.ChannelMode,
        outputKind = settings.OutputKind,
        sampleRate = 44100,
        activeGuitarProfileId = settings.ActiveGuitarProfileId ?? "",
        activeGuitarProfile = activeGuitar,
        guitarProfilesCount = guitars.Count,
        signalChain = chain,
        savedPresets = settings.SavedPresets ?? new List<SignalChainConfigDto>(),
    };
}

object ToolGetGuitarProfiles()
{
    if (TryCallLivePipe("get_guitar_profiles", new { }, out var data, out _))
        return data;

    var settings = AppSettings.Load();
    var guitars = settings.GuitarProfiles ?? new List<GuitarProfileDto>();
    var activeGuitar = !string.IsNullOrWhiteSpace(settings.ActiveGuitarProfileId)
        ? guitars.FirstOrDefault(g => string.Equals(g.Id, settings.ActiveGuitarProfileId, StringComparison.OrdinalIgnoreCase))
        : null;

    return new
    {
        liveConnected = false,
        note = "NeuralRig Studio GUI is not currently running; showing saved settings.json guitar profiles.",
        activeGuitarProfileId = settings.ActiveGuitarProfileId ?? "",
        activeGuitarProfile = activeGuitar,
        guitarProfiles = guitars,
    };
}

object ToolGenerateTone(JsonElement toolArgs)
{
    string prompt = GetStr(toolArgs, "prompt") ?? "";
    if (string.IsNullOrWhiteSpace(prompt))
        throw new InvalidOperationException("'prompt' is required.");
    bool autoSave = GetBool(toolArgs, "autoSave", true);

    if (TryCallLivePipe("generate_tone", new { prompt, autoSave }, out var liveData, out string? pipeErr))
        return new { liveConnected = true, applied = liveData };

    var settings = AppSettings.Load();
    var cur = settings.SignalChain ?? new SignalChainConfigDto();
    var guitars = settings.GuitarProfiles ?? new List<GuitarProfileDto>();
    string? gpId = GetStr(toolArgs, "guitarProfileId") ?? settings.ActiveGuitarProfileId;
    var activeGuitar = !string.IsNullOrWhiteSpace(gpId)
        ? guitars.FirstOrDefault(g => string.Equals(g.Id, gpId, StringComparison.OrdinalIgnoreCase))
        : null;
    var res = architect.GenerateViaBuiltinArchitect(prompt, cur, activeGuitar);
    SaveOfflinePreset(settings, res.Chain, autoSave);
    return new
    {
        liveConnected = false,
        offlineNote = $"Saved directly to settings.json ({pipeErr ?? "NeuralRig Studio not running"}). Launch NeuralRig Studio to audition.",
        result = res,
    };
}

object ToolSetPreset(JsonElement toolArgs)
{
    bool autoSave = GetBool(toolArgs, "autoSave", true);
    if (toolArgs.ValueKind != JsonValueKind.Object || !toolArgs.TryGetProperty("slots", out _))
        throw new InvalidOperationException("Valid preset fields (presetName and slots array) are required.");

    if (TryCallLivePipe("set_preset", toolArgs, out var liveData, out string? pipeErr))
        return new { liveConnected = true, applied = liveData };

    var norm = AntigravityToneArchitect.ParsePresetFromJson(toolArgs);
    var settings = AppSettings.Load();
    SaveOfflinePreset(settings, norm, autoSave);
    return new
    {
        liveConnected = false,
        offlineNote = $"Saved directly to settings.json ({pipeErr ?? "NeuralRig Studio not running"}).",
        presetId = norm.PresetId,
        presetName = norm.PresetName,
        signalChain = norm,
    };
}

object ToolUpdateSlot(JsonElement toolArgs)
{
    if (TryCallLivePipe("update_slot", toolArgs, out var liveData, out string? pipeErr))
        return new { liveConnected = true, applied = liveData };

    int slotIndex = GetInt(toolArgs, "slotIndex", -1);
    if (slotIndex < 0 || slotIndex >= SignalChainConfigDto.SlotCount)
        throw new InvalidOperationException("slotIndex must be 0..15.");
    string? modelId = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("modelId", out var mEl) && mEl.ValueKind == JsonValueKind.String
        ? mEl.GetString()
        : null;
    bool? enabled = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("enabled", out var eEl) && (eEl.ValueKind == JsonValueKind.True || eEl.ValueKind == JsonValueKind.False)
        ? eEl.GetBoolean()
        : null;
    int? syncDiv = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("syncDivision", out var sdEl) && sdEl.TryGetInt32(out int sdi)
        ? sdi
        : null;
    JsonElement paramsEl = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("params", out var pEl) ? pEl : default;

    var settings = AppSettings.Load();
    var updated = AntigravityToneArchitect.ApplySlotEdit(settings.SignalChain ?? new SignalChainConfigDto(), slotIndex, modelId, enabled, paramsEl, syncDiv);
    SaveOfflinePreset(settings, updated, autoSave: false);
    return new
    {
        liveConnected = false,
        offlineNote = $"Saved directly to settings.json ({pipeErr ?? "NeuralRig Studio not running"}).",
        slotIndex,
        signalChain = updated,
    };
}

object ToolSwitchScene(JsonElement toolArgs)
{
    if (TryCallLivePipe("switch_scene", toolArgs, out var liveData, out string? pipeErr))
        return new { liveConnected = true, applied = liveData };

    int? sceneIdx = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("sceneIndex", out var sEl) && sEl.TryGetInt32(out int si) ? si : null;
    int? routingMode = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("routingMode", out var rEl) && rEl.TryGetInt32(out int ri) ? ri : null;
    float? parallelMix = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("parallelMix", out var pEl) && pEl.TryGetSingle(out float pm) ? pm : null;
    float? tempoBpm = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("tempoBpm", out var tEl) && tEl.TryGetSingle(out float tb) ? tb : null;

    var settings = AppSettings.Load();
    var updated = AntigravityToneArchitect.SwitchScene(settings.SignalChain ?? new SignalChainConfigDto(), sceneIdx, routingMode, parallelMix, tempoBpm);
    SaveOfflinePreset(settings, updated, autoSave: false);
    return new
    {
        liveConnected = false,
        offlineNote = $"Saved directly to settings.json ({pipeErr ?? "NeuralRig Studio not running"}).",
        activeScene = updated.ActiveScene,
        routingMode = updated.RoutingMode,
        signalChain = updated,
    };
}

object ToolListSpiceModels(JsonElement toolArgs)
{
    string kind = GetStr(toolArgs, "kind")?.Trim().ToLowerInvariant() ?? "all";
    var all = SpiceModelLibrary.GetAll(kind);

    string[]? checkedFound = null;
    string[]? checkedMissing = null;
    if (toolArgs.ValueKind == JsonValueKind.Object &&
        toolArgs.TryGetProperty("checkParts", out var cpEl) &&
        cpEl.ValueKind == JsonValueKind.Array)
    {
        var req = new List<string>();
        foreach (var item in cpEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                req.Add(item.GetString()!);
        }
        (checkedFound, checkedMissing) = SpiceModelLibrary.CheckParts(req);
    }

    return new
    {
        totalCount = all.Length,
        kindFilter = kind,
        foundParts = checkedFound,
        missingParts = checkedMissing,
        missingPartsAction = checkedMissing is { Length: > 0 }
            ? "Do NOT substitute or guess missing parts! Search the real component datasheet/SPICE .MODEL parameters on the web and register each missing part using neuralrig_register_spice_model."
            : null,
        models = all,
    };
}

object ToolRegisterSpiceModel(JsonElement toolArgs)
{
    var model = JsonSerializer.Deserialize<SpiceComponentModelDto>(toolArgs.GetRawText(), jsonOpts)
        ?? throw new InvalidOperationException("Valid SpiceComponentModelDto JSON is required.");

    var registered = SpiceModelLibrary.RegisterOrUpdate(model);
    bool syncedLive = TryCallLivePipe("register_spice_model", registered, out var liveData, out _);

    return new
    {
        ok = true,
        liveSynced = syncedLive,
        model = registered,
        liveResult = syncedLive ? (object?)liveData : null,
        message = $"SPICE 부품 모델 '{registered.Name}'({registered.Kind})이 데이터시트 출처와 함께 라이브러리에 등록되었습니다.",
    };
}

object ToolSimulateSpice(JsonElement toolArgs)
{
    int sr = GetInt(toolArgs, "sampleRate", 48000);

    if (toolArgs.ValueKind == JsonValueKind.Object &&
        toolArgs.TryGetProperty("cabAcoustic", out var cabEl) &&
        cabEl.ValueKind == JsonValueKind.Object &&
        !toolArgs.TryGetProperty("spiceCircuit", out _))
    {
        var cab = JsonSerializer.Deserialize<CustomCabAcousticDto>(cabEl.GetRawText(), jsonOpts);
        return SpiceAnalyzer.AnalyzeCabinet(cab, sr);
    }

    SpiceCircuitDto? circuit = null;
    if (toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("spiceCircuit", out var scEl) && scEl.ValueKind == JsonValueKind.Object)
        circuit = JsonSerializer.Deserialize<SpiceCircuitDto>(scEl.GetRawText(), jsonOpts);
    else if (toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("stages", out _))
        circuit = JsonSerializer.Deserialize<SpiceCircuitDto>(toolArgs.GetRawText(), jsonOpts);

    if (circuit == null)
        throw new InvalidOperationException("'spiceCircuit' (with stages) or 'cabAcoustic' is required.");

    float[]? knobs = null;
    if (toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("knobs", out var kEl) && kEl.ValueKind == JsonValueKind.Array)
    {
        var kl = new List<float>();
        foreach (var v in kEl.EnumerateArray())
            if (v.TryGetSingle(out float fv)) kl.Add(fv);
        knobs = kl.ToArray();
    }

    float testFreq = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("testFreqHz", out var tfEl) && tfEl.TryGetSingle(out float tff)
        ? tff : 440f;
    float testAmp = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("testAmplitude", out var taEl) && taEl.TryGetSingle(out float taf)
        ? taf : 0.25f;

    return SpiceAnalyzer.AnalyzeCircuit(circuit, knobs, sr, testFreq, testAmp);
}

object ToolCommitSpiceDevice(JsonElement toolArgs)
{
    var spec = JsonSerializer.Deserialize<CustomDeviceSpecDto>(toolArgs.GetRawText(), jsonOpts)
        ?? throw new InvalidOperationException("Valid device payload is required.");

    if (string.IsNullOrWhiteSpace(spec.SchematicSourceUrl))
        throw new InvalidOperationException("'schematicSourceUrl' (참고 회로도/스펙 출처 URL)은 필수입니다.");
    if (string.IsNullOrWhiteSpace(spec.Name))
        throw new InvalidOperationException("'name'이 필요합니다.");

    string cat = (spec.Category ?? "drive").Trim().ToLowerInvariant();
    if (cat == "cab")
    {
        if (spec.CabAcoustic == null)
            throw new InvalidOperationException("Category 'cab'에는 'cabAcoustic' 스펙(measuredCurve 또는 공진/롤오프/WAV)이 필요합니다.");
        var cabCheck = SpiceAnalyzer.AnalyzeCabinet(spec.CabAcoustic);
        if (!cabCheck.Ok)
            return new { ok = false, verification = cabCheck, message = "캐비넷 IR 검증에 실패했습니다." };
    }
    else
    {
        if (spec.SpiceCircuit == null)
            throw new InvalidOperationException($"Category '{cat}'에는 'spiceCircuit' MNA 넷리스트가 필요합니다.");
        var simCheck = SpiceAnalyzer.AnalyzeCircuit(spec.SpiceCircuit);
        if (!simCheck.Ok)
        {
            return new
            {
                ok = false,
                verification = simCheck,
                missingModels = simCheck.MissingModels,
                errors = simCheck.Errors,
                message = simCheck.MissingModels.Length > 0
                    ? $"미등록 부품 .MODEL이 있습니다: {string.Join(", ", simCheck.MissingModels)}. neuralrig_register_spice_model로 먼저 데이터시트 모델을 등록하세요."
                    : "커밋 전 SPICE 시뮬레이션 검증(.OP/.AC/.TRAN/RT)에 실패했습니다.",
            };
        }
    }

    int? slotIndex = toolArgs.ValueKind == JsonValueKind.Object && toolArgs.TryGetProperty("slotIndex", out var sEl) && sEl.TryGetInt32(out int si)
        ? si : null;

    if (TryCallLivePipe("commit_spice_device", new { device = spec, slotIndex }, out var liveData, out string? pipeErr))
        return new { ok = true, liveConnected = true, result = liveData };

    var registered = CustomDeviceRegistry.RegisterOrUpdate(spec);
    SignalChainConfigDto? updatedChain = null;
    if (slotIndex is >= 0 and < SignalChainConfigDto.SlotCount)
    {
        var settings = AppSettings.Load();
        updatedChain = AntigravityToneArchitect.ApplySlotEdit(
            settings.SignalChain ?? new SignalChainConfigDto(),
            slotIndex.Value,
            registered.Id,
            enabled: true,
            paramsEl: default);
        SaveOfflinePreset(settings, updatedChain, autoSave: false);
    }

    return new
    {
        ok = true,
        liveConnected = false,
        offlineNote = $"Saved to custom_devices.json ({pipeErr ?? "NeuralRig Studio not running"}).",
        device = registered,
        assignedSlotIndex = slotIndex,
        signalChain = updatedChain,
    };
}

object ToolDeleteCustomDevice(JsonElement toolArgs)
{
    string modelId = GetStr(toolArgs, "modelId") ?? "";
    if (string.IsNullOrWhiteSpace(modelId))
        throw new InvalidOperationException("'modelId' is required.");

    if (TryCallLivePipe("delete_custom_device", new { modelId }, out var liveData, out string? pipeErr))
        return new { ok = true, liveConnected = true, result = liveData };

    bool removed = CustomDeviceRegistry.Remove(modelId);
    return new
    {
        ok = removed,
        liveConnected = false,
        offlineNote = $"({pipeErr ?? "NeuralRig Studio not running"})",
        modelId,
    };
}

object BuildResourcesList() => new
{
    resources = new[]
    {
        new
        {
            uri = "neuralrig://guides/spice-authoring",
            name = "NeuralRig AI-Led SPICE Schematic Authoring Guide",
            description = "Step-by-step guide, anti-hallucination protocol, element syntax reference, and verified reference netlist examples for adding real guitar pedals, amps, and cabinets.",
            mimeType = "application/json",
        },
    },
};

object HandleResourcesRead(JsonElement paramsEl)
{
    string uri = GetStr(paramsEl, "uri") ?? "";
    if (!string.Equals(uri, "neuralrig://guides/spice-authoring", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Unknown resource URI: {uri}");

    string guideJson = JsonSerializer.Serialize(SpiceAuthoringGuide.BuildGuidePayload(), prettyOpts);
    return new
    {
        contents = new[]
        {
            new { uri, mimeType = "application/json", text = guideJson },
        },
    };
}

object BuildPromptsList() => new
{
    prompts = new[]
    {
        new
        {
            name = "add_gear_from_schematic",
            description = "Guided AI workflow to research a real guitar pedal/amp/cabinet schematic and missing component datasheets on the internet, verify with MNA SPICE simulation, and commit to NeuralRig Studio (or explicitly tell the user if no schematic exists).",
            arguments = new[]
            {
                new { name = "gearName", description = "Requested pedal, amp, or cabinet name", required = true },
            },
        },
    },
};

object HandlePromptsGet(JsonElement paramsEl)
{
    string name = GetStr(paramsEl, "name") ?? "";
    if (!string.Equals(name, "add_gear_from_schematic", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Unknown prompt: {name}");

    string gear = "target gear";
    if (paramsEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
        gear = GetStr(argsEl, "gearName") ?? gear;

    string text =
        $"User wants to add '{gear}' into NeuralRig Studio.\n" +
        "Follow the NeuralRig AI-Led SPICE Pipeline:\n" +
        "1. Call neuralrig_get_spice_guide to inspect the MNA netlist schema and strict anti-hallucination rules.\n" +
        $"2. Search the internet for the authentic circuit schematic / BOM or measured speaker curve of '{gear}'.\n" +
        $"   IMPORTANT: If '{gear}' does not exist or no reliable schematic/BOM/curve is found, STOP and tell the user clearly that it could not be found.\n" +
        "3. Call neuralrig_list_spice_models with checkParts=[all semiconductors/tubes/op-amps in the schematic].\n" +
        "4. For EVERY part listed in missingParts, search its official datasheet / SPICE .MODEL parameters and call neuralrig_register_spice_model.\n" +
        "5. Construct the full MNA netlist (spiceCircuit or cabAcoustic) and call neuralrig_simulate_spice to verify .OP DC bias, .AC Bode response, .TRAN THD/asymmetry harmonics, and 0 B RT alloc.\n" +
        "6. Call neuralrig_commit_spice_device to save and deploy the verified circuit into NeuralRig Studio.";

    return new
    {
        description = "AI-led SPICE schematic modeling workflow",
        messages = new[]
        {
            new { role = "user", content = new { type = "text", text } },
        },
    };
}

static void SaveOfflinePreset(AppSettings settings, SignalChainConfigDto chain, bool autoSave)
{
    var clean = chain.Clone();
    settings.ToneMode = 6; // ToneMode.SignalChain
    settings.SignalChain = clean;
    if (autoSave)
    {
        settings.SavedPresets ??= new List<SignalChainConfigDto>();
        int idx = settings.SavedPresets.FindIndex(p => string.Equals(p.PresetId, clean.PresetId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) settings.SavedPresets[idx] = clean;
        else settings.SavedPresets.Add(clean);
    }
    settings.Save();
}

bool TryCallLivePipe(string cmd, object? cmdArgs, out JsonElement data, out string? errorMsg)
{
    data = default;
    errorMsg = null;
    try
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
        client.Connect(450);
        using var reader = new StreamReader(client, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        using var writer = new StreamWriter(client, utf8NoBom, bufferSize: 8192, leaveOpen: true) { AutoFlush = true };

        string reqLine = JsonSerializer.Serialize(new { cmd, args = cmdArgs }, jsonOpts);
        writer.WriteLine(reqLine);
        writer.Flush();

        string? respLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(respLine))
        {
            errorMsg = "Empty IPC response";
            return false;
        }
        using var doc = JsonDocument.Parse(respLine);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            string err = root.TryGetProperty("error", out var eEl) ? (eEl.GetString() ?? "Unknown IPC error") : "IPC command failed";
            throw new InvalidOperationException(err);
        }
        data = root.TryGetProperty("data", out var dEl) ? dEl.Clone() : default;
        return true;
    }
    catch (TimeoutException ex)
    {
        errorMsg = ex.Message;
        return false;
    }
    catch (IOException ex)
    {
        errorMsg = ex.Message;
        return false;
    }
}

static object[] BuildToolDefinitions() =>
[
    new
    {
        name = "neuralrig_get_catalog",
        description = "List or search schematic-modeled guitar pedals, amplifiers, and cabinets in NeuralRig Studio (with alias display names, original hardware names, search aliases, P0..P7 parameter indices, and min/max ranges).",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                category = new
                {
                    type = "string",
                    description = "Optional category filter: all, amp, nam, cab, drive, delay, reverb, mod, eq, dyn, pitch",
                    @enum = new[] { "all", "amp", "nam", "cab", "drive", "delay", "reverb", "mod", "eq", "dyn", "pitch" },
                },
                query = new
                {
                    type = "string",
                    description = "Optional search keyword matching original hardware name (e.g. 'BOSS SD-1', 'BOSS OD-1', 'Marshall JCM800', 'Ibanez TS808'), alias display name (가칭), brand, or Korean name (e.g. '보스', '마샬', '튜브스크리머').",
                },
            },
        },
    },
    new
    {
        name = "neuralrig_get_state",
        description = "Get NeuralRig Studio's current 16-slot signal chain preset, active Scene (A..H), saved presets, active analyzed guitar profile summary, ASIO/WASAPI audio running status, and live IPC connection status.",
        inputSchema = new
        {
            type = "object",
            properties = new { },
        },
    },
    new
    {
        name = "neuralrig_get_guitar_profiles",
        description = "Get all saved guitar acoustic/electrical analysis profiles (estimated pickup type, brightness/warmth/output scores, 5-band frequency tilt, noise floor, 60Hz hum, sustain, suggested Input Trim & Gate, per-note measurements) and the currently active guitar profile so Antigravity can tailor tone presets to the user's actual instrument.",
        inputSchema = new
        {
            type = "object",
            properties = new { },
        },
    },
    new
    {
        name = "neuralrig_generate_tone",
        description = "Automatically generate and immediately apply a full 16-slot + 8-scene preset from a Korean or English tone description (genre, artist, or relative tweaks like '현재 톤에서 게인 더 올리고 블루스카이 추가해줘').",
        inputSchema = new
        {
            type = "object",
            required = new[] { "prompt" },
            properties = new
            {
                prompt = new { type = "string", description = "Desired guitar tone description or modification request" },
                autoSave = new { type = "boolean", description = "Save into NeuralRig preset bank (default true)" },
            },
        },
    },
    new
    {
        name = "neuralrig_set_preset",
        description = "Apply an exact 16-slot signal chain preset crafted from the NeuralRig 72-device catalog directly to the live DSP engine and UI (or save to settings.json when offline). Use neuralrig_get_catalog first to verify modelIds and P0..P7 ranges.",
        inputSchema = new
        {
            type = "object",
            required = new[] { "presetName", "slots" },
            properties = new
            {
                presetId = new { type = "string", description = "Optional short ID such as AG01 or U101" },
                presetName = new { type = "string", description = "Display name of the preset" },
                routingMode = new { type = "integer", description = "0 = Serial (Row 1 -> Row 2), 1 = ParallelSplit" },
                splitCol = new { type = "integer", description = "Row 1 column 0..7 where parallel Row 2 splits off" },
                mergeCol = new { type = "integer", description = "Row 1 column 0..7 where parallel Row 2 merges back" },
                parallelMix = new { type = "number", description = "Parallel blend 0.0..1.0" },
                activeScene = new { type = "integer", description = "Active scene index 0..7 (A=0..H=7, default 3)" },
                autoSave = new { type = "boolean", description = "Save to SavedPresets list (default true)" },
                slots = new
                {
                    type = "array",
                    description = "Slots to configure (indices 0..7 = Row 1, 8..15 = Row 2)",
                    items = new
                    {
                        type = "object",
                        required = new[] { "index", "modelId" },
                        properties = new
                        {
                            index = new { type = "integer", description = "Slot index 0..15" },
                            modelId = new { type = "string", description = "Device ID from neuralrig_get_catalog, original hardware name (e.g. 'BOSS SD-1', 'Marshall JCM800'), or alias display name (가칭), or empty string to clear" },
                            enabled = new { type = "boolean", description = "Block enabled (default true)" },
                            @params = new
                            {
                                type = "array",
                                items = new { type = "number" },
                                description = "Up to 8 parameter floats [P0..P7] matching the device spec",
                            },
                        },
                    },
                },
            },
        },
    },
    new
    {
        name = "neuralrig_update_slot",
        description = "Update a single slot (assign/replace/clear modelId, toggle bypass, or tweak P0..P7 knobs) in real time without rebuilding the rest of the signal chain.",
        inputSchema = new
        {
            type = "object",
            required = new[] { "slotIndex" },
            properties = new
            {
                slotIndex = new { type = "integer", description = "Slot index 0..15" },
                modelId = new { type = "string", description = "Optional new device modelId, original hardware name (e.g. 'BOSS OD-1', 'BOSS SD-1', 'Marshall JCM800'), or alias display name (empty string clears slot)" },
                enabled = new { type = "boolean", description = "Optional bypass state (true=ON, false=BYPASSED)" },
                @params = new { description = "Optional array [P0..P7] or partial map {\"0\": 6.5, \"3\": 7.0}" },
            },
        },
    },
    new
    {
        name = "neuralrig_switch_scene",
        description = "Switch active Scene A..H (0..7) or routing mode (Serial=0 / Parallel=1) or parallel mix in real time.",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                sceneIndex = new { type = "integer", description = "Target Scene index 0..7 (0=A, 1=B, ..., 7=H)" },
                routingMode = new { type = "integer", description = "Optional routing mode 0=Serial, 1=ParallelSplit" },
                parallelMix = new { type = "number", description = "Optional parallel mix 0.0..1.0" },
            },
        },
    },
    new
    {
        name = "neuralrig_get_spice_guide",
        description = "Get the complete AI-led SPICE schematic authoring guide, netlist syntax rules (R, C, L, POT, SW, D, Q, J, M, U, T), strict anti-hallucination rules, required .MODEL parameters, and verified TS808 reference netlist example. Call this FIRST when a user asks to add a new pedal, amp, or cabinet.",
        inputSchema = new
        {
            type = "object",
            properties = new { },
        },
    },
    new
    {
        name = "neuralrig_list_spice_models",
        description = "List all verified SPICE component .MODEL definitions (diodes, BJTs, JFETs, MOSFETs, Op-Amps, Triodes) and optionally verify a list of part numbers (`checkParts`) to identify any missing components that the AI must research from datasheets before building the netlist.",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                kind = new
                {
                    type = "string",
                    description = "Optional filter: all, diode, bjt, jfet, mosfet, opamp, triode",
                    @enum = new[] { "all", "diode", "bjt", "jfet", "mosfet", "opamp", "triode" },
                },
                checkParts = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Optional list of schematic part numbers (e.g. ['JRC4558', '1N914', 'MA856']) to check which exist and which are missing.",
                },
            },
        },
    },
    new
    {
        name = "neuralrig_register_spice_model",
        description = "Register or update a researched component SPICE .MODEL (diode, BJT, JFET, MOSFET, op-amp, or triode) sourced from a real datasheet or SPICE library. Requires datasheetUrl and valid physical parameters.",
        inputSchema = new
        {
            type = "object",
            required = new[] { "name", "kind", "datasheetUrl", "params" },
            properties = new
            {
                name = new { type = "string", description = "Part number / model key (e.g. 'MA856', '2SK30A', 'CA3080')" },
                kind = new
                {
                    type = "string",
                    description = "Component type",
                    @enum = new[] { "diode", "bjt", "jfet", "mosfet", "opamp", "triode" },
                },
                polarity = new { type = "string", description = "For BJT/JFET/MOSFET: 'N' (NPN/N-ch) or 'P' (PNP/P-ch)" },
                datasheetUrl = new { type = "string", description = "Mandatory HTTP/HTTPS source URL of datasheet or SPICE model" },
                notes = new { type = "string", description = "Optional datasheet summary notes" },
                @params = new
                {
                    type = "object",
                    description = "Physical SPICE parameters map (e.g. diode: {Is, N, Rs}; bjt: {Is, Bf, Br, Nf, Nr, Vaf}; opamp: {Aol, GbwHz, SlewRateVus, VrailPos, VrailNeg, Rin, Rout})",
                },
            },
        },
    },
    new
    {
        name = "neuralrig_simulate_spice",
        description = "Run offline SPICE verification sandbox on a candidate MNA netlist (`spiceCircuit`) or cabinet acoustic curve (`cabAcoustic`). Executes .OP (DC bias per node), .AC (30Hz..16kHz complex Bode sweep), .TRAN (4x oversampled THD/H2/H3/asymmetry/stability), and 0 B RT allocation check without modifying the live rig.",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                spiceCircuit = new { type = "object", description = "Candidate SpiceCircuitDto JSON with knobs and stages" },
                cabAcoustic = new { type = "object", description = "Candidate CustomCabAcousticDto JSON with measuredCurve or resonance/rolloff/wavIrPath" },
                knobs = new { type = "array", items = new { type = "number" }, description = "Optional [P0..P7] knob values to evaluate at" },
                sampleRate = new { type = "integer", description = "Base sample rate (default 48000 -> runs at 192000Hz 4x OS)" },
                testFreqHz = new { type = "number", description = "Transient test sine frequency (default 440 Hz)" },
                testAmplitude = new { type = "number", description = "Transient normalized input amplitude (default 0.25)" },
            },
        },
    },
    new
    {
        name = "neuralrig_commit_spice_device",
        description = "Validate, persist, and hot-deploy an AI-authored and verified SPICE circuit (pedal/amp) or acoustic cabinet IR into NeuralRig Studio's live engine & UI, optionally assigning it into a signal chain slot (0..15). Requires schematicSourceUrl and passing .OP/.TRAN/0B verification.",
        inputSchema = new
        {
            type = "object",
            required = new[] { "id", "name", "category", "schematicSourceUrl" },
            properties = new
            {
                id = new { type = "string", description = "Unique modelId starting with 'custom_' (e.g. 'custom_drive_zendrive')" },
                name = new { type = "string", description = "Trademark-safe alias display name (가칭, e.g. 'Compact Quad OverDrive \\'77')" },
                shortName = new { type = "string", description = "Short grid tile alias name (max 12 chars, e.g. 'Quad OD \\'77')" },
                originalName = new { type = "string", description = "Original real-world hardware model name for search & MCP lookup (원래 이름, e.g. 'BOSS OD-1 OverDrive (1977)')" },
                aliases = new { type = "array", items = new { type = "string" }, description = "Search aliases / original model codes / Korean names (e.g. ['BOSS', 'OD-1', 'OD1', '보스'])" },
                category = new { type = "string", @enum = new[] { "drive", "amp", "cab" } },
                schematicSourceUrl = new { type = "string", description = "Mandatory source schematic or datasheet URL" },
                schematicSummary = new { type = "string", description = "Concise summary of circuit topology and components" },
                spiceCircuit = new { type = "object", description = "Full SpiceCircuitDto netlist (required for drive/amp)" },
                cabAcoustic = new { type = "object", description = "CustomCabAcousticDto (required for cab)" },
                slotIndex = new { type = "integer", description = "Optional slot index 0..15 to place immediately" },
            },
        },
    },
    new
    {
        name = "neuralrig_delete_custom_device",
        description = "Delete a previously committed custom device (`custom_*`) from NeuralRig Studio and sync UI.",
        inputSchema = new
        {
            type = "object",
            required = new[] { "modelId" },
            properties = new
            {
                modelId = new { type = "string", description = "Custom device modelId, original hardware name, or alias name to remove" },
            },
        },
    },
];

static string? ReadNextMessage(Stream stdin, ref bool sawContentLength)
{
    while (true)
    {
        string? line = ReadLineUtf8(stdin)?.TrimStart('\uFEFF');
        if (line is null) return null;
        if (string.IsNullOrWhiteSpace(line)) continue;

        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            sawContentLength = true;
            if (!int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out int len) || len < 0)
                return null;

            while (true)
            {
                string? hdr = ReadLineUtf8(stdin);
                if (hdr is null) return null;
                if (hdr.Length == 0) break;
            }

            byte[] buf = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = stdin.Read(buf, read, len - read);
                if (n <= 0) return null;
                read += n;
            }
            return Encoding.UTF8.GetString(buf);
        }

        return line;
    }
}

static string? ReadLineUtf8(Stream s)
{
    using var ms = new MemoryStream(256);
    while (true)
    {
        int b = s.ReadByte();
        if (b < 0)
            return ms.Length == 0 ? null : Encoding.UTF8.GetString(ms.ToArray());
        if (b == '\n')
        {
            byte[] arr = ms.ToArray();
            int len = arr.Length > 0 && arr[^1] == '\r' ? arr.Length - 1 : arr.Length;
            return Encoding.UTF8.GetString(arr, 0, len);
        }
        ms.WriteByte((byte)b);
    }
}

static void WriteMessage(Stream stdout, string json, bool useContentLength, Encoding utf8)
{
    byte[] payload = utf8.GetBytes(json);
    if (useContentLength)
    {
        byte[] hdr = utf8.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        stdout.Write(hdr, 0, hdr.Length);
        stdout.Write(payload, 0, payload.Length);
    }
    else
    {
        stdout.Write(payload, 0, payload.Length);
        stdout.WriteByte((byte)'\n');
    }
    stdout.Flush();
}

static string? GetStr(JsonElement el, string prop)
    => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString()
        : null;

static int GetInt(JsonElement el, string prop, int def)
    => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.TryGetInt32(out int n)
        ? n
        : def;

static bool GetBool(JsonElement el, string prop, bool def)
    => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
        ? v.GetBoolean()
        : def;

sealed class RpcMethodNotFoundException(string method) : Exception
{
    public string Method { get; } = method;
}
