# Antigravity AI MCP Integration Guide

NeuralRig Studio features direct integration with **Google Antigravity** via the Model Context Protocol (MCP). Through this adapter, AI agents can inspect your rig state, examine your connected guitar's acoustic profile, and craft customized analog-accurate guitar tones directly from natural language prompts.

---

## 1. Overview

The integration consists of:
- **`NeuralRigMcp.exe`**: A standalone single-file MCP server communicating over standard I/O (JSON-RPC).
- **`NeuralRigStudio_McpPipe_v1`**: A fast, local Windows Named Pipe IPC bridge between `NeuralRigMcp` and the running `NeuralRigStudio` application.

---

## 2. Setting Up in Antigravity

Add the `neuralrig` MCP server to your Antigravity configuration file (e.g., `~/.gemini/antigravity/mcp_servers.json` or your project `.gemini/config`):

```json
{
  "mcpServers": {
    "neuralrig": {
      "command": "C:\\path\\to\\NeuralRigStudio\\bin\\Release\\net10.0-windows\\win-x64\\publish\\NeuralRigMcp.exe",
      "args": []
    }
  }
}
```

Or when running from source:
```json
{
  "mcpServers": {
    "neuralrig": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\NeuralRigStudio\\tools\\NeuralRigMcp\\NeuralRigMcp.csproj", "-c", "Release"]
    }
  }
}
```

---

## 3. Available MCP Tools

| Tool Name | Description | Key Parameters |
|:---|:---|:---|
| `neuralrig_get_catalog` | Returns the complete catalog of all 72 available pedals, amps, cabs, and utility devices with parameter schemas. | `category` (optional) |
| `neuralrig_get_state` | Retrieves the current live state: active preset, 16 slot device assignments, parameter values, active scene, tempo BPM, and routing mode. | None |
| `neuralrig_get_guitar_profiles` | Retrieves all saved guitar DI profiles, including detected pickup type, noise floor, harmonic richness, and suggested trim/gate. | None |
| `neuralrig_generate_tone` | Uses the built-in circuit tone architect (auto-calibrated to active guitar DI profile) to generate or refine a 16-slot rig preset. | `prompt`, `guitarProfileId` (optional), `autoSave` (optional) |
| `neuralrig_set_preset` | Applies a complete 16-slot preset JSON structure directly to the live studio. | `presetName`, `slots`, `tempoBpm` (optional), `autoSave` (optional) |
| `neuralrig_update_slot` | Modifies device selection, enabled status, tempo sync division, or parameters for a specific slot in the 2x8 matrix. | `slotIndex` (0..15), `modelId`, `enabled`, `syncDivision` (0..5), `params` |
| `neuralrig_switch_scene` | Switches the active scene (A..H), routing mode, parallel mix, or global tempo BPM. | `sceneIndex` (0..7), `routingMode` (0..1), `parallelMix`, `tempoBpm` |

---

## 4. Example Prompts for Antigravity

You can simply converse with Antigravity inside your workspace or IDE:

### Example 1: Artist Tone Recreation
> *"Can you make me a dream-pop tone inspired by The 1975, using chorus, tape delay, and a clean Fender-style amp?"*

**What happens:**
Antigravity queries the device catalog via `neuralrig_get_catalog`, constructs an optimal signal chain (Compressor -> CE-2 Chorus -> AB763 Twin Clean Preamp -> 1x12 Jensen Cab -> Tape Echo -> Shimmer Reverb), and loads it into your running studio in real time.

### Example 2: Matching Tone to Your Specific Guitar
> *"Check my guitar profile and optimize an edge-of-breakup blues lead tone for my Stratocaster."*

**What happens:**
Antigravity calls `neuralrig_get_guitar_profiles`, sees that your guitar is a *Vintage Single-Coil* with a measured noise floor of -49.5 dBFS and recommended trim of +9.0 dB. It automatically sets the noise gate threshold to -54 dBFS, applies an input trim, and builds a Plexi/TS808 crunch tone tuned specifically for single-coil clarity.

### Example 3: Fine-Tuning in Live Session
> *"The distortion has too much harsh top end. Lower the treble and mid frequency on slot (row 0, col 2) and boost the power amp sag."*

**What happens:**
Antigravity calls `neuralrig_update_slot` with target parameter values, immediately adjusting the sound without any audio dropout or click.
