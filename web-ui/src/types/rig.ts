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

export interface ChainSlotDto {
  index: number
  modelId: string
  enabled: boolean
  params: number[]
}

export interface SceneSlotStateDto {
  enabled: boolean
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
  inputGainDb: number
  outputGainDb: number
  slots: ChainSlotDto[]
  scenes?: SceneSnapshotDto[]
}

export interface PresetDef {
  index: number
  name: string
  ringMs: number
  sharedMs: number
  exclMs: number
}

export interface AppSettingsDto {
  inputDriver?: string | null
  channelMode: number
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

export interface StudioInitSnapshot {
  settings: AppSettingsDto
  presets: PresetDef[]
  audio: {
    asioDrivers: string[]
    wasapiDevices: WasapiDeviceDto[]
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
  isAudioRunning: boolean
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
