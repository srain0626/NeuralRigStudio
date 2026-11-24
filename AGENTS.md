# NeuralRig Studio — AI Agent Routing Guide

> Read this file first (~90 lines). Jump only to the specific index file needed to minimize context tokens.

## Project Identity
- **Stack**: Native C# `.NET 10` (`WinForms + WebView2`) + `AVX2/FMA` DSP + React 19 / Vite (`web-ui/`) + Vercel Next.js 15 / Supabase (`cloud/`) + npm MCP (`packages/neuralrig-mcp/`).
- **Author / Maintainer**: `Ray Kim` (`raykim@users.noreply.github.com`). Do not reintroduce personal paths, usernames, or single-vendor hardware locks (`GP-100`).

## Build & Verification Commands
```powershell
npm --prefix web-ui run build                                   # Compile UI into wwwroot/
dotnet build NeuralRigStudio.sln -c Release                     # Build Core, Plugin, Host, MCP, Bench
dotnet publish tools/NeuralRigMcp/NeuralRigMcp.csproj -c Release -r win-x64 --self-contained false -o bin\Release\net10.0-windows\win-x64\publish
.\tools\RigBench\bin\Release\net10.0-windows\RigBench.exe      # Quantitative DSP/SPICE/Benchmark/Platform gate (must exit 0, RT alloc: 0 B)
node packages/neuralrig-mcp/test/verify-mcp.mjs                 # Verify npx MCP wrapper & 7 client configs
npm --prefix cloud run build                                    # Verify Next.js Cloud backend build
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1  # Produce Setup.exe + standalone.exe + SHA256SUMS.txt
```

## Non-Negotiable Engineering Invariants
1. **Zero RT Heap Allocation**: Audio callback paths (`ToneProcessor.Process`, `SignalChainEngine`, `RealtimeSpiceCircuit`, `PartitionedConvolver`, `AudioRouter`) must allocate **`0 B`**. No locks, LINQ, boxing, or transient allocations on RT thread; swap state via `Volatile.Write/Read`.
2. **Signal Chain Pool Caps**: Max `4` Amps, `4` Cabs, `4` Delays, `4` Reverbs, `3` concurrent `.nam` slots inside the `16`-slot (`2x8`) matrix.
3. **Process Lock Prevention**: Stop `NeuralRigStudio` and `NeuralRigMcp` before `dotnet build` / `publish`.
4. **Root `.csproj` Glob Safety**: `<DefaultItemExcludes>` in `NeuralRigStudio.csproj` excludes `src\**;tools\**;web-ui\**;wwwroot\**;cloud\**;packages\**;installer\**;scripts\**;dist-release\**`.
5. **PII Scrubbing**: Route all log emission and bug telemetry through `LogSanitizer.RecordLog` / `LogSanitizer.Scrub`.

## Progressive Documentation Index (Read On Demand)
| Task Domain | Compact Reference |
|:---|:---|
| Symbol-to-file + line map | [docs/INDEX.md](docs/INDEX.md) |
| DSP, WaveNet, Cab IR, SPICE & Benchmark | [docs/ai/01-dsp-and-spice.md](docs/ai/01-dsp-and-spice.md) |
| Audio I/O, IPC Bridge, Guitar Profiler & UI | [docs/ai/02-audio-and-bridge.md](docs/ai/02-audio-and-bridge.md) |
| MCP Named Pipe & `npx neuralrig-mcp` | [docs/ai/03-mcp-and-npx.md](docs/ai/03-mcp-and-npx.md) |
| Next.js Cloud, Supabase, Bug Report, OTA, Admin | [docs/ai/04-cloud-ota-admin.md](docs/ai/04-cloud-ota-admin.md) |
