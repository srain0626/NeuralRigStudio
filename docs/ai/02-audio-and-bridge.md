# 02 — Audio Routing, IPC Bridge & React Console

## AudioRouter (`Audio/AudioRouter.cs`)
- Driver-agnostic device discovery prioritizing pro interfaces (`IsProAudioInterfaceDriver` in `RigWebBridge.cs`) over onboard codecs (`Realtek`). No single vendor lock-in.
- Modes: `AsioDirectDuplex`, `AsioIn -> RingBuffer -> Wasapi` (Shared/Exclusive + MMCSS `"Pro Audio"`), `Wasapi` Line-In capture (`WasapiInputRaw = true` bypasses Windows APO speech filters), and Dual-ASIO.
- Zero-alloc `SincResampler` bridges `44.1 kHz <-> 48 kHz` clocks transparently.
- Supports `ChannelMode`: `0 = Stereo (Offset, Offset+1)`, `1 = Mono (Offset)` across multi-channel hardware.

## WebView2 Bridge (`Services/RigWebBridge.cs`)
- UI invokes native async/sync actions via `window.chrome.webview.postMessage({ reqId, action, payload })`.
- Important action groups:
  - Audio & MIDI: `init`, `refreshAudioDevices`, `startMonitoring`, `stopMonitoring`, `startMidiLearn`, `clearMidiBinding`
  - DSP & Chain: `updateSignalChain`, `saveUserPreset`, `generateAiTone`, `loadCustomCabIr`, `runBenchmarkSuite`
  - Profiling & Tools: `startGuitarAnalysis`, `looperCommand`, `recorderCommand`, `exportNrigBundle`, `exportDawPlugin`
  - v3.0 Platform: `updateOnboardingState`, `cloudSignInGoogle`, `cloudContinueGuest`, `cloudSyncNow`, `getCommunityHub`, `uploadCommunityGear`, `installCommunityGear`, `submitBugReport`, `checkOtaUpdate`, `applyOtaUpdate`, `getAdminOverview`
- Outbound events pushed via `EmitEvent(type, data)`: `telemetry` (~50 fps), `log`, `audioState`, `signalChainChanged`, `customDevicesChanged`, `cloudStateChanged`.

## Packaging & Bundle Resolution (`MainForm.cs`)
- `ResolveWwwRoot()` searches local `wwwroot/` folders first; in single-file `.exe` builds (`EmbedWwwRootBundle=true`), extracts `NeuralRigStudio.wwwroot.zip` once to `%LOCALAPPDATA%\NeuralRigStudio\wwwroot_bundle`.
