using System.Numerics;

namespace NeuralRigStudio.Audio;

public readonly record struct PitchDetectionResult(
    bool Active,
    string NoteName,
    int Octave,
    float Cents,
    float FreqHz,
    float Clarity,
    float LevelDb,
    float PeakDb);

/// <summary>
/// 실시간 스튜디오 크로매틱 튜너 & 기타 분석기 공용 McLeod Pitch Method(MPM/NSDF) 음정 검출기.
/// 약한 기음(Stratocaster E2) 옥타브 점프 방지 + 40Hz HPF + 3프레임 미디언/안정화 필터 내장.
/// </summary>
public sealed class GuitarPitchDetector
{
    public const int WindowSize = 4096;
    private static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private readonly float[] _work = new float[WindowSize];
    private readonly float[] _nsdf = new float[1280];
    private readonly int[] _peakTau = new int[96];
    private readonly float[] _peakVal = new float[96];
    private readonly float[] _histFreq = new float[3];
    private int _histCount;
    private float _smoothedFreq;
    private float _candidateFreq;
    private int _candidateCount;
    private int _holdTicks;

    public void Reset()
    {
        Array.Clear(_histFreq);
        _histCount = 0;
        _smoothedFreq = 0f;
        _candidateFreq = 0f;
        _candidateCount = 0;
        _holdTicks = 0;
    }

    public PitchDetectionResult Detect(
        ReadOnlySpan<float> input,
        int sampleRate,
        float refA4Hz = 440f,
        float minFreqHz = 64f)
    {
        int n = Math.Min(input.Length, WindowSize);
        if (n < 1024 || sampleRate < 8000)
            return new PitchDetectionResult(false, "--", 0, 0f, 0f, 0f, -96f, -96f);

        refA4Hz = Math.Clamp(refA4Hz, 420f, 460f);
        var src = input.Slice(input.Length - n, n);

        // 1) DC 평균 제거 + 1차 40Hz 고역 통과 필터
        double dc = 0.0;
        for (int i = 0; i < n; i++) dc += src[i];
        float mean = (float)(dc / n);

        float rc = 1f / (2f * MathF.PI * 40f);
        float dt = 1f / sampleRate;
        float alpha = rc / (rc + dt);
        float prevX = src[0] - mean;
        float prevY = 0f;
        _work[0] = 0f;
        for (int i = 1; i < n; i++)
        {
            float x = src[i] - mean;
            float y = alpha * (prevY + x - prevX);
            prevX = x;
            prevY = y;
            _work[i] = y;
        }

        // 2) 최신 1024샘플 순간 레벨/피크 산출
        int statLen = Math.Min(1024, n);
        int statStart = n - statLen;
        double sumSq = 0.0;
        float maxAbs = 0f;
        for (int i = statStart; i < n; i++)
        {
            float s = _work[i];
            sumSq += (double)s * s;
            float a = MathF.Abs(s);
            if (a > maxAbs) maxAbs = a;
        }
        float rms = MathF.Sqrt((float)(sumSq / statLen));
        float levelDb = ToDb(rms);
        float peakDb = ToDb(maxAbs);

        if (rms < 0.00035f)
        {
            if (_holdTicks > 0 && _smoothedFreq > 20f)
            {
                _holdTicks--;
                return FormatResult(_smoothedFreq, active: false, clarity: 0f, levelDb, peakDb, refA4Hz);
            }
            _smoothedFreq = 0f;
            _histCount = 0;
            _candidateCount = 0;
            return new PitchDetectionResult(false, "--", 0, 0f, 0f, 0f, levelDb, peakDb);
        }

        // 3) McLeod Pitch Method (NSDF) — 최신 Tail 윈도우 기반
        int tauMin = Math.Clamp(sampleRate / 1350, 14, 1270);
        int maxAllowedTau = Math.Min(1275, n / 2 - 4);
        int tauMax = Math.Clamp((int)MathF.Ceiling(sampleRate / Math.Max(45f, minFreqHz)), tauMin + 16, maxAllowedTau);
        int winLen = Math.Clamp(n - tauMax - 1, 512, 2048);
        int mpmStart = Math.Max(0, n - (winLen + tauMax));
        var xBuf = _work.AsSpan(mpmStart, winLen + tauMax);

        double e0 = 0.0;
        for (int j = 0; j < winLen; j++)
        {
            float v = xBuf[j];
            e0 += (double)v * v;
        }
        double eTau = e0;

        int vecN = Vector<float>.Count;
        bool useVec = Vector.IsHardwareAccelerated && winLen >= vecN;
        _nsdf[0] = 1f;

        for (int tau = 1; tau <= tauMax; tau++)
        {
            float leaving = xBuf[tau - 1];
            float entering = xBuf[tau - 1 + winLen];
            eTau = Math.Max(0.0, eTau + (double)entering * entering - (double)leaving * leaving);

            double ac = 0.0;
            int j = 0;
            if (useVec)
            {
                var acc = Vector<float>.Zero;
                for (; j <= winLen - vecN; j += vecN)
                {
                    var va = new Vector<float>(xBuf.Slice(j, vecN));
                    var vb = new Vector<float>(xBuf.Slice(j + tau, vecN));
                    acc += va * vb;
                }
                ac = Vector.Dot(acc, Vector<float>.One);
            }
            for (; j < winLen; j++)
                ac += xBuf[j] * xBuf[j + tau];

            double denom = e0 + eTau;
            _nsdf[tau] = denom > 1e-12 ? (float)(2.0 * ac / denom) : 0f;
        }

        // 4) 첫 0-크로싱 이후 양의 로브별 키 맥시마(Key Maxima) 수집
        int peakCount = 0;
        bool passedZero = false;
        int curMaxTau = -1;
        float curMaxVal = -1f;

        for (int tau = 1; tau <= tauMax; tau++)
        {
            float v = _nsdf[tau];
            if (!passedZero)
            {
                if (v <= 0f) passedZero = true;
                continue;
            }

            if (v > 0f)
            {
                if (curMaxTau < 0 || v > curMaxVal)
                {
                    curMaxTau = tau;
                    curMaxVal = v;
                }
            }
            else if (curMaxTau >= 0)
            {
                if (peakCount < _peakTau.Length)
                {
                    _peakTau[peakCount] = curMaxTau;
                    _peakVal[peakCount] = curMaxVal;
                    peakCount++;
                }
                curMaxTau = -1;
                curMaxVal = -1f;
            }
        }
        if (curMaxTau >= 0 && peakCount < _peakTau.Length)
        {
            _peakTau[peakCount] = curMaxTau;
            _peakVal[peakCount] = curMaxVal;
            peakCount++;
        }

        float globalMax = 0f;
        for (int i = 0; i < peakCount; i++)
        {
            if (_peakTau[i] >= tauMin && _peakVal[i] > globalMax)
                globalMax = _peakVal[i];
        }

        if (globalMax < 0.42f)
        {
            if (_holdTicks > 0 && _smoothedFreq > 20f)
            {
                _holdTicks--;
                return FormatResult(_smoothedFreq, active: true, clarity: globalMax, levelDb, peakDb, refA4Hz);
            }
            return new PitchDetectionResult(false, "--", 0, 0f, 0f, globalMax, levelDb, peakDb);
        }

        float selectThresh = 0.88f * globalMax;
        int bestIdx = -1;
        for (int i = 0; i < peakCount; i++)
        {
            if (_peakTau[i] >= tauMin && _peakVal[i] >= selectThresh)
            {
                bestIdx = i;
                break;
            }
        }
        if (bestIdx < 0)
            return new PitchDetectionResult(false, "--", 0, 0f, 0f, globalMax, levelDb, peakDb);

        int bestTau = _peakTau[bestIdx];
        float bestVal = _peakVal[bestIdx];

        // 5) 서브샘플 포물선 보간
        float betterTau = bestTau;
        if (bestTau > 1 && bestTau < tauMax)
        {
            float s0 = _nsdf[bestTau - 1];
            float s1 = _nsdf[bestTau];
            float s2 = _nsdf[bestTau + 1];
            float denom = 2f * s1 - s0 - s2;
            if (denom > 1e-9f)
                betterTau += Math.Clamp(0.5f * (s2 - s0) / denom, -1f, 1f);
        }

        float rawFreq = sampleRate / MathF.Max(1f, betterTau);
        if (!float.IsFinite(rawFreq) || rawFreq < minFreqHz * 0.9f || rawFreq > 1400f)
            return new PitchDetectionResult(false, "--", 0, 0f, 0f, bestVal, levelDb, peakDb);

        // 7) 3프레임 미디언 + 스냅 가드
        float medFreq = PushMedian(rawFreq);
        if (_smoothedFreq <= 0f)
        {
            _smoothedFreq = medFreq;
            _candidateCount = 0;
        }
        else if (MathF.Abs(1200f * MathF.Log2(medFreq / _smoothedFreq)) > 80f)
        {
            if (_candidateCount > 0 && MathF.Abs(1200f * MathF.Log2(medFreq / _candidateFreq)) <= 65f)
            {
                _smoothedFreq = medFreq;
                _candidateCount = 0;
            }
            else
            {
                _candidateFreq = medFreq;
                _candidateCount = 1;
            }
        }
        else
        {
            _candidateCount = 0;
            _smoothedFreq = _smoothedFreq * 0.62f + medFreq * 0.38f;
        }

        _holdTicks = 8;
        return FormatResult(_smoothedFreq, active: true, clarity: MathF.Round(bestVal, 2), levelDb, peakDb, refA4Hz);
    }

    private float PushMedian(float f)
    {
        if (_histCount < 3)
        {
            _histFreq[_histCount++] = f;
            return f;
        }
        _histFreq[0] = _histFreq[1];
        _histFreq[1] = _histFreq[2];
        _histFreq[2] = f;
        float a = _histFreq[0], b = _histFreq[1], c = _histFreq[2];
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) b = a;
        return b;
    }

    private static PitchDetectionResult FormatResult(
        float freq,
        bool active,
        float clarity,
        float levelDb,
        float peakDb,
        float refA4Hz)
    {
        if (freq <= 20f || !float.IsFinite(freq))
            return new PitchDetectionResult(false, "--", 0, 0f, 0f, clarity, levelDb, peakDb);

        double midiFloat = 69.0 + 12.0 * Math.Log2(freq / refA4Hz);
        int midi = (int)Math.Round(midiFloat);
        float cents = MathF.Round((float)((midiFloat - midi) * 100.0), 1);
        int noteIdx = ((midi % 12) + 12) % 12;
        int octave = midi / 12 - 1;
        return new PitchDetectionResult(active, NoteNames[noteIdx], octave, cents, MathF.Round(freq, 1), clarity, levelDb, peakDb);
    }

    private static float ToDb(float v)
    {
        float db = 20f * MathF.Log10(MathF.Max(1.6e-5f, v));
        return float.IsFinite(db) ? MathF.Round(Math.Clamp(db, -96f, 0f), 1) : -96f;
    }
}
