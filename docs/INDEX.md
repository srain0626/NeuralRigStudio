# NeuralRig Studio Codebase Symbol Index

> Read small line ranges via `StartLine`/`EndLine` based on the table below instead of scanning whole files.

## C# Core DSP (`src/NeuralRig.Core/Audio/`)
| Component | Path | Key Types / Entry Points |
|:---|:---|:---|
| Core tone host | `src/NeuralRig.Core/Audio/ToneProcessor.cs` | `ToneProcessor`, `Prepare`, `Process`, `LoadCustomCabIrFromSamples`, `SoftKneeCeiling` |
| 2x8 Matrix engine | `src/NeuralRig.Core/Audio/SignalChainEngine.cs` & `.Blocks.cs` | `SignalChainEngine`, `UpdateChain`, `BlockOp`, `ProcessCustomSpice` |
| Chain & preset DTOs | `src/NeuralRig.Core/Audio/SignalChainDtos.cs` | `SignalChainConfigDto` (`SlotCount=16`), `ChainSlotDto`, `SceneSnapshotDto` |
| App persistence | `src/NeuralRig.Core/Audio/AppSettings.cs` | `AppSettings` (audio, presets, guitar profiles, `HasChosenAuthMode`, `HasCompletedTutorial`, `CloudApiBaseUrl`) |
| MNA SPICE runtime | `src/NeuralRig.Core/Audio/Spice/RealtimeSpiceCircuit.cs` | `RealtimeSpiceCircuit.Compile`, `ProcessBlock4x`, `SpiceStageRuntime` (`MaxNodes=36`) |
| SPICE analyzer | `src/NeuralRig.Core/Audio/Spice/SpiceAnalyzer.cs` | `SpiceAnalyzer.AnalyzeCircuit`, `AnalyzeCabinet`, `SpiceSimulationReport` |
| SPICE DTO & parser | `src/NeuralRig.Core/Audio/Spice/SpiceNetlistDto.cs` & `SpiceModels.cs` | `SpiceCircuitDto`, `CustomCabAcousticDto`, `SpiceValueParser`, `SpiceModelLibrary` |
| WhiteBox tube amp | `src/NeuralRig.Core/Audio/WhiteBoxAmpEngine.cs` | `WhiteBoxAmpEngine`, 4x HIIR coeffs, 12AX7 Koren preamp, KCL FMV tone stack |
| Cab IR & convolver | `src/NeuralRig.Core/Audio/CabinetIrBank.cs` & `PartitionedConvolver.cs` | `PartitionedIr` (`MaxTaps=16384`), `StereoPartitionedConvolver`, `CompileCustomCabFromMeasuredCurve` |
| Benchmarks | `src/NeuralRig.Core/Audio/RigBenchmarkSuite.cs` | `RigBenchmarkSuite.Run` (Hardware similarity, 64/128/256fr latency, 16-slot stress) |

## C# Core Services & Desktop Host
| Component | Path | Key Types / Entry Points |
|:---|:---|:---|
| Tone Architect & Catalog | `src/NeuralRig.Core/Services/AntigravityToneArchitect.cs` | `Catalog` (75+ devices), `TryFindDevice` (Alias + OriginalName), `GenerateViaBuiltinArchitect` |
| Custom Gear & Schema | `src/NeuralRig.Core/Services/SchematicWebResearchService.cs` | `CustomDeviceRegistry`, `GetSpiceAuthoringGuide`, `CustomDeviceSpecDto` |
| PII Scrub & Log Buffer | `src/NeuralRig.Core/Services/LogSanitizer.cs` | `Scrub`, `RecordLog` (400-line ring), `GetRecentLogsText` |
| Cloud & Community Hub | `src/NeuralRig.Core/Services/CloudPlatformService.cs` | `StartGoogleSignInAsync`, `SyncSettingsAsync`, `GetCommunityHubAsync`, `InstallCommunityGearAsync`, `SubmitBugReportAsync`, `GetAdminOverviewAsync` |
| OTA Updater | `src/NeuralRig.Core/Services/OtaUpdateService.cs` | `CurrentVersion="3.0.0"`, `EvaluateReleasePolicy`, `CheckForUpdateAsync`, `DownloadAndStageUpdateAsync` |
| Tone Hub Vault | `src/NeuralRig.Core/Services/ToneHubService.cs` | Curated WaveNet/LSTM/ConvNet/IR/.nrig catalog & HTTPS fetch |
| Audio I/O Router | `Audio/AudioRouter.cs` | `AudioRouter`, `GetDriverNames`, `GetLatency`, `IsProAudioInterfaceDriver` |
| WinForms & WebView2 | `MainForm.cs` & `Services/RigWebBridge.cs` | `ResolveWwwRoot` (bundle zip fallback), `HandleWebMessage`, `BuildInitSnapshot`, `McpPipeName` |
| Bench CLI | `tools/RigBench/Program.cs` | Full DSP + SPICE + `RigBenchmarkSuite` + `Platform Gate` verification suite |

## Frontend (`web-ui/src/`), MCP (`packages/`), Cloud (`cloud/`), Packaging (`installer/`, `tools/NeuralRigSetup/`)
| Area | Path | Purpose |
|:---|:---|:---|
| Types & Catalog | `web-ui/src/types/rig.ts`, `web-ui/src/data/cortexCatalog.ts` | TS DTO mirrors, 75+ catalog metadata, `1A`–`2D` showcase presets |
| Main App Shell | `web-ui/src/App.tsx` | State wiring, first-run Auth -> Tutorial gate, auto OTA check, `BENCH`/`CRAFT`/`GUIDE`/`BUG`/`SIGN IN`/`ADMIN` |
| Studio Modals | `web-ui/src/components/{BenchmarkDialog,OnboardingTutorialModal,CustomGearGuideDialog,AuthAccountModal,BugReportDialog,OtaUpdateModal,AdminConsoleDialog,ToneHubModal}.tsx` | Benchmarks, Tour, Craft Guide, OAuth/Sync, Bug Log UI, OTA blocking/optional modal, Admin Console, SPICE Hub |
| npm MCP Package | `packages/neuralrig-mcp/{bin/neuralrig-mcp.mjs,src/clients.mjs,src/stdio-proxy.mjs,test/verify-mcp.mjs}` | Cross-client installer (7 AI hosts) & stdio MCP proxy/fallback |
| Vercel Next.js Cloud | `cloud/src/app/api/**`, `cloud/supabase/migrations/001_neuralrig_cloud.sql` | 12 API routes + RLS Postgres schema |
| Windows Packaging | `installer/NeuralRigStudio.iss`, `tools/NeuralRigSetup/`, `scripts/build-release.ps1`, `.github/workflows/release.yml` | Setup.exe + standalone single-file `.exe` + CI/CD release pipeline |
