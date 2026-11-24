# 03 — Native MCP Server (`NeuralRigMcp`) & `npx neuralrig-mcp`

## Architecture
- **Native Windows Binary (`tools/NeuralRigMcp/Program.cs`)**: Self-contained/framework-dependent JSON-RPC 2.0 stdio server referencing `NeuralRig.Core`, talking to live `NeuralRigStudio` over Windows Named Pipe `\\.\pipe\NeuralRigStudio_McpPipe_v1`.
- **Universal npm Wrapper (`packages/neuralrig-mcp/`)**: Zero-dependency ESM package (`node >= 18`).
  - `bin/neuralrig-mcp.mjs`: CLI router (`serve`, `install`, `print-config`, `doctor`).
  - `src/clients.mjs`: Idempotent JSON (`mcpServers.neuralrig`) and TOML (`[mcp_servers.neuralrig]`) installers for `claude-desktop`, `claude-code`, `cursor`, `windsurf`, `codex`, `gemini`, `antigravity`.
  - `src/stdio-proxy.mjs`: Resolves `NEURALRIG_MCP_BIN` or standard install paths (`%LOCALAPPDATA%\Programs\NeuralRigStudio\NeuralRigMcp.exe`); falls back to built-in JS stdio server + Named Pipe bridge if binary absent.

## Key Catalog & SPICE Rules
- Search accepts UI safe aliases, `OriginalName` (`Ibanez TS808`, `BOSS SD-1`), and compact tokens via `AntigravityToneArchitect.TryFindDevice`.
- SPICE generation pipeline (`neuralrig_get_spice_guide` -> `neuralrig_list_spice_models` -> `neuralrig_register_spice_model` -> `neuralrig_simulate_spice` -> `neuralrig_commit_spice_device`):
  - Mandatory `datasheetUrl` for new semiconductor `.MODEL` entries.
  - Mandatory schematic `schematicSourceUrl` or physical `cabAcoustic` curve. Never invent nonexistent schematics.
