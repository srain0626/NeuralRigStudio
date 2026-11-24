export type WhiteBoxChannel = 0 | 1 | 2

export interface WhiteBoxParamsDto {
  channel: WhiteBoxChannel
  bright: boolean
  tsEnabled: boolean
  tsDrive: number
  tsTone: number
  tsLevel: number
  preGain: number
  bass: number
  mid: number
  treble: number
  master: number
  sag: number
  presence: number
  depth: number
  powerAmpEnabled: boolean
}

export const DEFAULT_WHITEBOX_PARAMS: WhiteBoxParamsDto = {
  channel: 1,
  bright: false,
  tsEnabled: false,
  tsDrive: 3.5,
  tsTone: 5.5,
  tsLevel: 6.0,
  preGain: 5.5,
  bass: 5.0,
  mid: 5.5,
  treble: 5.5,
  master: 5.5,
  sag: 3.5,
  presence: 5.0,
  depth: 5.0,
  powerAmpEnabled: true,
}

export type DeviceCategoryId =
  | "amp"
  | "nam"
  | "cab"
  | "drive"
  | "delay"
  | "reverb"
  | "mod"
  | "eq"
  | "dyn"
  | "pitch"

export interface CabMicConfigDto {
  dualEnabled: boolean
  micAType: number // 0=SM57, 1=R121, 2=C414, 3=MD421, 4=M160, 5=SM7B
  micAX: number // 0(Cap) .. 1(Edge)
  micADist: number // 0(Close) .. 1(Far)
  micALevelDb: number // -18 .. +6
  micAPan: number // -1 .. +1
  micAPhaseInv: boolean
  micBType: number
  micBX: number
  micBDist: number
  micBLevelDb: number
  micBPan: number
  micBPhaseInv: boolean
  micBDelayMs: number // 0 .. 15
  blend: number // 0(A) .. 1(B)
}

export interface ChainSlotDto {
  index: number
  modelId: string
  namModelId?: string | null
  enabled: boolean
  syncDivision?: number // 0=Free, 1=1/4, 2=1/8., 3=1/8, 4=1/8T, 5=1/16
  params: number[]
  cabMic?: CabMicConfigDto | null
}

export interface SceneSlotStateDto {
  enabled: boolean
  syncDivision?: number
  params: number[]
}

export interface SceneSnapshotDto {
  parallelMix: number
  slots: SceneSlotStateDto[]
}

export interface SignalChainConfigDto {
  presetId: string
  presetName: string
  activeScene: number
  routingMode: number // 0 = Serial, 1 = ParallelSplit
  splitCol: number
  mergeCol: number
  parallelMix: number
  tempoBpm?: number
  inputGainDb: number
  outputGainDb: number
  slots: ChainSlotDto[]
  scenes?: SceneSnapshotDto[]
}

export interface MidiBindingDto {
  targetKind:
    | "param"
    | "bypass"
    | "scene"
    | "tapTempo"
    | "tunerMute"
    | "looperRecDub"
    | "looperPlayStop"
    | "looperUndo"
    | string
  cc: number
  channel: number
  slotIndex: number
  paramIndex: number
  sceneIndex: number
}

export interface MidiSnapshotDto {
  devices: string[]
  activeDevice: string
  connected: boolean
  bindings: MidiBindingDto[]
  learning?: {
    targetKind: string
    slotIndex: number
    paramIndex: number
    sceneIndex: number
  } | null
}

export interface PresetDef {
  index: number
  name: string
  ringMs: number
  sharedMs: number
  exclMs: number
}

export interface AppSettingsDto {
  inputKind?: string
  inputDriver?: string | null
  wasapiInputDeviceId?: string | null
  wasapiInputExclusive?: boolean
  wasapiInputRaw?: boolean
  channelMode: number
  inputChannelOffset?: number
  asioInBufferSize: number
  outputKind: string
  wasapiExclusive: boolean
  wasapiDeviceId?: string | null
  asioOutDriver?: string | null
  presetIndex: number
  selectedToneId?: string | null
  toneMode: number
  toneDrivePct: number
  toneMixPct: number
  toneGainDb: number
  toneGateDb: number
  customCabIrPath?: string | null
  whiteBox?: WhiteBoxParamsDto
  signalChain?: SignalChainConfigDto
  savedPresets?: SignalChainConfigDto[]
  hasChosenAuthMode?: boolean
  hasCompletedTutorial?: boolean
  lastCloudSyncUtc?: string | null
  autoCloudSync?: boolean
  lastGuestLoginPromptDate?: string | null
  firstGoogleSignInUtc?: string | null
  hasShownSponsorPrompt?: boolean
}

export interface ModelProfileSummary {
  id: string
  name: string
  architecture: string
  archPreset: string
  sampleRate: number
  esr: number
  esrDb: number
  similarityPct: number
  createdAt: string
  namFilePath: string
  hasCabIr: boolean
}

export interface LooperSnapshotDto {
  state: "empty" | "recording" | "playing" | "overdubbing" | "stopped" | string
  loopLengthFrames: number
  playheadFrames: number
  durationSec: number
  positionSec: number
  progressPct: number
  overdubLayers: number
  canUndo: boolean
  canRedo: boolean
  halfSpeed: boolean
  reverse: boolean
  quantizeSync: boolean
  level: number
  sampleRate: number
  maxDurationSec: number
}

export interface ToneHubItemDto {
  id: string
  title: string
  author: string
  category: "nam" | "ir" | "nrig" | string
  architecture: string
  archPreset: string
  genre: string
  description: string
  tags: string[]
  esrDb: number
  sampleRate: number
  taps: number
  downloads: number
  rating: number
  isInstalled: boolean
  downloadUrl: string
}

export interface DawPluginParamDto {
  id: number
  name: string
  module: string
  unit: string
  min: number
  max: number
  defaultVal: number
  steps: number
}

export interface DawPluginInspectorDto {
  pluginName: string
  vendor: string
  version: string
  clapId: string
  vst3ProcessorCid: string
  vst3ControllerCid: string
  subCategories: string
  nativeAotCommand: string
  defaultOutputDir: string
  parameters: DawPluginParamDto[]
}

export interface RecorderReampSnapshotDto {
  recording: boolean
  recordedSec: number
  droppedFrames: number
  lastDryPath: string
  lastWetPath: string
  lastDryName: string
  recordingsDir: string
  reampAvailable: boolean
  reampEnabled: boolean
  reampPlaying: boolean
  reampLoop: boolean
  reampPositionSec: number
  reampDurationSec: number
  reampName: string
  reampPath: string
}

export interface BackingTrackStateDto {
  loaded: boolean
  playing: boolean
  loop: boolean
  volume: number
  positionSec: number
  durationSec: number
  name: string
  path: string
}

export interface TunerSnapshotDto {
  active: boolean
  noteName: string
  octave: number
  cents: number
  freqHz: number
  clarity?: number
  levelDb?: number
}

export interface AsioCapabilitiesDto {
  driverName: string
  sampleRate: number
  preferred?: number
  outputChannelsCount?: number
  inputChannels: string[]
  bufferSizes: number[]
}

export interface WasapiDeviceDto {
  id: string
  label: string
  channels?: number
  sampleRate?: number
}

export interface GuitarNoteMeasurementDto {
  stepId: string
  targetLabel: string
  targetNote: string
  targetFreqHz: number
  detectedFreqHz: number
  centsOffset: number
  peakDb: number
  rmsDb: number
  sustainSec: number
  spectralCentroidHz: number
  h2RatioDb: number
  h3RatioDb: number
  skipped: boolean
}

export interface GuitarProfileDto {
  id: string
  name: string
  createdAt: string
  estimatedPickupType: string
  noiseFloorDb: number
  hum60HzDb: number
  suggestedGateDb: number
  peakDb: number
  rmsDb: number
  crestFactorDb: number
  suggestedInputTrimDb: number
  brightnessScore: number
  warmthScore: number
  outputPowerScore: number
  sustainSec: number
  spectralCentroidHz: number
  lowBandDb: number
  lowMidBandDb: number
  midBandDb: number
  highMidBandDb: number
  trebleBandDb: number
  toneRecommendation: string
  notes: GuitarNoteMeasurementDto[]
}

export interface GuitarAnalysisLiveStateDto {
  active: boolean
  completed: boolean
  stepIndex: number
  totalSteps: number
  stepId: string
  stepTitle: string
  instruction: string
  targetNote: string
  targetFreqHz: number
  progressPct: number
  isMatched: boolean
  liveNote: string
  liveFreqHz: number
  liveCents: number
  liveLevelDb: number
  livePeakDb?: number
  statusHint: string
  draftProfile?: GuitarProfileDto | null
}

export interface SpiceKnobDefDto {
  index: number
  label: string
  min: number
  max: number
  default: number
  unit?: string
  isSwitch?: boolean
}

export interface CustomDeviceSpecDto {
  id: string
  name: string
  shortName: string
  originalName?: string
  aliases?: string[]
  category: "drive" | "amp" | "cab" | string
  categoryLabel: string
  schematicSourceUrl?: string
  schematicSummary?: string
  referencedModels?: string[]
  spiceCircuit?: {
    knobs?: SpiceKnobDefDto[]
  } | null
  defaults: number[]
  paramLabels: string[]
}

export interface DawHostInfoDto {
  isPluginHost: boolean
  format: "VST3" | "CLAP" | string
  sampleRate: number
  maxBlockSize: number
  parameterCount: number
  hostBpm: number
  hostBpmSync: boolean
}

export interface DawParamSyncEvent {
  signalChain: SignalChainConfigDto
  hostBpm: number
  hostBpmSync: boolean
  inputGainDb: number
  outputGainDb: number
  gateThresholdDb: number
  driveMultiplier: number
  wetMix: number
}

export interface StudioInitSnapshot {
  hostMode?: "standalone" | "vst3" | string
  dawHost?: DawHostInfoDto
  settings: AppSettingsDto
  presets: PresetDef[]
  audio: {
    asioDrivers: string[]
    wasapiDevices: WasapiDeviceDto[]
    wasapiInputDevices?: WasapiDeviceDto[]
    initialDriver?: string | null
    initialCaps?: AsioCapabilitiesDto | null
  }
  tones: ModelProfileSummary[]
  selectedToneId: string
  customCabIrName?: string
  whiteBox?: WhiteBoxParamsDto
  signalChain?: SignalChainConfigDto
  savedPresets?: SignalChainConfigDto[]
  guitarProfiles?: GuitarProfileDto[]
  activeGuitarProfileId?: string
  tunerMute?: boolean
  tunerRefA4Hz?: number
  backing?: BackingTrackStateDto
  looper?: LooperSnapshotDto
  recorder?: RecorderReampSnapshotDto
  midi?: MidiSnapshotDto
  customDevices?: CustomDeviceSpecDto[]
  isAudioRunning: boolean
  appVersion?: string
  cloudState?: CloudStateDto
}

export interface TelemetryFrame {
  running: boolean
  busy: boolean
  directDuplex?: boolean
  meterL: number
  meterR: number
  clip: boolean
  guitarAnalysis?: GuitarAnalysisLiveStateDto | null
  latency: {
    inMs: number
    ringMs: number
    outMs: number
    totalMs: number
  } | null
  counters: {
    padded: number
    dropped: number
    lost: number
    sampleRate: number
  }
  backing?: BackingTrackStateDto
  looper?: LooperSnapshotDto
  recorder?: RecorderReampSnapshotDto
  vis: {
    running: boolean
    hasSignal: boolean
    hasIrCurve: boolean
    peakDb: number
    bands: number[]
    peakCaps: number[]
    wave: number[]
    irCurve: number[]
    tuner?: TunerSnapshotDto
  }
}

export interface HardwareSimilarityItemDto {
  id: string
  deviceName: string
  originalHardware: string
  topologySummary: string
  metricLabel: string
  measuredValue: string
  referenceTarget: string
  deviationSummary: string
  similarityPct: number
  passed: boolean
}

export interface LatencyBenchItemDto {
  profileName: string
  sampleRate: number
  bufferFrames: number
  budgetMs: number
  avgBlockMs: number
  p99BlockMs: number
  cpuLoadPct: number
  algorithmicLatencySamples: number
  algorithmicLatencyMs: number
  estimatedRoundTripMs: number
  rtAllocBytes: number
  passed: boolean
}

export interface MultiChainStressItemDto {
  scenarioName: string
  activeBlocks: number
  routingMode: string
  avgBlockMs: number
  peakDbfs: number
  rmsDbfs: number
  tailDcOffsetDbfs: number
  finiteSamplesOnly: boolean
  rtAllocBytes: number
  passed: boolean
  detail: string
}

export interface RigBenchmarkReportDto {
  generatedAtUtc: string
  sampleRate: number
  liveAudioRoundTripMs?: number | null
  overallSimilarityPct: number
  minBuffer64Pass: boolean
  zeroRtAllocAll: boolean
  overallPassed: boolean
  totalDurationMs: number
  similarityItems: HardwareSimilarityItemDto[]
  latencyItems: LatencyBenchItemDto[]
  multiChainItems: MultiChainStressItemDto[]
}

export interface CloudUserDto {
  id: string
  email: string
  displayName: string
  avatarUrl?: string
  isAdmin: boolean
}

export interface CloudStateDto {
  mode: "unselected" | "guest" | "google" | string
  isConfigured: boolean
  isAuthenticated: boolean
  user?: CloudUserDto | null
  lastSyncUtc?: string | null
  pendingBugReportsCount: number
  lastGuestLoginPromptDate?: string | null
  firstGoogleSignInUtc?: string | null
  hasShownSponsorPrompt?: boolean
  shouldShowGuestDailyLoginPrompt?: boolean
  shouldShowSponsorPrompt?: boolean
}

export interface CommunityGearItemDto {
  id: string
  ownerId: string
  ownerName: string
  slug: string
  name: string
  shortName: string
  originalName?: string
  aliases?: string[]
  category: "drive" | "amp" | "cab" | "preset" | string
  summary: string
  schematicSourceUrl?: string
  tags: string[]
  downloads: number
  likes: number
  createdAt: string
  specJson?: string
  spec?: CustomDeviceSpecDto | null
}

export interface OtaCheckResultDto {
  checked: boolean
  currentVersion: string
  latestVersion: string
  minSupportedVersion: string
  updateAvailable: boolean
  forceUpdate: boolean
  releaseNotesKo: string
  releaseNotesEn: string
  installerUrl?: string
  standaloneExeUrl?: string
  sha256Installer?: string
  releasePageUrl: string
  source: string
  error?: string
}

export interface AdminBugItemDto {
  id: string
  title: string
  category: string
  severity: string
  status: string
  appVersion: string
  userEmail?: string
  description: string
  sanitizedLogs: string
  diagnosticsJson?: string
  createdAt: string
}

export interface AdminReleasePolicyDto {
  version: string
  minSupportedVersion: string
  forceUpdate: boolean
  releaseNotesKo: string
  releaseNotesEn: string
  installerUrl?: string
  standaloneExeUrl?: string
  updatedAt?: string
}

export interface AdminOverviewDto {
  ok: boolean
  source: string
  kpis: {
    totalUsers: number
    active24hUsers: number
    totalSyncedConfigs: number
    communityGearCount: number
    openBugCount: number
    totalBugCount: number
    totalHubDownloads: number
  }
  topGears: { name: string; category: string; downloads: number; ownerName: string }[]
  recentTelemetry: { eventType: string; appVersion: string; platform: string; createdAt: string }[]
  bugs: AdminBugItemDto[]
  releasePolicy: AdminReleasePolicyDto
}


