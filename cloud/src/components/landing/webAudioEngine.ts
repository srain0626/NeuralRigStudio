import { HERO_VOICES, RigPresetVoice } from "./LandingData";

export function makeShaperCurve(drive: number, asymmetry: number): Float32Array<ArrayBuffer> {
  const n = 2048;
  const buf = new ArrayBuffer(n * 4);
  const out = new Float32Array(buf);
  const gain = 1 + drive * 15;
  const bias = asymmetry * 0.52;

  for (let i = 0; i < n; i++) {
    const x = (i * 2) / (n - 1) - 1;
    const u = x * gain + bias;
    const y =
      u >= 0
        ? Math.tanh(u)
        : Math.tanh(u * (1 + asymmetry * 1.35)) / (1 + asymmetry * 0.4);
    const dc = Math.tanh(bias);
    out[i] = Math.max(-0.98, Math.min(0.98, (y - dc * 0.62) * 0.88));
  }
  return out;
}

export class GuitarAuditionController {
  private ctx: AudioContext | null = null;
  private shaper: WaveShaperNode | null = null;
  private mid: BiquadFilterNode | null = null;
  private cab: BiquadFilterNode | null = null;
  private master: GainNode | null = null;
  private timer: number | null = null;
  private voice: RigPresetVoice = HERO_VOICES[1];
  private drive = 0.68;

  public start(voice: RigPresetVoice, driveOverride?: number, onStop?: () => void) {
    this.stop();
    this.voice = voice;
    this.drive = driveOverride ?? voice.drive;

    const AudioCtx =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    const ctx = new AudioCtx();

    const hp = ctx.createBiquadFilter();
    hp.type = "highpass";
    hp.frequency.value = 86;

    const mid = ctx.createBiquadFilter();
    mid.type = "peaking";
    mid.Q.value = 1.15;
    mid.frequency.value = 540 + voice.tone * 620;
    mid.gain.value = 2.4 + this.drive * 6.2;

    const shaper = ctx.createWaveShaper();
    shaper.oversample = "4x";
    shaper.curve = makeShaperCurve(this.drive, voice.asymmetry);

    const cab = ctx.createBiquadFilter();
    cab.type = "lowpass";
    cab.Q.value = 0.85;
    cab.frequency.value = 3900;

    const master = ctx.createGain();
    master.gain.value = 0.23;

    hp.connect(mid);
    mid.connect(shaper);
    shaper.connect(cab);
    cab.connect(master);
    master.connect(ctx.destination);

    let step = 0;
    const pluck = () => {
      if (!this.ctx) return;
      const seq = this.voice.notes;
      const item = seq[step % seq.length];
      const now = ctx.currentTime;

      item.freqs.forEach((f, idx) => {
        const t0 = now + idx * 0.02;
        const o1 = ctx.createOscillator();
        const o2 = ctx.createOscillator();
        const lp = ctx.createBiquadFilter();
        const g = ctx.createGain();

        o1.type = "sawtooth";
        o2.type = "triangle";
        o1.frequency.setValueAtTime(f, t0);
        o2.frequency.setValueAtTime(f * 1.0017, t0);

        lp.type = "lowpass";
        lp.frequency.setValueAtTime(f * 6.4, t0);
        lp.frequency.exponentialRampToValueAtTime(Math.max(250, f * 1.65), t0 + item.dur);

        g.gain.setValueAtTime(0.0001, t0);
        g.gain.linearRampToValueAtTime(0.21 / Math.sqrt(item.freqs.length), t0 + 0.009);
        g.gain.exponentialRampToValueAtTime(0.0008, t0 + item.dur);

        o1.connect(lp);
        o2.connect(lp);
        lp.connect(g);
        g.connect(hp);

        o1.start(t0);
        o2.start(t0);
        o1.stop(t0 + item.dur + 0.03);
        o2.stop(t0 + item.dur + 0.03);
      });

      step += 1;
    };

    pluck();
    this.timer = window.setInterval(pluck, 480);
    this.ctx = ctx;
    this.shaper = shaper;
    this.mid = mid;
    this.cab = cab;
    this.master = master;

    ctx.onstatechange = () => {
      if (ctx.state === "closed" && onStop) onStop();
    };
  }

  public update(voice: RigPresetVoice, driveOverride?: number) {
    this.voice = voice;
    this.drive = driveOverride ?? voice.drive;
    if (!this.ctx || !this.shaper || !this.mid) return;
    this.shaper.curve = makeShaperCurve(this.drive, voice.asymmetry);
    this.mid.frequency.setTargetAtTime(540 + voice.tone * 620, this.ctx.currentTime, 0.03);
    this.mid.gain.setTargetAtTime(2.4 + this.drive * 6.2, this.ctx.currentTime, 0.03);
  }

  public stop() {
    if (this.timer !== null) {
      window.clearInterval(this.timer);
      this.timer = null;
    }
    if (this.ctx && this.master) {
      const c = this.ctx;
      try {
        this.master.gain.setTargetAtTime(0.0001, c.currentTime, 0.03);
        window.setTimeout(() => {
          c.close().catch(() => {});
        }, 90);
      } catch {
        // Ignore close exception
      }
    }
    this.ctx = null;
    this.shaper = null;
    this.mid = null;
    this.cab = null;
    this.master = null;
  }
}
