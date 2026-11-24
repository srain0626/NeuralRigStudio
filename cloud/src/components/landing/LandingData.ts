export type Locale = "ko" | "en";

export interface RigPresetVoice {
  id: "clean" | "crunch" | "fuzz";
  bank: string;
  labelKo: string;
  labelEn: string;
  circuit: string;
  drive: number;
  asymmetry: number;
  tone: number;
  metricLabel: string;
  metricValue: string;
  notes: { freqs: number[]; dur: number }[];
}

export const HERO_VOICES: RigPresetVoice[] = [
  {
    id: "clean",
    bank: "1A",
    labelKo: "벨벳 클린",
    labelEn: "Velvet Clean",
    circuit: "12AX7 + FMV Stack",
    drive: 0.24,
    asymmetry: 0.14,
    tone: 0.62,
    metricLabel: "650Hz Scoop",
    metricValue: "-9.7 dB",
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
    labelKo: "비대칭 크런치",
    labelEn: "Asym Crunch",
    circuit: "SD-1 3×Diode + 2203",
    drive: 0.68,
    asymmetry: 0.58,
    tone: 0.64,
    metricLabel: "Even H2",
    metricValue: "-23.6 dBc",
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
    labelKo: "'68 게르마늄",
    labelEn: "'68 Ge Fuzz",
    circuit: "PNP AC128 MNA",
    drive: 0.92,
    asymmetry: 0.8,
    tone: 0.46,
    metricLabel: "Collector Vc2",
    metricValue: "-4.52 V",
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
    heroWhisper: "REAL-TIME SPICE & .NAM ENGINE",
    heroLines: ["살아있는 물리 회로,", "제로 바이트", "기타 스튜디오."],
    trioHeading: ["핵심만 남긴 세 가지", "사운드 엔진"],
    trioCta: "무료로 시작하기",
    trio: [
      {
        title: "SPICE 물리 회로",
        line: "다이오드와 12AX7 진공관 방정식을 매 샘플 직접 풉니다.",
        action: "파형 전환하기",
      },
      {
        title: "2×8 병렬 & .NAM",
        line: "아날로그 회로와 .NAM 캡처를 0 B 무할당으로 병렬 결합합니다.",
        action: "라우팅 살펴보기",
      },
      {
        title: "AI 회로도 제작",
        line: "명령어 한 줄로 실제 회로도를 검증해 새 페달을 즉시 꽂습니다.",
        action: "명령어 복사하기",
      },
    ],
    stickyTabs: ["SPICE 물리 회로", "2×8 병렬 .NAM", "AI 페달 핫스왑"],
    stickyChapters: [
      {
        title: ["만지는 즉시 반응하는", "실제 회로 파형"],
        cta: "사운드 들어보기",
        stopCta: "사운드 멈추기",
        subTitle: "0 B 오디오 콜백",
        subLine: "가비지 컬렉션 멈춤 없는 순수 네이티브 C# AVX2 연산.",
      },
      {
        title: ["두 줄로 갈라지는", "2×8 병렬 체인"],
        cta: "사운드 들어보기",
        stopCta: "사운드 멈추기",
        subTitle: "0.054 ms 연산",
        subLine: "64프레임 버퍼에서도 여유롭게 도는 듀얼 앰프 + 16k탭 IR.",
      },
      {
        title: ["없는 페달은 도면으로", "즉시 제작"],
        cta: "명령어 복사",
        stopCta: "복사 완료",
        subTitle: "npx neuralrig-mcp",
        subLine: "Claude · Cursor · Gemini · Antigravity 원클릭 연동.",
      },
    ],
    outroTyped: "가볍게 설치하고, 지연 없이 연주하세요.",
    installerBtn: "정식 인스톨러 (Setup.exe)",
    portableBtn: "무설치 단일 파일 (.exe)",
  },
  en: {
    downloadBtn: "Download app",
    heroWhisper: "REAL-TIME SPICE & .NAM ENGINE",
    heroLines: ["Live circuit physics,", "zero-byte", "guitar studio."],
    trioHeading: ["Three engines built", "for pure tone"],
    trioCta: "Get Studio free",
    trio: [
      {
        title: "Live SPICE circuits",
        line: "Solves diode junctions and 12AX7 triodes on every sample.",
        action: "Morph waveform",
      },
      {
        title: "2×8 parallel & .NAM",
        line: "Blend physical amps and .NAM captures at 0 B heap allocation.",
        action: "Inspect routing",
      },
      {
        title: "AI schematic craft",
        line: "Turn verified schematics into playable pedals in one command.",
        action: "Copy CLI command",
      },
    ],
    stickyTabs: ["Live SPICE circuit", "2×8 parallel .NAM", "AI pedal hot-swap"],
    stickyChapters: [
      {
        title: ["Touch-responsive", "analog waveforms"],
        cta: "Audition tone",
        stopCta: "Stop audio",
        subTitle: "0 B audio callback",
        subLine: "Native C# AVX2 DSP with zero garbage-collector stalls.",
      },
      {
        title: ["Dual-row split", "2×8 signal matrix"],
        cta: "Audition tone",
        stopCta: "Stop audio",
        subTitle: "0.054 ms runtime",
        subLine: "Dual amps, .NAM WaveNet, and 16k-tap IR inside 64 frames.",
      },
      {
        title: ["Missing a pedal?", "Compile its schematic"],
        cta: "Copy command",
        stopCta: "Copied",
        subTitle: "npx neuralrig-mcp",
        subLine: "One-line setup for Claude, Cursor, Gemini & Antigravity.",
      },
    ],
    outroTyped: "Built for zero-latency guitarists.",
    installerBtn: "Windows Installer (Setup.exe)",
    portableBtn: "Portable Binary (.exe)",
  },
} as const;
