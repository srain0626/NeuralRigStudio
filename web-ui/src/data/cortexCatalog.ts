import type { ChainSlotDto, CustomDeviceSpecDto, DeviceCategoryId, SceneSnapshotDto, SignalChainConfigDto } from "@/types/rig"

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
  originalName?: string
  aliases?: string[]
  category: DeviceCategoryId
  categoryLabel: string
  schematicSummary?: string
  schematicSourceUrl?: string
  params: DeviceParamDef[]
}

export interface CategoryMeta {
  id: DeviceCategoryId
  label: string
  colorVar: string
  hex: string
}

export const CATEGORIES: CategoryMeta[] = [
  { id: "amp", label: "AMP", colorVar: "var(--cat-amp)", hex: "#D93829" },
  { id: "nam", label: "NEURAL CAPTURE", colorVar: "var(--cat-nam)", hex: "#9E9A91" },
  { id: "cab", label: "CAB", colorVar: "var(--cat-cab)", hex: "#4353FF" },
  { id: "drive", label: "OVERDRIVE", colorVar: "var(--cat-drive)", hex: "#D96B18" },
  { id: "delay", label: "DELAY", colorVar: "var(--cat-delay)", hex: "#0E8A7D" },
  { id: "reverb", label: "REVERB", colorVar: "var(--cat-reverb)", hex: "#2B8A6E" },
  { id: "mod", label: "MODULATION", colorVar: "var(--cat-mod)", hex: "#2E8B57" },
  { id: "eq", label: "EQ", colorVar: "var(--cat-eq)", hex: "#2563EB" },
  { id: "dyn", label: "DYNAMICS", colorVar: "var(--cat-dyn)", hex: "#64748B" },
  { id: "pitch", label: "PITCH", colorVar: "var(--cat-pitch)", hex: "#C83E7B" },
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
    originalName: "Marshall 1959SLP Super Lead 100W Plexi",
    aliases: ["Marshall", "1959SLP", "Plexi", "Super Lead", "마샬", "플렉시"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 6.2 } : p)),
  },
  {
    id: "amp_2203",
    name: "Brit 800 Lead (2203)",
    shortName: "Brit 800",
    originalName: "Marshall JCM800 2203",
    aliases: ["Marshall", "JCM800", "JCM 800", "2203", "마샬"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 6.8 } : p.index === 2 ? { ...p, defaultVal: 6.2 } : p)),
  },
  {
    id: "amp_twin",
    name: "US Blackface Twin 85",
    shortName: "US Twin 85",
    originalName: "Fender '65 Twin Reverb",
    aliases: ["Fender", "Twin Reverb", "US Clean Twin", "Blackface", "펜더", "트윈리버브", "트윈"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 3.2 } : p.index === 2 ? { ...p, defaultVal: 4.5 } : p)),
  },
  {
    id: "amp_1star",
    name: "CA 1Star Clean 100W",
    shortName: "CA 1Star",
    originalName: "Mesa/Boogie Lone Star Classic",
    aliases: ["Mesa", "Mesa/Boogie", "Lone Star", "Lonestar", "메사", "메사부기", "론스타"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 3.8 } : p.index === 5 ? { ...p, defaultVal: 4.5 } : p)),
  },
  {
    id: "amp_slo100",
    name: "US Super Lead 100",
    shortName: "US Super 100",
    originalName: "Soldano SLO-100 Super Lead Overdrive",
    aliases: ["Soldano", "SLO-100", "SLO100", "SLO 100", "솔다노"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) => (p.index === 0 ? { ...p, defaultVal: 6.5 } : p.index === 6 ? { ...p, defaultVal: 6.0 } : p)),
  },
  {
    id: "amp_5153",
    name: "Panama 53 Red Lead",
    shortName: "Panama 53",
    originalName: "EVH 5150 III 100W Red",
    aliases: ["EVH", "Peavey", "5150", "5153", "5150 III", "피비"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 7.2 } : p.index === 2 ? { ...p, defaultVal: 4.6 } : p
    ),
  },
  {
    id: "amp_jc120",
    name: "JP Stereo Jazz 120",
    shortName: "JP Jazz 120",
    originalName: "Roland JC-120 Jazz Chorus",
    aliases: ["Roland", "JC-120", "JC120", "Jazz Chorus", "롤랜드", "재즈코러스", "재즈 코러스"],
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
    name: "Brit Chime 30 Top Boost",
    shortName: "Brit Chime",
    originalName: "VOX AC30 Top Boost",
    aliases: ["VOX", "AC30", "AC-30", "Top Boost", "복스"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 5.4 } : p.index === 3 ? { ...p, defaultVal: 6.6 } : p
    ),
  },
  {
    id: "amp_dsl",
    name: "Brit 2000 Dual Super",
    shortName: "Brit 2000",
    originalName: "Marshall JCM2000 DSL100",
    aliases: ["Marshall", "JCM2000", "JCM 2000", "DSL", "DSL100", "마샬"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 0 ? { ...p, defaultVal: 6.6 } : p.index === 2 ? { ...p, defaultVal: 6.0 } : p.index === 6 ? { ...p, defaultVal: 6.2 } : p
    ),
  },
  {
    id: "amp_recto",
    name: "CA Dual Treadplate",
    shortName: "CA Tread",
    originalName: "Mesa/Boogie Dual Rectifier",
    aliases: ["Mesa", "Mesa/Boogie", "Dual Rectifier", "Rectifier", "Recto", "메사", "메사부기", "듀얼렉티파이어", "렉티파이어", "렉티"],
    category: "amp",
    categoryLabel: "Guitar Amp",
    params: AMP_PARAMS.map((p) =>
      p.index === 7 ? { ...p, defaultVal: 1 } : p.index === 0 ? { ...p, defaultVal: 7.4 } : p.index === 2 ? { ...p, defaultVal: 4.4 } : p
    ),
  },
  {
    id: "amp_be100",
    name: "LA Brown Eye 100",
    shortName: "Brown Eye",
    originalName: "Friedman BE-100",
    aliases: ["Friedman", "BE-100", "BE100", "Brown Eye", "프리드먼"],
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
    originalName: "Neural Amp Modeler (.nam)",
    aliases: ["NAM", "Neural Amp Modeler", "Neural Capture", "뉴럴캡처"],
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
    name: "4x12 Brit Vintage 30",
    shortName: "4x12 V30",
    originalName: "4x12 Celestion Vintage 30",
    aliases: ["Celestion", "Vintage 30", "V30", "4x12 V30", "셀레스천", "빈티지30"],
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
    originalName: "Custom External WAV Cabinet IR",
    aliases: ["Custom IR", "WAV IR", "커스텀 IR"],
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
    name: "2x12 JP Stereo Jazz Open",
    shortName: "2x12 Jazz",
    originalName: "Roland JC-120 2x12 Open Back",
    aliases: ["Roland", "JC-120", "JC120", "2x12 JC-120", "롤랜드", "재즈코러스"],
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
    name: "2x12 Brit Alnico Blue",
    shortName: "2x12 Blue",
    originalName: "2x12 VOX Celestion Alnico Blue",
    aliases: ["VOX", "AC30", "Celestion", "Alnico Blue", "복스", "알니코 블루", "알니코"],
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
    name: "4x12 Brit 60A Angled (75W)",
    shortName: "4x12 60A",
    originalName: "Marshall 1960A (Celestion G12T-75)",
    aliases: ["Marshall", "1960A", "G12T-75", "G12T75", "마샬"],
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
    name: "4x12 Brit 60BV Straight",
    shortName: "4x12 60BV",
    originalName: "Marshall 1960BV (4x12 Vintage 30)",
    aliases: ["Marshall", "1960BV", "V30", "마샬"],
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
    name: "4x12 CA Oversized Treadplate",
    shortName: "Tread 4x12",
    originalName: "Mesa/Boogie Rectifier Oversized 4x12",
    aliases: ["Mesa", "Mesa/Boogie", "Rectifier 4x12", "Recto 4x12", "메사", "렉티파이어"],
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
    name: "1x12 US Vintage Open Back",
    shortName: "1x12 US",
    originalName: "1x12 Fender Jensen C12N Open Back",
    aliases: ["Fender", "Jensen", "C12N", "1x12 Fender", "펜더", "젠센"],
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
    name: "Green Screamer 808",
    shortName: "Green 808",
    originalName: "Ibanez TS808 Tube Screamer",
    aliases: ["Ibanez", "TS808", "TS-808", "Tube Screamer", "Green 808", "아이바네즈", "튜브스크리머", "튜브 스크리머"],
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
    name: "Green Screamer 9",
    shortName: "Green 9",
    originalName: "Ibanez TS9 Tube Screamer",
    aliases: ["Ibanez", "TS9", "TS-9", "Tube Screamer", "아이바네즈", "튜브스크리머", "티에스9"],
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
    name: "Tokyo Brass Ray OD",
    shortName: "Brass Ray",
    originalName: "Vemuram Jan Ray",
    aliases: ["Vemuram", "Jan Ray", "JanRay", "베무람", "잔레이", "잰레이"],
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
    name: "Azure Sky Transparent OD",
    shortName: "Azure Sky",
    originalName: "Caline CP-12 Pure Sky (Timmy)",
    aliases: ["Caline", "Pure Sky", "CP-12", "Timmy", "칼라인", "퓨어스카이", "티미"],
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
    name: "Blues Box Preamp",
    shortName: "Blues Box",
    originalName: "Xotic BB Preamp",
    aliases: ["Xotic", "BB Preamp", "BB프리앰프", "엑조틱"],
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
    name: "Compulsive MOSFET Drive",
    shortName: "MOSFET OD",
    originalName: "Fulltone OCD",
    aliases: ["Fulltone", "OCD", "Obsessive Compulsive Drive", "풀톤", "오씨디"],
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
    name: "Black Rodent MK2",
    shortName: "Rodent MK2",
    originalName: "ProCo RAT 2",
    aliases: ["ProCo", "RAT 2", "RAT2", "랫2", "랫", "프로코"],
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
    name: "Euro Euphoria Red",
    shortName: "Euphoria",
    originalName: "Bogner Ecstasy Red",
    aliases: ["Bogner", "Ecstasy Red", "XTC Red", "보그너", "엑스터시"],
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
    name: "White Micro Boost",
    shortName: "Micro Boost",
    originalName: "MXR M133 Micro Amp",
    aliases: ["MXR", "Micro Amp", "M133", "마이크로앰프", "마이크로 앰프"],
    category: "drive",
    categoryLabel: "Boost",
    params: [
      { index: 0, label: "GAIN", min: 0, max: 10, step: 0.1, defaultVal: 4.5 },
    ],
  },
  {
    id: "od_klon",
    name: "Mythic Gold Centaur",
    shortName: "Gold Centaur",
    originalName: "Klon Centaur",
    aliases: ["Klon", "Centaur", "Klon Centaur", "Mythic Centaur", "클론", "센타우르"],
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
    name: "Vintage Rodent 308",
    shortName: "Rodent 308",
    originalName: "ProCo Vintage RAT (LM308)",
    aliases: ["ProCo", "RAT", "Vintage RAT", "LM308", "Rodent Dist", "랫"],
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
    name: "NYC Big Pi Sustainer",
    shortName: "NYC Big Pi",
    originalName: "Electro-Harmonix Big Muff Pi",
    aliases: ["EHX", "Electro-Harmonix", "Big Muff", "Big Muff Pi", "Muff", "Big Pi Fuzz", "빅머프", "머프"],
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
    name: "Compact Yellow Super OD",
    shortName: "Super OD",
    originalName: "BOSS SD-1 Super OverDrive",
    aliases: ["BOSS", "SD-1", "SD1", "Super OverDrive", "BOSS SD-1", "보스", "슈퍼 오버드라이브", "슈퍼오버드라이브"],
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
    name: "Compact Sapphire Blues OD",
    shortName: "Blues OD",
    originalName: "BOSS BD-2 Blues Driver",
    aliases: ["BOSS", "BD-2", "BD2", "Blues Driver", "BOSS BD-2", "보스", "블루스 드라이버", "블루스드라이버"],
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
    name: "Compact Orange Dist 78",
    shortName: "Orange Dist",
    originalName: "BOSS DS-1 Distortion",
    aliases: ["BOSS", "DS-1", "DS1", "Distortion", "BOSS DS-1", "보스", "디스토션"],
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
    name: "Compact Obsidian Metal Zone",
    shortName: "Obsidian MT",
    originalName: "BOSS MT-2 Metal Zone",
    aliases: ["BOSS", "MT-2", "MT2", "Metal Zone", "BOSS MT-2", "보스", "메탈존", "메탈 존"],
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
    name: "Compact Swedish Chainsaw",
    shortName: "Chainsaw",
    originalName: "BOSS HM-2 Heavy Metal",
    aliases: ["BOSS", "HM-2", "HM2", "Heavy Metal", "BOSS HM-2", "Chainsaw", "보스", "헤비메탈", "체인소"],
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
    name: "Echo Pre FET Booster",
    shortName: "Echo Boost",
    originalName: "Xotic EP Booster",
    aliases: ["Xotic", "EP Booster", "Echoplex", "엑조틱", "EP부스터", "이피부스터"],
    category: "drive",
    categoryLabel: "Boost",
    params: [
      { index: 1, label: "BRIGHT", min: 0, max: 1, step: 1, defaultVal: 1, kind: "toggle", onLabel: "On", offLabel: "Off" },
      { index: 0, label: "BOOST", min: 0, max: 10, step: 0.1, defaultVal: 4.0 },
    ],
  },
  {
    id: "boost_rc",
    name: "Pure Clean EQ Booster",
    shortName: "Pure Boost",
    originalName: "Xotic RC Booster",
    aliases: ["Xotic", "RC Booster", "엑조틱", "RC부스터", "알씨부스터"],
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
    name: "Brit Monarch Blues MK1",
    shortName: "Monarch MK1",
    originalName: "Marshall Bluesbreaker MK1",
    aliases: ["Marshall", "Bluesbreaker", "BB-1", "마샬", "블루스브레이커"],
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
    name: "Brit Governor LED Dist",
    shortName: "Governor",
    originalName: "Marshall The Guv'nor MK1",
    aliases: ["Marshall", "Guv'nor", "Guvnor", "마샬", "구브너", "거브너"],
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
    name: "Round Smile Fuzz (Ge/Si)",
    shortName: "Smile Fuzz",
    originalName: "Dallas-Arbiter Fuzz Face (Ge/Si)",
    aliases: ["Dallas", "Arbiter", "Dunlop", "Fuzz Face", "AC128", "BC108", "퍼즈페이스", "퍼즈 페이스"],
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
    name: "Brit OC44 Treble Master",
    shortName: "OC44 Boost",
    originalName: "Dallas Rangemaster (OC44)",
    aliases: ["Dallas", "Rangemaster", "OC44", "Treble Booster", "레인지마스터", "트레블 부스터"],
    category: "drive",
    categoryLabel: "Treble Boost",
    params: [
      { index: 1, label: "INPUT CAP", min: 0, max: 1, step: 1, defaultVal: 0, kind: "toggle", onLabel: "Full 22n", offLabel: "Treb 4n7" },
      { index: 0, label: "BOOST", min: 0, max: 10, step: 0.1, defaultVal: 6.5 },
    ],
  },
  {
    id: "dist_plus",
    name: "Mustard Dist Plus",
    shortName: "Mustard+",
    originalName: "MXR M104 Distortion+",
    aliases: ["MXR", "Distortion+", "Dist+", "M104", "디스토션 플러스", "디스토션플러스"],
    category: "drive",
    categoryLabel: "Distortion",
    params: [
      { index: 0, label: "DISTORTION", min: 0, max: 10, step: 0.1, defaultVal: 6.6 },
      { index: 1, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.8 },
    ],
  },
  {
    id: "od_boxofrock",
    name: "Brit 45 Rock Box",
    shortName: "Brit 45 Box",
    originalName: "ZVEX Box of Rock (JTM45)",
    aliases: ["ZVEX", "Box of Rock", "JTM45", "지벡스", "박스오브락"],
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
    name: "Compact Crimson BBD Delay",
    shortName: "Crimson BBD",
    originalName: "BOSS DM-2W Waza Delay",
    aliases: ["BOSS", "DM-2W", "DM2W", "DM-2", "DM2", "BOSS DM-2W", "보스", "아날로그 딜레이"],
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
    name: "Compact White Digital Delay",
    shortName: "White Dly",
    originalName: "BOSS DD-3T Digital Delay",
    aliases: ["BOSS", "DD-3T", "DD3T", "DD-3", "DD3", "BOSS DD-3T", "보스", "디지털 딜레이"],
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
    originalName: "Analog BBD Bucket-Brigade Delay",
    aliases: ["Analog Delay", "BBD", "아날로그 딜레이"],
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
    originalName: "Digital Stereo Ping-Pong Delay",
    aliases: ["Digital Delay", "Stereo Delay", "디지털 딜레이"],
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
    name: "Horizon Studio Delay",
    shortName: "Horizon Dly",
    originalName: "Strymon TimeLine",
    aliases: ["Strymon", "TimeLine", "스트라이몬", "타임라인"],
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
    name: "Celestial Shimmer Reverb",
    shortName: "Celestial",
    originalName: "Strymon blueSky",
    aliases: ["Strymon", "blueSky", "Blue Sky", "Shimmer", "스트라이몬", "블루스카이"],
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
    name: "Compact Slate Multi Reverb",
    shortName: "Slate Rev",
    originalName: "BOSS RV-6 Reverb",
    aliases: ["BOSS", "RV-6", "RV6", "BOSS RV-6", "보스", "리버브"],
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
    name: "Vintage Tank Spring Reverb",
    shortName: "Spring Tank",
    originalName: "Accutronics 3-Spring Tank Reverb",
    aliases: ["Spring Reverb", "Accutronics", "스프링 리버브"],
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
    originalName: "Concert Hall Ambient Reverb",
    aliases: ["Hall Reverb", "홀 리버브"],
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
    originalName: "EMT 140 Studio Plate Reverb",
    aliases: ["Plate Reverb", "EMT 140", "플레이트 리버브"],
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
    name: "Compact Sky Analog Chorus",
    shortName: "Sky Chorus",
    originalName: "BOSS CE-2 Chorus",
    aliases: ["BOSS", "CE-2", "CE2", "BOSS CE-2", "Chorus", "보스", "코러스"],
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
    name: "Compact Violet Dimension",
    shortName: "Dimension",
    originalName: "BOSS DC-2W Dimension C",
    aliases: ["BOSS", "DC-2W", "DC2W", "DC-2", "BOSS DC-2W", "Dimension C", "보스", "디멘션"],
    category: "mod",
    categoryLabel: "Modulation",
    params: [
      { index: 0, label: "MODE (1-4)", min: 1, max: 4, step: 1, defaultVal: 2 },
      { index: 1, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 60 },
    ],
  },
  {
    id: "mod_bf2",
    name: "Compact Magenta Jet Flanger",
    shortName: "Jet Flanger",
    originalName: "BOSS BF-2 Flanger",
    aliases: ["BOSS", "BF-2", "BF2", "BOSS BF-2", "Flanger", "보스", "플랜저"],
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
    name: "Orange Script 90 Phaser",
    shortName: "Script 90",
    originalName: "MXR M101 Phase 90",
    aliases: ["MXR", "Phase 90", "Phase90", "M101", "페이즈90", "페이즈 90", "페이저"],
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
    name: "Optical CdS Tremolo",
    shortName: "Tremolo",
    originalName: "Optical Photoresistor Tremolo",
    aliases: ["Optical Tremolo", "Tremolo", "트레몰로"],
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
    name: "Grunge BBD Analog Chorus",
    shortName: "Grunge Cho",
    originalName: "Electro-Harmonix Small Clone",
    aliases: ["EHX", "Electro-Harmonix", "Small Clone", "스몰클론", "스몰 클론"],
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
    name: "Psychedelic 68 Optical Vibe",
    shortName: "68 Vibe",
    originalName: "Shin-ei Uni-Vibe (1968)",
    aliases: ["Shin-ei", "Uni-Vibe", "Univibe", "유니바이브"],
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
    name: "Classic Vocal 95 Wah",
    shortName: "Vocal 95",
    originalName: "Dunlop Cry Baby GCB-95 Wah",
    aliases: ["Dunlop", "Cry Baby", "Crybaby", "GCB-95", "GCB95", "Wah", "크라이베이비", "와우"],
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
    name: "Compact Cream 7-Band EQ",
    shortName: "Cream 7-EQ",
    originalName: "BOSS GE-7 Graphic EQ",
    aliases: ["BOSS", "GE-7", "GE7", "BOSS GE-7", "Equalizer", "Graphic EQ", "보스", "이퀄라이저"],
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
    name: "Studio Graphic 5-Band EQ",
    shortName: "5-Band EQ",
    originalName: "Mesa Mark-Style 5-Band Graphic EQ",
    aliases: ["5-Band EQ", "Graphic EQ", "5밴드 EQ"],
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
    name: "Studio Parametric EQ",
    shortName: "Param EQ",
    originalName: "Studio 3-Band Parametric Equalizer",
    aliases: ["Parametric EQ", "파라메트릭 EQ"],
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
    originalName: "Adaptive Hysteresis Noise Gate",
    aliases: ["Noise Gate", "Gate", "노이즈 게이트", "게이트"],
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
    name: "Mini OTA Blend Compressor",
    shortName: "OTA Comp",
    originalName: "Xotic SP Compressor",
    aliases: ["Xotic", "SP Compressor", "SP Comp", "엑조틱", "SP컴프"],
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
    name: "Citrus JFET Squeezer",
    shortName: "Citrus Sqz",
    originalName: "Dan Armstrong Orange Squeezer",
    aliases: ["Dan Armstrong", "Orange Squeezer", "오렌지 스퀴저", "오렌지스퀴저"],
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
    name: "Compact Cyan VCA Sustainer",
    shortName: "Cyan Comp",
    originalName: "BOSS CS-3 Compression Sustainer",
    aliases: ["BOSS", "CS-3", "CS3", "BOSS CS-3", "Comp Sustainer", "보스", "컴프레서"],
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
    name: "Red Script Studio Comp",
    shortName: "Red Comp",
    originalName: "MXR M102 Dyna Comp",
    aliases: ["MXR", "Dyna Comp", "DynaComp", "M102", "다이나컴프", "다이나 컴프"],
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
    name: "FET 76 Limiting Amplifier",
    shortName: "FET 76",
    originalName: "UREI 1176LN Studio FET Compressor",
    aliases: ["1176", "1176LN", "UREI", "Universal Audio", "1176 컴프"],
    category: "dyn",
    categoryLabel: "Compressor",
    params: [
      { index: 0, label: "INPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.0 },
      { index: 1, label: "OUTPUT", min: 0, max: 10, step: 0.1, defaultVal: 5.2 },
      { index: 2, label: "RATIO", min: 2, max: 20, step: 1, defaultVal: 4 },
      { index: 3, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 85 },
    ],
  },

  // ── PITCH ──
  {
    id: "pitch_capo",
    name: "Poly Virtual Capo",
    shortName: "Poly Capo",
    originalName: "Polyphonic Drop / Virtual Capo",
    aliases: ["DigiTech Drop", "Virtual Capo", "Capo", "카포", "드롭", "다운튜닝"],
    category: "pitch",
    categoryLabel: "Pitch Shifter",
    params: [
      { index: 0, label: "SEMITONES", unit: "st", min: -12, max: 12, step: 1, defaultVal: -2 },
      { index: 1, label: "DETUNE", unit: "ct", min: -50, max: 50, step: 1, defaultVal: 0 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 100 },
      { index: 3, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "pitch_whammy",
    name: "Red Expression Pitch Bender",
    shortName: "Pitch Bend",
    originalName: "DigiTech Whammy",
    aliases: ["DigiTech", "Whammy", "Poly Whammy Pedal", "와미", "와미 페달"],
    category: "pitch",
    categoryLabel: "Pitch Shifter",
    params: [
      { index: 0, label: "PEDAL", unit: "%", min: 0, max: 100, step: 0.5, defaultVal: 0 },
      { index: 1, label: "INTERVAL", unit: "st", min: -24, max: 24, step: 1, defaultVal: 12 },
      { index: 2, label: "MIX", unit: "%", min: 0, max: 100, step: 1, defaultVal: 100 },
      { index: 3, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
  {
    id: "pitch_octaver",
    name: "Poly Sub/Up Octaver",
    shortName: "Octaver",
    originalName: "Polyphonic Sub/Up Octaver (POG/OC-5)",
    aliases: ["BOSS OC-5", "OC-3", "EHX POG", "Octaver", "옥타버"],
    category: "pitch",
    categoryLabel: "Pitch Shifter",
    params: [
      { index: 0, label: "DRY", unit: "%", min: 0, max: 100, step: 1, defaultVal: 80 },
      { index: 1, label: "SUB -1 OCT", unit: "%", min: 0, max: 100, step: 1, defaultVal: 65 },
      { index: 2, label: "UP +1 OCT", unit: "%", min: 0, max: 100, step: 1, defaultVal: 45 },
      { index: 3, label: "TONE", min: 0, max: 10, step: 0.1, defaultVal: 5.5 },
      { index: 4, label: "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: 0 },
    ],
  },
]

export const DEVICE_MAP: Record<string, DeviceModelDef> = Object.fromEntries(
  DEVICE_CATALOG.map((d) => [d.id, d])
)

const BUILTIN_DEVICE_CATALOG: DeviceModelDef[] = [...DEVICE_CATALOG]

function buildCustomDeviceDef(spec: CustomDeviceSpecDto): DeviceModelDef {
  const cat = (spec.category === "amp" || spec.category === "cab" ? spec.category : "drive") as DeviceCategoryId
  const labels = spec.paramLabels ?? []
  const defs = spec.defaults ?? []

  let params: DeviceParamDef[]
  const spiceKnobs = spec.spiceCircuit?.knobs
  if (spiceKnobs && spiceKnobs.length > 0) {
    params = [...spiceKnobs]
      .sort((a, b) => a.index - b.index)
      .map((k) => ({
        index: Math.max(0, Math.min(7, k.index)),
        label: k.label || `P${k.index}`,
        unit: k.unit || undefined,
        min: typeof k.min === "number" ? k.min : 0,
        max: typeof k.max === "number" ? k.max : k.isSwitch ? 1 : 10,
        step: k.isSwitch ? 1 : 0.1,
        defaultVal: typeof k.default === "number" ? k.default : defs[k.index] ?? 5,
        kind: k.isSwitch ? "toggle" : "knob",
      }))
  } else if (cat === "amp") {
    params = AMP_PARAMS.map((p) => ({
      ...p,
      label: labels[p.index] || p.label,
      defaultVal: typeof defs[p.index] === "number" ? defs[p.index] : p.defaultVal,
    }))
  } else if (cat === "cab") {
    params = [
      { index: 0, label: labels[0] || "LOW CUT", unit: "Hz", min: 20, max: 350, step: 5, defaultVal: defs[0] ?? 78 },
      { index: 1, label: labels[1] || "HIGH CUT", unit: "Hz", min: 2000, max: 16000, step: 100, defaultVal: defs[1] ?? 9600 },
      { index: 2, label: labels[2] || "LEVEL", unit: "dB", min: -12, max: 12, step: 0.1, defaultVal: defs[2] ?? 0 },
    ]
  } else {
    const count = Math.max(3, Math.min(4, labels.length || 4))
    params = Array.from({ length: count }, (_, idx) => ({
      index: idx,
      label: labels[idx] || (idx === 0 ? "DRIVE" : idx === 1 ? "TONE" : idx === 2 ? "LEVEL" : "CHAR"),
      min: 0,
      max: 10,
      step: 0.1,
      defaultVal: defs[idx] ?? 5.0,
    }))
  }

  return {
    id: spec.id,
    name: spec.name,
    shortName: spec.shortName || spec.name.slice(0, 12),
    originalName: spec.originalName || spec.name,
    aliases: spec.aliases ?? [],
    category: cat,
    categoryLabel: spec.categoryLabel || "Custom Schematic",
    schematicSummary: spec.schematicSummary,
    schematicSourceUrl: spec.schematicSourceUrl,
    params,
  }
}

export function compactSearchToken(s?: string): string {
  return (s ?? "").toLowerCase().replace(/[\s\-_/'().]+/g, "")
}

export function getDeviceSearchKeywords(d: DeviceModelDef): string {
  const raw = [
    d.name,
    d.shortName,
    d.originalName ?? "",
    ...(d.aliases ?? []),
    d.categoryLabel,
    d.id,
    d.schematicSummary ?? "",
  ].join(" ")
  const compact = [
    compactSearchToken(d.name),
    compactSearchToken(d.shortName),
    compactSearchToken(d.originalName),
    ...(d.aliases ?? []).map(compactSearchToken),
    compactSearchToken(d.id),
  ].join(" ")
  return `${raw} ${compact}`
}

export function matchesDeviceSearch(d: DeviceModelDef, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  const haystack = getDeviceSearchKeywords(d).toLowerCase()
  const tokens = q.split(/\s+/).filter(Boolean)
  return tokens.every((t) => {
    const ct = compactSearchToken(t)
    return haystack.includes(t) || (ct.length > 0 && haystack.includes(ct))
  })
}

export function registerCustomDevices(specs?: CustomDeviceSpecDto[] | null): number {
  const customList = (specs ?? []).filter((s) => Boolean(s?.id)).map(buildCustomDeviceDef)
  DEVICE_CATALOG.length = 0
  DEVICE_CATALOG.push(...BUILTIN_DEVICE_CATALOG, ...customList)

  for (const k of Object.keys(DEVICE_MAP)) {
    delete DEVICE_MAP[k]
  }
  for (const d of DEVICE_CATALOG) {
    DEVICE_MAP[d.id] = d
  }
  return DEVICE_CATALOG.length
}

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
    syncDivision: 0,
    params: [0, 0, 0, 0, 0, 0, 0, 0],
  }))
}

export const TEMPO_SYNC_DIVISIONS = [
  { value: 0, label: "FREE", short: "" },
  { value: 1, label: "1/4", short: "1/4" },
  { value: 2, label: "1/8.", short: "1/8." },
  { value: 3, label: "1/8", short: "1/8" },
  { value: 4, label: "1/8T", short: "1/8T" },
  { value: 5, label: "1/16", short: "1/16" },
] as const

export function supportsTempoSync(modelId?: string): boolean {
  if (!modelId) return false
  const def = DEVICE_MAP[modelId]
  if (!def) return false
  if (def.category === "delay") return true
  return (
    modelId === "mod_ce2" ||
    modelId === "mod_bf2" ||
    modelId === "mod_phase90" ||
    modelId === "mod_tremolo" ||
    modelId === "mod_smallclone" ||
    modelId === "mod_univibe"
  )
}

export function computeSyncedValueLabel(modelId: string, syncDiv: number, bpm: number): string | null {
  if (!syncDiv || syncDiv <= 0) return null
  const safeBpm = Math.max(40, Math.min(280, bpm || 120))
  const def = DEVICE_MAP[modelId]
  if (!def) return null
  if (def.category === "delay") {
    const beatMs = 60000 / safeBpm
    const mult = syncDiv === 1 ? 1 : syncDiv === 2 ? 0.75 : syncDiv === 3 ? 0.5 : syncDiv === 4 ? 1 / 3 : 0.25
    return `${Math.round(beatMs * mult)} ms`
  }
  const beatHz = safeBpm / 60
  const mult = syncDiv === 1 ? 1 : syncDiv === 2 ? 4 / 3 : syncDiv === 3 ? 2 : syncDiv === 4 ? 3 : 4
  return `${(beatHz * mult).toFixed(2)} Hz`
}

function makePreset(
  presetId: string,
  presetName: string,
  routingMode: number,
  splitCol: number,
  mergeCol: number,
  parallelMix: number,
  entries: Array<{ index: number; modelId: string; enabled?: boolean; syncDivision?: number; overrides?: Record<number, number> }>,
  tempoBpm = 120
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
      syncDivision: e.syncDivision ?? 0,
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
    tempoBpm,
    inputGainDb: 0,
    outputGainDb: 0,
    slots,
  }
}

export const FACTORY_PRESETS: SignalChainConfigDto[] = [
  makePreset("1A", "Velvet Neo-Soul Clean", 1, 1, 5, 0.45, [
    { index: 0, modelId: "comp_sp", overrides: { 0: 54, 1: 5.6, 2: 1 } },
    { index: 1, modelId: "od_janray", overrides: { 0: 3.2, 1: 5.0, 2: 6.2, 3: 4.2, 4: 5.6 } },
    { index: 2, modelId: "amp_jc120", overrides: { 0: 4.6, 1: 4.8, 2: 5.4, 3: 6.4, 4: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_jc120", overrides: { 0: 65, 1: 12200, 2: 0 } },
    { index: 5, modelId: "dly_dd3t", overrides: { 0: 360, 1: 30, 2: 22 } },
    { index: 6, modelId: "rev_bluesky", overrides: { 0: 64, 1: 22, 2: 34, 3: 28, 4: 22 } },
    { index: 9, modelId: "mod_dc2w", overrides: { 0: 2, 1: 62 } },
  ]),
  makePreset("1B", "Golden Edge of Breakup", 0, 2, 5, 0.4, [
    { index: 0, modelId: "comp_orangesqueezer", overrides: { 0: 5.6, 1: 5.6, 2: 5.2, 3: 90 } },
    { index: 1, modelId: "od_klon", overrides: { 0: 4.2, 1: 6.0, 2: 6.2 } },
    { index: 2, modelId: "amp_twin", overrides: { 0: 4.4, 1: 5.4, 2: 5.4, 3: 6.2, 4: 5.8, 5: 4.0, 6: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_jensen112", overrides: { 0: 70, 1: 11400, 2: 0 } },
    { index: 5, modelId: "dly_analog", overrides: { 0: 370, 1: 32, 2: 52, 3: 24 } },
    { index: 6, modelId: "rev_spring", overrides: { 0: 52, 1: 42, 2: 24 } },
  ]),
  makePreset("1C", "Brit 800 Arena Crunch", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -60, 1: 110, 2: 75 } },
    { index: 1, modelId: "od_sd1", overrides: { 0: 4.0, 1: 5.6, 2: 6.4 } },
    { index: 2, modelId: "amp_2203", overrides: { 0: 6.8, 1: 5.4, 2: 6.4, 3: 5.8, 4: 5.8, 5: 4.0, 6: 5.8, 7: 0 } },
    { index: 3, modelId: "cab_1960a", overrides: { 0: 85, 1: 9800, 2: 0 } },
    { index: 5, modelId: "dly_dm2w", overrides: { 0: 360, 1: 30, 2: 22, 3: 1 } },
    { index: 6, modelId: "rev_plate", overrides: { 0: 48, 1: 34, 2: 20 } },
  ]),
  makePreset("1D", "Tokyo Anime Pop-Rock", 0, 2, 5, 0.4, [
    { index: 0, modelId: "comp_sp", overrides: { 0: 45, 1: 5.4, 2: 0 } },
    { index: 1, modelId: "od_ts9", overrides: { 0: 3.6, 1: 6.0, 2: 6.4 } },
    { index: 2, modelId: "amp_ac30", overrides: { 0: 5.6, 1: 4.8, 2: 6.0, 3: 6.6, 4: 5.8, 5: 4.0, 6: 6.0, 7: 1 } },
    { index: 3, modelId: "cab_alnico", overrides: { 0: 75, 1: 11000, 2: 0 } },
    { index: 5, modelId: "dly_timeline", overrides: { 0: 390, 1: 30, 2: 24, 3: 22, 4: 0 } },
    { index: 6, modelId: "rev_bluesky", overrides: { 0: 54, 1: 18, 2: 38, 3: 22, 4: 0 } },
  ]),
  makePreset("2A", "Tight Modern Djent", 0, 2, 5, 0.4, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -48, 1: 75, 2: 45 } },
    { index: 1, modelId: "od_ts808", overrides: { 0: 1.4, 1: 6.6, 2: 7.8 } },
    { index: 2, modelId: "amp_5153", overrides: { 0: 7.4, 1: 4.8, 2: 5.4, 3: 6.4, 4: 5.2, 5: 2.0, 6: 6.2, 7: 1 } },
    { index: 3, modelId: "cab_mesa412", overrides: { 0: 95, 1: 9000, 2: 0 } },
    { index: 4, modelId: "eq_graphic5", overrides: { 0: 1.5, 1: -2.0, 2: 1.5, 3: 2.0, 4: 1.0 } },
    { index: 6, modelId: "rev_plate", overrides: { 0: 36, 1: 35, 2: 14 } },
  ]),
  makePreset("2B", "Cathedral Shimmer Wash", 1, 2, 6, 0.55, [
    { index: 0, modelId: "comp_1176", overrides: { 0: 4.8, 1: 5.4, 2: 4, 3: 80 } },
    { index: 1, modelId: "boost_ep", overrides: { 0: 3.4, 1: 1 } },
    { index: 2, modelId: "amp_1star", overrides: { 0: 3.6, 1: 5.2, 2: 5.0, 3: 6.2, 4: 5.6, 5: 4.0, 6: 5.8, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 70, 1: 12000, 2: 0 } },
    { index: 5, modelId: "dly_timeline", overrides: { 0: 480, 1: 46, 2: 34, 3: 36, 4: 1 } },
    { index: 6, modelId: "rev_bluesky", overrides: { 0: 84, 1: 28, 2: 30, 3: 44, 4: 48 } },
    { index: 9, modelId: "mod_smallclone", overrides: { 0: 0.75, 1: 1, 2: 54 } },
    { index: 10, modelId: "mod_tremolo", overrides: { 0: 3.8, 1: 38, 2: 0 } },
  ]),
  makePreset("2C", "LA Hot-Rod Brown Lead", 1, 1, 5, 0.40, [
    { index: 0, modelId: "gate_adaptive", overrides: { 0: -54, 1: 95, 2: 60 } },
    { index: 1, modelId: "od_bbpreamp", overrides: { 0: 3.8, 1: 4.8, 2: 6.0, 3: 6.4 } },
    { index: 2, modelId: "amp_be100", overrides: { 0: 6.8, 1: 5.2, 2: 6.4, 3: 6.0, 4: 5.6, 5: 3.5, 6: 5.8, 7: 1 } },
    { index: 3, modelId: "cab_1960bv", overrides: { 0: 80, 1: 9600, 2: 0 } },
    { index: 5, modelId: "dly_digital", overrides: { 0: 410, 1: 32, 2: 20, 3: 24 } },
    { index: 6, modelId: "rev_hall", overrides: { 0: 56, 1: 42, 2: 22, 3: 26 } },
    { index: 9, modelId: "eq_parametric", overrides: { 0: 80, 1: 1200, 2: 2.5, 3: 1.3, 4: 10500 } },
  ]),
  makePreset("2D", "Psychedelic '68 Fuzz", 0, 2, 5, 0.4, [
    { index: 0, modelId: "eq_crybaby", overrides: { 0: 54, 1: 40, 2: 6.2, 3: 100 } },
    { index: 1, modelId: "fuzz_face", overrides: { 0: 8.0, 1: 5.0, 2: 5.8, 3: 0 } },
    { index: 2, modelId: "amp_plexi", overrides: { 0: 6.2, 1: 5.4, 2: 6.4, 3: 6.0, 4: 6.2, 5: 4.8, 6: 5.6, 7: 1 } },
    { index: 3, modelId: "cab_1960bv", overrides: { 0: 80, 1: 9600, 2: 0 } },
    { index: 4, modelId: "mod_univibe", overrides: { 0: 1.7, 1: 72, 2: 0 } },
    { index: 6, modelId: "rev_spring", overrides: { 0: 52, 1: 42, 2: 24 } },
  ]),
  makePreset("3A", "Compact Blues & Dimension", 0, 2, 5, 0.45, [
    { index: 0, modelId: "comp_cs3", overrides: { 0: 5.5, 1: 5.8, 2: 4.5, 3: 4.8 } },
    { index: 1, modelId: "od_bd2", overrides: { 0: 4.8, 1: 5.5, 2: 5.8 } },
    { index: 2, modelId: "amp_twin", overrides: { 0: 3.8, 1: 5.2, 2: 5.4, 3: 5.8, 4: 5.8, 5: 3.5, 6: 5.5, 7: 1 } },
    { index: 3, modelId: "cab_v30", overrides: { 0: 70, 1: 10800, 2: 0 } },
    { index: 4, modelId: "mod_dc2w", overrides: { 0: 2, 1: 62 } },
    { index: 5, modelId: "dly_dm2w", overrides: { 0: 350, 1: 36, 2: 26, 3: 1 } },
    { index: 6, modelId: "rev_spring", overrides: { 0: 50, 1: 40, 2: 24 } },
  ]),
  makePreset("3B", "Obsidian Metal Zone Rig", 0, 2, 5, 0.4, [
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
  makePreset("4C", "J-Rock Live House 2000", 0, 2, 5, 0.4, [
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
    syncDivision: s.syncDivision ?? 0,
    params: [...s.params],
  }))
  return { parallelMix: base.parallelMix, slots }
}

/**
 * 프리셋에 8개 씬(A~H) 스냅샷이 없는 경우 초기 스냅샷을 생성한다.
 */
export function ensurePresetScenes(chain: SignalChainConfigDto): SignalChainConfigDto {
  const normalizedTempo = chain.tempoBpm && chain.tempoBpm >= 40 ? chain.tempoBpm : 120
  if (chain.scenes && chain.scenes.length === 8) {
    return chain.tempoBpm === normalizedTempo ? chain : { ...chain, tempoBpm: normalizedTempo }
  }
  const scenes: SceneSnapshotDto[] = Array.from({ length: 8 }, (_, idx) =>
    idx === chain.activeScene
      ? {
          parallelMix: chain.parallelMix,
          slots: chain.slots.map((s) => ({
            enabled: s.enabled,
            syncDivision: s.syncDivision ?? 0,
            params: [...s.params],
          })),
        }
      : buildInitialSceneSnapshot(chain, idx)
  )
  return { ...chain, tempoBpm: normalizedTempo, scenes }
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
          slots: withScenes.slots.map((s) => ({
            enabled: s.enabled,
            syncDivision: s.syncDivision ?? 0,
            params: [...s.params],
          })),
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
      syncDivision: snapSlot.syncDivision ?? s.syncDivision ?? 0,
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
            syncDivision: 0,
            params: [...params],
          }
        : { enabled: sl.enabled, syncDivision: sl.syncDivision ?? 0, params: [...sl.params] }
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
    const slots = sc.slots.map((sl) => ({
      enabled: sl.enabled,
      syncDivision: sl.syncDivision ?? 0,
      params: [...sl.params],
    }))
    if (fromIdx >= 0 && fromIdx < slots.length && toIdx >= 0 && toIdx < slots.length) {
      const tmp = slots[fromIdx]
      slots[fromIdx] = slots[toIdx]
      slots[toIdx] = tmp
    }
    return { parallelMix: sc.parallelMix, slots }
  })
  return { ...withScenes, scenes: nextScenes }
}
