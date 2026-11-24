## Summary
<!-- Describe the fix, DSP change, SPICE model, or cloud/UI feature concisely. -->

## Verification Checklist
- [ ] `npm --prefix web-ui run build` passes (0 TS errors)
- [ ] `dotnet build NeuralRigStudio.sln -c Release` passes (0 errors)
- [ ] `RigBench.exe` passes completely with `RT alloc: 0 B`
- [ ] `node packages/neuralrig-mcp/test/verify-mcp.mjs` passes (if touching MCP)
- [ ] `npm --prefix cloud run build` passes (if touching `cloud/`)
- [ ] Verified no personal paths/tokens or hardcoded vendor locks introduced
