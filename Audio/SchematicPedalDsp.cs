using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

/// <summary>
/// 실제 기타 페달 회로도(Schematic) 기반 4x HIIR 폴리페이즈 오버샘플링 아날로그 회로 모델링 커널.
/// - 모든 비선형 소자(1N914 Si, 1N34A Ge, Red LED, 비대칭 3-다이오드, 2N7000 MOSFET, 2SK30A/J201 JFET, 2N5088 BJT, LM308 슬루레이트)를
///   물리 방정식(Shockley 다이오드 댐프드 뉴턴-랩슨, 슬루레이트 리미터, 커플링 HPF + 밀러 LPF)으로 실시간 무할당(RT alloc = 0 B) 실행한다.
/// </summary>
internal static class SchematicPedalDsp
{
    // ── 물리 상수 (25°C 열전압 Vt = 25.85 mV) ──
    private const double Vt = 0.02585;

    // 1N914 / 1N4148 / 1S1588 Silicon Switching Diode
    private const double SiIs = 2.52e-9;
    private const double SiEtaVt = 1.75 * Vt;
    private const double SiInvEtaVt = 1.0 / SiEtaVt;
    private const double SiMaxVd = SiEtaVt * 25.0;

    // 1N34A Germanium Diode (Klon Centaur / OCD)
    private const double GeIs = 2.0e-6;
    private const double GeEtaVt = 1.60 * Vt;
    private const double GeInvEtaVt = 1.0 / GeEtaVt;
    private const double GeMaxVd = GeEtaVt * 22.0;

    // Red LED (High headroom crunch)
    private const double LedIs = 1.0e-12;
    private const double LedEtaVt = 3.20 * Vt;
    private const double LedInvEtaVt = 1.0 / LedEtaVt;
    private const double LedMaxVd = LedEtaVt * 36.0;

    // ── HIIR 4x 폴리페이즈 올패스 계수 (Laurent de Soras) ──
    private static readonly double[] S1A0 =
    {
        0.0236683141988347, 0.1898947622718017, 0.4315731806211855, 0.6632020224193995, 0.860015542499582,
    };
    private static readonly double[] S1A1 =
    {
        0.0905655590499339, 0.3078575723749043, 0.5516782402507934, 0.7652146863779808, 0.9524728378667541,
    };
    private static readonly double[] S2A0 = { 0.0463483004647977, 0.4119865160140078 };
    private static readonly double[] S2A1 = { 0.1833581703161115, 0.7577900307152335 };

    /// <summary>슬롯별 4x HIIR 오버샘플러 상태 (사전 할당, RT 할당 0).</summary>
    internal sealed class SlotOversampler4x
    {
        private readonly double[] _u1x0 = new double[5], _u1y0 = new double[5];
        private readonly double[] _u1x1 = new double[5], _u1y1 = new double[5];
        private readonly double[] _u2x0 = new double[2], _u2y0 = new double[2];
        private readonly double[] _u2x1 = new double[2], _u2y1 = new double[2];

        private readonly double[] _d2x0 = new double[2], _d2y0 = new double[2];
        private readonly double[] _d2x1 = new double[2], _d2y1 = new double[2];
        private readonly double[] _d1x0 = new double[5], _d1y0 = new double[5];
        private readonly double[] _d1x1 = new double[5], _d1y1 = new double[5];

        public void Reset()
        {
            Array.Clear(_u1x0); Array.Clear(_u1y0);
            Array.Clear(_u1x1); Array.Clear(_u1y1);
            Array.Clear(_u2x0); Array.Clear(_u2y0);
            Array.Clear(_u2x1); Array.Clear(_u2y1);
            Array.Clear(_d2x0); Array.Clear(_d2y0);
            Array.Clear(_d2x1); Array.Clear(_d2y1);
            Array.Clear(_d1x0); Array.Clear(_d1y0);
            Array.Clear(_d1x1); Array.Clear(_d1y1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Ap5(double s, double[] a, double[] x, double[] y)
        {
            for (int i = 0; i < 5; i++)
            {
                double o = a[i] * (s - y[i]) + x[i];
                x[i] = s;
                y[i] = o;
                s = o;
            }
            return s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Ap2(double s, double[] a, double[] x, double[] y)
        {
            double o0 = a[0] * (s - y[0]) + x[0];
            x[0] = s; y[0] = o0;
            double o1 = a[1] * (o0 - y[1]) + x[1];
            x[1] = o0; y[1] = o1;
            return o1;
        }

        public Span<float> Upsample(ReadOnlySpan<float> input, int frames, double[] scratch2x, float[] scratch4x)
        {
            int n2 = frames * 2;
            for (int i = 0; i < frames; i++)
            {
                double x = input[i];
                int o = i * 2;
                scratch2x[o] = Ap5(x, S1A0, _u1x0, _u1y0);
                scratch2x[o + 1] = Ap5(x, S1A1, _u1x1, _u1y1);
            }
            for (int i = 0; i < n2; i++)
            {
                double x = scratch2x[i];
                int o = i * 2;
                scratch4x[o] = (float)Ap2(x, S2A0, _u2x0, _u2y0);
                scratch4x[o + 1] = (float)Ap2(x, S2A1, _u2x1, _u2y1);
            }
            return scratch4x.AsSpan(0, frames * 4);
        }

        public void Downsample(Span<float> output, int frames, double[] scratch2x, float[] scratch4x)
        {
            int n2 = frames * 2;
            for (int i = 0; i < n2; i++)
            {
                int idx = i * 2;
                scratch2x[i] = 0.5 * (Ap2(scratch4x[idx + 1], S2A0, _d2x0, _d2y0) + Ap2(scratch4x[idx], S2A1, _d2x1, _d2y1));
            }
            for (int i = 0; i < frames; i++)
            {
                int idx = i * 2;
                output[i] = (float)(0.5 * (Ap5(scratch2x[idx + 1], S1A0, _d1x0, _d1y0) + Ap5(scratch2x[idx], S1A1, _d1x1, _d1y1)));
            }
        }
    }

    /// <summary>슬롯별 4x 회로도 아날로그 상태 (순수 값 타입, 할당 0).</summary>
    internal struct CircuitState
    {
        // 4x 도메인 1차 RC 커플링 HPF / 밀러 LPF 상태 (최대 4단 캐스케이드)
        public float Hp0X, Hp0Y;
        public float Hp1X, Hp1Y;
        public float Hp2X, Hp2Y;
        public float Hp3X, Hp3Y;
        public float Lp0Y, Lp1Y, Lp2Y, Lp3Y;

        // LM308 Op-Amp 슬루레이트 리미터 상태
        public float SlewY;

        // JFET / BJT 동적 바이어스 시프트 (그리드/게이트 전류 포화)
        public float DynBias0, DynBias1;

        // CdS 옵토커플러 (Optical Tremolo) / BBD 프리엠퍼시스 상태
        public float OptoEnv;
        public float PreEmpL, PreEmpR;
        public float DeEmpL, DeEmpR;

        public void Reset()
        {
            Hp0X = Hp0Y = Hp1X = Hp1Y = Hp2X = Hp2Y = Hp3X = Hp3Y = 0f;
            Lp0Y = Lp1Y = Lp2Y = Lp3Y = 0f;
            SlewY = 0f;
            DynBias0 = DynBias1 = 0f;
            OptoEnv = 0f;
            PreEmpL = PreEmpR = DeEmpL = DeEmpR = 0f;
        }
    }

    // =========================================================================
    // 물리 소자 방정식 솔버 (Shockley Diode Newton-Raphson / Slew / JFET / BJT)
    // =========================================================================

    /// <summary>
    /// 대칭 안티-패럴렐 다이오드 페어 피드백/션트 클리퍼:
    /// vd + 2 * R * Is * sinh(vd / (eta * Vt)) = vDrive
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SolveSymDiodeNR(double vDrive, double coeff, double etaVt, double invEtaVt, double maxVd)
    {
        double absDrv = Math.Abs(vDrive);
        double maxAsinh = etaVt * Math.Asinh(Math.Max(0.0, absDrv / Math.Max(1e-18, coeff)));
        double vd = (vDrive >= 0.0 ? 1.0 : -1.0) * Math.Min(absDrv, Math.Min(maxAsinh, maxVd));

        for (int it = 0; it < 3; it++)
        {
            double u = Math.Clamp(vd * invEtaVt, -26.0, 26.0);
            double sh = Math.Sinh(u);
            double ch = Math.Cosh(u);
            double f = vd + coeff * sh - vDrive;
            double df = 1.0 + coeff * invEtaVt * ch;
            vd = Math.Clamp(vd - Math.Clamp(f / df, -0.28, 0.28), -maxVd, maxVd);
        }
        return (float)vd;
    }

    /// <summary>
    /// 비대칭 3-다이오드 클리퍼 (BOSS SD-1, Vemuram Jan Ray, Fulltone OCD):
    /// +반파: 2개 직렬 다이오드(2*eta*Vt), -반파: 1개 다이오드(eta*Vt)
    /// vd + R * Is * (exp(vd / (2*eta*Vt)) - exp(-vd / (eta*Vt))) = vDrive
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SolveAsymDiodeNR(double vDrive, double rIs, double etaVt, double invEtaVt, double maxVd)
    {
        double inv2 = 0.5 * invEtaVt;
        double vd = Math.Clamp(vDrive, -maxVd * 0.75, maxVd * 1.45);
        if (Math.Abs(vDrive) > 0.35)
        {
            vd = vDrive >= 0.0
                ? Math.Min(vDrive, 2.0 * etaVt * Math.Log(1.0 + vDrive / Math.Max(1e-18, rIs)))
                : -Math.Min(-vDrive, etaVt * Math.Log(1.0 + (-vDrive) / Math.Max(1e-18, rIs)));
        }

        for (int it = 0; it < 3; it++)
        {
            double ep = Math.Exp(Math.Clamp(vd * inv2, -26.0, 26.0));
            double en = Math.Exp(Math.Clamp(-vd * invEtaVt, -26.0, 26.0));
            double f = vd + rIs * (ep - en) - vDrive;
            double df = 1.0 + rIs * (ep * inv2 + en * invEtaVt);
            vd = Math.Clamp(vd - Math.Clamp(f / df, -0.32, 0.32), -maxVd, maxVd * 1.8);
        }
        return (float)vd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hpf1(float x, float a, ref float x1, ref float y1)
    {
        float y = 0.5f * (1f + a) * (x - x1) + a * y1;
        x1 = x;
        y1 = y;
        return y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lpf1(float x, float a, ref float y1)
    {
        y1 += a * (x - y1);
        return y1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastTanh(float x)
    {
        if (x < -4.5f) return -1f;
        if (x > 4.5f) return 1f;
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }

    // =========================================================================
    // 1) Ibanez TS808 / TS9 Tube Screamer (4x OS 1N914 피드백 다이오드 클리퍼)
    //    회로도: R4=4.7kΩ, C3=47nF (720.5Hz HPF), Rf=51k+500k*Drive, C4=51pF (~4.8kHz LPF)
    // =========================================================================
    public static void ProcessTubeScreamer4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float drive0to10,
        bool isTs9)
    {
        double dt = 1.0 / fs4;
        float inHpfA = (float)Math.Exp(-2.0 * Math.PI * 31.8 * dt);
        // TS9은 출력 버퍼 및 약간 타이트한 740Hz 피드백 HPF 특성
        float clipHpfA = (float)Math.Exp(-2.0 * Math.PI * (isTs9 ? 745.0 : 720.5) * dt);
        float fbLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (isTs9 ? 5400.0 : 4800.0) * dt));

        double d = Math.Clamp(drive0to10, 0f, 10f) / 10.0;
        double rf = 51000.0 + 500000.0 * (d * d);
        double coeff = 2.0 * rf * SiIs;
        float gainFb = (float)((rf / 4700.0) * (isTs9 ? 0.27 : 0.25));

        for (int i = 0; i < buf4.Length; i++)
        {
            float xAc = Hpf1(buf4[i], inHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            float vHp = Hpf1(xAc, clipHpfA, ref cs.Hp1X, ref cs.Hp1Y);
            float vd = SolveSymDiodeNR(vHp * gainFb, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            float fb = Lpf1(vd, fbLpfA, ref cs.Lp0Y);
            // Op-amp non-inverting feedback topology: Vout = Vin + Vdiode
            buf4[i] = xAc * 0.65f + fb * 0.90f;
        }
    }

    // =========================================================================
    // 2) Klon Centaur (4x OS 듀얼-갱 클린+1N34A 게르마늄 다이오드 병렬 서밍)
    //    회로도: Clean path(1 - Gain) + Clip path(392Hz/1kHz 부스트 -> 1N34A Ge 션트)
    // =========================================================================
    public static void ProcessKlonCentaur4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10)
    {
        double dt = 1.0 / fs4;
        float clipHpfA = (float)Math.Exp(-2.0 * Math.PI * 392.0 * dt);
        float clipLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 4600.0 * dt));

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        // Klon 듀얼-갱 Gain 포텐셔미터: Gain이 오를수록 클린 경로 감소, 클리핑 경로 증가
        float cleanGain = (float)(1.0 - 0.72 * g);
        float clipGain = (float)(0.20 + 1.05 * g);
        float preGain = (float)(1.6 + 22.0 * g * g);
        double coeff = 2.0 * 1000.0 * GeIs; // 1kΩ 직렬저항 + 1N34A Ge 션트

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = buf4[i];
            float vHp = Hpf1(x, clipHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            // 18V 차지펌프 높은 헤드룸 옵앰프 -> 1N34A 게르마늄 션트 클리핑
            float vGe = SolveSymDiodeNR(vHp * preGain, coeff, GeEtaVt, GeInvEtaVt, GeMaxVd);
            float vClip = Lpf1(vGe, clipLpfA, ref cs.Lp0Y);
            buf4[i] = x * cleanGain + vClip * clipGain * 1.45f;
        }
    }

    // =========================================================================
    // 3) ProCo RAT / RAT 2 (4x OS LM308 슬루레이트 리미터 + 1N914 션트 하드 클리퍼)
    //    회로도: 47Ω/2.2uF(1.54kHz) + 560Ω/4.7uF(60Hz) -> LM308 Slew(0.3V/us) -> 1kΩ + 1N914 션트
    // =========================================================================
    public static void ProcessRat4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float dist0to10,
        bool isRat2)
    {
        double dt = 1.0 / fs4;
        float hpfLowA = (float)Math.Exp(-2.0 * Math.PI * 60.0 * dt);
        float hpfHighA = (float)Math.Exp(-2.0 * Math.PI * (isRat2 ? 1540.0 : 1420.0) * dt);
        float millerA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (isRat2 ? 9200.0 : 7800.0) * dt));

        double d = Math.Clamp(dist0to10, 0f, 10f) / 10.0;
        float gain = (float)(2.4 + 48.0 * d * d);
        // LM308 슬루레이트(0.3V/us -> 정규화 오디오 도메인 스텝 제한; RAT2 OP07DP는 약간 더 빠름)
        float maxStep = (isRat2 ? 0.42f : 0.28f) * (176400f / fs4);
        double coeff = 2.0 * 1000.0 * SiIs; // 1kΩ + 1N914 션트

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = buf4[i];
            float vLow = Hpf1(x, hpfLowA, ref cs.Hp0X, ref cs.Hp0Y);
            float vHi = Hpf1(vLow, hpfHighA, ref cs.Hp1X, ref cs.Hp1Y);
            float target = (vLow * 0.35f + vHi * 0.95f) * gain;

            // LM308 슬루레이트 리미팅 (RAT 특유의 두툼한 삼각파 느낌의 고역 포화)
            float delta = Math.Clamp(target - cs.SlewY, -maxStep, maxStep);
            cs.SlewY += delta;

            // 9V 레일 포화 후 1N914 하드 션트 다이오드 클리핑
            float opOut = FastTanh(cs.SlewY * 0.45f) * 2.2f;
            float clipped = SolveSymDiodeNR(opOut, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            buf4[i] = Lpf1(clipped, millerA, ref cs.Lp0Y) * 1.35f;
        }
    }

    // =========================================================================
    // 4) EHX Big Muff Pi (4x OS 4단 2N5088 BJT 캐스케이드 + 피드백 다이오드 클리퍼)
    //    회로도: Q1 부스터 -> Q2(1N914 피드백 + 470pF) -> Q3(1N914 피드백 + 470pF)
    // =========================================================================
    public static void ProcessBigMuff4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float sustain0to10)
    {
        double dt = 1.0 / fs4;
        float hp1A = (float)Math.Exp(-2.0 * Math.PI * 52.0 * dt);
        float hp2A = (float)Math.Exp(-2.0 * Math.PI * 95.0 * dt);
        float hp3A = (float)Math.Exp(-2.0 * Math.PI * 110.0 * dt);
        float millerA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 3600.0 * dt)); // 470pF 피드백 LPF

        double s = Math.Clamp(sustain0to10, 0f, 10f) / 10.0;
        float q1Gain = (float)(2.2 + 12.0 * s);
        float q2Gain = (float)(3.5 + 14.0 * s);
        float q3Gain = (float)(3.0 + 10.0 * s);
        double coeff = 2.0 * 47000.0 * SiIs; // 47kΩ 피드백 + 1N914

        for (int i = 0; i < buf4.Length; i++)
        {
            // Q1: 2N5088 입력 트랜지스터 부스터
            float q1In = Hpf1(buf4[i], hp1A, ref cs.Hp0X, ref cs.Hp0Y) * q1Gain;
            float q1Out = FastTanh(q1In);

            // Q2: 1차 다이오드 클리핑단 + 470pF 밀러 LPF
            float q2In = Hpf1(q1Out, hp2A, ref cs.Hp1X, ref cs.Hp1Y) * q2Gain;
            float q2Clip = SolveSymDiodeNR(q2In, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            float q2Out = Lpf1(q2Clip, millerA, ref cs.Lp0Y);

            // Q3: 2차 다이오드 클리핑단 + 470pF 밀러 LPF (무한 서스테인 퍼즈)
            float q3In = Hpf1(q2Out, hp3A, ref cs.Hp2X, ref cs.Hp2Y) * q3Gain;
            float q3Clip = SolveSymDiodeNR(q3In, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            buf4[i] = Lpf1(q3Clip, millerA, ref cs.Lp1Y) * 1.45f;
        }
    }

    // =========================================================================
    // 5) BOSS SD-1 Super OverDrive (4x OS 비대칭 3-다이오드 1S1588 피드백 클리퍼)
    //    회로도: 4.7kΩ/47nF(720Hz) 피드백 루프 내 2개(+반파) vs 1개(-반파) 비대칭 클리핑
    // =========================================================================
    public static void ProcessBossSd14x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float drive0to10)
    {
        double dt = 1.0 / fs4;
        float inHpfA = (float)Math.Exp(-2.0 * Math.PI * 34.0 * dt);
        float clipHpfA = (float)Math.Exp(-2.0 * Math.PI * 640.0 * dt);
        float fbLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 5000.0 * dt));

        double d = Math.Clamp(drive0to10, 0f, 10f) / 10.0;
        double rf = 33000.0 + 1000000.0 * (d * d);
        double rIs = rf * SiIs;
        float gainFb = (float)((rf / 4700.0) * 0.16);

        for (int i = 0; i < buf4.Length; i++)
        {
            float xAc = Hpf1(buf4[i], inHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            float vHp = Hpf1(xAc, clipHpfA, ref cs.Hp1X, ref cs.Hp1Y);
            float vd = SolveAsymDiodeNR(vHp * gainFb, rIs, SiEtaVt, SiInvEtaVt, SiMaxVd);
            float fb = Lpf1(vd, fbLpfA, ref cs.Lp0Y);
            buf4[i] = xAc * 0.55f + fb * 0.85f;
        }
    }

    // =========================================================================
    // 6) BOSS DS-1 Distortion (4x OS 2SC1815 BJT 부스터 -> TA7136AP 옵앰프 -> 1N4148 션트)
    // =========================================================================
    public static void ProcessBossDs14x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float dist0to10)
    {
        double dt = 1.0 / fs4;
        float q2HpfA = (float)Math.Exp(-2.0 * Math.PI * 48.0 * dt);
        float opHpfA = (float)Math.Exp(-2.0 * Math.PI * 165.0 * dt);
        float opLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 6500.0 * dt));

        double d = Math.Clamp(dist0to10, 0f, 10f) / 10.0;
        float opGain = (float)(3.0 + 46.0 * d * d);
        double coeff = 2.0 * 2200.0 * SiIs; // 2.2kΩ 직렬 + 1N4148 션트

        for (int i = 0; i < buf4.Length; i++)
        {
            // Q2 2SC1815 트랜지스터 프리부스터 (약간의 비대칭 짝수 고조파)
            float q2In = Hpf1(buf4[i], q2HpfA, ref cs.Hp0X, ref cs.Hp0Y) * 2.1f;
            float q2Out = q2In >= 0f ? FastTanh(q2In * 0.92f) : FastTanh(q2In * 1.08f);

            // TA7136AP 옵앰프 게인단 -> 2.2kΩ + 1N4148 하드 션트 클리핑
            float opIn = Hpf1(q2Out, opHpfA, ref cs.Hp1X, ref cs.Hp1Y) * opGain;
            float opOut = FastTanh(opIn * 0.5f) * 2.0f;
            float vd = SolveSymDiodeNR(opOut, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            buf4[i] = Lpf1(vd, opLpfA, ref cs.Lp0Y) * 1.45f;
        }
    }

    // =========================================================================
    // 7) BOSS BD-2 Blues Driver (4x OS 4단 디스크리트 JFET 2SK246 캐스케이드)
    //    회로도: 앰프 프리앰프와 동일한 다단 JFET + 동적 게이트 바이어스 시프트
    // =========================================================================
    public static void ProcessBossBd24x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10)
    {
        double dt = 1.0 / fs4;
        float hp1A = (float)Math.Exp(-2.0 * Math.PI * 65.0 * dt);
        float hp2A = (float)Math.Exp(-2.0 * Math.PI * 115.0 * dt);
        float hp3A = (float)Math.Exp(-2.0 * Math.PI * 95.0 * dt);
        float millerA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 7600.0 * dt));
        float atkA = (float)(1.0 - Math.Exp(-1.0 / (0.001 * fs4)));
        float relA = (float)(1.0 - Math.Exp(-1.0 / (0.055 * fs4)));

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        float g1 = (float)(1.5 + 8.5 * g);
        float g2 = (float)(1.4 + 6.5 * g * g);
        float g3 = (float)(1.2 + 3.2 * g);

        for (int i = 0; i < buf4.Length; i++)
        {
            // JFET Stage 1 (2SK246)
            float x1 = Hpf1(buf4[i], hp1A, ref cs.Hp0X, ref cs.Hp0Y) * g1 - cs.DynBias0;
            if (x1 > 0f) cs.DynBias0 = Math.Clamp(cs.DynBias0 + atkA * (x1 * 0.22f - cs.DynBias0), 0f, 0.25f);
            else cs.DynBias0 = Math.Max(0f, cs.DynBias0 + relA * (0f - cs.DynBias0));
            float s1 = x1 >= 0f ? FastTanh(x1 * 0.88f) / 0.88f : FastTanh(x1 * 1.18f) / 1.18f;

            // JFET Stage 2
            float x2 = Hpf1(s1, hp2A, ref cs.Hp1X, ref cs.Hp1Y) * g2 - cs.DynBias1;
            if (x2 > 0f) cs.DynBias1 = Math.Clamp(cs.DynBias1 + atkA * (x2 * 0.26f - cs.DynBias1), 0f, 0.28f);
            else cs.DynBias1 = Math.Max(0f, cs.DynBias1 + relA * (0f - cs.DynBias1));
            float s2 = x2 >= 0f ? FastTanh(x2 * 1.12f) / 1.12f : FastTanh(x2 * 0.90f) / 0.90f;

            // JFET Stage 3 + 밀러 LPF
            float x3 = Hpf1(s2, hp3A, ref cs.Hp2X, ref cs.Hp2Y) * g3;
            float s3 = FastTanh(x3);
            buf4[i] = Lpf1(s3, millerA, ref cs.Lp0Y);
        }
    }

    // =========================================================================
    // 8) BOSS MT-2 Metal Zone & HM-2 Heavy Metal (4x OS 듀얼 하이게인 실리콘 클리퍼)
    // =========================================================================
    public static void ProcessBossHighGain4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float dist0to10,
        bool isHm2)
    {
        double dt = 1.0 / fs4;
        float hp1A = (float)Math.Exp(-2.0 * Math.PI * (isHm2 ? 95.0 : 185.0) * dt);
        float hp2A = (float)Math.Exp(-2.0 * Math.PI * (isHm2 ? 120.0 : 240.0) * dt);
        float lpA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (isHm2 ? 6200.0 : 7400.0) * dt));

        double d = Math.Clamp(dist0to10, 0f, 10f) / 10.0;
        float g1 = (float)(isHm2 ? (5.5 + 36.0 * d) : (4.2 + 28.0 * d));
        float g2 = (float)(isHm2 ? (3.2 + 16.0 * d) : (2.6 + 14.0 * d));
        double coeff = 2.0 * (isHm2 ? 1000.0 : 2200.0) * SiIs;

        for (int i = 0; i < buf4.Length; i++)
        {
            float x1 = Hpf1(buf4[i], hp1A, ref cs.Hp0X, ref cs.Hp0Y) * g1;
            float c1 = SolveSymDiodeNR(x1, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            float x2 = Hpf1(c1, hp2A, ref cs.Hp1X, ref cs.Hp1Y) * g2;
            float c2 = SolveSymDiodeNR(x2, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            buf4[i] = Lpf1(c2, lpA, ref cs.Lp0Y) * 1.45f;
        }
    }

    // =========================================================================
    // 9) Caline Pure Sky (Timmy) & Vemuram Jan Ray (4x OS 피드백 다이오드 + 프리-베이스 컷)
    // =========================================================================
    public static void ProcessTimmyJanRay4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10,
        float bass0to10,
        float sat0to10,
        bool isJanRay)
    {
        double dt = 1.0 / fs4;
        // Timmy / Jan Ray 핵심 회로 특징: Bass 노브가 클리핑 단 앞단(Pre-clip) HPF 컷오프를 조절!
        double bassFc = 42.0 + (10.0 - Math.Clamp(bass0to10, 0f, 10f)) * 22.0;
        float preBassHpfA = (float)Math.Exp(-2.0 * Math.PI * bassFc * dt);
        float fbLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (isJanRay ? 7600.0 : 8200.0) * dt));

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        double sat = isJanRay ? Math.Clamp(sat0to10, 0f, 10f) / 10.0 : 0.35;
        double rf = 10000.0 + (isJanRay ? (250000.0 + 250000.0 * sat) : 280000.0) * (g * g);
        float gainFb = (float)((rf / 4700.0) * (isJanRay ? (0.18 + 0.10 * sat) : 0.22));

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], preBassHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            float vd;
            if (isJanRay)
            {
                // Jan Ray: 비대칭 다이오드 + 새추레이션 트리머에 의한 짝수 고조파 밀도 조절
                vd = SolveAsymDiodeNR(x * gainFb, rf * SiIs, SiEtaVt, SiInvEtaVt, SiMaxVd);
            }
            else
            {
                // Pure Sky (Timmy): 대칭 1N4148 투명 피드백 클리핑
                vd = SolveSymDiodeNR(x * gainFb, 2.0 * rf * SiIs, SiEtaVt, SiInvEtaVt, SiMaxVd);
            }
            float fb = Lpf1(vd, fbLpfA, ref cs.Lp0Y);
            buf4[i] = x * 0.50f + fb * 0.88f;
        }
    }

    // =========================================================================
    // 10) Fulltone OCD (4x OS 2N7000 MOSFET + 1N34A Ge 비대칭 션트 클리핑)
    // =========================================================================
    public static void ProcessFulltoneOcd4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float drive0to10,
        bool highPeak)
    {
        double dt = 1.0 / fs4;
        float hpfA = (float)Math.Exp(-2.0 * Math.PI * (highPeak ? 125.0 : 75.0) * dt);
        float lpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (highPeak ? 7800.0 : 6200.0) * dt));

        double d = Math.Clamp(drive0to10, 0f, 10f) / 10.0;
        float gain = (float)(2.0 + 28.0 * d * d);
        // 2N7000 MOSFET(게이트-드레인 다이오드 연결, Vf ~ 1.4V) + 1N34A Ge 비대칭 조합
        double rIs = 10000.0 * SiIs;

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], hpfA, ref cs.Hp0X, ref cs.Hp0Y) * gain;
            float vd = SolveAsymDiodeNR(x, rIs, SiEtaVt * 1.35, SiInvEtaVt / 1.35, SiMaxVd * 1.5);
            buf4[i] = Lpf1(vd, lpfA, ref cs.Lp0Y) * 1.15f;
        }
    }

    // =========================================================================
    // 11) Xotic BB Preamp (4x OS 대칭 다이오드 + 듀얼 소프트 클리핑)
    // =========================================================================
    public static void ProcessBbPreamp4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10)
    {
        double dt = 1.0 / fs4;
        float hpfA = (float)Math.Exp(-2.0 * Math.PI * 88.0 * dt);
        float lpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 5600.0 * dt));

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        double rf = 47000.0 + 500000.0 * (g * g);
        double coeff = 2.0 * rf * SiIs;
        float gainFb = (float)((rf / 4700.0) * 0.28);

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], hpfA, ref cs.Hp0X, ref cs.Hp0Y);
            float vd = SolveSymDiodeNR(x * gainFb, coeff, SiEtaVt, SiInvEtaVt, SiMaxVd);
            float s2 = FastTanh(vd * 1.35f);
            buf4[i] = Lpf1(s2, lpfA, ref cs.Lp0Y) * 1.15f;
        }
    }

    // =========================================================================
    // 12) Bogner Ecstasy Red (4x OS 다단 JFET + Red LED 비대칭 하이게인 앰프-인-어-박스)
    // =========================================================================
    public static void ProcessBognerEcstasy4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10)
    {
        double dt = 1.0 / fs4;
        float hp1A = (float)Math.Exp(-2.0 * Math.PI * 135.0 * dt);
        float hp2A = (float)Math.Exp(-2.0 * Math.PI * 165.0 * dt);
        float hp3A = (float)Math.Exp(-2.0 * Math.PI * 110.0 * dt);
        float lpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 7200.0 * dt));

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        float g1 = (float)(3.0 + 18.0 * g);
        float g2 = (float)(2.4 + 10.0 * g);
        double ledCoeff = 2.0 * 4700.0 * LedIs;

        for (int i = 0; i < buf4.Length; i++)
        {
            float x1 = Hpf1(buf4[i], hp1A, ref cs.Hp0X, ref cs.Hp0Y) * g1;
            float s1 = x1 >= 0f ? FastTanh(x1 * 0.9f) : FastTanh(x1 * 1.25f) * 0.88f;

            float x2 = Hpf1(s1, hp2A, ref cs.Hp1X, ref cs.Hp1Y) * g2;
            float s2 = SolveSymDiodeNR(x2, ledCoeff, LedEtaVt, LedInvEtaVt, LedMaxVd);

            float x3 = Hpf1(s2, hp3A, ref cs.Hp2X, ref cs.Hp2Y);
            buf4[i] = Lpf1(FastTanh(x3 * 0.95f), lpfA, ref cs.Lp0Y) * 1.25f;
        }
    }

    // =========================================================================
    // 13) Xotic EP Booster (J201 JFET Echoplex EP-3), RC Booster, MXR Micro Amp (4x OS)
    // =========================================================================
    public static void ProcessCleanBoost4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10,
        int kind) // 0 = EP Booster (J201 JFET), 1 = RC Booster (OpAmp + soft Si), 2 = MXR Micro Amp (uA741)
    {
        double dt = 1.0 / fs4;
        float hpfA = (float)Math.Exp(-2.0 * Math.PI * (kind == 0 ? 28.0 : 20.0) * dt);
        float lpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (kind == 0 ? 11500.0 : 15000.0) * dt));

        float gNorm = Math.Clamp(gain0to10, 0f, 10f) / 10f;
        float linGain = kind switch
        {
            0 => MathF.Pow(10f, (gNorm * 18f) / 20f), // EP Booster (+20dB JFET)
            1 => 1.0f + 5.5f * gNorm * gNorm,         // RC Booster gain stage
            _ => MathF.Pow(10f, (gNorm * 24f) / 20f), // MXR Micro Amp (+26dB)
        };

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], hpfA, ref cs.Hp0X, ref cs.Hp0Y) * linGain;
            float y;
            if (kind == 0)
            {
                // J201 JFET: 따뜻한 2차 고조파 비대칭 헤드룸
                y = x >= 0f ? FastTanh(x * 0.58f) / 0.58f : FastTanh(x * 0.68f) / 0.68f;
            }
            else if (kind == 1)
            {
                // RC Booster: 초투명 옵앰프 + 최대 게인에서만 부드러운 실리콘 소프트 클립
                y = FastTanh(x * 0.55f) / 0.55f;
            }
            else
            {
                // MXR Micro Amp: 높은 헤드룸 클린 레일 포화
                y = FastTanh(x * 0.42f) / 0.42f;
            }
            buf4[i] = Lpf1(y, lpfA, ref cs.Lp0Y);
        }
    }

    // =========================================================================
    // 14) Marshall Bluesbreaker MK1 (1991 공개 회로도: TL072 + 2x1N914 직렬 피드백 클리퍼)
    //     회로도: Stage 1 (10kΩ/10nF 1.59kHz 트레블 리프트) -> Stage 2 (양방향 각 2개 직렬 1N914: 2*Eta*Vt)
    // =========================================================================
    public static void ProcessBluesbreaker4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10)
    {
        double dt = 1.0 / fs4;
        float inHpfA = (float)Math.Exp(-2.0 * Math.PI * 45.0 * dt);
        float s1TrebleHpfA = (float)Math.Exp(-2.0 * Math.PI * 1590.0 * dt); // 10kΩ + 10nF
        float fbLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 6800.0 * dt)); // 100pF

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        float s1Gain = (float)(1.2 + 5.8 * g);
        double rf = 22000.0 + 220000.0 * g;
        // 양방향 2개 직렬 1N914 다이오드: 유효 열전압 = 2 * SiEtaVt (TS808 대비 2배 높은 1.3V 헤드룸!)
        double bbEtaVt = 2.0 * SiEtaVt;
        double bbInvEtaVt = 0.5 * SiInvEtaVt;
        double bbMaxVd = 2.0 * SiMaxVd;
        double coeff = 2.0 * rf * SiIs;
        float gainFb = (float)((rf / 6800.0) * 0.24);

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], inHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            float hi = Hpf1(x, s1TrebleHpfA, ref cs.Hp1X, ref cs.Hp1Y);
            float s1 = (x + hi * 0.85f) * s1Gain;
            float vd = SolveSymDiodeNR(s1 * gainFb, coeff, bbEtaVt, bbInvEtaVt, bbMaxVd);
            float fb = Lpf1(vd, fbLpfA, ref cs.Lp0Y);
            buf4[i] = x * 0.45f + fb * 0.92f;
        }
    }

    // =========================================================================
    // 15) Marshall Guv'nor MK1 (1988 공개 회로도: TL072 2단 증폭 + Red LED 션트 하드 클리퍼)
    //     회로도: 22nF/1kΩ 입력 + 680nF/220Ω 미드 부스트 -> Red LED (Vf ~ 1.65V) 안티-패럴렐 션트
    // =========================================================================
    public static void ProcessGuvnor4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float gain0to10)
    {
        double dt = 1.0 / fs4;
        float hp1A = (float)Math.Exp(-2.0 * Math.PI * 120.0 * dt);
        float hp2A = (float)Math.Exp(-2.0 * Math.PI * 720.0 * dt);
        float lpA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 7800.0 * dt));

        double g = Math.Clamp(gain0to10, 0f, 10f) / 10.0;
        float preGain = (float)(2.5 + 42.0 * g * g);
        double ledCoeff = 2.0 * 1500.0 * LedIs; // 1.5kΩ + Red LED pair to ground

        for (int i = 0; i < buf4.Length; i++)
        {
            float x1 = Hpf1(buf4[i], hp1A, ref cs.Hp0X, ref cs.Hp0Y);
            float x2 = Hpf1(x1, hp2A, ref cs.Hp1X, ref cs.Hp1Y);
            float driven = (x1 * 0.45f + x2 * 0.85f) * preGain;
            float vd = SolveSymDiodeNR(driven, ledCoeff, LedEtaVt, LedInvEtaVt, LedMaxVd);
            buf4[i] = Lpf1(vd, lpA, ref cs.Lp0Y) * 0.85f;
        }
    }

    // =========================================================================
    // 16) Dallas Arbiter Fuzz Face (1966 공개 회로도: Q1+Q2 전압-션트 피드백 2-트랜지스터 퍼즈)
    //     회로도: 2.2uF 입력 -> Q1(AC128 Ge / BC108 Si) 직결 Q2 -> 100kΩ 콜렉터-베이스 NFB + Q2 Bias
    // =========================================================================
    public static void ProcessFuzzFace4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float fuzz0to10,
        float bias0to10,
        bool isSilicon)
    {
        double dt = 1.0 / fs4;
        float inHpfA = (float)Math.Exp(-2.0 * Math.PI * 65.0 * dt);
        float outHpfA = (float)Math.Exp(-2.0 * Math.PI * 110.0 * dt); // 10nF 출력 커플링 캡
        // Ge AC128은 높은 콜렉터 커패시턴스로 인해 ~4.8kHz 롤오프, Si BC108은 ~8.5kHz까지 확장
        float millerA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * (isSilicon ? 8500.0 : 4800.0) * dt));

        float fNorm = Math.Clamp(fuzz0to10, 0f, 10f) / 10f;
        float q1Gain = (isSilicon ? 2.4f : 1.8f) + (isSilicon ? 8.5f : 6.2f) * fNorm * fNorm;
        float q2Gain = (isSilicon ? 3.2f : 2.5f) + (isSilicon ? 9.5f : 7.0f) * fNorm;
        // Q1->Q2 직결 바이어스 오프셋 (듀티 사이클 비대칭 -> 10nF 출력 캡 통과 후에도 강한 2차 짝수 고조파 유지)
        float q2Bias = (Math.Clamp(bias0to10, 0f, 10f) - 5.0f) * 0.18f - 0.34f;
        float biasZero = FastTanh(q2Bias * q2Gain);

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], inHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            // 100kΩ Q2->Q1 전압-션트 네거티브 피드백 루프
            float vIn = x - cs.DynBias0 * 0.16f;
            float q1 = vIn >= 0f ? FastTanh(vIn * q1Gain * 0.85f) : FastTanh(vIn * q1Gain * 1.15f) * 0.90f;

            // Q1 컬렉터 -> Q2 베이스 직결(DC-coupled): 바이어스 오프셋이 영교차 듀티 사이클 자체를 이동시킴
            float q2Drive = (q1 + q2Bias) * q2Gain;
            float q2 = (q2Drive >= 0f ? FastTanh(q2Drive * 1.25f) / 1.25f : FastTanh(q2Drive * 0.85f) / 0.85f) - biasZero;
            cs.DynBias0 = Lpf1(q2, millerA, ref cs.Lp0Y);

            buf4[i] = Hpf1(cs.DynBias0, outHpfA, ref cs.Hp1X, ref cs.Hp1Y) * 1.15f;
        }
    }

    // =========================================================================
    // 17) Dallas Rangemaster Treble Booster (1965 공개 회로도: OC44 단일 게르마늄 트랜지스터)
    //     회로도: 4.7nF(Classic Treble, ~480Hz HPF) / 22nF(Full-Range, ~120Hz HPF) 입력 캡 + OC44 Ge
    // =========================================================================
    public static void ProcessRangemaster4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float boost0to10,
        bool fullRangeCap)
    {
        double dt = 1.0 / fs4;
        // 4.7nF 입력 커플링 커패시터가 기타 픽업/입력 임피던스와 만나 형성하는 클래식 트레블 부스트 HPF
        float inHpfA = (float)Math.Exp(-2.0 * Math.PI * (fullRangeCap ? 135.0 : 540.0) * dt);
        float oc44LpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 7200.0 * dt));

        float bNorm = Math.Clamp(boost0to10, 0f, 10f) / 10f;
        float gain = 2.0f + 16.0f * bNorm * bNorm;
        const float bias = -0.18f;
        float biasZero = FastTanh(bias * 1.28f) / 1.28f;

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], inHpfA, ref cs.Hp0X, ref cs.Hp0Y) * gain;
            float u = x + bias;
            // OC44 PNP 게르마늄 트랜지스터 비대칭 컬렉터 포화 (풍부한 2차 고조파)
            float y = (u >= 0f ? FastTanh(u * 0.82f) / 0.82f : FastTanh(u * 1.28f) / 1.28f) - biasZero;
            buf4[i] = Lpf1(y, oc44LpfA, ref cs.Lp0Y) * 1.45f;
        }
    }

    // =========================================================================
    // 18) MXR Distortion+ / DOD 250 (1978 공개 회로도: LM741 슬루레이트 + 1N270 게르마늄 션트)
    //     회로도: 4.7kΩ/47nF(720Hz) 가변 비반전 게인 -> LM741(0.5V/us) -> 10kΩ + 1N270 Ge 션트 클리퍼
    // =========================================================================
    public static void ProcessDistortionPlus4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float dist0to10)
    {
        double dt = 1.0 / fs4;
        float inHpfA = (float)Math.Exp(-2.0 * Math.PI * 72.0 * dt);
        float gainHpfA = (float)Math.Exp(-2.0 * Math.PI * 720.5 * dt); // 4.7kΩ + 47nF
        float rfCapLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 6400.0 * dt)); // 1nF shunt cap

        double d = Math.Clamp(dist0to10, 0f, 10f) / 10.0;
        float gain = (float)(1.8 + 44.0 * d * d);
        float maxStep = 0.36f * (176400f / fs4); // LM741 0.5V/us slew rate
        double geCoeff = 2.0 * 10000.0 * GeIs;   // 10kΩ + 1N270 Ge shunt pair

        for (int i = 0; i < buf4.Length; i++)
        {
            float x = Hpf1(buf4[i], inHpfA, ref cs.Hp0X, ref cs.Hp0Y);
            float xHi = Hpf1(x, gainHpfA, ref cs.Hp1X, ref cs.Hp1Y);
            float target = x + xHi * gain;

            float delta = Math.Clamp(target - cs.SlewY, -maxStep, maxStep);
            cs.SlewY += delta;

            float vd = SolveSymDiodeNR(cs.SlewY, geCoeff, GeEtaVt, GeInvEtaVt, GeMaxVd);
            buf4[i] = Lpf1(vd, rfCapLpfA, ref cs.Lp0Y) * 1.95f;
        }
    }

    // =========================================================================
    // 19) ZVEX Box of Rock (공개 회로도: 3단 BS170 MOSFET 5.1MΩ 드레인-게이트 바이어스 JTM45 에뮬레이터)
    // =========================================================================
    public static void ProcessBoxOfRock4x(
        ref CircuitState cs,
        Span<float> buf4,
        int fs4,
        float drive0to10)
    {
        double dt = 1.0 / fs4;
        float hp1A = (float)Math.Exp(-2.0 * Math.PI * 58.0 * dt);
        float hp2A = (float)Math.Exp(-2.0 * Math.PI * 95.0 * dt);
        float hp3A = (float)Math.Exp(-2.0 * Math.PI * 115.0 * dt);
        float lpA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 6800.0 * dt));

        float d = Math.Clamp(drive0to10, 0f, 10f) / 10f;
        float g1 = 1.8f + 9.5f * d;
        float g2 = 1.6f + 6.5f * d * d;
        float g3 = 1.4f + 3.5f * d;

        for (int i = 0; i < buf4.Length; i++)
        {
            // Stage 1: BS170 SHO-style MOSFET stage
            float x1 = Hpf1(buf4[i], hp1A, ref cs.Hp0X, ref cs.Hp0Y) * g1;
            float m1 = x1 >= 0f ? FastTanh(x1 * 0.85f) / 0.85f : FastTanh(x1 * 1.15f) / 1.15f;

            // Stage 2: BS170 JTM45 Preamp MOSFET stage
            float x2 = Hpf1(m1, hp2A, ref cs.Hp1X, ref cs.Hp1Y) * g2;
            float m2 = x2 >= 0f ? FastTanh(x2 * 1.10f) / 1.10f : FastTanh(x2 * 0.90f) / 0.90f;

            // Stage 3: BS170 Phase-Inverter/Power saturation stage
            float x3 = Hpf1(m2, hp3A, ref cs.Hp2X, ref cs.Hp2Y) * g3;
            float m3 = FastTanh(x3 * 0.95f);

            buf4[i] = Lpf1(m3, lpA, ref cs.Lp0Y) * 1.15f;
        }
    }
}
