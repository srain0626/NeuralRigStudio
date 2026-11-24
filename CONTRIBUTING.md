# Contributing to NeuralRig Studio

Thank you for contributing to NeuralRig Studio. Because the audio engine operates under hard real-time constraints, all contributions must preserve zero heap allocations on the DSP callback path.

## Development Setup

1. Install **.NET 10 SDK** (Windows Desktop workload) and **Node.js 20+**.
2. Build UI & solution:
   ```bash
   npm --prefix web-ui install
   npm --prefix web-ui run build
   dotnet build NeuralRigStudio.sln -c Release
   ```
3. Run standalone quantitative DSP, MNA SPICE, and platform tests before submitting changes:
   ```bash
   ./tools/RigBench/bin/Release/net10.0-windows/RigBench.exe
   node packages/neuralrig-mcp/test/verify-mcp.mjs
   ```

## Engineering Rules

- **Audio Thread Safety**: Never allocate managed heap memory (`new`, LINQ, boxing, `string.Format`, locks) inside `ToneProcessor.Process` or `SignalChainEngine`. Verify `RT alloc: 0 B` in `RigBench.exe`.
- **Vendor Neutrality**: Do not add hardware-specific UI shortcuts tied to a single brand. Route through generic ASIO/WASAPI discovery in `AudioRouter` and `RigWebBridge`.
- **PII Hygiene**: Route diagnostics through `LogSanitizer.RecordLog` / `LogSanitizer.Scrub`. Do not commit personal paths, tokens, or emails in code or test fixtures.
- **Custom SPICE Schematics**: Do not invent circuit values or `.MODEL` parameters. Every SPICE model or device addition must cite verifiable datasheets/schematics and pass `SpiceAnalyzer` (`.OP`, `.AC`, `.TRAN`, `0 B` RT alloc).

## Pull Request Checklist

- `npm --prefix web-ui run build` passes cleanly.
- `dotnet build NeuralRigStudio.sln -c Release` succeeds with 0 errors.
- `RigBench.exe` exits with code `0`.
- `node packages/neuralrig-mcp/test/verify-mcp.mjs` and (if touching `cloud/`) `npm --prefix cloud run build` pass.
