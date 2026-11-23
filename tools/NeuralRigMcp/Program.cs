using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NeuralRigStudio.Audio;
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
            "resources/list" => new { resources = Array.Empty<object>() },
            "prompts/list" => new { prompts = Array.Empty<object>() },
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
        },
        serverInfo = new
        {
            name = "neuralrig",
            version = "1.0.0",
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
    _ => throw new InvalidOperationException($"Unknown tool: {name}"),
};

object ToolGetCatalog(JsonElement toolArgs)
{
    string cat = GetStr(toolArgs, "category")?.Trim().ToLowerInvariant() ?? "all";
    var items = AntigravityToneArchitect.Catalog.Values
        .Where(d => cat == "all" || string.IsNullOrEmpty(cat) || d.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))
        .OrderBy(d => d.Category)
        .ThenBy(d => d.Id)
        .Select(d => new
        {
            id = d.Id,
            name = d.Name,
            category = d.Category,
            defaults = d.Defaults,
            ranges = d.Ranges.Select(r => new { min = r.Min, max = r.Max }).ToArray(),
            hint = d.Hint,
        })
        .ToArray();

    return new
    {
        totalCount = items.Length,
        categoryFilter = cat,
        layout = "2 rows x 8 slots (Row 1 = slots 0..7, Row 2 = slots 8..15). Active SignalChain runs real-time zero-allocation 4x HIIR schematic DSP.",
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
        description = "List all 72 schematic-modeled guitar pedals, amplifiers, and 512-tap cabinets in NeuralRig Studio with parameter indices (P0..P7) and valid min/max ranges.",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                category = new
                {
                    type = "string",
                    description = "Optional category filter: all, amp, nam, cab, drive, delay, reverb, mod, eq, dyn",
                    @enum = new[] { "all", "amp", "nam", "cab", "drive", "delay", "reverb", "mod", "eq", "dyn" },
                },
            },
        },
    },
    new
    {
        name = "neuralrig_get_state",
        description = "Get NeuralRig Studio's current 16-slot signal chain preset, active Scene (A..H), saved presets, active analyzed guitar profile summary, GP-100/ASIO audio running status, and live IPC connection status.",
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
                            modelId = new { type = "string", description = "Device ID from neuralrig_get_catalog, or empty string to clear" },
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
                modelId = new { type = "string", description = "Optional new device modelId (empty string clears slot)" },
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
