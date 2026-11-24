# Universal AI MCP Integration Guide (`neuralrig-mcp`)

NeuralRig Studio exposes its live DSP/MNA SPICE engine over the **Model Context Protocol (MCP)** through both the native Windows binary (`NeuralRigMcp.exe`) and the zero-dependency npm wrapper (`packages/neuralrig-mcp`).

---

## 1. One-Command Setup Across 7 AI Providers

Run the interactive or single-flag installer via `npx`:

```bash
# Configure all supported AI tools automatically
npx -y neuralrig-mcp install --client all

# Or target an individual provider
npx -y neuralrig-mcp install --client claude-desktop
npx -y neuralrig-mcp install --client claude-code
npx -y neuralrig-mcp install --client cursor
npx -y neuralrig-mcp install --client windsurf
npx -y neuralrig-mcp install --client codex
npx -y neuralrig-mcp install --client gemini
npx -y neuralrig-mcp install --client antigravity
```

Diagnose native binary resolution (`NeuralRigMcp.exe`) and live Named Pipe (`\\.\pipe\NeuralRigStudio_McpPipe_v1`) status:

```bash
npx -y neuralrig-mcp doctor
```

---

## 2. Config Targets Written by `neuralrig-mcp`

| Client ID | Target Path | Format |
|:---|:---|:---|
| `claude-desktop` | `%APPDATA%\Claude\claude_desktop_config.json` (Win) / `~/Library/Application Support/Claude/...` | JSON (`mcpServers`) |
| `claude-code` | `~/.claude.json` | JSON (`mcpServers`) |
| `cursor` | `~/.cursor/mcp.json` | JSON (`mcpServers`) |
| `windsurf` | `~/.codeium/windsurf/mcp_config.json` | JSON (`mcpServers`) |
| `codex` | `~/.codex/config.toml` | TOML (`[mcp_servers.neuralrig]`) |
| `gemini` | `~/.gemini/settings.json` | JSON (`mcpServers`) |
| `antigravity` | `~/.gemini/antigravity/mcp_config.json` | JSON (`mcpServers`) |

---

## 3. Available MCP Tools (`tools/NeuralRigMcp/Program.cs`)

| Tool Name | Purpose | Key Parameters |
|:---|:---|:---|
| `neuralrig_get_catalog` | Search catalog using either safe UI aliases or original hardware names (`TS808`, `SD-1`, `JCM800`, Korean aliases). | `category`, `query` |
| `neuralrig_get_state` | Read active preset, 16 slots, 8 scenes, tempo BPM, routing mode, ASIO/WASAPI status, and custom gear. | None |
| `neuralrig_get_guitar_profiles` | Retrieve calibrated Guitar DI profiles (pickup type, hum floor, suggested trim & gate). | None |
| `neuralrig_generate_tone` | Generate/refine a 16-slot signal chain from natural language calibrated to the active guitar profile. | `prompt`, `guitarProfileId`, `autoSave` |
| `neuralrig_set_preset` | Replace the full 16-slot signal chain and optional scenes live. | `presetName`, `slots`, `tempoBpm`, `autoSave` |
| `neuralrig_update_slot` | Modify model ID, bypass, tempo sync division (`0..5`), or params for a single slot (`0..15`). | `slotIndex`, `modelId`, `enabled`, `syncDivision`, `params` |
| `neuralrig_switch_scene` | Switch active scene (`0..7`), routing (`Serial`/`ParallelSplit`), mix, or BPM. | `sceneIndex`, `routingMode`, `parallelMix`, `tempoBpm` |
| `neuralrig_get_spice_guide` | Return MNA SPICE netlist schema (`R,C,L,POT,SW,D,Q,J,M,U,T`), anti-hallucination rules, and verified TS808 reference netlist. | None |
| `neuralrig_list_spice_models` | Query built-in & custom `.MODEL` library; pass `checkParts` to detect missing parts requiring datasheet research. | `kind`, `checkParts` |
| `neuralrig_register_spice_model` | Register a datasheet-verified semiconductor/op-amp/triode `.MODEL` (`datasheetUrl` mandatory). | `name`, `kind`, `polarity`, `datasheetUrl`, `params` |
| `neuralrig_simulate_spice` | Run offline `.OP`, `.AC`, `.TRAN`, and `0 B` RT allocation check on a SPICE netlist or acoustic cabinet curve without modifying live audio. | `spiceCircuit` / `cabAcoustic`, `knobs`, `testFreqHz`, `testAmplitude` |
| `neuralrig_commit_spice_device` | Validate, persist, and hot-deploy custom SPICE or Cab IR gear into `CustomDeviceRegistry` & running Studio UI. | `id`, `name`, `category`, `schematicSourceUrl`, `spiceCircuit` / `cabAcoustic`, `slotIndex` |
| `neuralrig_delete_custom_device` | Remove a `custom_*` device from Studio catalog and disk registry. | `modelId` |

Also exposes MCP Resource `neuralrig://guides/spice-authoring` and Prompt `add_gear_from_schematic`.

---

## 4. Strict Schematic Anti-Hallucination Workflow

1. Call `neuralrig_get_spice_guide` before constructing a netlist.
2. Search the web for genuine schematics & component datasheets. If no verifiable schematic exists for the requested gear, stop and report clearly that no authentic schematic could be verified.
3. Verify component coverage with `neuralrig_list_spice_models(checkParts=...)` and register any missing semiconductor models with official datasheet URLs.
4. Validate `.OP` DC bias, `.AC` transfer curve, `.TRAN` 4x harmonics, and `RtAllocBytes == 0` via `neuralrig_simulate_spice`.
5. Commit via `neuralrig_commit_spice_device` so the new gear appears in the Signal Chain catalog and Community Gear Hub.
