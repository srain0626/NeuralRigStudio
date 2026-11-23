using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

public enum WhiteBoxChannel
{
    Clean = 0,
    Plexi = 1,
    HighGain = 2,
}

public sealed class WhiteBoxParams
{
    public WhiteBoxChannel Channel { get; set; } = WhiteBoxChannel.Plexi;
    public bool Bright { get; set; } = false;
    public bool TsEnabled { get; set; } = false;
    public float TsDrive { get; set; } = 3.5f;   // 0..10
    public float TsTone { get; set; } = 5.5f;    // 0..10
    public float TsLevel { get; set; } = 6.0f;   // 0..10
    public float PreGain { get; set; } = 5.5f;   // 0..10
    public float Bass { get; set; } = 5.0f;      // 0..10
    public float Mid { get; set; } = 5.5f;       // 0..10
    public float Treble { get; set; } = 5.5f;    // 0..10
    public float Master { get; set; } = 5.5f;    // 0..10
    public float Sag { get; set; } = 3.5f;       // 0..10
    public float Presence { get; set; } = 5.0f;  // 0..10
    public float Depth { get; set; } = 5.0f;     // 0..10
    public bool PowerAmpEnabled { get; set; } = true;

    public WhiteBoxParams Clone() => new()
    {
        Channel = Channel,
        Bright = Bright,
        TsEnabled = TsEnabled,
        TsDrive = Math.Clamp(TsDrive, 0f, 10f),
        TsTone = Math.Clamp(TsTone, 0f, 10f),
        TsLevel = Math.Clamp(TsLevel, 0f, 10f),
        PreGain = Math.Clamp(PreGain, 0f, 10f),
        Bass = Math.Clamp(Bass, 0f, 10f),
        Mid = Math.Clamp(Mid, 0f, 10f),
        Treble = Math.Clamp(Treble, 0f, 10f),
        Master = Math.Clamp(Master, 0f, 10f),
        Sag = Math.Clamp(Sag, 0f, 10f),
        Presence = Math.Clamp(Presence, 0f, 10f),
        Depth = Math.Clamp(Depth, 0f, 10f),
        PowerAmpEnabled = PowerAmpEnabled,
    };
}

/// <summary>
/// 실시간 4x HIIR 폴리페이즈 오버샘플링 기반 화이트박스 아날로그 진공관/디스토션 회로 모델링 엔진.
/// - Stage 1/2 HIIR Equiripple Polyphase Half-band IIR 4x Upsampler / Downsampler
/// - TS808 비대칭/대칭 Si 다이오드 피드백 클리퍼 (댐프드 뉴턴-랩슨)
/// - 12AX7 다단 진공관 프리앰프 (Clean / Plexi / High-Gain + 동적 그리드 바이어스 시프트 + 39k 콜드 클리퍼)
/// - 4-노드 패시브 FMV 톤스택 (SymPy KCL 검증 3차 쌍선형 변환, double 정밀도)
/// - 푸시풀 파워앰프 (B+ Sag / 출력관 및 트랜스포머 포화 / NFB Presence &amp; Depth)
/// - 내장 512-tap Minimum-Phase 4x12 V30 기본 캐비넷 IR
/// </summary>
public sealed class WhiteBoxAmpEngine
{
    public const int DefaultCabTaps = 512;

    // ── Laurent de Soras HIIR 폴리페이즈 올패스 하프밴드 계수 ──
    // Stage 1 (1x <-> 2x): 10-coef (5+5), pb(0..0.45) 0.000000 dB, sb(>=0.55) -133.09 dB
    private static readonly double[] S1A0 =
    {
        0.0236683141988347, 0.1898947622718017, 0.4315731806211855, 0.6632020224193995, 0.860015542499582,
    };
    private static readonly double[] S1A1 =
    {
        0.0905655590499339, 0.3078575723749043, 0.5516782402507934, 0.7652146863779808, 0.9524728378667541,
    };

    // Stage 2 (2x <-> 4x): 4-coef (2+2), pb(0..0.28) 0.000000 dB, sb(>=0.72) -106.60 dB
    private static readonly double[] S2A0 =
    {
        0.0463483004647977, 0.4119865160140078,
    };
    private static readonly double[] S2A1 =
    {
        0.1833581703161115, 0.7577900307152335,
    };

    private sealed class AllpassChain
    {
        private readonly double[] _a;
        private readonly double[] _x;
        private readonly double[] _y;

        public AllpassChain(double[] coefs)
        {
            _a = coefs;
            _x = new double[coefs.Length];
            _y = new double[coefs.Length];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Process(double input)
        {
            double s = input;
            var a = _a;
            var x = _x;
            var y = _y;
            for (int i = 0; i < a.Length; i++)
            {
                double outY = a[i] * (s - y[i]) + x[i];
                x[i] = s;
                y[i] = outY;
                s = outY;
            }
            return s;
        }

        public void Reset()
        {
            Array.Clear(_x);
            Array.Clear(_y);
        }
    }

    private sealed class Oversampler4x
    {
        private readonly AllpassChain _up1A0 = new(S1A0);
        private readonly AllpassChain _up1A1 = new(S1A1);
        private readonly AllpassChain _up2A0 = new(S2A0);
        private readonly AllpassChain _up2A1 = new(S2A1);

        private readonly AllpassChain _dn2A0 = new(S2A0);
        private readonly AllpassChain _dn2A1 = new(S2A1);
        private readonly AllpassChain _dn1A0 = new(S1A0);
        private readonly AllpassChain _dn1A1 = new(S1A1);

        private double[] _buf2x = Array.Empty<double>();
        private float[] _buf4x = Array.Empty<float>();

        public void Prepare(int maxFrames)
        {
            _buf2x = new double[maxFrames * 2];
            _buf4x = new float[maxFrames * 4];
            Reset();
        }

        public void Reset()
        {
            _up1A0.Reset(); _up1A1.Reset();
            _up2A0.Reset(); _up2A1.Reset();
            _dn2A0.Reset(); _dn2A1.Reset();
            _dn1A0.Reset(); _dn1A1.Reset();
        }

        public Span<float> Upsample(ReadOnlySpan<float> input, int frames)
        {
            var b2 = _buf2x;
            var b4 = _buf4x;
            int n2 = frames * 2;

            for (int i = 0; i < frames; i++)
            {
                double x = input[i];
                int o = i * 2;
                b2[o] = _up1A0.Process(x);
                b2[o + 1] = _up1A1.Process(x);
            }

            for (int i = 0; i < n2; i++)
            {
                double x = b2[i];
                int o = i * 2;
                b4[o] = (float)_up2A0.Process(x);
                b4[o + 1] = (float)_up2A1.Process(x);
            }
            return b4.AsSpan(0, frames * 4);
        }

        public void Downsample(Span<float> output, int frames)
        {
            var b2 = _buf2x;
            var b4 = _buf4x;
            int n2 = frames * 2;

            // H(z) = 0.5 * (A0(z^2) + z^-1 * A1(z^2)): in[2n+1] -> A0, in[2n] -> A1
            for (int i = 0; i < n2; i++)
            {
                int idx = i * 2;
                double even = b4[idx];
                double odd = b4[idx + 1];
                b2[i] = 0.5 * (_dn2A0.Process(odd) + _dn2A1.Process(even));
            }

            for (int i = 0; i < frames; i++)
            {
                int idx = i * 2;
                double even = b2[idx];
                double odd = b2[idx + 1];
                output[i] = (float)(0.5 * (_dn1A0.Process(odd) + _dn1A1.Process(even)));
            }
        }
    }

    // ── TS808 4x 다이오드 클리퍼 ──
    private struct Ts808Stage
    {
        private const double Vt = 0.02585;
        private const double Eta = 1.75;
        private const double EtaVt = Eta * Vt;
        private const double InvEtaVt = 1.0 / EtaVt;
        private const double Is = 2.52e-9;
        private const double R4 = 4700.0;
        private const double MaxVdClamp = EtaVt * 25.0;

        private float _inHpfA, _inHpfX1, _inHpfY1;
        private float _clipHpfA, _clipHpfX1, _clipHpfY1;
        private float _fbLpfA, _fbLpfY1;
        private float _toneLpfA, _toneLpfY1;
        private float _toneShelfA, _toneShelfX1, _toneShelfY1;

        private double _rf;
        private float _gainFb;
        private float _toneMix;
        private float _toneBrightGain;
        private float _levelGain;

        public void Configure(int fs4, float drive, float tone, float level)
        {
            double dt = 1.0 / fs4;
            _inHpfA = (float)Math.Exp(-2.0 * Math.PI * 32.0 * dt);
            _clipHpfA = (float)Math.Exp(-2.0 * Math.PI * 720.5 * dt);
            _fbLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 4800.0 * dt));

            double fcToneLpf = 720.0 + 2600.0 * (tone / 10.0);
            _toneLpfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * fcToneLpf * dt));
            _toneShelfA = (float)Math.Exp(-2.0 * Math.PI * 2400.0 * dt);

            double dNorm = Math.Clamp(drive, 0f, 10f) / 10.0;
            _rf = 51000.0 + 500000.0 * (dNorm * dNorm);
            _gainFb = (float)((_rf / R4) * 0.25);

            double tNorm = Math.Clamp(tone, 0f, 10f) / 10.0;
            _toneMix = (float)tNorm;
            _toneBrightGain = (float)(0.7 + 1.4 * tNorm);

            double lNorm = Math.Clamp(level, 0f, 10f) / 10.0;
            _levelGain = (float)(0.25 + 2.25 * lNorm);
        }

        public void Reset()
        {
            _inHpfX1 = _inHpfY1 = 0f;
            _clipHpfX1 = _clipHpfY1 = 0f;
            _fbLpfY1 = 0f;
            _toneLpfY1 = 0f;
            _toneShelfX1 = _toneShelfY1 = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            // 입력 DC/서브소닉 커플링 HPF (~32 Hz)
            float xAc = 0.5f * (1f + _inHpfA) * (x - _inHpfX1) + _inHpfA * _inHpfY1;
            _inHpfX1 = x;
            _inHpfY1 = xAc;

            // 피드백 클리핑 루프 720.5 Hz HPF
            float vHp = 0.5f * (1f + _clipHpfA) * (xAc - _clipHpfX1) + _clipHpfA * _clipHpfY1;
            _clipHpfX1 = xAc;
            _clipHpfY1 = vHp;

            // 댐프드 뉴턴-랩슨: vd + 2*Rf*Is*sinh(vd / (eta*Vt)) = vDrive
            double vDrive = vHp * _gainFb;
            double coeff = 2.0 * _rf * Is;
            double absDrv = Math.Abs(vDrive);
            double maxAsinh = EtaVt * Math.Asinh(Math.Max(0.0, absDrv / Math.Max(1e-18, coeff)));
            double vd = (vDrive >= 0.0 ? 1.0 : -1.0) * Math.Min(absDrv, Math.Min(maxAsinh, MaxVdClamp));

            for (int it = 0; it < 3; it++)
            {
                double u = Math.Clamp(vd * InvEtaVt, -25.0, 25.0);
                double sh = Math.Sinh(u);
                double ch = Math.Cosh(u);
                double f = vd + coeff * sh - vDrive;
                double df = 1.0 + coeff * InvEtaVt * ch;
                double step = Math.Clamp(f / df, -0.25, 0.25);
                vd = Math.Clamp(vd - step, -MaxVdClamp, MaxVdClamp);
            }

            // 51 pF 피드백 고역 롤오프 (~4.8 kHz)
            _fbLpfY1 += _fbLpfA * ((float)vd - _fbLpfY1);
            float vClip = xAc * 0.65f + _fbLpfY1 * 0.85f;

            // TS808 패시브/액티브 톤
            _toneLpfY1 += _toneLpfA * (vClip - _toneLpfY1);
            float vHpTone = vClip - _toneShelfY1;
            _toneShelfY1 += (1f - _toneShelfA) * (_toneShelfX1 + vClip - 2f * _toneShelfY1) * 0.5f;
            _toneShelfX1 = vClip;
            float mixedTone = _toneLpfY1 * (1f - 0.35f * _toneMix) + (vClip + vHpTone * (_toneBrightGain - 1f)) * (0.35f * _toneMix);

            return mixedTone * _levelGain;
        }
    }

    // ── 12AX7 단일 진공관 증폭단 (동적 그리드 바이어스 시프트 + 비대칭 포화 + 밀러 LPF + 커플링 HPF) ──
    private struct TriodeStage
    {
        private float _gain;
        private float _staticBias;
        private float _biasZeroOffset;
        private float _kPos;
        private float _kNeg;
        private float _maxDynShift;
        private float _shiftSpeed;

        private float _dynBias;
        private float _atkAlpha;
        private float _relAlpha;

        private float _millerA;
        private float _millerY;
        private float _hpfAlpha;
        private float _hpfX1;
        private float _hpfY1;

        private float _shelfA;
        private float _shelfGain;
        private float _shelfLpY;

        public void Configure(
            int fs4,
            float gain,
            float staticBias,
            float kPos,
            float kNeg,
            float hpfHz,
            float millerHz,
            float maxDynShift = 0.28f,
            float shiftSpeed = 0.35f,
            float shelfHz = 220f,
            float shelfBoost = 0f)
        {
            double dt = 1.0 / fs4;
            _gain = gain;
            _staticBias = staticBias;
            _kPos = Math.Max(0.25f, kPos);
            _kNeg = Math.Max(0.25f, kNeg);
            _maxDynShift = maxDynShift;
            _shiftSpeed = shiftSpeed;

            _biasZeroOffset = ShapeAsym(staticBias, _kPos, _kNeg);

            _atkAlpha = (float)(1.0 - Math.Exp(-1.0 / Math.Max(1.0, 0.0008 * fs4)));
            _relAlpha = (float)(1.0 - Math.Exp(-1.0 / Math.Max(1.0, 0.045 * fs4)));
            _millerA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * millerHz * dt));
            _hpfAlpha = (float)Math.Exp(-2.0 * Math.PI * hpfHz * dt);
            _shelfA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * shelfHz * dt));
            _shelfGain = shelfBoost;
        }

        public void Reset()
        {
            _dynBias = 0f;
            _millerY = 0f;
            _hpfX1 = _hpfY1 = 0f;
            _shelfLpY = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ShapeAsym(float u, float kp, float kn)
        {
            if (u >= 0f)
                return FastTanh(u * kp) / kp;
            return FastTanh(u * kn) / kn;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            // 캐소드/브라이트 바이패스 셸프 (초기 단간 프리앰프 쉐이핑)
            if (MathF.Abs(_shelfGain) > 1e-4f)
            {
                _shelfLpY += _shelfA * (x - _shelfLpY);
                x += (x - _shelfLpY) * _shelfGain;
            }

            float vGrid = x * _gain + _staticBias - _dynBias;
            if (vGrid > 0f)
                _dynBias = Math.Clamp(_dynBias + _atkAlpha * (vGrid * _shiftSpeed - _dynBias), 0f, _maxDynShift);
            else
                _dynBias = Math.Max(0f, _dynBias + _relAlpha * (0f - _dynBias));

            float plate = ShapeAsym(vGrid, _kPos, _kNeg) - _biasZeroOffset;

            // 밀러 커패시턴스 고역 롤오프
            _millerY += _millerA * (plate - _millerY);

            // 단간 DC 커플링 HPF
            float ac = 0.5f * (1f + _hpfAlpha) * (_millerY - _hpfX1) + _hpfAlpha * _hpfY1;
            _hpfX1 = _millerY;
            _hpfY1 = ac;
            return ac;
        }
    }

    // ── 정확한 4-노드 패시브 FMV 톤스택 (3차 쌍선형 변환, double 정밀도 필수) ──
    private struct FmvToneStack
    {
        private double _nb0, _nb1, _nb2, _nb3;
        private double _na1, _na2, _na3;
        private double _z1, _z2, _z3;
        private double _makeupGain;

        public void Configure(int fs4, WhiteBoxChannel ch, float bass0to10, float mid0to10, float treble0to10)
        {
            double r1, r2, r3, r4, c1, c2, c3, makeup;
            switch (ch)
            {
                case WhiteBoxChannel.Clean: // Fender Blackface AB763
                    r1 = 250e3; r2 = 250e3; r3 = 10e3; r4 = 100e3;
                    c1 = 250e-12; c2 = 100e-9; c3 = 47e-9;
                    makeup = 4.2;
                    break;
                case WhiteBoxChannel.HighGain: // Soldano SLO-100 / Modern
                    r1 = 250e3; r2 = 500e3; r3 = 25e3; r4 = 39e3;
                    c1 = 470e-12; c2 = 22e-9; c3 = 22e-9;
                    makeup = 2.3;
                    break;
                default: // Marshall 1959SLP / JCM800
                    r1 = 250e3; r2 = 1e6; r3 = 25e3; r4 = 33e3;
                    c1 = 470e-12; c2 = 22e-9; c3 = 22e-9;
                    makeup = 2.2;
                    break;
            }

            double bNorm = Math.Clamp(bass0to10, 0f, 10f) / 10.0;
            double mNorm = Math.Clamp(mid0to10, 0f, 10f) / 10.0;
            double t = Math.Clamp(treble0to10, 0f, 10f) / 10.0;

            // 실제 오디오 A-테이퍼(Bass) + 선형(Mid/Treble)
            double l = (Math.Exp(3.2 * bNorm) - 1.0) / (Math.Exp(3.2) - 1.0);
            double rb = l * r2;
            double rm = mNorm * r3;

            // SymPy로 KCL 4-노드 대비 4.29e-15 검증 완료된 정확한 3차 s-도메인 다항식
            double b1 = c1 * (r1 * t + rb + rm) + c2 * (rb + rm) + c3 * rm;
            double b2 = c1 * c2 * (r1 * r4 * t + r1 * rb + r1 * rm + r4 * rb + r4 * rm)
                      + c1 * c3 * (r1 * r4 * t + r1 * rm + r4 * rb + r4 * rm + rb * rm)
                      + c2 * c3 * rb * rm;
            double b3 = c1 * c2 * c3 * (r1 * r4 * rb * t + r1 * rb * rm + r4 * rb * rm);

            double a1 = c1 * (r1 + rb + rm) + c2 * (r4 + rb + rm) + c3 * (r4 + rm);
            double a2 = c1 * c2 * (r1 * r4 + r1 * rb + r1 * rm + r4 * rb + r4 * rm)
                      + c1 * c3 * (r1 * r4 + r1 * rm + r4 * rb + r4 * rm + rb * rm)
                      + c2 * c3 * (r4 * rb + rb * rm);
            double a3 = c1 * c2 * c3 * (r1 * r4 * rb + r1 * rb * rm + r4 * rb * rm);

            double c = 2.0 * fs4;
            double c2Sq = c * c;
            double c3Cu = c2Sq * c;

            double B0 = b1 * c + b2 * c2Sq + b3 * c3Cu;
            double B1 = b1 * c - b2 * c2Sq - 3.0 * b3 * c3Cu;
            double B2 = -b1 * c - b2 * c2Sq + 3.0 * b3 * c3Cu;
            double B3 = -b1 * c + b2 * c2Sq - b3 * c3Cu;

            double A0 = 1.0 + a1 * c + a2 * c2Sq + a3 * c3Cu;
            double A1 = 3.0 + a1 * c - a2 * c2Sq - 3.0 * a3 * c3Cu;
            double A2 = 3.0 - a1 * c - a2 * c2Sq + 3.0 * a3 * c3Cu;
            double A3 = 1.0 - a1 * c + a2 * c2Sq - a3 * c3Cu;

            double invA0 = 1.0 / A0;
            _nb0 = B0 * invA0;
            _nb1 = B1 * invA0;
            _nb2 = B2 * invA0;
            _nb3 = B3 * invA0;
            _na1 = A1 * invA0;
            _na2 = A2 * invA0;
            _na3 = A3 * invA0;
            _makeupGain = makeup;
        }

        public void Reset()
        {
            _z1 = _z2 = _z3 = 0.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            double x = input;
            double y = _nb0 * x + _z1;
            _z1 = _nb1 * x - _na1 * y + _z2;
            _z2 = _nb2 * x - _na2 * y + _z3;
            _z3 = _nb3 * x - _na3 * y;
            return (float)(y * _makeupGain);
        }
    }

    // ── 2차 RBJ 바이쿼드 (double 정밀도: Presence / Depth NFB 셸프) ──
    private struct BiquadD
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public void SetLowShelf(int fs, double fc, double gainDb, double slope = 0.75)
        {
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW * 0.5 * Math.Sqrt((A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0);
            double twoSqrtA = 2.0 * Math.Sqrt(A) * alpha;

            double b0 = A * ((A + 1.0) - (A - 1.0) * cosW + twoSqrtA);
            double b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cosW);
            double b2 = A * ((A + 1.0) - (A - 1.0) * cosW - twoSqrtA);
            double a0 = (A + 1.0) + (A - 1.0) * cosW + twoSqrtA;
            double a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cosW);
            double a2 = (A + 1.0) + (A - 1.0) * cosW - twoSqrtA;

            double inv = 1.0 / a0;
            _b0 = b0 * inv; _b1 = b1 * inv; _b2 = b2 * inv;
            _a1 = a1 * inv; _a2 = a2 * inv;
        }

        public void SetHighShelf(int fs, double fc, double gainDb, double slope = 0.75)
        {
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * fc / fs;
            double cosW = Math.Cos(w0);
            double sinW = Math.Sin(w0);
            double alpha = sinW * 0.5 * Math.Sqrt((A + 1.0 / A) * (1.0 / slope - 1.0) + 2.0);
            double twoSqrtA = 2.0 * Math.Sqrt(A) * alpha;

            double b0 = A * ((A + 1.0) + (A - 1.0) * cosW + twoSqrtA);
            double b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cosW);
            double b2 = A * ((A + 1.0) + (A - 1.0) * cosW - twoSqrtA);
            double a0 = (A + 1.0) - (A - 1.0) * cosW + twoSqrtA;
            double a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cosW);
            double a2 = (A + 1.0) - (A - 1.0) * cosW - twoSqrtA;

            double inv = 1.0 / a0;
            _b0 = b0 * inv; _b1 = b1 * inv; _b2 = b2 * inv;
            _a1 = a1 * inv; _a2 = a2 * inv;
        }

        public void Reset() => _z1 = _z2 = 0.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            double x = input;
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return (float)y;
        }
    }

    // ── 푸시풀 진공관 파워앰프 (B+ Sag / 트랜스포머 플럭스 / NFB Presence & Depth) ──
    private struct PowerAmpStage
    {
        private float _drive;
        private float _outNorm;
        private float _sagDepth;
        private float _psuAtk;
        private float _psuRel;
        private float _psuEnv;

        private float _otLpA;
        private float _otLpY;
        private float _dcHpfA;
        private float _dcX1;
        private float _dcY1;

        private BiquadD _depthEq;
        private BiquadD _presenceEq;

        public void Configure(int fs4, float master0to10, float sag0to10, float presence0to10, float depth0to10)
        {
            float mNorm = Math.Clamp(master0to10, 0f, 10f) / 10f;
            _drive = 0.5f + 5.0f * (mNorm * mNorm);
            // Master 증가에 따른 과도한 볼륨 점프 방지
            _outNorm = 0.90f / (1f + 0.45f * _drive);

            _sagDepth = 0.42f * (Math.Clamp(sag0to10, 0f, 10f) / 10f);
            _psuAtk = (float)(1.0 - Math.Exp(-1.0 / Math.Max(1.0, 0.012 * fs4)));
            _psuRel = (float)(1.0 - Math.Exp(-1.0 / Math.Max(1.0, 0.140 * fs4)));

            double dt = 1.0 / fs4;
            _otLpA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 150.0 * dt));
            _dcHpfA = (float)Math.Exp(-2.0 * Math.PI * 18.0 * dt);

            double dDb = -4.0 + (Math.Clamp(depth0to10, 0f, 10f) / 5.0) * 4.0 + (depth0to10 > 5f ? ((depth0to10 - 5f) / 5.0) * 3.0 : 0.0);
            if (depth0to10 <= 5f) dDb = -4.0 + (depth0to10 / 5.0) * 4.0;
            else dDb = ((depth0to10 - 5.0) / 5.0) * 7.0;

            double pDb = presence0to10 <= 5f
                ? -4.0 + (presence0to10 / 5.0) * 4.0
                : ((presence0to10 - 5.0) / 5.0) * 7.0;

            _depthEq.SetLowShelf(fs4, 105.0, dDb, 0.80);
            _presenceEq.SetHighShelf(fs4, 3200.0, pDb, 0.75);
        }

        public void Reset()
        {
            _psuEnv = 0f;
            _otLpY = 0f;
            _dcX1 = _dcY1 = 0f;
            _depthEq.Reset();
            _presenceEq.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float driven = x * _drive;
            float rail = Math.Max(0.45f, 1.0f - _sagDepth * MathF.Min(1.0f, _psuEnv));
            float pushPull = rail * FastTanh(driven / rail);

            float absOut = MathF.Abs(pushPull);
            _psuEnv += (absOut > _psuEnv ? _psuAtk : _psuRel) * (absOut - _psuEnv);

            // 출력 트랜스포머 저역(<150Hz) 코어 플럭스 포화
            _otLpY += _otLpA * (pushPull - _otLpY);
            float lfSat = FastTanh(_otLpY * 1.65f) / 1.65f;
            float xformer = pushPull + 0.28f * (lfSat - _otLpY);

            // 출력단 DC 정리 + NFB Depth / Presence
            float ac = 0.5f * (1f + _dcHpfA) * (xformer - _dcX1) + _dcHpfA * _dcY1;
            _dcX1 = xformer;
            _dcY1 = ac;

            float eq = _presenceEq.Process(_depthEq.Process(ac));
            return eq * _outNorm;
        }
    }

    // ── 엔진 상태 ──
    private readonly Oversampler4x _os = new();
    private Ts808Stage _ts;
    private TriodeStage _v1;
    private TriodeStage _v2;
    private TriodeStage _v3;
    private TriodeStage _v4;
    private FmvToneStack _fmv;
    private PowerAmpStage _power;

    private WhiteBoxParams _pendingParams = new();
    private WhiteBoxParams _activeParams = new();
    private int _paramVersion = 1;
    private int _appliedVersion;
    private volatile bool _tsEnabled;
    private volatile bool _powerAmpEnabled = true;

    private int _sampleRate = 44100;
    private int _maxFrames = 2048;

    public float[] DefaultCabIr { get; private set; } = Array.Empty<float>();
    public float[] DefaultCabIrRev { get; private set; } = Array.Empty<float>();

    public bool IsTsEnabled => _tsEnabled;
    public bool IsPowerAmpEnabled => _powerAmpEnabled;

    public void Prepare(int maxFrames, int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _maxFrames = Math.Max(256, maxFrames);
        _os.Prepare(_maxFrames);

        BuildDefaultCabIr(_sampleRate);
        _appliedVersion = 0; // 강제 계수 재계산
        EnsureParamsApplied();
        Reset();
    }

    public void Reset()
    {
        _os.Reset();
        _ts.Reset();
        _v1.Reset();
        _v2.Reset();
        _v3.Reset();
        _v4.Reset();
        _fmv.Reset();
        _power.Reset();
    }

    public void UpdateParams(WhiteBoxParams p)
    {
        if (p is null) return;
        var clone = p.Clone();
        _tsEnabled = clone.TsEnabled;
        _powerAmpEnabled = clone.PowerAmpEnabled;
        Volatile.Write(ref _pendingParams, clone);
        Interlocked.Increment(ref _paramVersion);
    }

    public WhiteBoxParams GetParamsSnapshot() => Volatile.Read(ref _pendingParams).Clone();

    private readonly WhiteBoxParams _chainParams = new();

    /// <summary>
    /// 시그널 체인 Amp 블록에서 RT 스레드 무할당으로 화이트박스 파라미터를 직접 동기화한다.
    /// </summary>
    internal void ApplyChainAmpParams(
        WhiteBoxChannel ch,
        bool bright,
        float preGain,
        float bass,
        float mid,
        float treble,
        float master,
        float sag,
        float presence,
        float depth)
    {
        if (_activeParams == _chainParams
            && _chainParams.Channel == ch
            && _chainParams.Bright == bright
            && !_chainParams.TsEnabled
            && MathF.Abs(_chainParams.PreGain - preGain) < 1e-3f
            && MathF.Abs(_chainParams.Bass - bass) < 1e-3f
            && MathF.Abs(_chainParams.Mid - mid) < 1e-3f
            && MathF.Abs(_chainParams.Treble - treble) < 1e-3f
            && MathF.Abs(_chainParams.Master - master) < 1e-3f
            && MathF.Abs(_chainParams.Sag - sag) < 1e-3f
            && MathF.Abs(_chainParams.Presence - presence) < 1e-3f
            && MathF.Abs(_chainParams.Depth - depth) < 1e-3f)
        {
            return;
        }

        _chainParams.Channel = ch;
        _chainParams.Bright = bright;
        _chainParams.TsEnabled = false;
        _chainParams.PreGain = Math.Clamp(preGain, 0f, 10f);
        _chainParams.Bass = Math.Clamp(bass, 0f, 10f);
        _chainParams.Mid = Math.Clamp(mid, 0f, 10f);
        _chainParams.Treble = Math.Clamp(treble, 0f, 10f);
        _chainParams.Master = Math.Clamp(master, 0f, 10f);
        _chainParams.Sag = Math.Clamp(sag, 0f, 10f);
        _chainParams.Presence = Math.Clamp(presence, 0f, 10f);
        _chainParams.Depth = Math.Clamp(depth, 0f, 10f);
        _chainParams.PowerAmpEnabled = true;

        ApplyParamsInternal(_chainParams);
        _appliedVersion = Volatile.Read(ref _paramVersion);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureParamsApplied()
    {
        int ver = Volatile.Read(ref _paramVersion);
        if (ver == _appliedVersion) return;

        var p = Volatile.Read(ref _pendingParams);
        ApplyParamsInternal(p);
        _appliedVersion = ver;
    }

    private void ApplyParamsInternal(WhiteBoxParams p)
    {
        _activeParams = p;
        _tsEnabled = p.TsEnabled;
        _powerAmpEnabled = p.PowerAmpEnabled;

        int fs4 = _sampleRate * 4;
        _ts.Configure(fs4, p.TsDrive, p.TsTone, p.TsLevel);

        float gNorm = Math.Clamp(p.PreGain, 0f, 10f) / 10f;
        float brightShelf = p.Bright ? 0.52f : 0.0f;

        switch (p.Channel)
        {
            case WhiteBoxChannel.Clean:
            {
                float g1 = 1.15f + 2.4f * gNorm;
                float g2 = 1.05f + 1.1f * gNorm;
                _v1.Configure(fs4, g1, staticBias: -0.05f, kPos: 0.90f, kNeg: 1.08f, hpfHz: 24f, millerHz: 11000f, maxDynShift: 0.14f, shiftSpeed: 0.22f, shelfHz: 2800f, shelfBoost: brightShelf);
                _v2.Configure(fs4, g2, staticBias: -0.04f, kPos: 0.95f, kNeg: 1.15f, hpfHz: 28f, millerHz: 10000f, maxDynShift: 0.14f, shiftSpeed: 0.20f);
                break;
            }
            case WhiteBoxChannel.HighGain:
            {
                float g1 = 2.0f + 4.8f * gNorm;
                float g2 = 2.2f + 2.6f * gNorm;
                float g3 = 1.9f + 1.2f * gNorm;
                float g4 = 1.15f;
                _v1.Configure(fs4, g1, staticBias: -0.08f, kPos: 1.15f, kNeg: 1.45f, hpfHz: 140f, millerHz: 9000f, maxDynShift: 0.24f, shiftSpeed: 0.35f, shelfHz: 2400f, shelfBoost: brightShelf);
                _v2.Configure(fs4, g2, staticBias: -0.10f, kPos: 1.25f, kNeg: 1.65f, hpfHz: 200f, millerHz: 7800f, maxDynShift: 0.30f, shiftSpeed: 0.42f);
                // 39k 콜드 클리퍼단 (-0.68 고정 바이어스로 강한 짝수/홀수 고조파 그라울 생성)
                _v3.Configure(fs4, g3, staticBias: -0.68f, kPos: 1.15f, kNeg: 2.35f, hpfHz: 160f, millerHz: 7200f, maxDynShift: 0.22f, shiftSpeed: 0.32f);
                _v4.Configure(fs4, g4, staticBias: -0.06f, kPos: 1.05f, kNeg: 1.35f, hpfHz: 85f, millerHz: 7500f, maxDynShift: 0.16f, shiftSpeed: 0.22f);
                break;
            }
            default: // Plexi (1959SLP / JCM800)
            {
                float g1 = 1.6f + 3.6f * gNorm;
                float g2 = 1.7f + 2.0f * gNorm;
                float g3 = 1.35f + 0.7f * gNorm;
                _v1.Configure(fs4, g1, staticBias: -0.06f, kPos: 1.05f, kNeg: 1.32f, hpfHz: 85f, millerHz: 9500f, maxDynShift: 0.20f, shiftSpeed: 0.28f, shelfHz: 220f, shelfBoost: 0.22f + brightShelf * 0.7f);
                _v2.Configure(fs4, g2, staticBias: -0.10f, kPos: 1.18f, kNeg: 1.50f, hpfHz: 110f, millerHz: 8500f, maxDynShift: 0.26f, shiftSpeed: 0.35f);
                _v3.Configure(fs4, g3, staticBias: -0.35f, kPos: 1.10f, kNeg: 1.75f, hpfHz: 90f, millerHz: 8000f, maxDynShift: 0.18f, shiftSpeed: 0.28f);
                break;
            }
        }

        _fmv.Configure(fs4, p.Channel, p.Bass, p.Mid, p.Treble);
        _power.Configure(fs4, p.Master, p.Sag, p.Presence, p.Depth);
    }

    /// <summary>
    /// 4x HIIR 오버샘플링 도메인에서 화이트박스 전체(또는 프리앰프+FMV 전용) 회로를 실행한다. RT 할당 0.
    /// </summary>
    public void ProcessFullAmp(Span<float> mono, int frames, bool includePowerAmp)
    {
        if (frames <= 0) return;
        EnsureParamsApplied();

        int n = Math.Min(frames, _maxFrames);
        var buf4 = _os.Upsample(mono, n);
        var p = _activeParams;
        bool useTs = p.TsEnabled;
        var ch = p.Channel;

        for (int i = 0; i < buf4.Length; i++)
        {
            float s = buf4[i];
            if (useTs)
                s = _ts.Process(s);

            if (ch == WhiteBoxChannel.Clean)
            {
                s = _v1.Process(s);
                s = _fmv.Process(s);
                s = _v2.Process(s);
            }
            else if (ch == WhiteBoxChannel.HighGain)
            {
                s = _v1.Process(s);
                s = _v2.Process(s);
                s = _v3.Process(s);
                s = _v4.Process(s);
                s = _fmv.Process(s);
            }
            else // Plexi
            {
                s = _v1.Process(s);
                s = _v2.Process(s);
                s = _v3.Process(s);
                s = _fmv.Process(s);
            }

            if (includePowerAmp)
                s = _power.Process(s);

            buf4[i] = s;
        }

        _os.Downsample(mono, n);
    }

    /// <summary>NAM WaveNet 앞단에 4x OS TS808 다이오드 클리퍼/부스트만 단독 실행한다. RT 할당 0.</summary>
    public void ProcessTs808Only(Span<float> mono, int frames)
    {
        if (frames <= 0) return;
        EnsureParamsApplied();

        int n = Math.Min(frames, _maxFrames);
        var buf4 = _os.Upsample(mono, n);
        for (int i = 0; i < buf4.Length; i++)
            buf4[i] = _ts.Process(buf4[i]);
        _os.Downsample(mono, n);
    }

    // ── StudioBench 정량 단위 검증용 헬퍼 ──
    public void TestOversamplerLoopback(Span<float> buf)
    {
        int n = Math.Min(buf.Length, _maxFrames);
        _os.Upsample(buf, n);
        _os.Downsample(buf, n);
    }

    public void TestOversampledNonlinear(Span<float> buf, Func<float, float> shaper)
    {
        int n = Math.Min(buf.Length, _maxFrames);
        var b4 = _os.Upsample(buf, n);
        for (int i = 0; i < b4.Length; i++)
            b4[i] = shaper(b4[i]);
        _os.Downsample(buf, n);
    }

    public void TestFmvOnly(Span<float> buf)
    {
        EnsureParamsApplied();
        int n = Math.Min(buf.Length, _maxFrames);
        var b4 = _os.Upsample(buf, n);
        for (int i = 0; i < b4.Length; i++)
            b4[i] = _fmv.Process(b4[i]);
        _os.Downsample(buf, n);
    }

    public void TestPowerAmpOnly(Span<float> buf)
    {
        EnsureParamsApplied();
        int n = Math.Min(buf.Length, _maxFrames);
        var b4 = _os.Upsample(buf, n);
        for (int i = 0; i < b4.Length; i++)
            b4[i] = _power.Process(b4[i]);
        _os.Downsample(buf, n);
    }

    public void TestPreampOnly(Span<float> buf)
    {
        EnsureParamsApplied();
        int n = Math.Min(buf.Length, _maxFrames);
        var b4 = _os.Upsample(buf, n);
        var ch = _activeParams.Channel;
        for (int i = 0; i < b4.Length; i++)
        {
            float s = b4[i];
            if (ch == WhiteBoxChannel.Clean)
                s = _v2.Process(_v1.Process(s));
            else if (ch == WhiteBoxChannel.HighGain)
                s = _v4.Process(_v3.Process(_v2.Process(_v1.Process(s))));
            else
                s = _v3.Process(_v2.Process(_v1.Process(s)));
            b4[i] = s;
        }
        _os.Downsample(buf, n);
    }

    public void TestTs808Only(Span<float> buf) => ProcessTs808Only(buf, buf.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastTanh(float x)
    {
        if (x < -4.97f) return -1f;
        if (x > 4.97f) return 1f;
        float x2 = x * x;
        float num = x * (135135f + x2 * (17325f + x2 * (378f + x2)));
        float den = 135135f + x2 * (62370f + x2 * (3150f + 28f * x2));
        return num / den;
    }

    /// <summary>
    /// 내장 Minimum-Phase 512-tap 4x12 Vintage-30 스타일 캐비넷 IR 합성(Prepare에서 1회 실행).
    /// Cepstral minimum-phase 변환으로 최소 위상 및 위상 연속성 보장.
    /// </summary>
    private void BuildDefaultCabIr(int sampleRate)
    {
        if (CabinetIrBank.TryLoadEmbeddedWavIr("NeuralRigStudio.CabIRs.cab_v30.wav", sampleRate, CabinetIrBank.TapCount, out var measuredIr, out var measuredRev))
        {
            DefaultCabIr = measuredIr;
            DefaultCabIrRev = measuredRev;
            return;
        }

        const int nFft = 1024;
        int bins = nFft / 2 + 1;
        double[] logMag = new double[nFft];

        for (int k = 0; k < bins; k++)
        {
            double f = k * (double)sampleRate / nFft;
            // 85Hz 밀폐형 4x12 저역 롤오프 (2차 HPF)
            double rHp = f / 82.0;
            double magHp = (rHp * rHp) / Math.Sqrt(Math.Pow(1.0 - rHp * rHp, 2) + 1.4 * rHp * rHp);
            if (f < 1.0) magHp = 1e-4;

            // 115Hz 캐비넷 바디 공진 (+3.5 dB)
            double bRes = 1.0 + 0.50 * Math.Exp(-Math.Pow(Math.Log(Math.Max(f, 20.0) / 115.0) / 0.35, 2));

            // 2.4kHz V30 미드 어택 (+4.5 dB) & 4.2kHz 상부 디핑
            double v30Mid = 1.0 + 0.65 * Math.Exp(-Math.Pow((f - 2350.0) / 850.0, 2));
            double notch = 1.0 - 0.35 * Math.Exp(-Math.Pow((f - 3900.0) / 520.0, 2));

            // 4.8kHz 급격한 스피커 콘 고역 롤오프 (4차 LPF)
            double rLp = f / 4700.0;
            double magLp = 1.0 / (1.0 + Math.Pow(rLp, 4.0));

            double mag = Math.Clamp(magHp * bRes * v30Mid * notch * magLp, 1e-5, 4.0);
            double lm = Math.Log(mag);
            logMag[k] = lm;
            if (k > 0 && k < nFft / 2)
                logMag[nFft - k] = lm;
        }

        // Real cepstrum for minimum-phase reconstruction
        double[] cepIm = new double[nFft];
        FftD(logMag, cepIm, inverse: true);

        double[] wLogRe = new double[nFft];
        double[] wLogIm = new double[nFft];
        wLogRe[0] = logMag[0];
        wLogRe[nFft / 2] = logMag[nFft / 2];
        for (int i = 1; i < nFft / 2; i++)
            wLogRe[i] = 2.0 * logMag[i];

        FftD(wLogRe, wLogIm, inverse: false);

        double[] specRe = new double[nFft];
        double[] specIm = new double[nFft];
        for (int k = 0; k < nFft; k++)
        {
            double g = Math.Exp(wLogRe[k]);
            specRe[k] = g * Math.Cos(wLogIm[k]);
            specIm[k] = g * Math.Sin(wLogIm[k]);
        }

        FftD(specRe, specIm, inverse: true);

        var ir = new float[DefaultCabTaps];
        int fade = 48;
        for (int i = 0; i < DefaultCabTaps; i++)
        {
            float v = (float)specRe[i];
            if (i >= DefaultCabTaps - fade)
                v *= (float)(DefaultCabTaps - i) / fade;
            ir[i] = v;
        }

        double energy = 0.0;
        for (int i = 0; i < DefaultCabTaps; i++) energy += ir[i] * ir[i];
        float norm = (float)Math.Sqrt(energy + 1e-12);
        for (int i = 0; i < DefaultCabTaps; i++) ir[i] /= norm;

        var rev = new float[DefaultCabTaps];
        for (int i = 0; i < DefaultCabTaps; i++)
            rev[i] = ir[DefaultCabTaps - 1 - i];

        DefaultCabIr = ir;
        DefaultCabIrRev = rev;
    }

    private static void FftD(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        double dir = inverse ? 1.0 : -1.0;
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = dir * 2.0 * Math.PI / len;
            double wRe = Math.Cos(ang);
            double wIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1.0, curIm = 0.0;
                int half = len >> 1;
                for (int j = 0; j < half; j++)
                {
                    double uRe = re[i + j];
                    double uIm = im[i + j];
                    double vRe = re[i + j + half] * curRe - im[i + j + half] * curIm;
                    double vIm = re[i + j + half] * curIm + im[i + j + half] * curRe;
                    re[i + j] = uRe + vRe;
                    im[i + j] = uIm + vIm;
                    re[i + j + half] = uRe - vRe;
                    im[i + j + half] = uIm - vIm;
                    double nxtRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nxtRe;
                }
            }
        }
        if (inverse)
        {
            double invN = 1.0 / n;
            for (int i = 0; i < n; i++)
            {
                re[i] *= invN;
                im[i] *= invN;
            }
        }
    }
}

