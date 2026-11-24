using System.Runtime.CompilerServices;

namespace NeuralRigStudio.Audio.Spice;

public sealed class SpiceCompileResult
{
    public bool Ok { get; init; }
    public RealtimeSpiceCircuit? Circuit { get; init; }
    public string[] Errors { get; init; } = Array.Empty<string>();
    public string[] MissingModels { get; init; } = Array.Empty<string>();
}

internal enum SpiceDeviceKind : byte
{
    Diode = 0,
    Bjt = 1,
    Jfet = 2,
    Mosfet = 3,
    OpAmp = 4,
    Triode = 5,
}

internal struct SpiceLinearRes
{
    public int N0;
    public int N1;
    public double G;
}

internal struct SpiceReactiveElem
{
    public int N0;
    public int N1;
    public double Val; // C (F) or L (H)
    public double Geq;
    public double DCGeq;
}

internal struct SpicePotElem
{
    public int Na;
    public int Nw;
    public int Nb;
    public double TotalR;
    public byte KnobIdx;
    public byte Taper; // 0=LIN, 1=LOG, 2=RLOG
    public bool Reversed;
}

internal struct SpiceSwitchElem
{
    public int N0;
    public int N1;
    public byte KnobIdx;
    public double Threshold;
    public double ClosedG;
    public double OpenG;
    public bool Inverted;
}

internal struct SpiceNonlinearElem
{
    public SpiceDeviceKind Kind;
    public sbyte Pol; // +1 for N, -1 for P
    public int N0;    // D:Anode, Q:C, J:D, M:D, U:In+, T:P
    public int N1;    // D:Cathode, Q:B, J:G, M:G, U:In-, T:G
    public int N2;    // Q:E, J:S, M:S, U:Out, T:K

    // Diode: P0=Is, P1=1/(N*Vt), P2=N*Vt, P3=Rs
    // BJT: P0=Is, P1=1/Bf, P2=1/Br, P3=1/(Nf*Vt), P4=1/(Nr*Vt), P5=1/Vaf, P6=Nf*Vt
    // JFET: P0=Vto, P1=Beta, P2=Lambda, P3=Is, P4=1/(N*Vt)
    // MOSFET: P0=Vto, P1=Kp, P2=Lambda, P3=BodyIs, P4=1/(BodyN*Vt)
    // OpAmp: P0=Aol, P1=OmegaP(rad/s), P2=SlewRate(V/s), P3=VrailPos, P4=VrailNeg, P5=Gout
    // Triode: P0=Mu, P1=Ex, P2=Kg1, P3=Kp, P4=Kvb, P5=RgGrid, P6=VgkClip
    public double P0, P1, P2, P3, P4, P5, P6;
    public double State0; // OpAmp previous output voltage V(n-1)
}

/// <summary>
/// 단일 회로 스테이지의 실시간 무할당 MNA + Schur 여인수 + 일반화 사다리꼴 + 감쇠 뉴턴-랩슨 커널.
/// </summary>
internal sealed class SpiceStageRuntime
{
    public const int MaxNodes = 36;
    public const double Theta = 0.55; // L-안정화 사다리꼴 적분 계수

    private readonly string _name;
    private readonly int _sampleRate;
    private readonly double _dt;
    private readonly int _nodeCount; // total unknown nodes N
    private readonly int _nLin;      // linear-only nodes [0 .. _nLin - 1]
    private readonly int _nNl;       // nonlinear-incident nodes [_nLin .. _nodeCount - 1]
    private readonly int _inNode;    // unknown node driven by Norton input source
    private readonly int _outNode;   // observed output node (can be unknown >= 0 or < 0)
    private readonly double _inG;
    private readonly double[] _supplyVoltages;
    private readonly string[] _nodeNames;

    private readonly SpiceLinearRes[] _resistors;
    private readonly SpiceReactiveElem[] _caps;
    private readonly SpiceReactiveElem[] _inds;
    private readonly SpicePotElem[] _pots;
    private readonly SpiceSwitchElem[] _switches;
    private readonly SpiceNonlinearElem[] _nlElems;

    // Pre-allocated flat matrices (ZERO runtime heap allocations)
    private readonly double[] _gBase;     // N * N static + companion conductance matrix
    private readonly double[] _gllInv;    // nLin * nLin
    private readonly double[] _gln;       // nLin * nNl
    private readonly double[] _gnlGllInv; // nNl * nLin
    private readonly double[] _kMat;      // nLin * nNl (= Gll^-1 * Gln)
    private readonly double[] _schurLin;  // nNl * nNl (= Gnn - Gnl * K)
    private readonly double[] _gllScratch;// nLin * (2 * nLin) Gauss-Jordan buffer

    private readonly double[] _jNl;       // nNl * nNl Newton Jacobian
    private readonly double[] _rhsNl;     // nNl Newton RHS
    private readonly double[] _dvNl;      // nNl Newton delta
    private readonly double[] _fNl;       // nNl Nonlinear current sum
    private readonly double[] _bTotal;    // N total RHS
    private readonly double[] _bLinConst; // N constant fixed-supply Norton RHS
    private readonly double[] _bLinScratch; // nLin (= Gll^-1 * bL)
    private readonly double[] _bTildeNl;  // nNl (= bN - Gnl * Gll^-1 * bL)
    private readonly double[] _vNodes;    // N converged node voltages
    private readonly double[] _vOpDc;     // N .OP DC node voltages
    private readonly double[] _vTrial;    // N trial voltages in damped Newton
    private readonly double[] _vPrevNl;   // nNl previous sample voltages for 1st-order predictor
    private readonly double[] _jAcRe;     // N * N for AC analysis (non-RT)
    private readonly double[] _jAcIm;     // N * N for AC analysis (non-RT)

    private readonly double[] _cPrevV;
    private readonly double[] _cPrevI;
    private readonly double[] _lPrevV;
    private readonly double[] _lPrevI;

    private double _dcOutVoltage;
    private double _tranMaxStepV = 0.5;
    private double _tranPredLim = 0.05;
    private readonly bool _hasOpAmp;
    private bool _hasPrevSample;
    private readonly float[] _lastKnobs = new float[8];
    private readonly float[] _knobMin;
    private readonly float[] _knobMax;
    private bool _knobsInitialized;

    public string Name => _name;
    public int NodeCount => _nodeCount;
    public double DcOutputVoltage => _dcOutVoltage;
    public string[] UnknownNodeNames => _nodeNames;

    public SpiceStageRuntime(
        string name,
        int sampleRate,
        int nodeCount,
        int nLin,
        int inNode,
        int outNode,
        double inSeriesR,
        double[] supplyVoltages,
        string[] nodeNames,
        float[] knobMin,
        float[] knobMax,
        SpiceLinearRes[] resistors,
        SpiceReactiveElem[] caps,
        SpiceReactiveElem[] inds,
        SpicePotElem[] pots,
        SpiceSwitchElem[] switches,
        SpiceNonlinearElem[] nlElems)
    {
        _name = name;
        _sampleRate = Math.Max(8000, sampleRate);
        _dt = 1.0 / _sampleRate;
        _nodeCount = nodeCount;
        _nLin = nLin;
        _nNl = nodeCount - nLin;
        _inNode = inNode;
        _outNode = outNode;
        double safeRin = inSeriesR > 0.01 ? inSeriesR : 0.01;
        _inG = 1.0 / safeRin;
        _supplyVoltages = supplyVoltages;
        _nodeNames = nodeNames;
        _knobMin = knobMin;
        _knobMax = knobMax;

        _resistors = resistors;
        _caps = caps;
        _inds = inds;
        _pots = pots;
        _switches = switches;
        _nlElems = nlElems;

        for (int i = 0; i < _caps.Length; i++)
        {
            _caps[i].Geq = _caps[i].Val / (Theta * _dt);
            _caps[i].DCGeq = 0.0;
        }
        for (int i = 0; i < _inds.Length; i++)
        {
            _inds[i].Geq = (Theta * _dt) / Math.Max(1e-9, _inds[i].Val);
            _inds[i].DCGeq = 1000.0; // 1 mOhm inductor DCR in .OP
        }

        int n = Math.Max(1, _nodeCount);
        int nl = Math.Max(1, _nNl);
        int ll = Math.Max(1, _nLin);

        _gBase = new double[n * n];
        _gllInv = new double[ll * ll];
        _gln = new double[ll * nl];
        _gnlGllInv = new double[nl * ll];
        _kMat = new double[ll * nl];
        _schurLin = new double[nl * nl];
        _gllScratch = new double[ll * (2 * ll)];

        _jNl = new double[nl * nl];
        _rhsNl = new double[nl];
        _dvNl = new double[nl];
        _fNl = new double[nl];
        _bTotal = new double[n];
        _bLinConst = new double[n];
        _bLinScratch = new double[ll];
        _bTildeNl = new double[nl];
        _vNodes = new double[n];
        _vOpDc = new double[n];
        _vTrial = new double[n];
        _vPrevNl = new double[nl];
        _jAcRe = new double[n * n];
        _jAcIm = new double[n * n];

        bool hasOpAmp = false;
        for (int i = 0; i < _nlElems.Length; i++)
        {
            if (_nlElems[i].Kind == SpiceDeviceKind.OpAmp)
            {
                hasOpAmp = true;
                break;
            }
        }
        _hasOpAmp = hasOpAmp;

        _cPrevV = new double[_caps.Length];
        _cPrevI = new double[_caps.Length];
        _lPrevV = new double[_inds.Length];
        _lPrevI = new double[_inds.Length];
    }

    public void InitializeWithKnobs(ReadOnlySpan<float> knobs)
    {
        for (int i = 0; i < 8; i++)
            _lastKnobs[i] = i < knobs.Length ? knobs[i] : 5f;
        _knobsInitialized = true;
        SolveDcOperatingPoint(0f, 0f);
        BuildTransientLinearMatrices(1e-11);
        ResetToOperatingPoint();
    }

    public void ApplyKnobsIfChanged(ReadOnlySpan<float> knobs)
    {
        bool changed = !_knobsInitialized;
        for (int i = 0; i < 8; i++)
        {
            float kv = i < knobs.Length ? knobs[i] : 5f;
            if (!changed && MathF.Abs(kv - _lastKnobs[i]) > 1e-4f)
                changed = true;
            _lastKnobs[i] = kv;
        }
        if (!changed) return;
        _knobsInitialized = true;
        _hasPrevSample = false;
        BuildTransientLinearMatrices(1e-11);
    }

    public void ResetToOperatingPoint()
    {
        Array.Copy(_vOpDc, _vNodes, _nodeCount);
        for (int r = 0; r < _nNl; r++)
            _vPrevNl[r] = _vOpDc[_nLin + r];
        _hasPrevSample = false;
        for (int i = 0; i < _caps.Length; i++)
        {
            _cPrevV[i] = GetNodeV(_vOpDc, _caps[i].N0) - GetNodeV(_vOpDc, _caps[i].N1);
            _cPrevI[i] = 0.0;
        }
        for (int i = 0; i < _inds.Length; i++)
        {
            double vDiff = GetNodeV(_vOpDc, _inds[i].N0) - GetNodeV(_vOpDc, _inds[i].N1);
            _lPrevV[i] = vDiff;
            _lPrevI[i] = vDiff * _inds[i].DCGeq;
        }
        for (int i = 0; i < _nlElems.Length; i++)
        {
            if (_nlElems[i].Kind == SpiceDeviceKind.OpAmp)
                _nlElems[i].State0 = GetNodeV(_vOpDc, _nlElems[i].N2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetNodeV(double[] vUnknown, int idx)
    {
        if (idx >= 0) return vUnknown[idx];
        if (idx == -1) return 0.0;
        int sIdx = -idx - 2;
        return (uint)sIdx < (uint)_supplyVoltages.Length ? _supplyVoltages[sIdx] : 0.0;
    }

    public Dictionary<string, double> GetDcOperatingPointSnapshot()
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _nodeCount && i < _nodeNames.Length; i++)
            dict[_nodeNames[i]] = Math.Round(_vOpDc[i], 4);
        return dict;
    }

    private void RebuildLinearCore(bool isDcOp, double gmin)
    {
        int n = _nodeCount;
        Array.Clear(_gBase, 0, n * n);
        Array.Clear(_bLinConst, 0, n);

        // Anchor each unknown node with Gmin to avoid singularity on floating caps
        for (int i = 0; i < n; i++)
            _gBase[i * n + i] += gmin;

        // Input Norton source
        if (_inNode >= 0)
            _gBase[_inNode * n + _inNode] += _inG;

        void StampConductance(int a, int b, double g)
        {
            if (a == b || g <= 0.0) return;
            if (a >= 0)
            {
                _gBase[a * n + a] += g;
                if (b >= 0) _gBase[a * n + b] -= g;
                else if (b < -1) _bLinConst[a] += g * GetSupplyV(b);
            }
            if (b >= 0)
            {
                _gBase[b * n + b] += g;
                if (a >= 0) _gBase[b * n + a] -= g;
                else if (a < -1) _bLinConst[b] += g * GetSupplyV(a);
            }
        }

        double GetSupplyV(int idx)
        {
            int s = -idx - 2;
            return (uint)s < (uint)_supplyVoltages.Length ? _supplyVoltages[s] : 0.0;
        }

        for (int i = 0; i < _resistors.Length; i++)
            StampConductance(_resistors[i].N0, _resistors[i].N1, _resistors[i].G);

        if (!isDcOp)
        {
            for (int i = 0; i < _caps.Length; i++)
                StampConductance(_caps[i].N0, _caps[i].N1, _caps[i].Geq);
            for (int i = 0; i < _inds.Length; i++)
                StampConductance(_inds[i].N0, _inds[i].N1, _inds[i].Geq);
        }
        else
        {
            for (int i = 0; i < _inds.Length; i++)
                StampConductance(_inds[i].N0, _inds[i].N1, _inds[i].DCGeq);
        }

        for (int i = 0; i < _pots.Length; i++)
        {
            ref readonly var pot = ref _pots[i];
            float raw = _lastKnobs[pot.KnobIdx];
            double span = Math.Max(1e-5, _knobMax[pot.KnobIdx] - _knobMin[pot.KnobIdx]);
            double norm = Math.Clamp((raw - _knobMin[pot.KnobIdx]) / span, 0.0, 1.0);
            if (pot.Reversed) norm = 1.0 - norm;
            double ratio = pot.Taper switch
            {
                1 => (Math.Exp(3.0 * norm) - 1.0) / (Math.Exp(3.0) - 1.0),         // LOG (Audio)
                2 => 1.0 - (Math.Exp(3.0 * (1.0 - norm)) - 1.0) / (Math.Exp(3.0) - 1.0), // RLOG
                _ => norm,
            };
            ratio = Math.Clamp(ratio, 0.0005, 0.9995);
            double gAw = 1.0 / ((1.0 - ratio) * pot.TotalR);
            double gWb = 1.0 / (ratio * pot.TotalR);
            if (pot.Na != int.MinValue && pot.Nw != int.MinValue)
                StampConductance(pot.Na, pot.Nw, gAw);
            if (pot.Nw != int.MinValue && pot.Nb != int.MinValue)
                StampConductance(pot.Nw, pot.Nb, gWb);
        }

        for (int i = 0; i < _switches.Length; i++)
        {
            ref readonly var sw = ref _switches[i];
            bool closed = _lastKnobs[sw.KnobIdx] >= sw.Threshold;
            if (sw.Inverted) closed = !closed;
            StampConductance(sw.N0, sw.N1, closed ? sw.ClosedG : sw.OpenG);
        }
    }

    private void BuildTransientLinearMatrices(double gmin)
    {
        RebuildLinearCore(isDcOp: false, gmin: gmin);
        FactorSchurComplement();
    }

    private void FactorSchurComplement()
    {
        int n = _nodeCount;
        int nL = _nLin;
        int nN = _nNl;

        if (nN == 0)
        {
            // Purely linear circuit: invert G_LL once
            InvertDense(_gBase, n, _gllInv, nL, nL, _gllScratch);
            return;
        }

        if (nL == 0)
        {
            for (int r = 0; r < nN; r++)
                for (int c = 0; c < nN; c++)
                    _schurLin[r * nN + c] = _gBase[r * n + c];
            return;
        }

        // Extract G_LL, G_LN
        for (int r = 0; r < nL; r++)
            for (int c = 0; c < nN; c++)
                _gln[r * nN + c] = _gBase[r * n + (nL + c)];

        // Invert G_LL -> _gllInv (nL x nL)
        InvertDense(_gBase, n, _gllInv, nL, nL, _gllScratch);

        // K = G_LL^-1 * G_LN (nL x nN)
        for (int r = 0; r < nL; r++)
        {
            for (int c = 0; c < nN; c++)
            {
                double sum = 0.0;
                int rowOff = r * nL;
                for (int k = 0; k < nL; k++)
                    sum += _gllInv[rowOff + k] * _gln[k * nN + c];
                _kMat[r * nN + c] = sum;
            }
        }

        // G_NL * G_LL^-1 (nN x nL)
        for (int r = 0; r < nN; r++)
        {
            int gRow = (nL + r) * n;
            for (int c = 0; c < nL; c++)
            {
                double sum = 0.0;
                for (int k = 0; k < nL; k++)
                    sum += _gBase[gRow + k] * _gllInv[k * nL + c];
                _gnlGllInv[r * nL + c] = sum;
            }
        }

        // S_lin = G_NN - G_NL * K (nN x nN)
        for (int r = 0; r < nN; r++)
        {
            int gRow = (nL + r) * n + nL;
            for (int c = 0; c < nN; c++)
            {
                double dot = 0.0;
                int rowOff = (nL + r) * n;
                for (int k = 0; k < nL; k++)
                    dot += _gBase[rowOff + k] * _kMat[k * nN + c];
                _schurLin[r * nN + c] = _gBase[gRow + c] - dot;
            }
        }
    }

    private void SolveDcOperatingPoint(double aolScaleStart = 0.02, double dummy = 0)
    {
        // Determine initial supply midpoint voltage guess and step ceiling (for 250V+ tube rails vs 9V pedals)
        double vSeed = 0.0;
        double maxSupplyAbs = 9.0;
        if (_supplyVoltages.Length > 0)
        {
            double sum = 0.0;
            maxSupplyAbs = 0.0;
            for (int i = 0; i < _supplyVoltages.Length; i++)
            {
                sum += _supplyVoltages[i];
                double absV = Math.Abs(_supplyVoltages[i]);
                if (absV > maxSupplyAbs) maxSupplyAbs = absV;
            }
            vSeed = sum / _supplyVoltages.Length;
            if (Math.Abs(vSeed) > 20.0) vSeed = 1.5; // High-voltage tube amp cathode/grid guess
        }
        for (int i = 0; i < _nodeCount; i++) _vNodes[i] = vSeed;

        double dcMaxStepV = maxSupplyAbs > 30.0 ? Math.Min(80.0, maxSupplyAbs * 0.35) : 1.5;
        int dcMaxIter = maxSupplyAbs > 30.0 ? 80 : 40;
        _tranMaxStepV = maxSupplyAbs > 30.0 ? 25.0 : 0.45;
        _tranPredLim = maxSupplyAbs > 30.0 ? 4.0 : 0.05;

        double[] gmins = { 1e-8, 1e-9, 1e-10, 1e-11 };
        double[] aolScales = { 0.001, 0.02, 0.2, 1.0 };

        for (int stage = 0; stage < gmins.Length; stage++)
        {
            RebuildLinearCore(isDcOp: true, gmin: gmins[stage]);
            FactorSchurComplement();
            Array.Copy(_bLinConst, _bTotal, _nodeCount);
            SolveNonlinearStep(isDcOp: true, dt: 1.0, maxIter: dcMaxIter, tol: 1e-6, aolScale: aolScales[stage], maxStepV: dcMaxStepV);
        }

        Array.Copy(_vNodes, _vOpDc, _nodeCount);
        _dcOutVoltage = GetNodeV(_vOpDc, _outNode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ProcessSample(double vin)
    {
        int n = _nodeCount;
        // Build total RHS: fixed supply currents + cap/ind history + Norton input
        for (int i = 0; i < n; i++)
            _bTotal[i] = _bLinConst[i];
        if (_inNode >= 0)
            _bTotal[_inNode] += _inG * vin;

        double capHistMult = (1.0 - Theta) / Theta;
        for (int i = 0; i < _caps.Length; i++)
        {
            ref readonly var c = ref _caps[i];
            double iHist = c.Geq * _cPrevV[i] + capHistMult * _cPrevI[i];
            if (c.N0 >= 0) _bTotal[c.N0] += iHist;
            if (c.N1 >= 0) _bTotal[c.N1] -= iHist;
        }
        if (_inds.Length > 0)
        {
            for (int i = 0; i < _inds.Length; i++)
            {
                ref readonly var ind = ref _inds[i];
                double iHistL = _lPrevI[i] + capHistMult * ind.Geq * _lPrevV[i];
                if (ind.N0 >= 0) _bTotal[ind.N0] -= iHistL;
                if (ind.N1 >= 0) _bTotal[ind.N1] += iHistL;
            }
        }

        if (_nNl > 0)
        {
            int nL = _nLin;
            int nN = _nNl;
            var vNodes = _vNodes;
            var vPrev = _vPrevNl;
            if (_hasPrevSample)
            {
                double lim = _tranPredLim;
                for (int r = 0; r < nN; r++)
                {
                    double cur = vNodes[nL + r];
                    double dv = cur - vPrev[r];
                    vPrev[r] = cur;
                    if (dv > lim) dv = lim;
                    else if (dv < -lim) dv = -lim;
                    vNodes[nL + r] = cur + dv;
                }
            }
            else
            {
                for (int r = 0; r < nN; r++)
                    vPrev[r] = vNodes[nL + r];
                _hasPrevSample = true;
            }
        }

        SolveNonlinearStep(isDcOp: false, dt: _dt, maxIter: 6, tol: 2e-4, aolScale: 1.0, maxStepV: _tranMaxStepV);

        // Update dynamic capacitor & inductor & op-amp states
        for (int i = 0; i < _caps.Length; i++)
        {
            ref readonly var c = ref _caps[i];
            double vNew = GetNodeV(_vNodes, c.N0) - GetNodeV(_vNodes, c.N1);
            double iHist = c.Geq * _cPrevV[i] + capHistMult * _cPrevI[i];
            _cPrevI[i] = c.Geq * vNew - iHist;
            _cPrevV[i] = vNew;
        }
        if (_inds.Length > 0)
        {
            for (int i = 0; i < _inds.Length; i++)
            {
                ref readonly var ind = ref _inds[i];
                double vNew = GetNodeV(_vNodes, ind.N0) - GetNodeV(_vNodes, ind.N1);
                double iHistL = _lPrevI[i] + capHistMult * ind.Geq * _lPrevV[i];
                _lPrevI[i] = ind.Geq * vNew + iHistL;
                _lPrevV[i] = vNew;
            }
        }
        if (_hasOpAmp)
        {
            for (int i = 0; i < _nlElems.Length; i++)
            {
                if (_nlElems[i].Kind == SpiceDeviceKind.OpAmp)
                    _nlElems[i].State0 = GetNodeV(_vNodes, _nlElems[i].N2);
            }
        }

        double vOut = GetNodeV(_vNodes, _outNode);
        if (!double.IsFinite(vOut))
        {
            ResetToOperatingPoint();
            return 0f;
        }
        return (float)(vOut - _dcOutVoltage);
    }

    private void SolveNonlinearStep(bool isDcOp, double dt, int maxIter, double tol, double aolScale, double maxStepV)
    {
        int nL = _nLin;
        int nN = _nNl;
        var vNodes = _vNodes;
        var schurLin = _schurLin;
        var jNl = _jNl;
        var fNl = _fNl;
        var rhsNl = _rhsNl;
        var dvNl = _dvNl;
        var bTildeNl = _bTildeNl;

        // Compute bL_inv = G_LL^-1 * bL (nL)
        if (nL > 0)
        {
            for (int r = 0; r < nL; r++)
            {
                double sum = 0.0;
                int row = r * nL;
                for (int c = 0; c < nL; c++)
                    sum += _gllInv[row + c] * _bTotal[c];
                _bLinScratch[r] = sum;
            }
        }

        if (nN == 0)
        {
            for (int r = 0; r < nL; r++) vNodes[r] = _bLinScratch[r];
            return;
        }

        // Compute reduced Schur RHS b_tilde_N = b_N - (G_NL * G_LL^-1) * b_L
        for (int r = 0; r < nN; r++)
        {
            double sub = 0.0;
            int row = r * nL;
            for (int c = 0; c < nL; c++)
                sub += _gnlGllInv[row + c] * _bTotal[c];
            bTildeNl[r] = _bTotal[nL + r] - sub;
        }

        int nN2 = nN * nN;
        for (int iter = 0; iter < maxIter; iter++)
        {
            for (int i = 0; i < nN2; i++) jNl[i] = schurLin[i];
            for (int r = 0; r < nN; r++) fNl[r] = 0.0;

            EvaluateAndStampNonlinearDevices(isDcOp, dt, aolScale);

            // rhs = -(S_lin * v_N + f_N - b_tilde_N)
            double oldNormSq = 0.0;
            for (int r = 0; r < nN; r++)
            {
                double sV = 0.0;
                int row = r * nN;
                for (int c = 0; c < nN; c++)
                    sV += schurLin[row + c] * vNodes[nL + c];
                double res = bTildeNl[r] - (sV + fNl[r]);
                rhsNl[r] = res;
                if (isDcOp) oldNormSq += res * res;
            }

            if (!SolveLinearInPlace(jNl, rhsNl, dvNl, nN))
                break;

            // Step clamping for diode/semiconductor Newton stability
            double maxDv = 0.0;
            for (int r = 0; r < nN; r++)
            {
                double a = Math.Abs(dvNl[r]);
                if (a > maxDv) maxDv = a;
            }

            double scale = maxDv > maxStepV ? (maxStepV / maxDv) : 1.0;
            if (isDcOp && oldNormSq > 1e-14 && maxDv > 1e-3)
            {
                for (int r = 0; r < nN; r++)
                    _vTrial[r] = vNodes[nL + r];

                double bestScale = scale;
                double bestNormSq = double.PositiveInfinity;
                bool accepted = false;
                for (int bt = 0; bt < 4; bt++)
                {
                    for (int r = 0; r < nN; r++)
                    {
                        double cand = _vTrial[r] + scale * dvNl[r];
                        vNodes[nL + r] = double.IsFinite(cand) ? cand : 0.0;
                    }
                    for (int r = 0; r < nN; r++) fNl[r] = 0.0;
                    EvaluateAndStampNonlinearDevices(isDcOp, dt, aolScale);
                    double newNormSq = 0.0;
                    for (int r = 0; r < nN; r++)
                    {
                        double sV = 0.0;
                        int row = r * nN;
                        for (int c = 0; c < nN; c++)
                            sV += schurLin[row + c] * vNodes[nL + c];
                        double res = bTildeNl[r] - (sV + fNl[r]);
                        newNormSq += res * res;
                    }
                    if (newNormSq < bestNormSq)
                    {
                        bestNormSq = newNormSq;
                        bestScale = scale;
                    }
                    if (newNormSq <= oldNormSq * 1.02)
                    {
                        accepted = true;
                        break;
                    }
                    scale *= 0.5;
                }
                if (!accepted)
                {
                    for (int r = 0; r < nN; r++)
                    {
                        double bestNext = _vTrial[r] + bestScale * dvNl[r];
                        vNodes[nL + r] = double.IsFinite(bestNext) ? bestNext : 0.0;
                    }
                }
            }
            else
            {
                for (int r = 0; r < nN; r++)
                {
                    double next = vNodes[nL + r] + scale * dvNl[r];
                    if (!double.IsFinite(next)) next = 0.0;
                    vNodes[nL + r] = next;
                }
            }

            if (isDcOp)
            {
                if (maxDv < tol && iter >= 1)
                    break;
            }
            else
            {
                bool conv = true;
                for (int r = 0; r < nN; r++)
                {
                    if (Math.Abs(dvNl[r]) > tol + 2e-4 * Math.Abs(vNodes[nL + r]))
                    {
                        conv = false;
                        break;
                    }
                }
                if (conv)
                    break;
            }
        }

        // Recover linear nodes: v_L = G_LL^-1 * b_L - K * v_N
        for (int r = 0; r < nL; r++)
        {
            double sub = 0.0;
            int row = r * nN;
            for (int c = 0; c < nN; c++)
                sub += _kMat[row + c] * vNodes[nL + c];
            vNodes[r] = _bLinScratch[r] - sub;
        }
    }

    private void EvaluateAndStampNonlinearDevices(bool isDcOp, double dt, double aolScale, bool includeOpAmp = true)
    {
        int nL = _nLin;
        int nN = _nNl;

        void AddF(int node, double iLeave)
        {
            if (node >= nL)
                _fNl[node - nL] += iLeave;
        }

        void AddJ(int rowNode, int colNode, double g)
        {
            if (rowNode >= nL && colNode >= nL)
                _jNl[(rowNode - nL) * nN + (colNode - nL)] += g;
        }

        for (int eIdx = 0; eIdx < _nlElems.Length; eIdx++)
        {
            ref readonly var el = ref _nlElems[eIdx];
            switch (el.Kind)
            {
                case SpiceDeviceKind.Diode:
                {
                    double va = GetNodeV(_vNodes, el.N0);
                    double vk = GetNodeV(_vNodes, el.N1);
                    double v = va - vk;
                    EvalDiodeWithRs(v, el.P0, el.P1, el.P2, el.P3, out double id, out double gd);
                    AddF(el.N0, id);
                    AddF(el.N1, -id);
                    AddJ(el.N0, el.N0, gd);
                    AddJ(el.N1, el.N1, gd);
                    AddJ(el.N0, el.N1, -gd);
                    AddJ(el.N1, el.N0, -gd);
                    break;
                }

                case SpiceDeviceKind.Bjt:
                {
                    double vc = GetNodeV(_vNodes, el.N0);
                    double vb = GetNodeV(_vNodes, el.N1);
                    double ve = GetNodeV(_vNodes, el.N2);
                    double p = el.Pol;
                    double vbe = p * (vb - ve);
                    double vbc = p * (vb - vc);
                    double vce = vbe - vbc;

                    SafeExp(vbe * el.P3, out double expBe, out double dExpBe);
                    SafeExp(vbc * el.P4, out double expBc, out double dExpBc);
                    double ifwd = el.P0 * (expBe - 1.0);
                    double irev = el.P0 * (expBc - 1.0);
                    double dIf = el.P0 * el.P3 * dExpBe;
                    double dIr = el.P0 * el.P4 * dExpBc;

                    double early = Math.Max(0.1, 1.0 + vce * el.P5);
                    double dEarlyDvce = (1.0 + vce * el.P5) > 0.1 ? el.P5 : 0.0;

                    double itran = (ifwd - irev) * early;
                    double dItranDvbe = dIf * early + (ifwd - irev) * dEarlyDvce;
                    double dItranDvbc = -dIr * early - (ifwd - irev) * dEarlyDvce;

                    double icBase = itran - irev * el.P2;
                    double ibBase = ifwd * el.P1 + irev * el.P2;
                    double ieBase = -(icBase + ibBase);

                    double dIcDvbe = dItranDvbe;
                    double dIcDvbc = dItranDvbc - dIr * el.P2;
                    double dIbDvbe = dIf * el.P1;
                    double dIbDvbc = dIr * el.P2;

                    // Convert (d/dvbe, d/dvbc) -> (d/dvc, d/dvb, d/dve) with p sign factor
                    // vbe = p*(vb-ve), vbc = p*(vb-vc)
                    double ic = p * icBase;
                    double ib = p * ibBase;
                    double ie = p * ieBase;

                    AddF(el.N0, ic);
                    AddF(el.N1, ib);
                    AddF(el.N2, ie);

                    // Chain rule: I = p * Ibase(vbe, vbc) -> dI/dv = dIbase/dvbe*(d vbe'/dv)*p + ... = I_mat * p^2 = I_mat!
                    double gCc = -dIcDvbc;
                    double gCb = dIcDvbe + dIcDvbc;
                    double gCe = -dIcDvbe;

                    double gBc = -dIbDvbc;
                    double gBb = dIbDvbe + dIbDvbc;
                    double gBe = -dIbDvbe;

                    double gEc = -(gCc + gBc);
                    double gEb = -(gCb + gBb);
                    double gEe = -(gCe + gBe);

                    AddJ(el.N0, el.N0, gCc); AddJ(el.N0, el.N1, gCb); AddJ(el.N0, el.N2, gCe);
                    AddJ(el.N1, el.N0, gBc); AddJ(el.N1, el.N1, gBb); AddJ(el.N1, el.N2, gBe);
                    AddJ(el.N2, el.N0, gEc); AddJ(el.N2, el.N1, gEb); AddJ(el.N2, el.N2, gEe);
                    break;
                }

                case SpiceDeviceKind.Jfet:
                {
                    double vd = GetNodeV(_vNodes, el.N0);
                    double vg = GetNodeV(_vNodes, el.N1);
                    double vs = GetNodeV(_vNodes, el.N2);
                    double p = el.Pol;
                    double vgs = p * (vg - vs);
                    double vgd = p * (vg - vd);
                    double vds = vgs - vgd;

                    // Normal vs reverse mode symmetry
                    double dir = vds >= 0.0 ? 1.0 : -1.0;
                    double vgsEff = dir > 0 ? vgs : vgd;
                    double vdsEff = dir > 0 ? vds : -vds;

                    double vto = el.P0;
                    double beta = el.P1;
                    double lambda = el.P2;
                    double vov = vgsEff - vto;

                    double idEff = 0.0, gM = 0.0, gDs = 0.0;
                    if (vov > 0.0)
                    {
                        double mod = 1.0 + lambda * vdsEff;
                        if (vdsEff < vov)
                        {
                            // Triode region
                            idEff = beta * (2.0 * vov - vdsEff) * vdsEff * mod;
                            gM = beta * 2.0 * vdsEff * mod;
                            gDs = beta * 2.0 * (vov - vdsEff) * mod + beta * (2.0 * vov - vdsEff) * vdsEff * lambda;
                        }
                        else
                        {
                            // Saturation region
                            idEff = beta * vov * vov * mod;
                            gM = 2.0 * beta * vov * mod;
                            gDs = beta * vov * vov * lambda;
                        }
                    }

                    // Gate junction diodes
                    SafeExp(vgs * el.P4, out double eGs, out double dEgs);
                    SafeExp(vgd * el.P4, out double eGd, out double dEgd);
                    double igs = el.P3 * (eGs - 1.0);
                    double igd = el.P3 * (eGd - 1.0);
                    double gGs = el.P3 * el.P4 * dEgs;
                    double gGd = el.P3 * el.P4 * dEgd;

                    double idLeave = p * (dir * idEff - igd);
                    double igLeave = p * (igs + igd);
                    double isLeave = -(idLeave + igLeave);

                    AddF(el.N0, idLeave);
                    AddF(el.N1, igLeave);
                    AddF(el.N2, isLeave);

                    double dIdDvg = dir > 0 ? gM : -gM;
                    double dIdDvd = dir > 0 ? gDs : (gM + gDs);
                    double dIdDvs = -(dIdDvg + dIdDvd);

                    AddJ(el.N0, el.N0, dIdDvd + gGd); AddJ(el.N0, el.N1, dIdDvg - gGd); AddJ(el.N0, el.N2, dIdDvs);
                    AddJ(el.N1, el.N0, -gGd);         AddJ(el.N1, el.N1, gGs + gGd);   AddJ(el.N1, el.N2, -gGs);
                    AddJ(el.N2, el.N0, -dIdDvd);      AddJ(el.N2, el.N1, -(dIdDvg + gGs)); AddJ(el.N2, el.N2, -dIdDvs + gGs);
                    break;
                }

                case SpiceDeviceKind.Mosfet:
                {
                    double vd = GetNodeV(_vNodes, el.N0);
                    double vg = GetNodeV(_vNodes, el.N1);
                    double vs = GetNodeV(_vNodes, el.N2);
                    double p = el.Pol;
                    double vgs = p * (vg - vs);
                    double vds = p * (vd - vs);
                    double dir = vds >= 0.0 ? 1.0 : -1.0;
                    double vgsEff = dir > 0 ? vgs : p * (vg - vd);
                    double vdsEff = Math.Abs(vds);

                    double vov = vgsEff - el.P0;
                    double idEff = 0.0, gM = 0.0, gDs = 0.0;
                    if (vov > 0.0)
                    {
                        double mod = 1.0 + el.P2 * vdsEff;
                        if (vdsEff < vov)
                        {
                            idEff = el.P1 * (vov * vdsEff - 0.5 * vdsEff * vdsEff) * mod;
                            gM = el.P1 * vdsEff * mod;
                            gDs = el.P1 * (vov - vdsEff) * mod + el.P1 * (vov * vdsEff - 0.5 * vdsEff * vdsEff) * el.P2;
                        }
                        else
                        {
                            idEff = 0.5 * el.P1 * vov * vov * mod;
                            gM = el.P1 * vov * mod;
                            gDs = 0.5 * el.P1 * vov * vov * el.P2;
                        }
                    }

                    // Integral body diode (S -> D for NMOS, crucial for 2N7000/BS170 pedal clippers!)
                    SafeExp((-vds) * el.P4, out double eBody, out double dEBody);
                    double iBody = el.P3 * (eBody - 1.0); // flows S -> D
                    double gBody = el.P3 * el.P4 * dEBody;

                    double idLeave = p * (dir * idEff - iBody);
                    double isLeave = -idLeave;
                    AddF(el.N0, idLeave);
                    AddF(el.N2, isLeave);

                    double dIdDvg = dir > 0 ? gM : -gM;
                    double dIdDvd = (dir > 0 ? gDs : (gM + gDs)) + gBody;
                    double dIdDvs = -(dIdDvg + dIdDvd);

                    AddJ(el.N0, el.N0, dIdDvd);  AddJ(el.N0, el.N1, dIdDvg);  AddJ(el.N0, el.N2, dIdDvs);
                    AddJ(el.N2, el.N0, -dIdDvd); AddJ(el.N2, el.N1, -dIdDvg); AddJ(el.N2, el.N2, -dIdDvs);
                    break;
                }

                case SpiceDeviceKind.OpAmp:
                {
                    if (!includeOpAmp) break;
                    double vp = GetNodeV(_vNodes, el.N0);
                    double vn = GetNodeV(_vNodes, el.N1);
                    double vOut = GetNodeV(_vNodes, el.N2);
                    double vd = vp - vn;

                    double aol = el.P0 * aolScale;
                    double omegaP = el.P1;
                    double slew = el.P2;
                    double vPos = el.P3;
                    double vNeg = el.P4;
                    double gOut = el.P5;

                    double vMid = 0.5 * (vPos + vNeg);
                    double vSpan = Math.Max(0.1, 0.5 * (vPos - vNeg));
                    double vPole, dPoleDVd;
                    if (isDcOp)
                    {
                        vPole = vMid + aol * vd;
                        dPoleDVd = aol;
                    }
                    else
                    {
                        double alpha = omegaP * dt;
                        double invDen = 1.0 / (1.0 + alpha);
                        vPole = (el.State0 + alpha * (vMid + aol * vd)) * invDen;
                        dPoleDVd = (alpha * aol) * invDen;
                    }

                    double uRail = Math.Clamp((vPole - vMid) / vSpan, -20.0, 20.0);
                    double tRail = FastTanh(uRail);
                    double vRail = vMid + vSpan * tRail;
                    double dRailDPole = Math.Max(1e-5 * (isDcOp ? 10.0 : 1.0), 1.0 - tRail * tRail);

                    double vTarget, dTargetDRail;
                    if (isDcOp || slew <= 0.0)
                    {
                        vTarget = vRail;
                        dTargetDRail = 1.0;
                    }
                    else
                    {
                        double dMax = Math.Max(1e-4, slew * dt);
                        double diff = vRail - el.State0;
                        double uSlew = Math.Clamp(diff / dMax, -20.0, 20.0);
                        double tSlew = FastTanh(uSlew);
                        vTarget = el.State0 + dMax * tSlew;
                        dTargetDRail = Math.Max(1e-4, 1.0 - tSlew * tSlew);
                    }

                    double dTargetDVd = dTargetDRail * dRailDPole * dPoleDVd;
                    double iOut = gOut * (vOut - vTarget);
                    AddF(el.N2, iOut);
                    AddJ(el.N2, el.N2, gOut);
                    AddJ(el.N2, el.N0, -gOut * dTargetDVd);
                    AddJ(el.N2, el.N1, gOut * dTargetDVd);
                    break;
                }

                case SpiceDeviceKind.Triode:
                {
                    double vp = GetNodeV(_vNodes, el.N0);
                    double vg = GetNodeV(_vNodes, el.N1);
                    double vk = GetNodeV(_vNodes, el.N2);
                    double vpk = vp - vk;
                    double vgk = vg - vk;

                    double mu = el.P0;
                    double ex = el.P1;
                    double kg1 = el.P2;
                    double kp = el.P3;
                    double kvb = el.P4;
                    double rgGrid = Math.Max(100.0, el.P5);
                    double vgkClip = el.P6;

                    double ip = 0.0, gP = 0.0, gM = 0.0;
                    if (vpk > 0.0)
                    {
                        double sqrtTerm = Math.Sqrt(kvb + vpk * vpk);
                        double arg = kp * (1.0 / mu + vgk / sqrtTerm);
                        double lnVal, sigVal;
                        if (arg > 30.0)
                        {
                            lnVal = arg;
                            sigVal = 1.0;
                        }
                        else if (arg < -30.0)
                        {
                            lnVal = Math.Exp(arg);
                            sigVal = lnVal;
                        }
                        else
                        {
                            double exA = Math.Exp(arg);
                            lnVal = Math.Log(1.0 + exA);
                            sigVal = exA / (1.0 + exA);
                        }

                        double e1 = (vpk / kp) * lnVal;
                        if (e1 > 1e-9)
                        {
                            ip = Math.Pow(e1, ex) / kg1;
                            double dIpDe1 = ex * ip / e1;
                            double dE1Dvgk = (vpk / sqrtTerm) * sigVal;
                            double dArgDvpk = -kp * vgk * vpk / (sqrtTerm * sqrtTerm * sqrtTerm);
                            double dE1Dvpk = lnVal / kp + (vpk / kp) * sigVal * dArgDvpk;
                            gM = Math.Max(0.0, dIpDe1 * dE1Dvgk);
                            gP = Math.Max(1e-9, dIpDe1 * dE1Dvpk);
                        }
                    }

                    // Positive grid-cathode conduction
                    double dg = (vgk - vgkClip) / 0.05;
                    double ig = 0.0, gGrid = 0.0;
                    if (dg > 20.0)
                    {
                        ig = (0.05 * dg) / rgGrid;
                        gGrid = 1.0 / rgGrid;
                    }
                    else if (dg > -20.0)
                    {
                        double exG = Math.Exp(dg);
                        double inv1p = 1.0 / (1.0 + exG);
                        ig = (0.05 * Math.Log(1.0 + exG)) / rgGrid;
                        gGrid = (exG * inv1p) / rgGrid;
                    }

                    AddF(el.N0, ip);
                    AddF(el.N1, ig);
                    AddF(el.N2, -(ip + ig));

                    double gPk = -(gP + gM);
                    AddJ(el.N0, el.N0, gP);   AddJ(el.N0, el.N1, gM);        AddJ(el.N0, el.N2, gPk);
                    AddJ(el.N1, el.N1, gGrid); AddJ(el.N1, el.N2, -gGrid);
                    AddJ(el.N2, el.N0, -gP);  AddJ(el.N2, el.N1, -(gM + gGrid)); AddJ(el.N2, el.N2, gP + gM + gGrid);
                    break;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double FastTanh(double u)
    {
        if (Math.Abs(u) <= 0.15)
        {
            double u2 = u * u;
            return u * (15.0 + u2) / (15.0 + 6.0 * u2);
        }
        return Math.Tanh(u);
    }

    // Internal Shockley diode with parasitic series resistance Rs
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvalDiodeWithRs(double v, double satI, double invNVt, double nVt, double rs, out double id, out double gd)
    {
        double x = v * invNVt;
        if (x < -14.0)
        {
            id = -satI;
            gd = 0.0;
            return;
        }
        if (rs <= 1e-6 || x <= 0.0)
        {
            SafeExp(x, out double e, out double de);
            id = satI * (e - 1.0);
            gd = satI * invNVt * de;
            return;
        }
        if (x < 32.0)
        {
            double e0 = Math.Exp(x);
            double i0 = satI * (e0 - 1.0);
            if (rs * i0 < 1e-4)
            {
                double g0 = satI * invNVt * e0;
                double invDen = 1.0 / (1.0 + rs * g0);
                id = i0 * invDen;
                gd = g0 * invDen;
                return;
            }
        }

        double vd = Math.Min(v, nVt * Math.Log(1.0 + v / Math.Max(1e-18, rs * satI)));
        for (int it = 0; it < 2; it++)
        {
            SafeExp(vd * invNVt, out double e, out double de);
            double f = vd + rs * satI * (e - 1.0) - v;
            double df = 1.0 + rs * satI * invNVt * de;
            vd -= Math.Clamp(f / df, -0.35, 0.35);
        }
        SafeExp(vd * invNVt, out double ef, out double def);
        id = satI * (ef - 1.0);
        double gj = satI * invNVt * def;
        gd = gj / (1.0 + rs * gj);
    }

    // Smooth C1 SafeExp clamping exponent above x = 32 to avoid IEEE overflow in Newton steps
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SafeExp(double x, out double e, out double de)
    {
        if (x > 32.0)
        {
            double e32 = 7.896296018268069e13; // exp(32)
            e = e32 * (1.0 + (x - 32.0));
            de = e32;
        }
        else if (x < -40.0)
        {
            e = 0.0;
            de = 0.0;
        }
        else
        {
            e = Math.Exp(x);
            de = e;
        }
    }

    /// <summary>비-RT 분석 전용(.AC 복소 주파수 스윕): 현재 바이어스 기준 복소 어드미턴스 행렬로 복소 전달함수 계산.</summary>
    internal (double GainDb, double PhaseDeg) ComputeAcTransfer(double freqHz)
    {
        int n = _nodeCount;
        if (n == 0 || _outNode < 0 || _inNode < 0)
            return (-96.0, 0.0);

        var vSave = new double[n];
        Array.Copy(_vNodes, vSave, n);

        try
        {
            RebuildLinearCore(isDcOp: true, gmin: 1e-11);
            Array.Copy(_gBase, _jAcRe, n * n);
            Array.Clear(_jAcIm, 0, n * n);

            Array.Copy(_vOpDc, _vNodes, n);

            // Stamp all non-opamp small-signal Jacobians (Diode, BJT, JFET, MOSFET, Triode) ONCE
            if (_nNl > 0)
            {
                Array.Clear(_jNl, 0, _nNl * _nNl);
                Array.Clear(_fNl, 0, _nNl);
                EvaluateAndStampNonlinearDevices(isDcOp: true, dt: 1.0, aolScale: 1.0, includeOpAmp: false);
                for (int r = 0; r < _nNl; r++)
                    for (int c = 0; c < _nNl; c++)
                        _jAcRe[(_nLin + r) * n + (_nLin + c)] += _jNl[r * _nNl + c];
            }

            double omega = 2.0 * Math.PI * Math.Max(1.0, freqHz);

            void StampAcY(int a, int b, double gRe, double gIm)
            {
                if (a == b) return;
                if (a >= 0)
                {
                    _jAcRe[a * n + a] += gRe;
                    _jAcIm[a * n + a] += gIm;
                    if (b >= 0) { _jAcRe[a * n + b] -= gRe; _jAcIm[a * n + b] -= gIm; }
                }
                if (b >= 0)
                {
                    _jAcRe[b * n + b] += gRe;
                    _jAcIm[b * n + b] += gIm;
                    if (a >= 0) { _jAcRe[b * n + a] -= gRe; _jAcIm[b * n + a] -= gIm; }
                }
            }

            for (int i = 0; i < _caps.Length; i++)
                StampAcY(_caps[i].N0, _caps[i].N1, 0.0, omega * _caps[i].Val);
            for (int i = 0; i < _inds.Length; i++)
            {
                double rl = 1.0 / _inds[i].DCGeq;
                double wl = omega * _inds[i].Val;
                double denom = rl * rl + wl * wl;
                StampAcY(_inds[i].N0, _inds[i].N1, rl / denom, -wl / denom);
            }

            // Stamp complex frequency-dependent op-amp gain A(jw) = Aol_eff / (1 + j * w / wp)
            for (int eIdx = 0; eIdx < _nlElems.Length; eIdx++)
            {
                ref readonly var el = ref _nlElems[eIdx];
                if (el.Kind != SpiceDeviceKind.OpAmp) continue;

                double vd = GetNodeV(_vOpDc, el.N0) - GetNodeV(_vOpDc, el.N1);
                double vSpan = Math.Max(0.1, 0.5 * (el.P3 - el.P4));
                double tRail = Math.Tanh(Math.Clamp((el.P0 * vd) / vSpan, -20.0, 20.0));
                double slope = Math.Max(1e-5, 1.0 - tRail * tRail);

                double wOverWp = omega / Math.Max(1.0, el.P1);
                double aolEff = el.P0 * slope;
                double denom = 1.0 + wOverWp * wOverWp;
                double aRe = aolEff / denom;
                double aIm = -aolEff * wOverWp / denom;
                double gOut = el.P5;
                if (el.N2 >= 0)
                {
                    _jAcRe[el.N2 * n + el.N2] += gOut;
                    if (el.N0 >= 0) { _jAcRe[el.N2 * n + el.N0] -= gOut * aRe; _jAcIm[el.N2 * n + el.N0] -= gOut * aIm; }
                    if (el.N1 >= 0) { _jAcRe[el.N2 * n + el.N1] += gOut * aRe; _jAcIm[el.N2 * n + el.N1] += gOut * aIm; }
                }
            }

            var rhsRe = new double[n];
            var rhsIm = new double[n];
            rhsRe[_inNode] = _inG;
            if (!SolveComplexLinear(_jAcRe, _jAcIm, rhsRe, rhsIm, n))
                return (-96.0, 0.0);

            double vOutRe = rhsRe[_outNode], vOutIm = rhsIm[_outNode];
            double outMag = Math.Sqrt(vOutRe * vOutRe + vOutIm * vOutIm);
            double outAng = Math.Atan2(vOutIm, vOutRe) * (180.0 / Math.PI);
            double db = 20.0 * Math.Log10(Math.Max(1e-6, outMag));
            return (db, outAng);
        }
        finally
        {
            Array.Copy(vSave, _vNodes, n);
            RebuildLinearCore(isDcOp: false, gmin: 1e-11);
        }
    }

    private static void InvertDense(double[] src, int srcStride, double[] dstInv, int dim, int dstStride, double[] aug)
    {
        if (dim <= 0) return;
        int cols = 2 * dim;
        for (int r = 0; r < dim; r++)
        {
            for (int c = 0; c < dim; c++)
            {
                aug[r * cols + c] = src[r * srcStride + c];
                aug[r * cols + (dim + c)] = r == c ? 1.0 : 0.0;
            }
        }

        for (int p = 0; p < dim; p++)
        {
            int best = p;
            double bestVal = Math.Abs(aug[p * cols + p]);
            for (int r = p + 1; r < dim; r++)
            {
                double v = Math.Abs(aug[r * cols + p]);
                if (v > bestVal) { bestVal = v; best = r; }
            }
            if (best != p)
            {
                for (int c = p; c < cols; c++)
                    (aug[p * cols + c], aug[best * cols + c]) = (aug[best * cols + c], aug[p * cols + c]);
            }

            double diag = aug[p * cols + p];
            if (Math.Abs(diag) < 1e-18) diag = diag >= 0 ? 1e-18 : -1e-18;
            double invDiag = 1.0 / diag;
            for (int c = p; c < cols; c++) aug[p * cols + c] *= invDiag;

            for (int r = 0; r < dim; r++)
            {
                if (r == p) continue;
                double f = aug[r * cols + p];
                if (f == 0.0) continue;
                for (int c = p; c < cols; c++)
                    aug[r * cols + c] -= f * aug[p * cols + c];
            }
        }

        for (int r = 0; r < dim; r++)
            for (int c = 0; c < dim; c++)
                dstInv[r * dstStride + c] = aug[r * cols + (dim + c)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SolveLinearInPlace(double[] mat, double[] rhs, double[] x, int dim)
    {
        if (dim == 1)
        {
            double a00 = mat[0];
            if (Math.Abs(a00) < 1e-18) return false;
            x[0] = rhs[0] / a00;
            return true;
        }
        if (dim == 2)
        {
            double a00 = mat[0], a01 = mat[1], a10 = mat[2], a11 = mat[3];
            double det = a00 * a11 - a01 * a10;
            if (Math.Abs(det) > 1e-24)
            {
                double invDet = 1.0 / det;
                double b0 = rhs[0], b1 = rhs[1];
                x[0] = (a11 * b0 - a01 * b1) * invDet;
                x[1] = (a00 * b1 - a10 * b0) * invDet;
                return true;
            }
        }
        if (dim == 3)
        {
            double a00 = mat[0], a01 = mat[1], a02 = mat[2];
            double a10 = mat[3], a11 = mat[4], a12 = mat[5];
            double a20 = mat[6], a21 = mat[7], a22 = mat[8];
            double c00 = a11 * a22 - a12 * a21;
            double c01 = a12 * a20 - a10 * a22;
            double c02 = a10 * a21 - a11 * a20;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            if (Math.Abs(det) > 1e-24)
            {
                double invDet = 1.0 / det;
                double b0 = rhs[0], b1 = rhs[1], b2 = rhs[2];
                double c10 = a02 * a21 - a01 * a22;
                double c11 = a00 * a22 - a02 * a20;
                double c12 = a01 * a20 - a00 * a21;
                double c20 = a01 * a12 - a02 * a11;
                double c21 = a02 * a10 - a00 * a12;
                double c22 = a00 * a11 - a01 * a10;
                x[0] = (c00 * b0 + c10 * b1 + c20 * b2) * invDet;
                x[1] = (c01 * b0 + c11 * b1 + c21 * b2) * invDet;
                x[2] = (c02 * b0 + c12 * b1 + c22 * b2) * invDet;
                return true;
            }
        }

        for (int p = 0; p < dim; p++)
        {
            int best = p;
            double bestVal = Math.Abs(mat[p * dim + p]);
            for (int r = p + 1; r < dim; r++)
            {
                double v = Math.Abs(mat[r * dim + p]);
                if (v > bestVal) { bestVal = v; best = r; }
            }
            if (bestVal < 1e-18) return false;
            if (best != p)
            {
                for (int c = p; c < dim; c++)
                    (mat[p * dim + c], mat[best * dim + c]) = (mat[best * dim + c], mat[p * dim + c]);
                (rhs[p], rhs[best]) = (rhs[best], rhs[p]);
            }

            double invDiag = 1.0 / mat[p * dim + p];
            for (int r = p + 1; r < dim; r++)
            {
                double factor = mat[r * dim + p] * invDiag;
                if (factor == 0.0) continue;
                for (int c = p + 1; c < dim; c++)
                    mat[r * dim + c] -= factor * mat[p * dim + c];
                rhs[r] -= factor * rhs[p];
            }
        }

        for (int r = dim - 1; r >= 0; r--)
        {
            double sum = rhs[r];
            for (int c = r + 1; c < dim; c++)
                sum -= mat[r * dim + c] * x[c];
            x[r] = sum / mat[r * dim + r];
        }
        return true;
    }

    private static bool SolveComplexLinear(double[] aRe, double[] aIm, double[] bRe, double[] bIm, int dim)
    {
        var re = (double[])aRe.Clone();
        var im = (double[])aIm.Clone();
        for (int p = 0; p < dim; p++)
        {
            int best = p;
            double bestMag = 0.0;
            for (int r = p; r < dim; r++)
            {
                double mag = re[r * dim + p] * re[r * dim + p] + im[r * dim + p] * im[r * dim + p];
                if (mag > bestMag) { bestMag = mag; best = r; }
            }
            if (bestMag < 1e-32) return false;
            if (best != p)
            {
                for (int c = p; c < dim; c++)
                {
                    (re[p * dim + c], re[best * dim + c]) = (re[best * dim + c], re[p * dim + c]);
                    (im[p * dim + c], im[best * dim + c]) = (im[best * dim + c], im[p * dim + c]);
                }
                (bRe[p], bRe[best]) = (bRe[best], bRe[p]);
                (bIm[p], bIm[best]) = (bIm[best], bIm[p]);
            }

            double pr = re[p * dim + p], pi = im[p * dim + p];
            double pDen = pr * pr + pi * pi;
            for (int r = p + 1; r < dim; r++)
            {
                double xr = re[r * dim + p], xi = im[r * dim + p];
                double fRe = (xr * pr + xi * pi) / pDen;
                double fIm = (xi * pr - xr * pi) / pDen;
                if (fRe == 0.0 && fIm == 0.0) continue;
                for (int c = p + 1; c < dim; c++)
                {
                    double cr = re[p * dim + c], ci = im[p * dim + c];
                    re[r * dim + c] -= fRe * cr - fIm * ci;
                    im[r * dim + c] -= fRe * ci + fIm * cr;
                }
                double brp = bRe[p], bip = bIm[p];
                bRe[r] -= fRe * brp - fIm * bip;
                bIm[r] -= fRe * bip + fIm * brp;
            }
        }

        for (int r = dim - 1; r >= 0; r--)
        {
            double sRe = bRe[r], sIm = bIm[r];
            for (int c = r + 1; c < dim; c++)
            {
                double cr = re[r * dim + c], ci = im[r * dim + c];
                double xr = bRe[c], xi = bIm[c];
                sRe -= cr * xr - ci * xi;
                sIm -= cr * xi + ci * xr;
            }
            double dr = re[r * dim + r], di = im[r * dim + r];
            double dDen = dr * dr + di * di;
            bRe[r] = (sRe * dr + sIm * di) / dDen;
            bIm[r] = (sIm * dr - sRe * di) / dDen;
        }
        return true;
    }
}

/// <summary>
/// 다단 실시간 MNA SPICE 회로 실행기 (4x HIIR 도메인에서 0 B 힙 할당 실행).
/// </summary>
public sealed class RealtimeSpiceCircuit
{
    private readonly SpiceStageRuntime[] _stages;
    private readonly float _inScale;
    private readonly float _outScale;
    private readonly bool _dcBlock;
    private readonly float[] _knobs = new float[8];
    private readonly float _dcBlockA;
    private float _dcX1, _dcY1;

    internal IReadOnlyList<SpiceStageRuntime> Stages => _stages;

    internal void PrepareForAnalysis(ReadOnlySpan<float> knobs)
    {
        for (int i = 0; i < 8; i++)
            _knobs[i] = i < knobs.Length ? knobs[i] : _knobs[i];
        for (int s = 0; s < _stages.Length; s++)
            _stages[s].InitializeWithKnobs(_knobs);
        _dcX1 = 0f;
        _dcY1 = 0f;
    }

    private RealtimeSpiceCircuit(
        SpiceStageRuntime[] stages,
        float inScale,
        float outScale,
        bool dcBlock,
        int sampleRate4x,
        ReadOnlySpan<float> defaultKnobs)
    {
        _stages = stages;
        _inScale = inScale;
        _outScale = outScale;
        _dcBlock = dcBlock;
        _dcBlockA = (float)Math.Exp(-2.0 * Math.PI * 10.0 / Math.Max(8000, sampleRate4x));
        for (int i = 0; i < 8; i++)
            _knobs[i] = i < defaultKnobs.Length ? defaultKnobs[i] : 5f;
        for (int s = 0; s < _stages.Length; s++)
            _stages[s].InitializeWithKnobs(_knobs);
    }

    public void Reset()
    {
        for (int i = 0; i < _stages.Length; i++)
            _stages[i].ResetToOperatingPoint();
        _dcX1 = 0f;
        _dcY1 = 0f;
    }

    public void ProcessBlock4x(
        Span<float> buf4,
        float p0, float p1, float p2, float p3,
        float p4, float p5, float p6, float p7)
    {
        _knobs[0] = p0; _knobs[1] = p1; _knobs[2] = p2; _knobs[3] = p3;
        _knobs[4] = p4; _knobs[5] = p5; _knobs[6] = p6; _knobs[7] = p7;

        for (int s = 0; s < _stages.Length; s++)
            _stages[s].ApplyKnobsIfChanged(_knobs);

        float inScale = _inScale;
        float outScale = _outScale;
        bool dcBlock = _dcBlock;
        float dcA = _dcBlockA;
        int stageCount = _stages.Length;

        for (int i = 0; i < buf4.Length; i++)
        {
            double sig = buf4[i] * inScale;
            for (int s = 0; s < stageCount; s++)
                sig = _stages[s].ProcessSample(sig);

            float y = (float)sig * outScale;
            if (!float.IsFinite(y)) y = 0f;
            if (dcBlock)
            {
                float filtered = 0.5f * (1f + dcA) * (y - _dcX1) + dcA * _dcY1;
                _dcX1 = y;
                _dcY1 = filtered;
                y = filtered;
            }
            buf4[i] = Math.Clamp(y, -4.0f, 4.0f);
        }
    }

    public static SpiceCompileResult Compile(SpiceCircuitDto? dto, int sampleRate4x)
    {
        var errors = new List<string>();
        var missing = new List<string>();

        if (dto == null || dto.Stages == null || dto.Stages.Count == 0)
        {
            return new SpiceCompileResult
            {
                Ok = false,
                Errors = new[] { "SpiceCircuit에는 최소 1개 이상의 stage가 필요합니다." },
            };
        }

        const double Vt = 0.02585;
        var defaultKnobs = new float[8];
        var knobMin = new float[8];
        var knobMax = new float[8];
        for (int i = 0; i < 8; i++)
        {
            defaultKnobs[i] = 5f;
            knobMin[i] = 0f;
            knobMax[i] = 10f;
        }
        if (dto.Knobs != null)
        {
            foreach (var k in dto.Knobs)
            {
                if (k.Index is >= 0 and < 8)
                {
                    float lo = float.IsFinite(k.Min) ? k.Min : 0f;
                    float hi = float.IsFinite(k.Max) && k.Max > lo ? k.Max : (k.IsSwitch ? 1f : 10f);
                    knobMin[k.Index] = lo;
                    knobMax[k.Index] = hi;
                    defaultKnobs[k.Index] = float.IsFinite(k.Default) ? Math.Clamp(k.Default, lo, hi) : lo;
                }
            }
        }

        var compiledStages = new List<SpiceStageRuntime>(dto.Stages.Count);

        foreach (var st in dto.Stages)
        {
            if (st == null || st.Elements == null || st.Elements.Count == 0)
            {
                errors.Add($"Stage '{st?.Name ?? "(unnamed)"}'에 회로 소자(elements)가 없습니다.");
                continue;
            }

            var supplyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var supplyVolts = new List<double>();
            if (st.Supplies != null)
            {
                foreach (var sup in st.Supplies)
                {
                    if (string.IsNullOrWhiteSpace(sup.Node)) continue;
                    string sKey = sup.Node.Trim();
                    if (IsGroundName(sKey))
                    {
                        errors.Add($"Stage '{st.Name}': 접지 노드 '{sKey}'를 전원으로 지정할 수 없습니다.");
                        continue;
                    }
                    if (!supplyMap.ContainsKey(sKey))
                    {
                        supplyMap[sKey] = -(2 + supplyVolts.Count);
                        supplyVolts.Add(double.IsFinite(sup.Voltage) ? sup.Voltage : 9.0);
                    }
                }
            }

            string inName = string.IsNullOrWhiteSpace(st.InputNode) ? "in" : st.InputNode.Trim();
            string outName = string.IsNullOrWhiteSpace(st.OutputNode) ? "out" : st.OutputNode.Trim();
            if (IsGroundName(inName))
                errors.Add($"Stage '{st.Name}': InputNode는 접지일 수 없습니다.");
            if (IsGroundName(outName))
                errors.Add($"Stage '{st.Name}': OutputNode는 접지일 수 없습니다.");

            // Collect unknown node names and tag which are touched by nonlinear devices
            var allUnknownNodes = new List<string>();
            var nlNodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TrackNode(string? raw, bool isNonlinear)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                string name = raw.Trim();
                if (IsGroundName(name) || IsNcName(name) || supplyMap.ContainsKey(name)) return;
                if (!allUnknownNodes.Contains(name, StringComparer.OrdinalIgnoreCase))
                    allUnknownNodes.Add(name);
                if (isNonlinear)
                    nlNodeSet.Add(name);
            }

            TrackNode(inName, false);
            TrackNode(outName, false);

            var resistors = new List<SpiceLinearRes>();
            var caps = new List<SpiceReactiveElem>();
            var inds = new List<SpiceReactiveElem>();
            var pots = new List<SpicePotElem>();
            var switches = new List<SpiceSwitchElem>();
            var nlElems = new List<SpiceNonlinearElem>();

            // Pre-pass: gather nodes + validate .MODEL existence without any hallucinated fallback!
            foreach (var el in st.Elements)
            {
                if (el == null) continue;
                string t = (el.Type ?? "").Trim().ToUpperInvariant();
                bool isNl = t is "D" or "Q" or "J" or "M" or "U" or "T";
                if (el.Nodes != null)
                {
                    foreach (var nd in el.Nodes)
                        TrackNode(nd, isNl);
                }

                if (isNl)
                {
                    string mName = (el.Model ?? "").Trim();
                    if (string.IsNullOrEmpty(mName))
                    {
                        errors.Add($"Stage '{st.Name}', 소자 '{el.Name}' ({t}): '.MODEL' 이름(model)이 누락되었습니다.");
                    }
                    else if (!SpiceModelLibrary.TryGet(mName, out _, dto.InlineModels))
                    {
                        if (!missing.Contains(mName, StringComparer.OrdinalIgnoreCase))
                            missing.Add(mName);
                    }
                }
            }

            if (allUnknownNodes.Count > SpiceStageRuntime.MaxNodes)
            {
                errors.Add($"Stage '{st.Name}'의 미지 노드 수({allUnknownNodes.Count})가 최대 허용치({SpiceStageRuntime.MaxNodes})를 초과했습니다.");
                continue;
            }

            // Order linear nodes first [0 .. nLin-1], then nonlinear nodes [nLin .. N-1]
            var linNodes = allUnknownNodes.Where(n => !nlNodeSet.Contains(n)).ToList();
            var nlNodes = allUnknownNodes.Where(n => nlNodeSet.Contains(n)).ToList();
            int nLin = linNodes.Count;

            var nodeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < linNodes.Count; i++) nodeMap[linNodes[i]] = i;
            for (int i = 0; i < nlNodes.Count; i++) nodeMap[nlNodes[i]] = nLin + i;
            foreach (var kv in supplyMap) nodeMap[kv.Key] = kv.Value;

            var orderedNodeNames = new string[linNodes.Count + nlNodes.Count];
            for (int i = 0; i < linNodes.Count; i++) orderedNodeNames[i] = linNodes[i];
            for (int i = 0; i < nlNodes.Count; i++) orderedNodeNames[nLin + i] = nlNodes[i];

            int Resolve(string? raw, bool allowNc = false)
            {
                string s = (raw ?? "").Trim();
                if (IsGroundName(s)) return -1;
                if (allowNc && IsNcName(s)) return int.MinValue;
                if (nodeMap.TryGetValue(s, out int idx)) return idx;
                return -1;
            }

            double defaultPosRail = supplyVolts.Count > 0 ? supplyVolts.Max() : 9.0;
            double defaultNegRail = supplyVolts.Any(v => v < 0) ? supplyVolts.Min() : 0.0;

            foreach (var el in st.Elements)
            {
                if (el == null) continue;
                string t = (el.Type ?? "").Trim().ToUpperInvariant();
                var nd = el.Nodes ?? Array.Empty<string>();

                switch (t)
                {
                    case "R":
                    {
                        if (nd.Length < 2 || !SpiceValueParser.TryParse(el.Value, el.ValueText, out double rVal) || rVal <= 0)
                        {
                            errors.Add($"저항 '{el.Name}': 노드 2개와 양수 저항값(Value/ValueText)이 필요합니다.");
                            break;
                        }
                        resistors.Add(new SpiceLinearRes { N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), G = 1.0 / rVal });
                        break;
                    }
                    case "C":
                    {
                        if (nd.Length < 2 || !SpiceValueParser.TryParse(el.Value, el.ValueText, out double cVal) || cVal <= 0)
                        {
                            errors.Add($"커패시터 '{el.Name}': 노드 2개와 양수 정전용량이 필요합니다.");
                            break;
                        }
                        caps.Add(new SpiceReactiveElem { N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), Val = cVal });
                        break;
                    }
                    case "L":
                    {
                        if (nd.Length < 2 || !SpiceValueParser.TryParse(el.Value, el.ValueText, out double lVal) || lVal <= 0)
                        {
                            errors.Add($"인덕터 '{el.Name}': 노드 2개와 양수 인덕턴스가 필요합니다.");
                            break;
                        }
                        inds.Add(new SpiceReactiveElem { N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), Val = lVal });
                        break;
                    }
                    case "POT":
                    {
                        if (nd.Length < 3 || !SpiceValueParser.TryParse(el.Value, el.ValueText, out double rTot) || rTot <= 0)
                        {
                            errors.Add($"포텐셔미터 '{el.Name}': [CW, Wiper, CCW] 3개 노드와 총 저항값이 필요합니다.");
                            break;
                        }
                        byte taper = (el.Taper ?? "LIN").Trim().ToUpperInvariant() switch
                        {
                            "LOG" or "A" => (byte)1,
                            "RLOG" or "C" => (byte)2,
                            _ => (byte)0,
                        };
                        pots.Add(new SpicePotElem
                        {
                            Na = Resolve(nd[0], allowNc: true),
                            Nw = Resolve(nd[1], allowNc: false),
                            Nb = Resolve(nd[2], allowNc: true),
                            TotalR = rTot,
                            KnobIdx = (byte)Math.Clamp(el.KnobIndex, 0, 7),
                            Taper = taper,
                            Reversed = el.Reversed,
                        });
                        break;
                    }
                    case "SW":
                    {
                        if (nd.Length < 2)
                        {
                            errors.Add($"스위치 '{el.Name}': 노드 2개가 필요합니다.");
                            break;
                        }
                        switches.Add(new SpiceSwitchElem
                        {
                            N0 = Resolve(nd[0]),
                            N1 = Resolve(nd[1]),
                            KnobIdx = (byte)Math.Clamp(el.KnobIndex, 0, 7),
                            Threshold = el.Threshold,
                            ClosedG = 1.0 / Math.Max(1e-3, el.ClosedOhms),
                            OpenG = 1.0 / Math.Max(100.0, el.OpenOhms),
                            Inverted = el.Inverted,
                        });
                        break;
                    }
                    case "D":
                    {
                        if (nd.Length < 2) { errors.Add($"다이오드 '{el.Name}': [anode, cathode] 2개 노드가 필요합니다."); break; }
                        if (!SpiceModelLibrary.TryGet(el.Model, out var m, dto.InlineModels) || m == null) break;
                        double isVal = m.GetParamOrDefault("Is", 2.5e-9);
                        double nF = m.GetParamOrDefault("N", 1.75);
                        double rs = m.GetParamOrDefault("Rs", 0.0);
                        nlElems.Add(new SpiceNonlinearElem
                        {
                            Kind = SpiceDeviceKind.Diode,
                            N0 = Resolve(nd[0]), N1 = Resolve(nd[1]),
                            P0 = isVal, P1 = 1.0 / (nF * Vt), P2 = nF * Vt, P3 = rs,
                        });
                        break;
                    }
                    case "Q":
                    {
                        if (nd.Length < 3) { errors.Add($"BJT '{el.Name}': [C, B, E] 3개 노드가 필요합니다."); break; }
                        if (!SpiceModelLibrary.TryGet(el.Model, out var m, dto.InlineModels) || m == null) break;
                        nlElems.Add(new SpiceNonlinearElem
                        {
                            Kind = SpiceDeviceKind.Bjt,
                            Pol = m.Polarity == "P" ? (sbyte)-1 : (sbyte)1,
                            N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), N2 = Resolve(nd[2]),
                            P0 = m.GetParamOrDefault("Is", 1e-14),
                            P1 = 1.0 / Math.Max(1.0, m.GetParamOrDefault("Bf", 200.0)),
                            P2 = 1.0 / Math.Max(0.2, m.GetParamOrDefault("Br", 4.0)),
                            P3 = 1.0 / (m.GetParamOrDefault("Nf", 1.0) * Vt),
                            P4 = 1.0 / (m.GetParamOrDefault("Nr", 1.0) * Vt),
                            P5 = 1.0 / Math.Max(10.0, m.GetParamOrDefault("Vaf", 80.0)),
                        });
                        break;
                    }
                    case "J":
                    {
                        if (nd.Length < 3) { errors.Add($"JFET '{el.Name}': [D, G, S] 3개 노드가 필요합니다."); break; }
                        if (!SpiceModelLibrary.TryGet(el.Model, out var m, dto.InlineModels) || m == null) break;
                        nlElems.Add(new SpiceNonlinearElem
                        {
                            Kind = SpiceDeviceKind.Jfet,
                            Pol = m.Polarity == "P" ? (sbyte)-1 : (sbyte)1,
                            N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), N2 = Resolve(nd[2]),
                            P0 = m.GetParamOrDefault("Vto", -1.5),
                            P1 = m.GetParamOrDefault("Beta", 1.2e-3),
                            P2 = m.GetParamOrDefault("Lambda", 0.004),
                            P3 = m.GetParamOrDefault("Is", 1e-13),
                            P4 = 1.0 / (m.GetParamOrDefault("N", 1.0) * Vt),
                        });
                        break;
                    }
                    case "M":
                    {
                        if (nd.Length < 3) { errors.Add($"MOSFET '{el.Name}': [D, G, S] 3개 노드가 필요합니다."); break; }
                        if (!SpiceModelLibrary.TryGet(el.Model, out var m, dto.InlineModels) || m == null) break;
                        nlElems.Add(new SpiceNonlinearElem
                        {
                            Kind = SpiceDeviceKind.Mosfet,
                            Pol = m.Polarity == "P" ? (sbyte)-1 : (sbyte)1,
                            N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), N2 = Resolve(nd[2]),
                            P0 = m.GetParamOrDefault("Vto", 2.0),
                            P1 = m.GetParamOrDefault("Kp", 0.18),
                            P2 = m.GetParamOrDefault("Lambda", 0.015),
                            P3 = m.GetParamOrDefault("BodyDiodeIs", 2.5e-9),
                            P4 = 1.0 / (m.GetParamOrDefault("BodyDiodeN", 1.75) * Vt),
                        });
                        break;
                    }
                    case "U":
                    {
                        if (nd.Length < 3) { errors.Add($"Op-Amp '{el.Name}': [in+, in-, out] 3개 노드가 필요합니다."); break; }
                        if (!SpiceModelLibrary.TryGet(el.Model, out var m, dto.InlineModels) || m == null) break;
                        double aol = m.GetParamOrDefault("Aol", 100000.0);
                        double gbw = m.GetParamOrDefault("GbwHz", 3e6);
                        double srVs = m.GetParamOrDefault("SlewRateVus", 1.0) * 1.0e6;
                        double vPos = el.VposOverride ?? (defaultPosRail > 10.5 ? defaultPosRail - 1.4 : m.GetParamOrDefault("VrailPos", 7.6));
                        double vNeg = el.VnegOverride ?? (defaultNegRail < -0.5 ? defaultNegRail + 1.4 : m.GetParamOrDefault("VrailNeg", 1.4));
                        double rIn = m.GetParamOrDefault("Rin", 1e7);
                        double rOut = m.GetParamOrDefault("Rout", 75.0);

                        int nP = Resolve(nd[0]), nM = Resolve(nd[1]), nO = Resolve(nd[2]);
                        // Finite input resistance between in+ and in-
                        resistors.Add(new SpiceLinearRes { N0 = nP, N1 = nM, G = 1.0 / rIn });
                        nlElems.Add(new SpiceNonlinearElem
                        {
                            Kind = SpiceDeviceKind.OpAmp,
                            N0 = nP, N1 = nM, N2 = nO,
                            P0 = aol,
                            P1 = (2.0 * Math.PI * gbw) / aol,
                            P2 = srVs,
                            P3 = vPos,
                            P4 = vNeg,
                            P5 = 1.0 / rOut,
                        });
                        break;
                    }
                    case "T":
                    {
                        if (nd.Length < 3) { errors.Add($"Triode '{el.Name}': [P, G, K] 3개 노드가 필요합니다."); break; }
                        if (!SpiceModelLibrary.TryGet(el.Model, out var m, dto.InlineModels) || m == null) break;
                        nlElems.Add(new SpiceNonlinearElem
                        {
                            Kind = SpiceDeviceKind.Triode,
                            N0 = Resolve(nd[0]), N1 = Resolve(nd[1]), N2 = Resolve(nd[2]),
                            P0 = m.GetParamOrDefault("Mu", 100.0),
                            P1 = m.GetParamOrDefault("Ex", 1.4),
                            P2 = m.GetParamOrDefault("Kg1", 1060.0),
                            P3 = m.GetParamOrDefault("Kp", 600.0),
                            P4 = m.GetParamOrDefault("Kvb", 300.0),
                            P5 = m.GetParamOrDefault("RgGrid", 1000.0),
                            P6 = m.GetParamOrDefault("VgkClip", 0.0),
                        });
                        break;
                    }
                    default:
                        errors.Add($"알 수 없는 소자 타입 '{el.Type}' (소자 '{el.Name}'). 지원: R,C,L,POT,SW,D,Q,J,M,U,T");
                        break;
                }
            }

            if (errors.Count > 0 || missing.Count > 0)
                continue;

            compiledStages.Add(new SpiceStageRuntime(
                st.Name ?? "stage",
                sampleRate4x,
                linNodes.Count + nlNodes.Count,
                nLin,
                Resolve(inName),
                Resolve(outName),
                st.InputSeriesResistance,
                supplyVolts.ToArray(),
                orderedNodeNames,
                knobMin,
                knobMax,
                resistors.ToArray(),
                caps.ToArray(),
                inds.ToArray(),
                pots.ToArray(),
                switches.ToArray(),
                nlElems.ToArray()));
        }

        if (errors.Count > 0 || missing.Count > 0)
        {
            return new SpiceCompileResult
            {
                Ok = false,
                Errors = errors.ToArray(),
                MissingModels = missing.ToArray(),
            };
        }

        float inScale = float.IsFinite(dto.InputVoltageScale) && dto.InputVoltageScale > 0.001f
            ? dto.InputVoltageScale
            : 0.45f;
        float outScale = float.IsFinite(dto.OutputVoltageScale) && dto.OutputVoltageScale > 0.001f
            ? dto.OutputVoltageScale
            : (1.0f / inScale);

        return new SpiceCompileResult
        {
            Ok = true,
            Circuit = new RealtimeSpiceCircuit(
                compiledStages.ToArray(),
                inScale,
                outScale,
                dto.OutputDcBlock,
                sampleRate4x,
                defaultKnobs),
        };
    }

    private static bool IsGroundName(string s)
        => s.Equals("0", StringComparison.Ordinal) ||
           s.Equals("gnd", StringComparison.OrdinalIgnoreCase) ||
           s.Equals("ground", StringComparison.OrdinalIgnoreCase);

    private static bool IsNcName(string s)
        => s.Equals("nc", StringComparison.OrdinalIgnoreCase);
}
