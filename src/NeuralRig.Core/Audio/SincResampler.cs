namespace NeuralRigStudio.Audio;

/// <summary>
/// 4-Term Blackman-Harris 윈도우(-92dB 사이드로브 억제) 기반 대역 제한 Windowed Sinc 오프라인 리샘플러.
/// 외부 캐비넷 IR(.wav) 및 백킹 트랙(MR) 로드 시 고역 해상도 손실과 에일리어싱을 방지한다.
/// </summary>
public static class SincResampler
{
    private const int BaseHalfTaps = 24;

    public static float[] Resample(ReadOnlySpan<float> src, int srcRate, int dstRate)
    {
        if (src.Length == 0) return Array.Empty<float>();
        if (srcRate <= 0 || dstRate <= 0 || srcRate == dstRate || src.Length < 2)
            return src.ToArray();

        double ratio = (double)srcRate / dstRate;
        int outLen = Math.Max(16, (int)Math.Round(src.Length / ratio));
        var dst = new float[outLen];

        // 다운샘플링 시 출력 나이퀴스트 기준으로 커널 스케일 및 윈도우 반경 확장 (패스밴드 고역 감쇠 제로 보장)
        double rateScale = dstRate < srcRate ? (double)dstRate / srcRate : 1.0;
        double cutoff = rateScale * 0.92;
        double winRadius = BaseHalfTaps / rateScale;
        int tapSpan = (int)Math.Ceiling(winRadius);
        int srcMax = src.Length - 1;

        for (int i = 0; i < outLen; i++)
        {
            double center = i * ratio;
            int baseIdx = (int)Math.Floor(center);
            double acc = 0.0;
            double weightSum = 0.0;

            for (int tap = -tapSpan; tap <= tapSpan; tap++)
            {
                int k = baseIdx + tap;
                double delta = center - k;
                double u = (delta + winRadius) / (2.0 * winRadius);
                if (u <= 0.0 || u >= 1.0) continue;

                // 4-term Blackman-Harris window
                double w = 0.35875
                         - 0.48829 * Math.Cos(2.0 * Math.PI * u)
                         + 0.14128 * Math.Cos(4.0 * Math.PI * u)
                         - 0.01168 * Math.Cos(6.0 * Math.PI * u);

                double x = Math.PI * delta * cutoff;
                double sinc = Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(x) / x;
                double h = sinc * w;

                int clampedIdx = k < 0 ? 0 : (k > srcMax ? srcMax : k);
                acc += src[clampedIdx] * h;
                weightSum += h;
            }

            dst[i] = weightSum > 1e-12 ? (float)(acc / weightSum) : src[Math.Clamp(baseIdx, 0, srcMax)];
        }

        return dst;
    }
}
