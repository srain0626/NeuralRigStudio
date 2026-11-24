export type Locale = "ko" | "en";

export interface StudioSlotPreview {
  cat: string;
  dot: string;
  name: string;
  pct: number;
  badge?: string;
}

export interface RigPresetVoice {
  id: "clean" | "crunch" | "fuzz";
  bank: string;
  presetTitle: string;
  scene: "A" | "B" | "C" | "D";
  bpm: number;
  parallel: boolean;
  labelKo: string;
  labelEn: string;
  inspectorBadge: string;
  inspectorModel: string;
  drive: number;
  asymmetry: number;
  tone: number;
  knobs: [
    { label: string; val: number; unit?: string },
    { label: string; val: number; unit?: string },
    { label: string; val: number; unit?: string },
  ];
  row1: (StudioSlotPreview | null)[];
  row2: (StudioSlotPreview | null)[];
  notes: { freqs: number[]; dur: number }[];
}

export const HERO_VOICES: RigPresetVoice[] = [
  {
    id: "clean",
    bank: "1A",
    presetTitle: "Velvet Neo-Soul Clean",
    scene: "D",
    bpm: 120,
    parallel: true,
    labelKo: "벨벳 클린",
    labelEn: "Velvet Clean",
    inspectorBadge: "Guitar Amp",
    inspectorModel: "JP Stereo Jazz 120",
    drive: 0.24,
    asymmetry: 0.14,
    tone: 0.62,
    knobs: [
      { label: "VOLUME", val: 4.6 },
      { label: "TREBLE", val: 6.4 },
      { label: "MASTER", val: 5.5 },
    ],
    row1: [
      { cat: "DYN", dot: "#64748b", name: "OTA Comp", pct: 46 },
      { cat: "DRIVE", dot: "#ff8c42", name: "Brass Ray", pct: 28 },
      { cat: "AMP", dot: "#ff6b24", name: "JP Jazz 120", pct: 74 },
      { cat: "CAB", dot: "#4353ff", name: "2x12 Jazz", pct: 22, badge: "2×" },
      { cat: "DELAY", dot: "#0e8a7d", name: "White Dly", pct: 34, badge: "1/8." },
      { cat: "REVERB", dot: "#2b8a6e", name: "Celestial", pct: 42 },
    ],
    row2: [
      null,
      { cat: "MOD", dot: "#15803d", name: "Dimension", pct: 52 },
      null,
      null,
      null,
      null,
    ],
    notes: [
      { freqs: [164.81, 246.94, 311.13, 392.0], dur: 0.7 },
      { freqs: [329.63, 493.88], dur: 0.42 },
      { freqs: [146.83, 220.0, 293.66, 369.99], dur: 0.7 },
      { freqs: [293.66, 440.0], dur: 0.45 },
    ],
  },
  {
    id: "crunch",
    bank: "1C",
    presetTitle: "Brit 800 Arena Crunch",
    scene: "B",
    bpm: 128,
    parallel: true,
    labelKo: "아레나 크런치",
    labelEn: "Arena Crunch",
    inspectorBadge: "Overdrive",
    inspectorModel: "BOSS SD-1 MNA",
    drive: 0.68,
    asymmetry: 0.58,
    tone: 0.64,
    knobs: [
      { label: "DRIVE", val: 6.8 },
      { label: "TONE", val: 6.4 },
      { label: "LEVEL", val: 5.8 },
    ],
    row1: [
      { cat: "DYN", dot: "#64748b", name: "Adapt Gate", pct: 38 },
      { cat: "DRIVE", dot: "#ff8c42", name: "SD-1 MNA", pct: 68 },
      { cat: "AMP", dot: "#ff6b24", name: "Brit 800", pct: 76 },
      { cat: "CAB", dot: "#4353ff", name: "1960BV V30", pct: 54, badge: "2×" },
      { cat: "DELAY", dot: "#0e8a7d", name: "Tape Echo", pct: 30 },
      { cat: "REVERB", dot: "#2b8a6e", name: "Arena Hall", pct: 36 },
    ],
    row2: [
      null,
      null,
      { cat: "NAM", dot: "#9aa4a7", name: "JCM Capture", pct: 65 },
      { cat: "CAB", dot: "#4353ff", name: "4x12 G12M", pct: 48 },
      null,
      null,
    ],
    notes: [
      { freqs: [110.0, 164.81, 220.0], dur: 0.52 },
      { freqs: [110.0, 164.81], dur: 0.26 },
      { freqs: [146.83, 220.0, 293.66], dur: 0.52 },
      { freqs: [130.81, 196.0, 261.63], dur: 0.5 },
    ],
  },
  {
    id: "fuzz",
    bank: "2D",
    presetTitle: "Psychedelic '68 Fuzz",
    scene: "A",
    bpm: 108,
    parallel: false,
    labelKo: "'68 게르마늄",
    labelEn: "'68 Ge Fuzz",
    inspectorBadge: "SPICE Fuzz",
    inspectorModel: "Ge AC128 Fuzz Face",
    drive: 0.92,
    asymmetry: 0.8,
    tone: 0.46,
    knobs: [
      { label: "FUZZ", val: 9.2 },
      { label: "BIAS", val: 4.5, unit: "V" },
      { label: "VOLUME", val: 7.4 },
    ],
    row1: [
      { cat: "MOD", dot: "#15803d", name: "Fasel Wah", pct: 44 },
      { cat: "DRIVE", dot: "#ff6b24", name: "Ge AC128", pct: 92 },
      { cat: "AMP", dot: "#ff6b24", name: "Plexi 100", pct: 82 },
      { cat: "CAB", dot: "#4353ff", name: "Basket 4x12", pct: 60 },
      { cat: "MOD", dot: "#15803d", name: "Photo Vibe", pct: 58 },
      { cat: "REVERB", dot: "#2b8a6e", name: "Spring Tank", pct: 39 },
    ],
    row2: [null, null, null, null, null, null],
    notes: [
      { freqs: [82.41, 164.81, 246.94], dur: 0.62 },
      { freqs: [196.0, 293.66], dur: 0.44 },
      { freqs: [110.0, 220.0, 261.63], dur: 0.58 },
      { freqs: [146.83, 220.0], dur: 0.48 },
    ],
  },
];

export const MINIMAL_COPY = {
  ko: {
    downloadBtn: "Windows 다운로드",
    heroWhisper: "NEURALRIG STUDIO v3.0 · WINDOWS x64",
    heroLines: ["살아있는 물리 회로,", "제로 바이트", "기타 스튜디오."],
    trioHeading: ["실제 스튜디오 화면 속", "세 가지 핵심 엔진"],
    trioCta: "무료로 시작하기",
    trio: [
      {
        title: "2×8 병렬 시그널 체인",
        line: "SPICE 회로와 .NAM 캡처를 두 줄로 분기해 0 B 무할당으로 합성합니다.",
        action: "프리셋 전환하기",
      },
      {
        title: "11-틱 노브 & 버추얼 캡 룸",
        line: "스피커 콘 위 SM57·R-121 듀얼 마이크 위치와 위상을 실시간 조합합니다.",
        action: "마이크 위치 바꾸기",
      },
      {
        title: "TP-7 루퍼 & AI 페달 제작",
        line: "DI+WET 동시 녹음 리앰핑부터 npx MCP 회로도 페달 생성까지 내장했습니다.",
        action: "MCP 명령어 복사",
      },
    ],
    stickyTabs: ["2×8 체인 & 인스펙터", "버추얼 캡 룸 & 스펙트럼", "AI 회로도 페달 제작"],
    stickyChapters: [
      {
        title: ["필요한 순간에만 열리는", "포슬린 시그널 체인"],
        cta: "사운드 들어보기",
        stopCta: "사운드 멈추기",
        subTitle: "Scene A–H 무지연 전환",
        subLine: "블록을 클릭할 때만 하단 11-틱 로터리 인스펙터가 떠오릅니다.",
      },
      {
        title: ["눈으로 보고 맞추는", "듀얼 마이크 & DI 분석"],
        cta: "사운드 들어보기",
        stopCta: "사운드 멈추기",
        subTitle: "16,384탭 무지연 IR & 36밴드 FFT",
        subLine: "콘 중심부터 엣지까지 마이크를 옮기고 내 기타 DI를 계측합니다.",
      },
      {
        title: ["없는 장비는 회로도로", "즉시 검증·장착"],
        cta: "명령어 복사",
        stopCta: "복사 완료",
        subTitle: "npx -y neuralrig-mcp",
        subLine: "실제 데이터시트가 확인된 회로만 0 B 검증 후 슬롯에 꽂습니다.",
      },
    ],
    outroTyped: "가볍게 설치하고, 지연 없이 연주하세요.",
    installerBtn: "정식 인스톨러 (Setup.exe)",
    portableBtn: "무설치 단일 파일 (.exe)",
  },
  en: {
    downloadBtn: "Download app",
    heroWhisper: "NEURALRIG STUDIO v3.0 · WINDOWS x64",
    heroLines: ["Live circuit physics,", "zero-byte", "guitar studio."],
    trioHeading: ["Inside the actual", "studio workspace"],
    trioCta: "Get Studio free",
    trio: [
      {
        title: "2×8 parallel signal grid",
        line: "Split SPICE circuits and .NAM captures across dual rows at 0 B alloc.",
        action: "Cycle preset",
      },
      {
        title: "11-tick knobs & Cab Room",
        line: "Drag dual SM57 + R-121 mics across the speaker cone in real time.",
        action: "Shift mic placement",
      },
      {
        title: "TP-7 Looper & AI Craft",
        line: "Record DI+WET for instant re-amping or compile new pedals via MCP.",
        action: "Copy MCP command",
      },
    ],
    stickyTabs: ["2×8 Grid & Inspector", "Cab Room & Spectrum Lab", "AI Schematic Craft"],
    stickyChapters: [
      {
        title: [" distraction-free", "2×8 porcelain grid"],
        cta: "Audition tone",
        stopCta: "Stop audio",
        subTitle: "Gapless Scene A–H switching",
        subLine: "Click any block to summon the 11-tick parameter inspector.",
      },
      {
        title: ["Visual dual-mic radar", "& Guitar DI Profiler"],
        cta: "Audition tone",
        stopCta: "Stop audio",
        subTitle: "16,384-tap IR & 36-band FFT",
        subLine: "Blend SM57 + R-121 phase and profile your guitar pickups.",
      },
      {
        title: ["Missing a boutique pedal?", "Compile its schematic"],
        cta: "Copy command",
        stopCta: "Copied",
        subTitle: "npx -y neuralrig-mcp",
        subLine: "Validates real datasheets and 0 B RT budget before hot-swap.",
      },
    ],
    outroTyped: "Built for zero-latency guitarists.",
    installerBtn: "Windows Installer (Setup.exe)",
    portableBtn: "Portable Binary (.exe)",
  },
} as const;
