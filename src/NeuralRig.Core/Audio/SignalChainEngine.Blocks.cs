using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio;

public sealed partial class SignalChainEngine
{
    /// <summary>
    /// 모든 비선형 드라이브/디스토션/퍼즈/부스트 페달의 4x HIIR 오버샘플링 실제 회로도 실행 엔진 (RT 할당 0).
    /// </summary>
    private void ProcessNonlinearPedal4x(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ref CompiledSlot sl)
    {
        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]);

        int fs4 = _sampleRate * 4;
        var buf4 = st.Os4x.Upsample(_monoTmp.AsSpan(0, n), n, _os2x, _os4x);

        switch (sl.Op)
        {
            case BlockOp.OdTs808:
                SchematicPedalDsp.ProcessTubeScreamer4x(ref st.Circuit, buf4, fs4, sl.P0, isTs9: false);
                break;
            case BlockOp.OdTs9:
                SchematicPedalDsp.ProcessTubeScreamer4x(ref st.Circuit, buf4, fs4, sl.P0, isTs9: true);
                break;
            case BlockOp.OdKlon:
                SchematicPedalDsp.ProcessKlonCentaur4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistRat:
                SchematicPedalDsp.ProcessRat4x(ref st.Circuit, buf4, fs4, sl.P0, isRat2: false);
                break;
            case BlockOp.DistRat2:
                SchematicPedalDsp.ProcessRat4x(ref st.Circuit, buf4, fs4, sl.P0, isRat2: true);
                break;
            case BlockOp.FuzzBigMuff:
                SchematicPedalDsp.ProcessBigMuff4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.OdSd1:
                SchematicPedalDsp.ProcessBossSd14x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistBossDs1:
                SchematicPedalDsp.ProcessBossDs14x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.OdBossBd2:
                SchematicPedalDsp.ProcessBossBd24x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistBossMt2:
                SchematicPedalDsp.ProcessBossHighGain4x(ref st.Circuit, buf4, fs4, sl.P0, isHm2: false);
                break;
            case BlockOp.DistBossHm2:
                SchematicPedalDsp.ProcessBossHighGain4x(ref st.Circuit, buf4, fs4, sl.P0, isHm2: true);
                break;
            case BlockOp.OdPureSky:
                SchematicPedalDsp.ProcessTimmyJanRay4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P1, 0f, isJanRay: false);
                break;
            case BlockOp.OdJanRay:
                SchematicPedalDsp.ProcessTimmyJanRay4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P1, sl.P3, isJanRay: true);
                break;
            case BlockOp.OdOcd:
                SchematicPedalDsp.ProcessFulltoneOcd4x(ref st.Circuit, buf4, fs4, sl.P0, highPeak: sl.P3 > 0.5f);
                break;
            case BlockOp.OdBbPreamp:
                SchematicPedalDsp.ProcessBbPreamp4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistEcstasy:
                SchematicPedalDsp.ProcessBognerEcstasy4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.BoostEp:
                SchematicPedalDsp.ProcessCleanBoost4x(ref st.Circuit, buf4, fs4, sl.P0, kind: 0);
                break;
            case BlockOp.BoostRc:
                SchematicPedalDsp.ProcessCleanBoost4x(ref st.Circuit, buf4, fs4, sl.P0, kind: 1);
                break;
            case BlockOp.BoostMicroAmp:
                SchematicPedalDsp.ProcessCleanBoost4x(ref st.Circuit, buf4, fs4, sl.P0, kind: 2);
                break;
            case BlockOp.OdBluesbreaker:
                SchematicPedalDsp.ProcessBluesbreaker4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.DistGuvnor:
                SchematicPedalDsp.ProcessGuvnor4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.FuzzFace:
                SchematicPedalDsp.ProcessFuzzFace4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P1, isSilicon: sl.P3 > 0.5f);
                break;
            case BlockOp.BoostRangemaster:
                SchematicPedalDsp.ProcessRangemaster4x(ref st.Circuit, buf4, fs4, sl.P0, fullRangeCap: sl.P1 > 0.5f);
                break;
            case BlockOp.DistPlus:
                SchematicPedalDsp.ProcessDistortionPlus4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.OdBoxOfRock:
                SchematicPedalDsp.ProcessBoxOfRock4x(ref st.Circuit, buf4, fs4, sl.P0);
                break;
            case BlockOp.CustomPedal:
                SchematicPedalDsp.ProcessCustomSchematicPedal4x(ref st.Circuit, buf4, fs4, sl.P0, sl.P3, in sl.CustomPedal);
                break;
        }

        st.Os4x.Downsample(_monoTmp.AsSpan(0, n), n, _os2x, _os4x);

        // 1x 도메인 각 페달 고유 회로도 출력 톤스택 + 스무딩 아웃풋 레벨 (지퍼 노이즈 차단)
        float alpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.005f * _sampleRate));
        float targetOutG = sl.Op switch
        {
            BlockOp.OdTs808 or BlockOp.OdSd1 => 0.25f + 1.65f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdTs9 => 0.25f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdKlon => 0.35f + 1.90f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistRat => 0.22f + 1.50f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistRat2 => 0.22f + 1.55f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.FuzzBigMuff => 0.28f + 1.85f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistBossDs1 => 0.30f + 1.95f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdBossBd2 => 0.28f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistBossMt2 => 0.20f + 1.45f * (Math.Clamp(sl.P5, 0f, 10f) / 10f),
            BlockOp.DistBossHm2 => 0.20f + 1.40f * (Math.Clamp(sl.P3, 0f, 10f) / 10f),
            BlockOp.OdPureSky => 0.30f + 1.85f * (Math.Clamp(sl.P3, 0f, 10f) / 10f),
            BlockOp.OdJanRay => 0.30f + 1.85f * (Math.Clamp(sl.P4, 0f, 10f) / 10f),
            BlockOp.OdOcd => 0.26f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.OdBbPreamp => 0.28f + 1.75f * (Math.Clamp(sl.P3, 0f, 10f) / 10f),
            BlockOp.DistEcstasy => 0.22f + 1.55f * (Math.Clamp(sl.P4, 0f, 10f) / 10f),
            BlockOp.BoostRc => 0.45f + 2.40f * (Math.Clamp(sl.P1, 0f, 10f) / 10f),
            BlockOp.OdBluesbreaker => 0.32f + 1.90f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.DistGuvnor => 0.24f + 1.65f * (Math.Clamp(sl.P4, 0f, 10f) / 10f),
            BlockOp.FuzzFace => 0.26f + 1.60f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.BoostRangemaster => 0.90f,
            BlockOp.DistPlus => 0.26f + 1.65f * (Math.Clamp(sl.P1, 0f, 10f) / 10f),
            BlockOp.OdBoxOfRock => 0.28f + 1.80f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            BlockOp.CustomPedal => 0.28f + 1.75f * (Math.Clamp(sl.P2, 0f, 10f) / 10f),
            _ => 1.0f,
        };
        if (!st.SmoothReady)
        {
            st.SmoothOutGain = targetOutG;
            st.SmoothReady = true;
        }

        switch (sl.Op)
        {
            case BlockOp.OdTs808:
            case BlockOp.OdTs9:
            {
                // 액티브 TS 톤스택: 패시브 LPF(F1L) + 액티브 하이셸프(F2L)
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F2L.Process(st.F1L.Process(_monoTmp[i])) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.OdSd1:
            case BlockOp.DistRat:
            case BlockOp.OdBluesbreaker:
            case BlockOp.FuzzFace:
            case BlockOp.BoostRangemaster:
            case BlockOp.DistPlus:
            case BlockOp.OdBoxOfRock:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F1L.Process(_monoTmp[i]) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.OdKlon:
            case BlockOp.DistRat2:
            case BlockOp.OdBossBd2:
            case BlockOp.OdPureSky:
            case BlockOp.OdOcd:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F2L.Process(st.F1L.Process(_monoTmp[i])) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.FuzzBigMuff:
            case BlockOp.DistBossDs1:
            {
                // 실제 회로도 병렬 RC 크로스페이드 패시브 톤스택:
                // y = (1 - t) * LPF(x) + t * HPF(x)  -> 중앙(t=0.5)에서 자연 미드 스쿱, 양 끝(0/1)에서 스쿱 소멸!
                float t = Math.Clamp(sl.P1, 0f, 10f) / 10f;
                float lpW = 1f - t;
                float hpW = t;
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float x = _monoTmp[i];
                    float lp = st.F0L.Process(x);
                    float hp = st.F1L.Process(x);
                    float mixed = lp * lpW + hp * hpW;
                    float y = st.F2L.Process(mixed) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.DistBossMt2:
            case BlockOp.DistBossHm2:
            case BlockOp.DistEcstasy:
            case BlockOp.DistGuvnor:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F4L.Process(st.F3L.Process(st.F2L.Process(st.F1L.Process(_monoTmp[i])))) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.OdJanRay:
            case BlockOp.OdBbPreamp:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F3L.Process(st.F2L.Process(st.F1L.Process(_monoTmp[i]))) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.BoostEp:
            case BlockOp.BoostMicroAmp:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F0L.Process(_monoTmp[i]) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.BoostRc:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F1L.Process(st.F0L.Process(_monoTmp[i])) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
            case BlockOp.CustomPedal:
            {
                for (int i = 0; i < n; i++)
                {
                    st.SmoothOutGain += alpha * (targetOutG - st.SmoothOutGain);
                    float y = st.F2L.Process(st.F1L.Process(st.F0L.Process(_monoTmp[i]))) * st.SmoothOutGain;
                    bufL[i] = y; bufR[i] = y;
                }
                break;
            }
        }
    }

    // ── DSP Implementations (All Zero-Allocation) ──

    private static void ProcessGate(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float threshDb, float decayMs, float holdMs)
    {
        if (threshDb <= -78f) return;
        float threshOpen = MathF.Pow(10f, Math.Clamp(threshDb, -80f, -15f) / 20f);
        float threshClose = threshOpen * 0.5623413f;
        int holdMax = Math.Max(32, (int)(Math.Clamp(holdMs, 10f, 400f) * 0.001f * sr));
        float envAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0005f * sr));
        float envRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.050f * sr));
        float gainAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0012f * sr));
        float gainRel = 1f - MathF.Exp(-1f / MathF.Max(1f, Math.Clamp(decayMs, 20f, 500f) * 0.001f * sr));

        for (int i = 0; i < n; i++)
        {
            float a = MathF.Max(MathF.Abs(bufL[i]), MathF.Abs(bufR[i]));
            st.Env += (a > st.Env ? envAtk : envRel) * (a - st.Env);
            if (st.Env >= threshOpen)
            {
                st.GateOpen = true;
                st.HoldCount = holdMax;
            }
            else if (st.GateOpen)
            {
                if (st.Env >= threshClose) st.HoldCount = holdMax;
                else if (st.HoldCount > 0) st.HoldCount--;
                else st.GateOpen = false;
            }
            float target = st.GateOpen ? 1f : 0.006f;
            st.Gain += (target > st.Gain ? gainAtk : gainRel) * (target - st.Gain);
            bufL[i] *= st.Gain;
            bufR[i] *= st.Gain;
        }
    }

    private static void ProcessCompressor(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float sens, float outLvl, float atkMs, float mixPct, bool is1176, float ratio = 4f)
    {
        float sNorm = Math.Clamp(sens, 0f, 10f) / 10f;
        float thresh = MathF.Pow(10f, (-6f - 26f * sNorm) / 20f);
        float compRatio = is1176 ? ratio : (2.5f + 5.5f * sNorm);
        float slope = 1f - 1f / compRatio;
        float makeup = (0.5f + 2.2f * (Math.Clamp(outLvl, 0f, 10f) / 10f)) * (1f + sNorm * 0.75f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, atkMs * 0.001f * sr));
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, (is1176 ? 0.065f : 0.140f) * sr));

        for (int i = 0; i < n; i++)
        {
            float dL = bufL[i], dR = bufR[i];
            float peak = MathF.Max(MathF.Abs(dL), MathF.Abs(dR));
            st.Env += (peak > st.Env ? atk : rel) * (peak - st.Env);
            float gr = st.Env > thresh ? MathF.Pow(st.Env / MathF.Max(1e-6f, thresh), -slope) : 1f;
            float g = gr * makeup;
            bufL[i] = dL * (1f - mix) + dL * g * mix;
            bufR[i] = dR * (1f - mix) + dR * g * mix;
        }
    }

    private void ProcessWhiteBoxAmp(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, ref CompiledSlot sl)
    {
        // 독립 듀얼-앰프 풀에서 슬롯 전용 엔진 선택 (듀얼 앰프 병렬 시 상태 충돌 0)
        var amp = (st.AmpPoolIdx >= 0 && st.AmpPoolIdx < _ampPool.Length) ? _ampPool[st.AmpPoolIdx] : host.WhiteBox;

        WhiteBoxChannel ch = sl.SubModel switch
        {
            0 or 1 => WhiteBoxChannel.Clean,
            2 or 3 or 6 or 7 => WhiteBoxChannel.Plexi, // Plexi, 2203, AC30, JCM2000 DSL
            _ => WhiteBoxChannel.HighGain,             // SLO-100, 5153, Recto, BE-100
        };
        bool bright = sl.P7 > 0.5f;
        float depth = sl.SubModel is 4 or 5 or 8 or 9 ? 6.2f : 5.0f;

        amp.ApplyChainAmpParams(
            ch,
            bright,
            preGain: sl.P0,
            bass: sl.P1,
            mid: sl.P2,
            treble: sl.P3,
            master: sl.P4,
            sag: sl.P5,
            presence: sl.P6,
            depth: depth);

        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]);

        amp.ProcessFullAmp(_monoTmp.AsSpan(0, n), n, includePowerAmp: true);

        bool hasContour = sl.SubModel >= 6;
        for (int i = 0; i < n; i++)
        {
            float v = hasContour ? st.F1L.Process(st.F0L.Process(_monoTmp[i])) : _monoTmp[i];
            bufL[i] = v;
            bufR[i] = v;
        }
    }

    private void ProcessCustomAmp(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, ref CompiledSlot sl)
    {
        var amp = (st.AmpPoolIdx >= 0 && st.AmpPoolIdx < _ampPool.Length) ? _ampPool[st.AmpPoolIdx] : host.WhiteBox;
        WhiteBoxChannel ch = sl.CustomAmp.BaseAmpChannel switch
        {
            0 => WhiteBoxChannel.Clean,
            2 => WhiteBoxChannel.HighGain,
            _ => WhiteBoxChannel.Plexi,
        };
        bool bright = sl.P7 > 0.5f;
        float depth = sl.CustomAmp.BaseAmpChannel == 2 ? 6.0f : 5.0f;

        amp.ApplyChainAmpParams(
            ch,
            bright,
            preGain: sl.P0,
            bass: sl.P1,
            mid: sl.P2,
            treble: sl.P3,
            master: sl.P4,
            sag: sl.P5,
            presence: sl.P6,
            depth: depth);

        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]);

        amp.ProcessFullAmp(_monoTmp.AsSpan(0, n), n, includePowerAmp: true);

        for (int i = 0; i < n; i++)
        {
            float v = st.F2L.Process(st.F1L.Process(st.F0L.Process(_monoTmp[i])));
            bufL[i] = v;
            bufR[i] = v;
        }
    }

    private void ProcessCustomSpice(int idx, SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ref CompiledSlot sl)
    {
        var spiceRt = _activeSnapshot.CustomSpiceSlots[idx];
        if (spiceRt == null) return;

        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]);

        var buf4 = st.Os4x.Upsample(_monoTmp.AsSpan(0, n), n, _os2x, _os4x);
        spiceRt.ProcessBlock4x(buf4, sl.P0, sl.P1, sl.P2, sl.P3, sl.P4, sl.P5, sl.P6, sl.P7);
        st.Os4x.Downsample(_monoTmp.AsSpan(0, n), n, _os2x, _os4x);

        for (int i = 0; i < n; i++)
        {
            float v = _monoTmp[i];
            bufL[i] = v;
            bufR[i] = v;
        }
    }

    private void ProcessNamBlock(Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, ref CompiledSlot sl)
    {
        float inGain = MathF.Pow(10f, Math.Clamp(sl.P0, -12f, 12f) / 20f);
        float outGain = MathF.Pow(10f, Math.Clamp(sl.P1, -12f, 12f) / 20f);
        float mix = Math.Clamp(sl.P2, 0f, 100f) / 100f;

        for (int i = 0; i < n; i++)
            _monoTmp[i] = 0.5f * (bufL[i] + bufR[i]) * inGain;

        var namPool = _activeSnapshot.NamWorkspaces;
        var ws = (sl.NamPoolIdx >= 0 && sl.NamPoolIdx < namPool.Length) ? namPool[sl.NamPoolIdx] : null;
        if (ws != null && ws.IsReady)
        {
            ws.Process(_monoTmp.AsSpan(0, n), _monoTmp.AsSpan(0, n));
        }
        else if (!host.ProcessWaveNetRaw(_monoTmp.AsSpan(0, n), _monoTmp.AsSpan(0, n)))
        {
            return;
        }

        for (int i = 0; i < n; i++)
        {
            float w = _monoTmp[i] * outGain;
            bufL[i] = bufL[i] * (1f - mix) + w * mix;
            bufR[i] = bufR[i] * (1f - mix) + w * mix;
        }
    }

    private void ProcessCabBlock(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, ToneProcessor host, ref CompiledSlot sl, PartitionedIr? overridePartIr = null)
    {
        float targetGain = MathF.Pow(10f, Math.Clamp(sl.P2, -12f, 12f) / 20f);
        float alpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.005f * _sampleRate));
        if (!st.SmoothReady) { st.SmoothOutGain = targetGain; st.SmoothReady = true; }

        // 최대 16,384탭 Zero-Latency Partitioned FFT 컨볼루션
        PartitionedIr? partIr = overridePartIr ?? (sl.SubModel == 1
            ? (host.GetCustomOrProfilePartitionedIr() ?? _cabBank.GetPartitionedIr(0))
            : _cabBank.GetPartitionedIr(sl.SubModel));

        if (partIr != null && st.CabPoolIdx >= 0 && st.CabPoolIdx < _cabPool.Length)
        {
            var unit = _cabPool[st.CabPoolIdx];
            unit.Convolver.ConvolveStereo(bufL, bufR, n, partIr);

            if (sl.CabMic.DualEnabled && unit.MicAL.Length >= n && unit.HaasBufL.Length == 2048)
            {
                ref readonly var cm = ref sl.CabMic;
                var mAL = unit.MicAL.AsSpan(0, n);
                var mAR = unit.MicAR.AsSpan(0, n);
                var mBL = unit.MicBL.AsSpan(0, n);
                var mBR = unit.MicBR.AsSpan(0, n);

                bufL.Slice(0, n).CopyTo(mAL);
                bufR.Slice(0, n).CopyTo(mAR);
                bufL.Slice(0, n).CopyTo(mBL);
                bufR.Slice(0, n).CopyTo(mBR);

                float blendAngle = Math.Clamp(cm.Blend, 0f, 1f) * (MathF.PI * 0.5f);
                float gA = cm.MicALevelGain * MathF.Cos(blendAngle) * (cm.MicAPhaseInv ? -1f : 1f);
                float gB = cm.MicBLevelGain * MathF.Sin(blendAngle) * (cm.MicBPhaseInv ? -1f : 1f);

                float panAL = cm.MicAPan <= 0f ? 1f : 1f - cm.MicAPan;
                float panAR = cm.MicAPan >= 0f ? 1f : 1f + cm.MicAPan;
                float panBL = cm.MicBPan <= 0f ? 1f : 1f - cm.MicBPan;
                float panBR = cm.MicBPan >= 0f ? 1f : 1f + cm.MicBPan;

                float delaySamps = Math.Clamp(cm.MicBDelayMs, 0f, 15f) * 0.001f * _sampleRate;
                const int haasMask = 2047;
                var hL = unit.HaasBufL;
                var hR = unit.HaasBufR;

                for (int i = 0; i < n; i++)
                {
                    float aL = unit.A2L.Process(unit.A1L.Process(unit.A0L.Process(mAL[i]))) * gA;
                    float aR = unit.A2R.Process(unit.A1R.Process(unit.A0R.Process(mAR[i]))) * gA;

                    float bFiltL = unit.B2L.Process(unit.B1L.Process(unit.B0L.Process(mBL[i])));
                    float bFiltR = unit.B2R.Process(unit.B1R.Process(unit.B0R.Process(mBR[i])));

                    int w = unit.HaasWrite;
                    hL[w] = bFiltL;
                    hR[w] = bFiltR;
                    float bL = (delaySamps > 1e-3f ? ReadFrac(hL, w, delaySamps, haasMask) : bFiltL) * gB;
                    float bR = (delaySamps > 1e-3f ? ReadFrac(hR, w, delaySamps, haasMask) : bFiltR) * gB;
                    unit.HaasWrite = (w + 1) & haasMask;

                    bufL[i] = aL * panAL + bL * panBL;
                    bufR[i] = aR * panAR + bR * panBR;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            st.SmoothOutGain += alpha * (targetGain - st.SmoothOutGain);
            bufL[i] = st.F1L.Process(st.F0L.Process(bufL[i])) * st.SmoothOutGain;
            bufR[i] = st.F1R.Process(st.F0R.Process(bufR[i])) * st.SmoothOutGain;
        }
    }

    private static void ProcessEq5(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, float levelDb)
    {
        float gain = MathF.Pow(10f, Math.Clamp(levelDb, -12f, 12f) / 20f);
        for (int i = 0; i < n; i++)
        {
            float l = st.F4L.Process(st.F3L.Process(st.F2L.Process(st.F1L.Process(st.F0L.Process(bufL[i]))))) * gain;
            float r = st.F4R.Process(st.F3R.Process(st.F2R.Process(st.F1R.Process(st.F0R.Process(bufR[i]))))) * gain;
            bufL[i] = l;
            bufR[i] = r;
        }
    }

    private static void ProcessEqParam(SlotDspState st, Span<float> bufL, Span<float> bufR, int n)
    {
        for (int i = 0; i < n; i++)
        {
            bufL[i] = st.F2L.Process(st.F1L.Process(st.F0L.Process(bufL[i])));
            bufR[i] = st.F2R.Process(st.F1R.Process(st.F0R.Process(bufR[i])));
        }
    }

    private static void ProcessChorus(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float depthPct, float mixPct)
    {
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        float rate = Math.Clamp(rateHz, 0.1f, 6.0f);
        float depthMs = 0.8f + 4.5f * Math.Clamp(depthPct / 100f, 0f, 1f);
        float baseDelayMs = 6.5f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;
        // BOSS CE-2 MN3007 BBD 프리엠퍼시스/디엠퍼시스 + 3.6kHz 안티앨리어싱 LPF
        float bbdLpA = 1f - MathF.Exp(-2f * MathF.PI * 3600f / sr);

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            // BBD Pre-emphasis (고역 부스트 후 BBD 입력)
            st.Circuit.PreEmpL += bbdLpA * (inL - st.Circuit.PreEmpL);
            st.Circuit.PreEmpR += bbdLpA * (inR - st.Circuit.PreEmpR);
            cL[w] = inL + (inL - st.Circuit.PreEmpL) * 0.45f;
            cR[w] = inR + (inR - st.Circuit.PreEmpR) * 0.45f;

            // CE-2 트라이앵글 LFO 근사 (부드러운 아날로그 코러스 스윕)
            float lfoL = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase));
            float lfoR = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase + MathF.PI * 0.5f));
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float dL = (baseDelayMs + depthMs * lfoL) * 0.001f * sr;
            float dR = (baseDelayMs + depthMs * lfoR) * 0.001f * sr;

            float rawL = ReadFrac(cL, w, dL, mask);
            float rawR = ReadFrac(cR, w, dR, mask);

            // BBD De-emphasis + Reconstruction LPF (따뜻한 MN3007 톤)
            st.Circuit.DeEmpL += bbdLpA * (rawL - st.Circuit.DeEmpL);
            st.Circuit.DeEmpR += bbdLpA * (rawR - st.Circuit.DeEmpR);

            bufL[i] = inL * (1f - mix * 0.5f) + st.Circuit.DeEmpL * mix;
            bufR[i] = inR * (1f - mix * 0.5f) + st.Circuit.DeEmpR * mix;

            st.ChorusWrite = (w + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadFrac(float[] buf, int writePos, float delaySamps, int mask)
    {
        float rd = writePos - delaySamps;
        int i0 = (int)MathF.Floor(rd);
        float frac = rd - i0;
        float s0 = buf[i0 & mask];
        float s1 = buf[(i0 + 1) & mask];
        return s0 + (s1 - s0) * frac;
    }

    private static void ProcessPhaser(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float depthPct, float fbPct, float mixPct)
    {
        float rate = Math.Clamp(rateHz, 0.1f, 6.0f);
        float depth = Math.Clamp(depthPct, 0f, 100f) / 100f;
        float fb = Math.Clamp(fbPct, 0f, 80f) / 100f * 0.72f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;

        for (int i = 0; i < n; i++)
        {
            float lfo = 0.5f + 0.5f * MathF.Sin(st.LfoPhase);
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float fHz = 240f + depth * 2200f * lfo;
            float t = MathF.Tan(MathF.PI * MathF.Min(fHz, sr * 0.4f) / sr);
            float a = (1f - t) / (1f + t);

            float inL = bufL[i];
            float yL = inL + st.PhaserFbL * fb;
            yL = Allpass1(yL, a, ref st.Ap0L);
            yL = Allpass1(yL, a, ref st.Ap1L);
            yL = Allpass1(yL, a, ref st.Ap2L);
            yL = Allpass1(yL, a, ref st.Ap3L);
            st.PhaserFbL = yL;
            bufL[i] = inL * (1f - mix * 0.5f) + yL * mix;

            float inR = bufR[i];
            float yR = inR + st.PhaserFbR * fb;
            yR = Allpass1(yR, a, ref st.Ap0R);
            yR = Allpass1(yR, a, ref st.Ap1R);
            yR = Allpass1(yR, a, ref st.Ap2R);
            yR = Allpass1(yR, a, ref st.Ap3R);
            st.PhaserFbR = yR;
            bufR[i] = inR * (1f - mix * 0.5f) + yR * mix;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Allpass1(float x, float a, ref float z)
    {
        float y = -a * x + z;
        z = x + a * y;
        return y;
    }

    private static void ProcessTremolo(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float depthPct, float wave)
    {
        float rate = Math.Clamp(rateHz, 0.5f, 12.0f);
        float depth = Math.Clamp(depthPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;
        bool square = wave > 0.5f;
        // Fender Blackface CdS 옵토커플러: 빠른 점등(attack 4ms), 느린 잔광(release 28ms)
        float optoAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.004f * sr));
        float optoRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.028f * sr));

        for (int i = 0; i < n; i++)
        {
            float s = MathF.Sin(st.LfoPhase);
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;
            float lamp = square ? (s >= 0f ? 1f : 0f) : (0.5f + 0.5f * s);
            st.Circuit.OptoEnv += (lamp > st.Circuit.OptoEnv ? optoAtk : optoRel) * (lamp - st.Circuit.OptoEnv);
            float g = 1f - depth + depth * st.Circuit.OptoEnv;
            bufL[i] *= g;
            bufR[i] *= g;
        }
    }

    private static void ProcessDelay(StereoDelayLine dl, Span<float> bufL, Span<float> bufR, int n, int sr, float timeMs, float fbPct, float toneOrMod, float mixPct, bool isAnalog)
    {
        var bL = dl.BufL;
        var bR = dl.BufR;
        int len = bL.Length;
        if (len == 0) return;

        float delaySampsL = Math.Clamp(timeMs, 20f, 1400f) * 0.001f * sr;
        float delaySampsR = isAnalog ? delaySampsL : Math.Clamp(delaySampsL * 0.75f, 20f * 0.001f * sr, len - 4);
        if (!dl.DelayReady)
        {
            dl.SmoothDelayL = delaySampsL;
            dl.SmoothDelayR = delaySampsR;
            dl.DelayReady = true;
        }
        float dlyAlpha = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.025f * sr));
        float fb = Math.Clamp(fbPct, 0f, 88f) / 100f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float lpAlpha = isAnalog
            ? 1f - MathF.Exp(-2f * MathF.PI * (900f + 3800f * Math.Clamp(toneOrMod / 100f, 0f, 1f)) / sr)
            : 1f - MathF.Exp(-2f * MathF.PI * 8500f / sr);
        float modDepth = isAnalog ? 0f : Math.Clamp(toneOrMod / 100f, 0f, 1f) * 0.0018f * sr;
        float modInc = 2f * MathF.PI * 0.8f / sr;

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = dl.WritePos;

            dl.SmoothDelayL += dlyAlpha * (delaySampsL - dl.SmoothDelayL);
            dl.SmoothDelayR += dlyAlpha * (delaySampsR - dl.SmoothDelayR);

            float mod = modDepth > 0f ? MathF.Sin(dl.ModPhase) * modDepth : 0f;
            dl.ModPhase += modInc;
            if (dl.ModPhase > 2f * MathF.PI) dl.ModPhase -= 2f * MathF.PI;

            float rL = ReadLinear(bL, w, dl.SmoothDelayL + mod, len);
            float rR = ReadLinear(bR, w, dl.SmoothDelayR - mod, len);

            dl.LpL += lpAlpha * (rL - dl.LpL);
            dl.LpR += lpAlpha * (rR - dl.LpR);

            float fbL = isAnalog ? FastTanh(dl.LpL * 1.15f) * fb : dl.LpR * fb; // Ping-pong for digital!
            float fbR = isAnalog ? FastTanh(dl.LpR * 1.15f) * fb : dl.LpL * fb;

            bL[w] = inL + fbL;
            bR[w] = inR + fbR;

            bufL[i] = inL + dl.LpL * mix;
            bufR[i] = inR + dl.LpR * mix;

            if (++w >= len) w = 0;
            dl.WritePos = w;
        }
    }

    // ── BOSS Pedals DSP Implementations (All Zero-Allocation) ──

    private static void ProcessBossCs3(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float level, float attack, float sustain)
    {
        float susNorm = Math.Clamp(sustain, 0f, 10f) / 10f;
        float thresh = MathF.Pow(10f, (-10f - 28f * susNorm) / 20f);
        float ratio = 3.0f + 7.0f * susNorm;
        float slope = 1f - 1f / ratio;
        float atkMs = 1.5f + (10f - Math.Clamp(attack, 0f, 10f)) * 4.5f;
        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, atkMs * 0.001f * sr));
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, (0.12f + 0.25f * susNorm) * sr));
        float makeup = (0.45f + 2.0f * (Math.Clamp(level, 0f, 10f) / 10f)) * (1f + susNorm * 0.9f);

        for (int i = 0; i < n; i++)
        {
            float l = bufL[i], r = bufR[i];
            float peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            st.Env += (peak > st.Env ? atk : rel) * (peak - st.Env);
            float gr = st.Env > thresh ? MathF.Pow(st.Env / MathF.Max(1e-6f, thresh), -slope) : 1f;
            float g = gr * makeup;
            bufL[i] = st.F0L.Process(l * g);
            bufR[i] = st.F0R.Process(r * g);
        }
    }

    private static void ProcessEq7(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, float levelDb)
    {
        float gain = MathF.Pow(10f, Math.Clamp(levelDb, -15f, 15f) / 20f);
        for (int i = 0; i < n; i++)
        {
            float l = st.F6L.Process(st.F5L.Process(st.F4L.Process(st.F3L.Process(st.F2L.Process(st.F1L.Process(st.F0L.Process(bufL[i]))))))) * gain;
            float r = st.F6R.Process(st.F5R.Process(st.F4R.Process(st.F3R.Process(st.F2R.Process(st.F1R.Process(st.F0R.Process(bufR[i]))))))) * gain;
            bufL[i] = l;
            bufR[i] = r;
        }
    }

    private static void ProcessBossDc2w(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float mode1to4, float mixPct)
    {
        // BOSS DC-2W Dimension C: 역위상 듀얼 MN3007 BBD + 컴팬더 프리/디엠퍼시스 + 모노 합산 위상 안전 설계
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        int mode = Math.Clamp((int)MathF.Round(mode1to4), 1, 4);
        float rateHz = mode switch { 1 => 0.45f, 2 => 0.65f, 3 => 0.90f, _ => 1.25f };
        float depthMs = mode switch { 1 => 1.1f, 2 => 1.8f, 3 => 2.6f, _ => 3.5f };
        float baseMs = 5.2f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float inc = 2f * MathF.PI * rateHz / sr;
        float bbdLpA = 1f - MathF.Exp(-2f * MathF.PI * 4200f / sr);

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            st.Circuit.PreEmpL += bbdLpA * (inL - st.Circuit.PreEmpL);
            st.Circuit.PreEmpR += bbdLpA * (inR - st.Circuit.PreEmpR);
            cL[w] = inL + (inL - st.Circuit.PreEmpL) * 0.35f;
            cR[w] = inR + (inR - st.Circuit.PreEmpR) * 0.35f;

            float tri = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase));
            st.LfoPhase += inc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float d1 = (baseMs + depthMs * tri) * 0.001f * sr;
            float d2 = (baseMs - depthMs * tri) * 0.001f * sr;

            float raw1 = ReadFrac(cL, w, d1, mask);
            float raw2 = ReadFrac(cR, w, d2, mask);

            st.Circuit.DeEmpL += bbdLpA * (raw1 - st.Circuit.DeEmpL);
            st.Circuit.DeEmpR += bbdLpA * (raw2 - st.Circuit.DeEmpR);

            // 동위상 크로스-믹스(w1 + 0.25*w2 / w2 + 0.25*w1) + 역방향 LFO:
            // 스테레오에서는 풍성한 3D 공간감, 모노 합산 시에도 위상 상쇄 없음!
            bufL[i] = inL * (1f - mix * 0.35f) + (st.Circuit.DeEmpL * 0.85f + st.Circuit.DeEmpR * 0.15f) * mix;
            bufR[i] = inR * (1f - mix * 0.35f) + (st.Circuit.DeEmpR * 0.85f + st.Circuit.DeEmpL * 0.15f) * mix;

            st.ChorusWrite = (w + 1) & mask;
        }
    }

    private static void ProcessBossBf2(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float manual, float depthPct, float rateHz, float resPct)
    {
        // BOSS BF-2 Flanger: short BBD sweep (0.6ms .. 9ms) with resonant feedback
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        float baseMs = 0.7f + (Math.Clamp(manual, 0f, 10f) / 10f) * 4.5f;
        float depthMs = 0.3f + (Math.Clamp(depthPct, 0f, 100f) / 100f) * 3.8f;
        float rate = Math.Clamp(rateHz, 0.08f, 6.0f);
        float res = (Math.Clamp(resPct, 0f, 90f) / 100f) * 0.78f;
        float inc = 2f * MathF.PI * rate / sr;

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            float lfo = 0.5f + 0.5f * MathF.Sin(st.LfoPhase);
            st.LfoPhase += inc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float dSamps = (baseMs + depthMs * lfo) * 0.001f * sr;
            float flL = ReadFrac(cL, w, dSamps, mask);
            float flR = ReadFrac(cR, w, dSamps * 1.06f, mask);

            cL[w] = inL + FastTanh(flL * 1.1f) * res;
            cR[w] = inR + FastTanh(flR * 1.1f) * res;

            bufL[i] = inL * 0.65f + flL * 0.65f;
            bufR[i] = inR * 0.65f + flR * 0.65f;

            st.ChorusWrite = (w + 1) & mask;
        }
    }

    private static void ProcessJc120(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, float volKnob, float masterKnob)
    {
        // Roland JC-120 Jazz Chorus: 진정한 스테레오 L/R 솔리드스테이트 하이-헤드룸 스튜디오 앰프
        float v = 0.55f + 2.2f * (Math.Clamp(volKnob, 0f, 10f) / 10f);
        float m = 0.35f + 1.45f * (Math.Clamp(masterKnob, 0f, 10f) / 10f);
        for (int i = 0; i < n; i++)
        {
            float xL = st.F3L.Process(bufL[i]) * v;
            float eqL = st.F2L.Process(st.F1L.Process(st.F0L.Process(xL)));
            bufL[i] = FastTanh(eqL * 0.55f) / 0.55f * m;

            float xR = st.F3R.Process(bufR[i]) * v;
            float eqR = st.F2R.Process(st.F1R.Process(st.F0R.Process(xR)));
            bufR[i] = FastTanh(eqR * 0.55f) / 0.55f * m;
        }
    }

    // ── Public Schematic Modulation / Wah / Compressor DSP (Zero-Allocation) ──

    private static void ProcessOrangeSqueezer(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float outputKnob, float biasOrSquash, float mixPct)
    {
        // Dan Armstrong Orange Squeezer: 2N5457 JFET + 4558 반파 Ge 정류 전압더블러 컴프레서
        float squash = 0.3f + 0.7f * (Math.Clamp(biasOrSquash, 0f, 10f) / 10f);
        float thresh = MathF.Pow(10f, (-12f - 22f * squash) / 20f);
        float compSlope = 1f - 1f / (4.5f + 5.5f * squash);
        float outG = 0.50f + 2.15f * (Math.Clamp(outputKnob, 0f, 10f) / 10f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;

        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.0025f * sr)); // 빠른 어택 (2.5ms)
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.095f * sr));  // 펀치감 있는 릴리즈

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            // 반파 정류 엔벨로프 검출 (Orange Squeezer 특유의 비대칭 펌핑)
            float halfRect = MathF.Max(0f, MathF.Max(inL, inR));
            st.Env += (halfRect > st.Env ? atk : rel) * (halfRect - st.Env);
            float gr = st.Env > thresh ? MathF.Pow(st.Env / MathF.Max(1e-6f, thresh), -compSlope) : 1f;

            // 2N5457 JFET 미세 2차 고조파 온기
            float sL = inL * gr * 1.35f;
            float sR = inR * gr * 1.35f;
            float jL = (sL >= 0f ? FastTanh(sL * 0.75f) / 0.75f : FastTanh(sL * 0.92f) / 0.92f);
            float jR = (sR >= 0f ? FastTanh(sR * 0.75f) / 0.75f : FastTanh(sR * 0.92f) / 0.92f);

            float wL = st.F0L.Process(jL) * outG;
            float wR = st.F0R.Process(jR) * outG;
            bufL[i] = inL * (1f - mix) + wL * mix;
            bufR[i] = inR * (1f - mix) + wR * mix;
        }
    }

    private static void ProcessCryBabyWah(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float pedalPct, float envPct, float qKnob, float mixPct)
    {
        // Dunlop Cry Baby GCB-95: 500mH Fasel/Halo 인덕터 자이레이터 공진 피크 (350Hz..2200Hz)
        float pedal = Math.Clamp(pedalPct, 0f, 100f) / 100f;
        float autoSens = Math.Clamp(envPct, 0f, 100f) / 100f;
        float q = 2.2f + 2.8f * (Math.Clamp(qKnob, 0f, 10f) / 10f);
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;

        float atk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.006f * sr));
        float rel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.140f * sr));

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            float absIn = MathF.Max(MathF.Abs(inL), MathF.Abs(inR));
            st.Env += (absIn > st.Env ? atk : rel) * (absIn - st.Env);

            if ((i & 15) == 0)
            {
                float sweep = Math.Clamp(pedal + autoSens * MathF.Min(1f, st.Env * 4.5f), 0f, 1f);
                if (i == 0 || MathF.Abs(sweep - st.Circuit.DynBias1) > 1e-4f)
                {
                    st.Circuit.DynBias1 = sweep;
                    // 지수 주파수 매핑: 360Hz * (2160 / 360)^sweep
                    double fc = 360.0 * Math.Pow(6.0, sweep);
                    st.F0L.SetPeaking(sr, fc, q, 13.5);
                    st.F0R.CopyCoeffsFrom(in st.F0L);
                }
            }

            float wL = st.F0L.Process(inL) * 0.62f;
            float wR = st.F0R.Process(inR) * 0.62f;
            bufL[i] = inL * (1f - mix) + wL * mix;
            bufR[i] = inR * (1f - mix) + wR * mix;
        }
    }

    private static void ProcessSmallClone(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float rateHz, float deepSwitch, float mixPct)
    {
        // EHX Small Clone (4600): MN3007 BBD 코러스 + 3.2kHz Sallen-Key 안티앨리어싱 LPF + Deep 스위치
        var cL = st.ChorusBufL;
        var cR = st.ChorusBufR;
        if (cL.Length == 0) return;
        int mask = cL.Length - 1;

        float rate = Math.Clamp(rateHz, 0.1f, 5.5f);
        bool deep = deepSwitch > 0.5f;
        float baseMs = deep ? 6.8f : 4.6f;
        float depthMs = deep ? 4.8f : 2.3f;
        float mix = Math.Clamp(mixPct, 0f, 100f) / 100f;
        float phaseInc = 2f * MathF.PI * rate / sr;
        float bbdLpA = 1f - MathF.Exp(-2f * MathF.PI * 3200f / sr);

        for (int i = 0; i < n; i++)
        {
            float inL = bufL[i], inR = bufR[i];
            int w = st.ChorusWrite;

            st.Circuit.PreEmpL += bbdLpA * (inL - st.Circuit.PreEmpL);
            st.Circuit.PreEmpR += bbdLpA * (inR - st.Circuit.PreEmpR);
            cL[w] = inL + (inL - st.Circuit.PreEmpL) * 0.42f;
            cR[w] = inR + (inR - st.Circuit.PreEmpR) * 0.42f;

            float triL = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase));
            float triR = (2f / MathF.PI) * MathF.Asin(MathF.Sin(st.LfoPhase + MathF.PI * 0.45f));
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            float rawL = ReadFrac(cL, w, (baseMs + depthMs * triL) * 0.001f * sr, mask);
            float rawR = ReadFrac(cR, w, (baseMs + depthMs * triR) * 0.001f * sr, mask);

            st.Circuit.DeEmpL += bbdLpA * (rawL - st.Circuit.DeEmpL);
            st.Circuit.DeEmpR += bbdLpA * (rawR - st.Circuit.DeEmpR);

            bufL[i] = inL * (1f - mix * 0.45f) + st.Circuit.DeEmpL * mix;
            bufR[i] = inR * (1f - mix * 0.45f) + st.Circuit.DeEmpR * mix;
            st.ChorusWrite = (w + 1) & mask;
        }
    }

    private static void ProcessUniVibe(SlotDspState st, Span<float> bufL, Span<float> bufR, int n, int sr, float speedHz, float intensityPct, float vibratoMode)
    {
        // Shin-ei Uni-Vibe (1968): 4단 비대칭 스태거드 커패시터 페이즈 시프터 + CdS 램프/포토셀 열관성 스로브
        float rate = Math.Clamp(speedHz, 0.2f, 8.0f);
        float intensity = Math.Clamp(intensityPct, 0f, 100f) / 100f;
        bool isVibrato = vibratoMode > 0.5f;
        float phaseInc = 2f * MathF.PI * rate / sr;

        // CdS 램프 열관성 비대칭 응답 (점등 10ms, 잔광 42ms -> Uni-Vibe 특유의 맥박 치는 '스롭' 파형)
        float lampAtk = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.010f * sr));
        float lampRel = 1f - MathF.Exp(-1f / MathF.Max(1f, 0.042f * sr));

        for (int i = 0; i < n; i++)
        {
            float sine = 0.5f + 0.5f * MathF.Sin(st.LfoPhase);
            st.LfoPhase += phaseInc;
            if (st.LfoPhase > 2f * MathF.PI) st.LfoPhase -= 2f * MathF.PI;

            st.Circuit.OptoEnv += (sine > st.Circuit.OptoEnv ? lampAtk : lampRel) * (sine - st.Circuit.OptoEnv);
            float fBase = 160f + intensity * 1450f * st.Circuit.OptoEnv;

            // 4단 스태거드(비균등) 주파수 비율: 0.45x, 0.88x, 1.45x, 2.25x (Phase 90과 다른 보컬형 노치 간격)
            float a0 = CalcAp1(fBase * 0.45f, sr);
            float a1 = CalcAp1(fBase * 0.88f, sr);
            float a2 = CalcAp1(fBase * 1.45f, sr);
            float a3 = CalcAp1(fBase * 2.25f, sr);

            float inL = bufL[i];
            float yL = Allpass1(inL + st.PhaserFbL * 0.25f, a0, ref st.Ap0L);
            yL = Allpass1(yL, a1, ref st.Ap1L);
            yL = Allpass1(yL, a2, ref st.Ap2L);
            yL = Allpass1(yL, a3, ref st.Ap3L);
            st.PhaserFbL = yL;
            bufL[i] = isVibrato ? yL : (inL * 0.52f + yL * 0.52f);

            float inR = bufR[i];
            float yR = Allpass1(inR + st.PhaserFbR * 0.25f, a0, ref st.Ap0R);
            yR = Allpass1(yR, a1, ref st.Ap1R);
            yR = Allpass1(yR, a2, ref st.Ap2R);
            yR = Allpass1(yR, a3, ref st.Ap3R);
            st.PhaserFbR = yR;
            bufR[i] = isVibrato ? yR : (inR * 0.52f + yR * 0.52f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalcAp1(float fHz, int sr)
    {
        float t = MathF.Tan(MathF.PI * MathF.Min(fHz, sr * 0.4f) / sr);
        return (1f - t) / (1f + t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadLinear(float[] buf, int writePos, float delaySamps, int len)
    {
        float rd = writePos - delaySamps;
        while (rd < 0f) rd += len;
        int i0 = (int)rd;
        if (i0 >= len) i0 -= len;
        int i1 = i0 + 1;
        if (i1 >= len) i1 = 0;
        float frac = rd - (int)rd;
        return buf[i0] + (buf[i1] - buf[i0]) * frac;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastTanh(float x)
    {
        if (x < -4.5f) return -1f;
        if (x > 4.5f) return 1f;
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }
}