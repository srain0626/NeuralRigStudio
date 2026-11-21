import type { ChainSlotDto, DeviceCategoryId, SceneSnapshotDto, SignalChainConfigDto } from "@/types/rig"

export interface DeviceParamDef {
  index: number
  label: string
  unit?: string
  min: number
  max: number
  step: number
  defaultVal: number
  kind?: "knob" | "toggle"
  onLabel?: string
  offLabel?: string
}

export interface DeviceModelDef {
  id: string
  name: string
  shortName: string
  category: DeviceCategoryId
  categoryLabel: string
  params: DeviceParamDef[]
}

export interface CategoryMeta {
  id: DeviceCategoryId
  label: string
  colorVar: string
  hex: string
}

export const CATEGORIES: CategoryMeta[] = [
  { id: "amp", label: "AMP", colorVar: "var(--cat-amp)", hex: "#ef233c" },
  { id: "nam", label: "NEURAL CAPTURE", colorVar: "var(--cat-nam)", hex: "#e4e4e7" },
  { id: "cab", label: "CAB", colorVar: "var(--cat-cab)", hex: "#7c5cff" },
  { id: "drive", label: "OVERDRIVE", colorVar: "var(--cat-drive)", hex: "#f97316" },
  { id: "delay", label: "DELAY", colorVar: "var(--cat-delay)", hex: "#14b8a6" },
  { id: "reverb", label: "REVERB", colorVar: "var(--cat-reverb)", hex: "#2dd4bf" },
  { id: "mod", label: "MODULATION", colorVar: "var(--cat-mod)", hex: "#22c55e" },
  { id: "eq", label: "EQ", colorVar: "var(--cat-eq)", hex: "#3b82f6" },
  { id: "dyn", label: "DYNAMICS", colorVar: "var(--cat-dyn)", hex: "#94a3b8" },
]

export const CATEGORY_MAP: Record<DeviceCategoryId, CategoryMeta> = Object.fromEntries(
  CATEGORIES.map((c) => [c.id, c])
) as Record<DeviceCategoryId, CategoryMeta>

const AMP_PARAMS: DeviceParamDef[] = [
  { index: 7, label: "BRIGHT", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "On", offLabel: "Off" },
  { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
  { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
  { index: 2, label: "MID", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
  { index: 3, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
  { index: 4, label: "MASTER", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
  { index: 5, label: "SAG", min: 0, max: 10, step: 0.1, defaultVal: 3.5 },
  { index: 6, label: "PRESENCE", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
]

export const DEVICE_CATALOG: DeviceModelDef[] = [
  // ── AMP ──
  {
    id: "amp_plexi",
    name: "Brit Plexi 100 Bright",
    shortName: "Plexi 100",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 6.2 } : p)),
  },
  {
    id: "amp_2203",
    name: "Marshall JCM800 (2203)",
    shortName: "JCM800",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 6.8 } : p.index === 2 ? { ...p, defaultVal: 6.2 } : p)),
  },
  {
    id: "amp_twin",
    name: "US Clean Twin",
    shortName: "US Twin",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 3.2 } : p.index === 2 ? { ...p, defaultVal: 4.5 } : p)),
  },
  {
    id: "amp_1star",
    name: "CA 1Star Clean 100W",
    shortName: "CA 1Star",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 3.8 } : p.index === 5 ? { ...p, defaultVal: 4.5 } : p)),
  },
  {
    id: "amp_slo100",
    name: "SLO 100 Lead",
    shortName: "SLO 100",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 6.5 } : p.index === 6 ? { ...p, defaultVal: 6.0 } : p)),
  },
  {
    id: "amp_5153",
    name: "5153 Red High-Gain",
    shortName: "5153 Red",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 7.2 } : p.index === 2 ? { ...p, defaultVal: 4.6 } : p
    ),
  },
  {
    id: "amp_jc120",
    name: "Roland JC-120 Jazz Chorus",
    shortName: "JC-120",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: [
      { index: 7, label: "BRIGHT", min: 0, max: 1, step: 1, defaultVal: 1, kind: "toggle", onLabel: "On", offLabel: "Off" },
      { index: 0, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
      { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 4.8 },
      { index: 2, label: "MID", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 3, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 6.2 },
      { index: 4, label: "MASTER", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
    ],
  },
  {
    id: "amp_ac30",
    name: "VOX AC30 Top Boost",
    shortName: "AC30 TB",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 5.4 } : p.index === 3 ? { ...p, defaultVal: 6.6 } : p
    ),
  },
  {
    id: "amp_dsl",
    name: "Marshall JCM2000 DSL",
    shortName: "JCM2000",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 0 ? { ...p, defaultVal: 6.6 } : p.index === 2 ? { ...p, defaultVal: 6.0 } : p.index === 6 ? { ...p, defaultVal: 6.2 } : p
    ),
  },
  {
    id: "amp_recto",
    name: "Mesa/Boogie Dual Rectifier",
    shortName: "Dual Recto",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 7.4 } : p.index === 2 ? { ...p, defaultVal: 4.4 } : p
    ),
  },
  {
    id: "amp_be100",
    name: "Friedman BE-100",
    shortName: "BE-100",
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 0 ? { ...p, defaultVal: 6.9 } : p.index === 2 ? { ...p, defaultVal: 6.4 } : p.index === 6 ? { ...p, defaultVal: 6.0 } : p
    ),
  },

  // ── NEURAL CAPTURE (.nam) ──
  {
    id: "nam_capture",
    name: "NAM Neural Capture",
    shortName: "NAM",
    category: "nam",
    categoryLabel: "Neural Capture",
    params: [
      { index: 0, label: "INPUT", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
      { index: 1, label: "OUTPUT", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 100 },
    ],
  },

  // ── CAB ──
  {
    id: "cab_v30",
    name: "4x12 V30 Modern",
    shortName: "4x12 V30",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 80 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 9500 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_custom",
    name: "Custom WAV IR",
    shortName: "Custom IR",
    category: "cab",
    categoryLabel: "Cabinet IR",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 75 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 10500 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_jc120",
    name: "2x12 JC-120 Open Back",
    shortName: "2x12 JC120",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 65 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 12500 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_alnico",
    name: "2x12 VOX Alnico Blue",
    shortName: "2x12 Blue",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 75 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 11000 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_1960a",
    name: "Marshall 1960A (G12T-75)",
    shortName: "1960A",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 85 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 9800 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_1960bv",
    name: "Marshall 1960BV (4x12 V30)",
    shortName: "1960BV",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 80 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 9600 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_mesa412",
    name: "Mesa/Boogie Rectifier 4x12",
    shortName: "Recto 4x12",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 90 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 9200 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "cab_jensen112",
    name: "1x12 Fender Jensen Open",
    shortName: "1x12 Jensen",
    category: "cab",
    categoryLabel: "Cabinet",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 70 },
      { index: 1, label: "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: 11800 },
      { index: 2, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },

  // ── OVERDRIVE / DISTORTION / FUZZ (Famous Pedals) ──
  {
    id: "od_ts808",
    name: "Green 808 (TS808)",
    shortName: "Green 808",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "DRIVE", min: 0, max: 10, step: 0.1, defaultVal: 3.5 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 2, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 6.2 },
    ],
  },
  {
    id: "od_ts9",
    name: "Ibanez TS9 Tube Screamer",
    shortName: "TS9",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "DRIVE", min: 0, max: 10, step: 0.1, defaultVal: 3.8 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 6.0 },
      { index: 2, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 6.4 },
    ],
  },
  {
    id: "od_janray",
    name: "Vemuram Jan Ray",
    shortName: "Jan Ray",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
      { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 2, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 6.0 },
      { index: 3, label: "SATURATION", min: 0, max: 10, step: 0.1, defaultVal: 4.8 },
      { index: 4, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
    ],
  },
  {
    id: "od_puresky",
    name: "Caline Pure Sky",
    shortName: "Pure Sky",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 4.0 },
      { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
      { index: 2, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
      { index: 3, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
    ],
  },
  {
    id: "od_bbpreamp",
    name: "Xotic BB Preamp",
    shortName: "BB Preamp",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
      { index: 2, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 3, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
    ],
  },
  {
    id: "od_ocd",
    name: "Fulltone OCD",
    shortName: "OCD",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 3, label: "PEAK", min: 0, max: 1, step: 1, defaultVal: 1, kind: "toggle", onLabel: "HP", offLabel: "LP" },
      { index: 0, label: "DRIVE", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
    ],
  },
  {
    id: "dist_rat2",
    name: "ProCo RAT 2",
    shortName: "RAT 2",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DISTORTION", min: 0, max: 10, step: 0.1, defaultVal: 6.2 },
      { index: 1, label: "FILTER", min: 0, max: 10, step: 0.1, defaultVal: 4.4 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
    ],
  },
  {
    id: "dist_ecstasy",
    name: "Bogner Ecstasy Red",
    shortName: "Ecstasy Red",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 6.8 },
      { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 2, label: "MIDDLE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 3, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 6.0 },
      { index: 4, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
    ],
  },
  {
    id: "boost_microamp",
    name: "MXR Micro Amp",
    shortName: "Micro Amp",
    category: "drive",
    categoryLabel: "Boost",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
    ],
  },
  {
    id: "od_klon",
    name: "Mythic Centaur (Klon)",
    shortName: "Centaur",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 4.2 },
      { index: 1, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 2, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
    ],
  },
  {
    id: "dist_rat",
    name: "Rodent Dist (RAT)",
    shortName: "Rodent",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DISTORTION", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 1, label: "FILTER", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
    ],
  },
  {
    id: "fuzz_bigmuff",
    name: "Big Pi Fuzz (Muff)",
    shortName: "Big Pi",
    category: "drive",
    categoryLabel: "Fuzz",
    params: [
      { index: 0, label: "SUSTAIN", min: 0, max: 10, step: 0.1, defaultVal: 6.5 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
    ],
  },
  {
    id: "od_sd1",
    name: "BOSS SD-1 Super OverDrive",
    shortName: "BOSS SD-1",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "DRIVE", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 2, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
    ],
  },
  {
    id: "od_bd2",
    name: "BOSS BD-2 Blues Driver",
    shortName: "BOSS BD-2",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 4.6 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
      { index: 2, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
    ],
  },
  {
    id: "dist_ds1",
    name: "BOSS DS-1 Distortion",
    shortName: "BOSS DS-1",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DIST", min: 0, max: 10, step: 0.1, defaultVal: 6.2 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 4.2 },
      { index: 2, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
    ],
  },
  {
    id: "dist_mt2",
    name: "BOSS MT-2 Metal Zone",
    shortName: "BOSS MT-2",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DIST", min: 0, max: 10, step: 0.1, defaultVal: 7.2 },
      { index: 1, label: "HIGH", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 2.5 },
      { index: 2, label: "LOW", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 3.0 },
      { index: 3, label: "MID", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: -4.0 },
      { index: 4, label: "MID FREQ", unit: "Hz", min: 200, max: 5000, step: 25, defaultVal: 950 },
      { index: 5, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
    ],
  },
  {
    id: "dist_hm2",
    name: "BOSS HM-2 Heavy Metal",
    shortName: "BOSS HM-2",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DIST", min: 0, max: 10, step: 0.1, defaultVal: 8.5 },
      { index: 1, label: "LOW", min: 0, max: 10, step: 0.1, defaultVal: 8.5 },
      { index: 2, label: "HIGH", min: 0, max: 10, step: 0.1, defaultVal: 9.0 },
      { index: 3, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
    ],
  },
  {
    id: "boost_ep",
    name: "Xotic EP Booster",
    shortName: "EP Booster",
    category: "drive",
    categoryLabel: "Boost",
    params: [
      { index: 1, label: "BRIGHT", min: 0, max: 1, step: 1, defaultVal: 1, kind: "toggle", onLabel: "On", offLabel: "Off" },
      { index: 0, label: "BOOST", min: 0, max: 10, step: 0.1, defaultVal: 4.0 },
    ],
  },
  {
    id: "boost_rc",
    name: "Xotic RC Booster",
    shortName: "RC Booster",
    category: "drive",
    categoryLabel: "Boost",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 2.5 },
      { index: 1, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 6.0 },
      { index: 2, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 3, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
    ],
  },
  {
    id: "od_bluesbreaker",
    name: "Marshall Bluesbreaker MK1",
    shortName: "Bluesbreaker",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 6.0 },
    ],
  },
  {
    id: "dist_guvnor",
    name: "Marshall Guv'nor MK1",
    shortName: "Guv'nor",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 6.4 },
      { index: 1, label: "BASS", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 2, label: "MIDDLE", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
      { index: 3, label: "TREBLE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 4, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
    ],
  },
  {
    id: "fuzz_face",
    name: "Dallas Fuzz Face (Ge/Si)",
    shortName: "Fuzz Face",
    category: "drive",
    categoryLabel: "Fuzz",
    params: [
      { index: 3, label: "TRANSISTOR", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "Si BC108", offLabel: "Ge AC128" },
      { index: 0, label: "FUZZ", min: 0, max: 10, step: 0.1, defaultVal: 7.5 },
      { index: 1, label: "BIAS", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
    ],
  },
  {
    id: "boost_rangemaster",
    name: "Dallas Rangemaster (OC44)",
    shortName: "Rangemaster",
    category: "drive",
    categoryLabel: "Treble Boost",
    params: [
      { index: 1, label: "INPUT CAP", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "Full 22n", offLabel: "Treb 4n7" },
      { index: 0, label: "BOOST", min: 0, max: 10, step: 0.1, defaultVal: 6.5 },
    ],
  },
  {
    id: "dist_plus",
    name: "MXR Distortion+",
    shortName: "Distortion+",
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DISTORTION", min: 0, max: 10, step: 0.1, defaultVal: 6.6 },
      { index: 1, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
    ],
  },
  {
    id: "od_boxofrock",
    name: "ZVEX Box of Rock (JTM45)",
    shortName: "Box of Rock",
    category: "drive",
    categoryLabel: "Overdrive",
    params: [
      { index: 0, label: "DRIVE", min: 0, max: 10, step: 0.1, defaultVal: 6.0 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
      { index: 2, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
    ],
  },

  // ── DELAY ──
  {
    id: "dly_dm2w",
    name: "BOSS DM-2W Delay",
    shortName: "BOSS DM-2W",
    category: "delay",
    categoryLabel: "Delay",
    params: [
      { index: 3, label: "CUSTOM", min: 0, max: 1, step: 1, defaultVal: 1, kind: "toggle", onLabel: "Custom", offLabel: "Std" },
      { index: 0, label: "REPEAT RATE", unit: "ms", min: 40, max: 800, step: 5, defaultVal: 340 },
      { index: 1, label: "INTENSITY", unit: "%", min: 0, max: 85, step: 1, defaultVal: 38 },
      { index: 2, label: "ECHO", unit: "%", min: 0, max: 100, step: 1, defaultVal: 28 },
    ],
  },
  {
    id: "dly_dd3t",
    name: "BOSS DD-3T Digital Delay",
    shortName: "BOSS DD-3T",
    category: "delay",
    categoryLabel: "Delay",
    params: [
      { index: 0, label: "D.TIME", unit: "ms", min: 20, max: 800, step: 5, defaultVal: 380 },
      { index: 1, label: "F.BACK", unit: "%", min: 0, max: 85, step: 1, defaultVal: 34 },
      { index: 2, label: "E.LEVEL", unit: "%", min: 0, max: 100, step: 1, defaultVal: 26 },
    ],
  },
  {
    id: "dly_analog",
    name: "Analog BBD Delay",
    shortName: "Analog Dly",
    category: "delay",
    categoryLabel: "Delay",
    params: [
      { index: 0, label: "TIME", unit: "ms", min: 40, max: 1200, step: 5, defaultVal: 360 },
      { index: 1, label: "FEEDBACK", unit: "%", min: 0, max: 85, step: 1, defaultVal: 36 },
      { index: 2, label: "TONE", unit: "%", min: 0, max: 100, step: 1, defaultVal: 48 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 26 },
    ],
  },
  {
    id: "dly_digital",
    name: "Digital Stereo Delay",
    shortName: "Stereo Dly",
    category: "delay",
    categoryLabel: "Delay",
    params: [
      { index: 0, label: "TIME", unit: "ms", min: 40, max: 1350, step: 5, defaultVal: 420 },
      { index: 1, label: "FEEDBACK", unit: "%", min: 0, max: 85, step: 1, defaultVal: 34 },
      { index: 2, label: "MOD", unit: "%", min: 0, max: 100, step: 1, defaultVal: 18 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 24 },
    ],
  },

  {
    id: "dly_timeline",
    name: "Strymon TimeLine",
    shortName: "TimeLine",
    category: "delay",
    categoryLabel: "Delay",
    params: [
      { index: 4, label: "MACHINE", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "dTape", offLabel: "dBucket" },
      { index: 0, label: "TIME", unit: "ms", min: 40, max: 1350, step: 5, defaultVal: 430 },
      { index: 1, label: "REPEATS", unit: "%", min: 0, max: 85, step: 1, defaultVal: 36 },
      { index: 2, label: "MOD / FILTER", unit: "%", min: 0, max: 100, step: 1, defaultVal: 28 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 26 },
    ],
  },

  // ── REVERB ──
  {
    id: "rev_bluesky",
    name: "Strymon blueSky",
    shortName: "blueSky",
    category: "reverb",
    categoryLabel: "Reverb",
    params: [
      { index: 0, label: "DECAY", unit: "%", min: 5, max: 100, step: 1, defaultVal: 66 },
      { index: 1, label: "PRE-DELAY", unit: "ms", min: 0, max: 120, step: 1, defaultVal: 22 },
      { index: 2, label: "HIGH DAMP", unit: "%", min: 0, max: 100, step: 1, defaultVal: 36 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 32 },
      { index: 4, label: "SHIMMER", unit: "%", min: 0, max: 100, step: 1, defaultVal: 28 },
    ],
  },
  {
    id: "rev_rv6",
    name: "BOSS RV-6 Reverb",
    shortName: "BOSS RV-6",
    category: "reverb",
    categoryLabel: "Reverb",
    params: [
      { index: 0, label: "E.LEVEL", unit: "%", min: 0, max: 100, step: 1, defaultVal: 28 },
      { index: 1, label: "TONE", unit: "%", min: 0, max: 100, step: 1, defaultVal: 58 },
      { index: 2, label: "TIME", unit: "%", min: 5, max: 100, step: 1, defaultVal: 56 },
      { index: 3, label: "MODE (0:Hall 1:Plate 2:Spr 3:Shim)", min: 0, max: 3, step: 1, defaultVal: 0 },
    ],
  },
  {
    id: "rev_spring",
    name: "Spring Reverb",
    shortName: "Spring",
    category: "reverb",
    categoryLabel: "Reverb",
    params: [
      { index: 0, label: "DECAY", unit: "%", min: 5, max: 100, step: 1, defaultVal: 48 },
      { index: 1, label: "DAMPING", unit: "%", min: 0, max: 100, step: 1, defaultVal: 42 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 25 },
    ],
  },
  {
    id: "rev_hall",
    name: "Hall Ambient Reverb",
    shortName: "Hall Rev",
    category: "reverb",
    categoryLabel: "Reverb",
    params: [
      { index: 0, label: "DECAY", unit: "%", min: 5, max: 100, step: 1, defaultVal: 58 },
      { index: 1, label: "DAMPING", unit: "%", min: 0, max: 100, step: 1, defaultVal: 45 },
      { index: 2, label: "PRE-DELAY", unit: "ms", min: 0, max: 120, step: 1, defaultVal: 24 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 26 },
    ],
  },
  {
    id: "rev_plate",
    name: "Plate Studio Reverb",
    shortName: "Plate Rev",
    category: "reverb",
    categoryLabel: "Reverb",
    params: [
      { index: 0, label: "DECAY", unit: "%", min: 5, max: 100, step: 1, defaultVal: 52 },
      { index: 1, label: "DAMPING", unit: "%", min: 0, max: 100, step: 1, defaultVal: 32 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 22 },
    ],
  },

  // ── MODULATION ──
  {
    id: "mod_ce2",
    name: "BOSS CE-2 Chorus",
    shortName: "BOSS CE-2",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 0, label: "RATE", unit: "Hz", min: 0.1, max: 5.5, step: 0.05, defaultVal: 1.1 },
      { index: 1, label: "DEPTH", unit: "%", min: 0, max: 100, step: 1, defaultVal: 48 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 45 },
    ],
  },
  {
    id: "mod_dc2w",
    name: "BOSS DC-2W Dimension C",
    shortName: "BOSS DC-2W",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 0, label: "MODE (1-4)", min: 1, max: 4, step: 1, defaultVal: 2 },
      { index: 1, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 60 },
    ],
  },
  {
    id: "mod_bf2",
    name: "BOSS BF-2 Flanger",
    shortName: "BOSS BF-2",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 0, label: "MANUAL", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
      { index: 1, label: "DEPTH", unit: "%", min: 0, max: 100, step: 1, defaultVal: 55 },
      { index: 2, label: "RATE", unit: "Hz", min: 0.1, max: 5.5, step: 0.05, defaultVal: 0.65 },
      { index: 3, label: "RES", unit: "%", min: 0, max: 85, step: 1, defaultVal: 48 },
    ],
  },
  {
    id: "mod_phase90",
    name: "Phase 90 Phaser",
    shortName: "Phase 90",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 0, label: "SPEED", unit: "Hz", min: 0.1, max: 5.5, step: 0.05, defaultVal: 0.8 },
      { index: 1, label: "DEPTH", unit: "%", min: 0, max: 100, step: 1, defaultVal: 65 },
      { index: 2, label: "FEEDBACK", unit: "%", min: 0, max: 80, step: 1, defaultVal: 35 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 50 },
    ],
  },
  {
    id: "mod_tremolo",
    name: "Optical Tremolo",
    shortName: "Tremolo",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 2, label: "WAVE", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "Square", offLabel: "Sine" },
      { index: 0, label: "RATE", unit: "Hz", min: 0.5, max: 12, step: 0.1, defaultVal: 4.5 },
      { index: 1, label: "DEPTH", unit: "%", min: 0, max: 100, step: 1, defaultVal: 55 },
    ],
  },
  {
    id: "mod_smallclone",
    name: "EHX Small Clone Chorus",
    shortName: "Small Clone",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 1, label: "DEPTH", min: 0, max: 1, step: 1, defaultVal: 1, kind: "toggle", onLabel: "Deep", offLabel: "Normal" },
      { index: 0, label: "RATE", unit: "Hz", min: 0.1, max: 5.5, step: 0.05, defaultVal: 0.85 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 52 },
    ],
  },
  {
    id: "mod_univibe",
    name: "Shin-ei Uni-Vibe (1968)",
    shortName: "Uni-Vibe",
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 2, label: "MODE", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "Vibrato", offLabel: "Chorus" },
      { index: 0, label: "SPEED", unit: "Hz", min: 0.2, max: 8.0, step: 0.05, defaultVal: 1.6 },
      { index: 1, label: "INTENSITY", unit: "%", min: 0, max: 100, step: 1, defaultVal: 72 },
    ],
  },

  // ── EQ ──
  {
    id: "eq_crybaby",
    name: "Dunlop Cry Baby GCB-95 Wah",
    shortName: "Cry Baby",
    category: "eq",
    categoryLabel: "Wah / Filter",
    params: [
      { index: 0, label: "PEDAL", unit: "%", min: 0, max: 100, step: 1, defaultVal: 52 },
      { index: 1, label: "AUTO ENV", unit: "%", min: 0, max: 100, step: 1, defaultVal: 35 },
      { index: 2, label: "RESONANCE", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 100 },
    ],
  },
  {
    id: "eq_ge7",
    name: "BOSS GE-7 Graphic EQ",
    shortName: "BOSS GE-7",
    category: "eq",
    categoryLabel: "EQ",
    params: [
      { index: 0, label: "100 Hz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 1, label: "200 Hz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 2, label: "400 Hz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 3, label: "800 Hz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 4, label: "1.6 kHz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 5, label: "3.2 kHz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 6, label: "6.4 kHz", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
      { index: 7, label: "LEVEL", unit: "dB", min: -15, max: 15, step: 0.5, defaultVal: 0 },
    ],
  },
  {
    id: "eq_graphic5",
    name: "Graphic 5-Band EQ",
    shortName: "5-Band EQ",
    category: "eq",
    categoryLabel: "EQ",
    params: [
      { index: 0, label: "100 Hz", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 0 },
      { index: 1, label: "300 Hz", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 0 },
      { index: 2, label: "800 Hz", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 0 },
      { index: 3, label: "2.2 kHz", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 0 },
      { index: 4, label: "5.5 kHz", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 0 },
      { index: 5, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 0 },
    ],
  },
  {
    id: "eq_parametric",
    name: "Parametric EQ",
    shortName: "Param EQ",
    category: "eq",
    categoryLabel: "EQ",
    params: [
      { index: 0, label: "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: 70 },
      { index: 1, label: "MID FREQ", unit: "Hz", min: 150, max: 5000, step: 25, defaultVal: 900 },
      { index: 2, label: "MID GAIN", unit: "dB", min: -12, max: 12, step: 0.5, defaultVal: 2.0 },
      { index: 3, label: "MID Q", min: 0.4, max: 6.0, step: 0.1, defaultVal: 1.4 },
      { index: 4, label: "HIGH CUT", unit: "Hz", min: 2500, max: 16000, step: 100, defaultVal: 11000 },
    ],
  },

  // ── DYNAMICS ──
  {
    id: "gate_adaptive",
    name: "Adaptive Noise Gate",
    shortName: "Gate",
    category: "dyn",
    categoryLabel: "Dynamics",
    params: [
      { index: 0, label: "THRESHOLD", unit: "dB", min: -80, max: -20, step: 1, defaultVal: -56 },
      { index: 1, label: "DECAY", unit: "ms", min: 20, max: 400, step: 5, defaultVal: 120 },
      { index: 2, label: "HOLD", unit: "ms", min: 10, max: 300, step: 5, defaultVal: 80 },
    ],
  },
  {
    id: "comp_sp",
    name: "Xotic SP Compressor",
    shortName: "SP Comp",
    category: "dyn",
    categoryLabel: "Compressor",
    params: [
      { index: 0, label: "BLEND", unit: "%", min: 0, max: 100, step: 1, defaultVal: 55 },
      { index: 1, label: "VOLUME", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
      { index: 2, label: "COMP (0=Lo 1=Mid 2=Hi)", min: 0, max: 2, step: 1, defaultVal: 1 },
    ],
  },
  {
    id: "comp_orangesqueezer",
    name: "Dan Armstrong Orange Squeezer",
    shortName: "Orange Sqz",
    category: "dyn",
    categoryLabel: "Compressor",
    params: [
      { index: 0, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.6 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.4 },
      { index: 2, label: "BIAS / SQUASH", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 100 },
    ],
  },
  {
    id: "comp_cs3",
    name: "BOSS CS-3 Comp Sustainer",
    shortName: "BOSS CS-3",
    category: "dyn",
    categoryLabel: "Compressor",
    params: [
      { index: 0, label: "LEVEL", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 1, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 2, label: "ATTACK", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
      { index: 3, label: "SUSTAIN", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
    ],
  },
  {
    id: "comp_dyna",
    name: "Dyna Comp",
    shortName: "Dyna Comp",
    category: "dyn",
    categoryLabel: "Compressor",
    params: [
      { index: 0, label: "SENSITIVITY", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
      { index: 1, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 2, label: "ATTACK", unit: "ms", min: 1, max: 50, step: 1, defaultVal: 12 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 100 },
    ],
  },
  {
    id: "comp_1176",
    name: "1176 Studio Comp",
    shortName: "1176 Comp",
    category: "dyn",
    categoryLabel: "Compressor",
    params: [
      { index: 0, label: "INPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
      { index: 1, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 2, label: "RATIO", min: 2, max: 20, step: 1, defaultVal: 4 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 85 },
    ],
  },
]

export const DEVICE_MAP: Record<string, DeviceModelDef> = Object.fromEntries(
  DEVICE_CATALOG.map((d) => [d.id, d])
)

export function createDefaultParams(modelId: string): number[] {
  const arr = [0, 0, 0, 0, 0, 0, 0, 0]
  const def = DEVICE_MAP[modelId]
  if (!def) return arr
  for (const p of def.params) {
    if (p.index >= 0 && p.index < 8) arr[p.index] = p.defaultVal
  }
  return arr
}

export function createEmptySlots(): ChainSlotDto[] {
  return Array.from({ length: 16 }, (_, index) => ({
    index,
    modelId: "",
    enabled: true,
    params: [0, 0, 0, 0, 0, 0, 0, 0],
  }))
}

function makePreset(
  presetId: string,
  presetName: string,
  routingMode: number,
  splitCol: number,
  mergeCol: number,
  parallelMix: number,
  entries: Array<{ index: number; modelId: string; enabled?: boolean; overrides?: Record<number, number> }>
): SignalChainConfigDto {
  const slots = createEmptySlots()
  for (const e of entries) {
    const params = createDefaultParams(e.modelId)
    if (e.overrides) {
      for (const [k, v] of Object.entries(e.overrides)) {
        params[Number(k)] = v
      }
    }
    slots[e.index] = {
      index: e.index,
      modelId: e.modelId,
      enabled: e.enabled ?? true,
      params,
    }
  }
  return {
    presetId,
    presetName,
    activeScene: 3, // Scene D active by default like Cortex screenshot
    routingMode,
    splitCol,
    mergeCol,
    parallelMix,
    inputGainDb: 0,
    outputGainDb: 0,
    slots,
  }
}

export const FACTORY_PRESETS: SignalChainConfigDto[] = [
  makePreset("1A", "Beast Lead", 1, 1, 5, 0.42, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -56, 1: 110, 2: 75 } },
    { index: 1, modelId: "od_ts808", overrides: { 0: 3.5, 1: 5.8, 2: 6.4 } },
    { index: 2, modelId: "amp_plexi", overrides: { 0: 6.4, 1: 5.2, 2: 5.8, 3: 6.0, 4: 5.6, 5: 3.5, 6: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 80, 1: 9500, 2: 0 } },
    { index: 5, modelId: "dly_digital", overrides: { 0: 410, 1: 34, 2: 18, 3: 24 } },
    { index: 6, modelId: "rev_hall", overrides: { 0: 56, 1: 44, 2: 24, 3: 26 } },
    { index: 8, modelId: "eq_graphic5", enabled: true, overrides: { 0: 1.0, 1: -1.5, 2: 2.0, 3: 1.5, 4: 0.5 } },
    { index: 9, modelId: "mod_ce2", enabled: true, overrides: { 0: 1.0, 1: 42, 2: 38 } },
  ]),
  makePreset("1B", "Brit Plexi Crunch", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -64 } },
    { index: 1, modelId: "od_klon", overrides: { 0: 4.0, 1: 5.8, 2: 6.0 } },
    { index: 2, modelId: "amp_plexi", overrides: { 0: 5.8, 1: 5.0, 2: 6.2, 3: 5.8, 4: 6.2, 5: 4.5, 6: 5.4, 7: 1 } },
    { index: 3, modelId: "cab_v30" },
    { index: 5, modelId: "rev_spring", overrides: { 0: 45, 1: 40, 2: 22 } },
  ]),
  makePreset("1C", "US Clean Dream", 0, 2, 5, 0.45, [
    { index: 0, modelId: "comp_dyna", overrides: { 0: 4.2, 1: 5.6, 2: 14, 3: 85 } },
    { index: 2, modelId: "amp_twin", overrides: { 0: 3.0, 1: 5.4, 2: 4.6, 3: 6.2, 4: 5.5, 5: 2.5, 6: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 65, 1: 11500, 2: 0.5 } },
    { index: 4, modelId: "mod_ce2", overrides: { 0: 0.9, 1: 52, 2: 46 } },
    { index: 5, modelId: "dly_analog", overrides: { 0: 380, 1: 36, 2: 52, 3: 28 } },
    { index: 6, modelId: "rev_hall", overrides: { 0: 68, 1: 40, 2: 30, 3: 32 } },
  ]),
  makePreset("1D", "Modern Djent 5153", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -48, 1: 75, 2: 45 } },
    { index: 1, modelId: "od_ts808", overrides: { 0: 1.5, 1: 6.5, 2: 7.5 } },
    { index: 2, modelId: "amp_5153", overrides: { 0: 7.4, 1: 4.8, 2: 5.2, 3: 6.4, 4: 5.2, 5: 2.0, 6: 6.2, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 95, 1: 8800, 2: 0 } },
    { index: 4, modelId: "eq_graphic5", overrides: { 0: 1.5, 1: -2.0, 2: 1.5, 3: 2.0, 4: 1.0 } },
    { index: 6, modelId: "rev_plate", overrides: { 0: 38, 1: 35, 2: 14 } },
  ]),
  makePreset("2A", "JCM 800 Classic Rock", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -60 } },
    { index: 1, modelId: "od_sd1", overrides: { 0: 4.2, 1: 5.6, 2: 6.2 } },
    { index: 2, modelId: "amp_2203", overrides: { 0: 6.8, 1: 5.5, 2: 6.4, 3: 5.8, 4: 5.8, 5: 4.0, 6: 5.8, 7: 0 } },
    { index: 3, modelId: "cab_v30" },
    { index: 5, modelId: "dly_analog", overrides: { 0: 340, 1: 28, 2: 45, 3: 20 } },
    { index: 6, modelId: "rev_plate", overrides: { 0: 48, 1: 32, 2: 20 } },
  ]),
  makePreset("2B", "Fuzz Stoner Wall", 0, 2, 5, 0.4, [
    { index: 1, modelId: "fuzz_bigmuff", overrides: { 0: 7.2, 1: 5.4, 2: 5.2 } },
    { index: 2, modelId: "amp_plexi", overrides: { 0: 4.8, 1: 6.2, 2: 5.8, 3: 5.2, 4: 6.0, 5: 5.5, 6: 4.8, 7: 0 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 65, 1: 8200, 2: 0 } },
    { index: 4, modelId: "mod_phase90", overrides: { 0: 0.65, 1: 68, 2: 38, 3: 48 } },
    { index: 6, modelId: "rev_spring", overrides: { 0: 55, 1: 42, 2: 26 } },
  ]),
  makePreset("2C", "NAM Capture Rig", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -56 } },
    { index: 1, modelId: "od_ts808", enabled: false, overrides: { 0: 2.5, 1: 6.0, 2: 6.5 } },
    { index: 2, modelId: "nam_capture", overrides: { 0: 0, 1: 0, 2: 100 } },
    { index: 3, modelId: "cab_custom", overrides: { 0: 80, 1: 10000, 2: 0 } },
    { index: 5, modelId: "dly_digital", overrides: { 0: 400, 1: 30, 2: 15, 3: 22 } },
    { index: 6, modelId: "rev_hall", overrides: { 0: 52, 1: 42, 2: 20, 3: 22 } },
  ]),
  makePreset("2D", "Ambient Shimmer", 1, 2, 6, 0.55, [
    { index: 0, modelId: "comp_1176", overrides: { 0: 4.8, 1: 5.4, 2: 4, 3: 80 } },
    { index: 1, modelId: "boost_ep", overrides: { 0: 3.2, 1: 1 } },
    { index: 2, modelId: "amp_1star", overrides: { 0: 3.4, 1: 5.2, 2: 5.0, 3: 6.0, 4: 5.5, 5: 4.0, 6: 5.6, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 70, 1: 12000, 2: 0 } },
    { index: 5, modelId: "dly_digital", overrides: { 0: 520, 1: 48, 2: 35, 3: 36 } },
    { index: 6, modelId: "rev_hall", overrides: { 0: 82, 1: 35, 2: 40, 3: 42 } },
    { index: 9, modelId: "mod_ce2", overrides: { 0: 0.75, 1: 62, 2: 55 } },
    { index: 10, modelId: "mod_tremolo", overrides: { 0: 3.8, 1: 38, 2: 0 } },
  ]),
  makePreset("3A", "BOSS Blues & Dimension", 0, 2, 5, 0.45, [
    { index: 0, modelId: "comp_cs3", overrides: { 0: 5.5, 1: 5.8, 2: 4.5, 3: 4.8 } },
    { index: 1, modelId: "od_bd2", overrides: { 0: 4.8, 1: 5.5, 2: 5.8 } },
    { index: 2, modelId: "amp_twin", overrides: { 0: 3.8, 1: 5.2, 2: 5.4, 3: 5.8, 4: 5.8, 5: 3.5, 6: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 70, 1: 10800, 2: 0 } },
    { index: 4, modelId: "mod_dc2w", overrides: { 0: 2, 1: 62 } },
    { index: 5, modelId: "dly_dm2w", overrides: { 0: 350, 1: 36, 2: 26, 3: 1 } },
    { index: 6, modelId: "rev_spring", overrides: { 0: 50, 1: 40, 2: 24 } },
  ]),
  makePreset("3B", "BOSS Metal Zone Rig", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -50, 1: 80, 2: 50 } },
    { index: 1, modelId: "dist_mt2", overrides: { 0: 7.4, 1: 3.0, 2: 3.5, 3: -4.5, 4: 900, 5: 5.4 } },
    { index: 2, modelId: "amp_twin", overrides: { 0: 2.5, 1: 5.0, 2: 5.0, 3: 5.0, 4: 5.5, 5: 2.0, 6: 5.0, 7: 0 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 90, 1: 9200, 2: 0 } },
    { index: 4, modelId: "eq_ge7", overrides: { 0: 2.0, 1: 0.5, 2: -1.5, 3: 1.0, 4: 2.0, 5: 1.5, 6: 0.5, 7: 0 } },
    { index: 5, modelId: "dly_dd3t", overrides: { 0: 360, 1: 28, 2: 20 } },
  ]),
  makePreset("4A", "J-Pop Clean Sparkle", 0, 2, 5, 0.45, [
    { index: 0, modelId: "comp_sp", overrides: { 0: 55, 1: 5.5, 2: 1 } },
    { index: 1, modelId: "od_janray", overrides: { 0: 3.2, 1: 5.0, 2: 6.2, 3: 4.0, 4: 5.6 } },
    { index: 2, modelId: "amp_jc120", overrides: { 0: 4.6, 1: 4.8, 2: 5.2, 3: 6.4, 4: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_jc120", overrides: { 0: 65, 1: 12500, 2: 0 } },
    { index: 4, modelId: "mod_dc2w", overrides: { 0: 2, 1: 55 } },
    { index: 5, modelId: "rev_bluesky", overrides: { 0: 62, 1: 20, 2: 34, 3: 30, 4: 26 } },
  ]),
  makePreset("4B", "J-Rock Anime Crunch", 0, 2, 5, 0.4, [
    { index: 0, modelId: "comp_sp", overrides: { 0: 45, 1: 5.4, 2: 0 } },
    { index: 1, modelId: "od_ts9", overrides: { 0: 3.6, 1: 6.0, 2: 6.4 } },
    { index: 2, modelId: "amp_ac30", overrides: { 0: 5.6, 1: 4.8, 2: 6.0, 3: 6.6, 4: 5.8, 5: 4.0, 6: 6.0, 7: 1 } },
    { index: 3, modelId: "cab_alnico", overrides: { 0: 75, 1: 11000, 2: 0 } },
    { index: 5, modelId: "dly_timeline", overrides: { 0: 390, 1: 30, 2: 24, 3: 22, 4: 0 } },
    { index: 6, modelId: "rev_bluesky", overrides: { 0: 52, 1: 18, 2: 40, 3: 22, 4: 0 } },
  ]),
  makePreset("4C", "J-Rock Live House DSL", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -56, 1: 100, 2: 65 } },
    { index: 1, modelId: "od_ocd", overrides: { 0: 4.6, 1: 5.8, 2: 6.0, 3: 1 } },
    { index: 2, modelId: "amp_dsl", overrides: { 0: 6.6, 1: 5.2, 2: 6.2, 3: 6.0, 4: 5.8, 5: 3.5, 6: 6.2, 7: 0 } },
    { index: 3, modelId: "cab_1960a", overrides: { 0: 85, 1: 9800, 2: 0 } },
    { index: 5, modelId: "dly_dd3t", overrides: { 0: 350, 1: 28, 2: 20 } },
    { index: 6, modelId: "rev_plate", overrides: { 0: 44, 1: 35, 2: 18 } },
  ]),
  makePreset("4D", "Modern Anisong Lead", 1, 1, 5, 0.42, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -52, 1: 90, 2: 55 } },
    { index: 1, modelId: "od_bbpreamp", overrides: { 0: 4.2, 1: 4.8, 2: 6.0, 3: 6.2 } },
    { index: 2, modelId: "amp_be100", overrides: { 0: 7.0, 1: 5.2, 2: 6.4, 3: 6.0, 4: 5.6, 5: 3.2, 6: 6.0, 7: 1 } },
    { index: 3, modelId: "cab_mesa412", overrides: { 0: 90, 1: 9400, 2: 0 } },
    { index: 5, modelId: "dly_timeline", overrides: { 0: 420, 1: 34, 2: 26, 3: 24, 4: 0 } },
    { index: 6, modelId: "rev_bluesky", overrides: { 0: 64, 1: 24, 2: 36, 3: 26, 4: 22 } },
    { index: 8, modelId: "eq_ge7", overrides: { 0: 1.0, 1: 0, 2: -1.5, 3: 2.0, 4: 2.5, 5: 1.5, 6: 0.5, 7: 0 } },
  ]),
  makePreset("4E", "Public Schematic Legends", 0, 2, 5, 0.4, [
    { index: 0, modelId: "comp_orangesqueezer", overrides: { 0: 5.6, 1: 5.5, 2: 5.5, 3: 100 } },
    { index: 1, modelId: "od_bluesbreaker", overrides: { 0: 5.2, 1: 5.8, 2: 6.0 } },
    { index: 2, modelId: "fuzz_face", enabled: false, overrides: { 0: 7.8, 1: 5.0, 2: 5.8, 3: 0 } },
    { index: 3, modelId: "amp_plexi", overrides: { 0: 5.8, 1: 5.2, 2: 6.0, 3: 5.8, 4: 5.8, 5: 4.5, 6: 5.5, 7: 1 } },
    { index: 4, modelId: "cab_1960bv", overrides: { 0: 80, 1: 9600, 2: 0 } },
    { index: 5, modelId: "mod_univibe", overrides: { 0: 1.6, 1: 72, 2: 0 } },
    { index: 6, modelId: "dly_dm2w", overrides: { 0: 360, 1: 34, 2: 24, 3: 1 } },
    { index: 7, modelId: "rev_spring", overrides: { 0: 52, 1: 42, 2: 24 } },
  ]),
]

export const SCENE_LABELS = ["A", "B", "C", "D", "E", "F", "G", "H"] as const

function getSceneDefaultEnabled(modelId: string, baseEnabled: boolean, sceneIdx: number): boolean {
  let enabled = baseEnabled
  const def = modelId ? DEVICE_MAP[modelId] : undefined
  if (def) {
    switch (sceneIdx) {
      case 0: // A: Clean / Dry
        if (def.category === "drive" || def.category === "delay" || def.category === "mod") enabled = false
        if (def.category === "reverb") enabled = true
        break
      case 1: // B: Crunch
        if (def.category === "drive") enabled = true
        if (def.category === "delay" || def.category === "mod") enabled = false
        if (def.category === "reverb") enabled = true
        break
      case 2: // C: Tight Rhythm
        if (def.category === "drive") enabled = true
        if (def.category === "delay" || def.category === "reverb" || def.category === "mod") enabled = false
        break
      case 3: // D: Main / Lead (Base Preset)
        enabled = baseEnabled
        break
      case 4: // E: Wet Solo
        if (def.category === "drive" || def.category === "delay" || def.category === "reverb") enabled = true
        if (def.category === "mod") enabled = false
        break
      case 5: // F: Mod Clean
        if (def.category === "drive") enabled = false
        if (def.category === "mod" || def.category === "delay" || def.category === "reverb") enabled = true
        break
      case 6: // G: Raw Amp Only
        if (def.category === "drive" || def.category === "mod" || def.category === "delay" || def.category === "reverb")
          enabled = false
        break
      case 7: // H: Ambient Wash
        if (def.category === "mod" || def.category === "delay" || def.category === "reverb") enabled = true
        break
    }
  }
  return enabled
}

function buildInitialSceneSnapshot(base: SignalChainConfigDto, sceneIdx: number): SceneSnapshotDto {
  const slots = base.slots.map((s) => ({
    enabled: getSceneDefaultEnabled(s.modelId, s.enabled, sceneIdx),
    params: [...s.params],
  }))
  return { parallelMix: base.parallelMix, slots }
}

/**
 * 프리셋에 8개 씬(A~H) 스냅샷이 없는 경우 초기 스냅샷을 생성한다.
 */
export function ensurePresetScenes(chain: SignalChainConfigDto): SignalChainConfigDto {
  if (chain.scenes && chain.scenes.length === 8) return chain
  const scenes: SceneSnapshotDto[] = Array.from({ length: 8 }, (_, idx) =>
    idx === chain.activeScene
      ? {
          parallelMix: chain.parallelMix,
          slots: chain.slots.map((s) => ({ enabled: s.enabled, params: [...s.params] })),
        }
      : buildInitialSceneSnapshot(chain, idx)
  )
  return { ...chain, scenes }
}

/**
 * 현재 활성 씬(A~H)에 사용자의 블록 ON/OFF 및 노브 변경 사항을 실시간 저장한다.
 */
export function commitActiveScene(chain: SignalChainConfigDto): SignalChainConfigDto {
  const withScenes = ensurePresetScenes(chain)
  const idx = Math.max(0, Math.min(7, withScenes.activeScene))
  const nextScenes = withScenes.scenes!.map((sc, i) =>
    i === idx
      ? {
          parallelMix: withScenes.parallelMix,
          slots: withScenes.slots.map((s) => ({ enabled: s.enabled, params: [...s.params] })),
        }
      : sc
  )
  return { ...withScenes, scenes: nextScenes }
}

/**
 * Quad Cortex 진짜 Scene A~H 전환: 현재 씬을 저장한 뒤 대상 씬의 스냅샷(ON/OFF + 모든 노브 값 + ParallelMix)을 복원한다.
 */
export function switchSceneOnChain(chain: SignalChainConfigDto, targetSceneIdx: number): SignalChainConfigDto {
  const saved = commitActiveScene(chain)
  const idx = Math.max(0, Math.min(7, targetSceneIdx))
  const target = saved.scenes![idx]
  if (!target) return { ...saved, activeScene: idx }

  const nextSlots = saved.slots.map((s, i) => {
    const snapSlot = target.slots[i]
    if (!snapSlot || !s.modelId) return { ...s, params: [...s.params] }
    return {
      ...s,
      enabled: snapSlot.enabled,
      params: snapSlot.params?.length === 8 ? [...snapSlot.params] : [...s.params],
    }
  })

  return {
    ...saved,
    activeScene: idx,
    parallelMix: typeof target.parallelMix === "number" ? target.parallelMix : saved.parallelMix,
    slots: nextSlots,
  }
}

/**
 * 슬롯에 모델을 새로 배치하거나 제거할 때 모든 8개 씬(A~H)의 해당 슬롯 파라미터와 기본 활성 상태를 동기화한다.
 */
export function syncScenesOnAssignSlot(
  chain: SignalChainConfigDto,
  slotIdx: number,
  modelId: string,
  params: number[]
): SignalChainConfigDto {
  const withScenes = commitActiveScene(chain)
  const nextScenes = withScenes.scenes!.map((sc, sceneIdx) => {
    const slots = sc.slots.map((sl, i) =>
      i === slotIdx
        ? {
            enabled:
              sceneIdx === withScenes.activeScene
                ? withScenes.slots[slotIdx]?.enabled ?? true
                : getSceneDefaultEnabled(modelId, true, sceneIdx),
            params: [...params],
          }
        : { enabled: sl.enabled, params: [...sl.params] }
    )
    return { parallelMix: sc.parallelMix, slots }
  })
  return { ...withScenes, scenes: nextScenes }
}

/**
 * 두 슬롯을 이동·교환할 때 모든 8개 씬(A~H) 내부의 슬롯 스냅샷도 함께 교환한다.
 */
export function syncScenesOnSwapSlots(
  chain: SignalChainConfigDto,
  fromIdx: number,
  toIdx: number
): SignalChainConfigDto {
  const withScenes = commitActiveScene(chain)
  const nextScenes = withScenes.scenes!.map((sc) => {
    const slots = sc.slots.map((sl) => ({ enabled: sl.enabled, params: [...sl.params] }))
    if (fromIdx >= 0 && fromIdx < slots.length && toIdx >= 0 && toIdx < slots.length) {
      const tmp = slots[fromIdx]
      slots[fromIdx] = slots[toIdx]
      slots[toIdx] = tmp
    }
    return { parallelMix: sc.parallelMix, slots }
  })
  return { ...withScenes, scenes: nextScenes }
}
