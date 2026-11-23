using NAudio.Midi;
using NeuralRigStudio.Audio;

namespace NeuralRigStudio.Services;

public sealed record MidiLearnTarget(
    string TargetKind,
    int SlotIndex = 0,
    int ParamIndex = 0,
    int SceneIndex = 0);

public sealed record MidiEventMessage(
    bool IsProgramChange,
    int Channel,
    int Number,
    int Value);

/// <summary>
/// 외부 USB/표준 MIDI 컨트롤러 및 익스프레션 페달 입력 라우터.
/// WinMM 콜백 스레드에서 수신한 CC/PC 이벤트를 통해 Scene 전환, 탭 템포, 노브 연속 제어 및 MIDI Learn을 수행한다.
/// </summary>
public sealed class MidiRouter : IDisposable
{
    private readonly object _gate = new();
    private MidiIn? _midiIn;
    private string _activeDeviceName = "";
    private MidiLearnTarget? _learnTarget;

    private readonly long[] _tapTicks = new long[4];
    private int _tapCount;
    private int _lastTriggerCc = -1;
    private long _lastTriggerTick;

    public event Action<MidiBindingDto>? BindingLearned;
    public event Action<MidiEventMessage>? MidiReceived;
    public event Action<string>? Log;

    public string ActiveDeviceName
    {
        get { lock (_gate) return _activeDeviceName; }
    }

    public bool IsConnected
    {
        get { lock (_gate) return _midiIn != null; }
    }

    public MidiLearnTarget? CurrentLearnTarget
    {
        get { lock (_gate) return _learnTarget; }
    }

    public static string[] GetDeviceNames()
    {
        try
        {
            int count = MidiIn.NumberOfDevices;
            if (count <= 0) return Array.Empty<string>();
            var names = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string name = MidiIn.DeviceInfo(i).ProductName;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }
            return names.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public bool Connect(string? deviceName)
    {
        lock (_gate)
        {
            CloseInternal();
            if (string.IsNullOrWhiteSpace(deviceName))
                return false;

            int count = 0;
            try { count = MidiIn.NumberOfDevices; } catch { return false; }
            int targetIdx = -1;
            string resolvedName = "";

            for (int i = 0; i < count; i++)
            {
                string prod = MidiIn.DeviceInfo(i).ProductName?.Trim() ?? "";
                if (string.Equals(prod, deviceName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    targetIdx = i;
                    resolvedName = prod;
                    break;
                }
            }

            if (targetIdx < 0)
            {
                for (int i = 0; i < count; i++)
                {
                    string prod = MidiIn.DeviceInfo(i).ProductName?.Trim() ?? "";
                    if (prod.Contains(deviceName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        targetIdx = i;
                        resolvedName = prod;
                        break;
                    }
                }
            }

            if (targetIdx < 0)
                return false;

            try
            {
                var dev = new MidiIn(targetIdx);
                dev.MessageReceived += OnMidiMessageReceived;
                dev.ErrorReceived += (_, e) => Log?.Invoke($"[MIDI 경고] raw=0x{e.RawMessage:X8}");
                dev.Start();
                _midiIn = dev;
                _activeDeviceName = resolvedName;
                Log?.Invoke($"[MIDI] '{resolvedName}' 입력 포트 연결됨");
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[MIDI] '{resolvedName}' 열기 실패: {ex.Message}");
                CloseInternal();
                return false;
            }
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            if (_midiIn != null)
            {
                string prev = _activeDeviceName;
                CloseInternal();
                Log?.Invoke($"[MIDI] '{prev}' 연결 해제됨");
            }
            _learnTarget = null;
        }
    }

    public void StartLearn(MidiLearnTarget target)
    {
        lock (_gate)
        {
            _learnTarget = target;
        }
    }

    public void CancelLearn()
    {
        lock (_gate)
        {
            _learnTarget = null;
        }
    }

    public float? RegisterTapAndComputeBpm()
    {
        long now = Environment.TickCount64;
        if (_tapCount > 0 && now - _tapTicks[(_tapCount - 1) & 3] > 2200)
            _tapCount = 0;

        _tapTicks[_tapCount & 3] = now;
        _tapCount++;

        if (_tapCount < 2) return null;

        int samples = Math.Min(_tapCount, 4);
        long first = _tapTicks[(_tapCount - samples) & 3];
        long last = _tapTicks[(_tapCount - 1) & 3];
        double avgMs = (double)(last - first) / (samples - 1);
        if (avgMs < 210.0 || avgMs > 1600.0) return null;
        return MathF.Round((float)Math.Clamp(60000.0 / avgMs, 40.0, 280.0), 1);
    }

    public bool ShouldFireSwitchEdge(int cc, int value)
    {
        if (value < 64) return false;
        long now = Environment.TickCount64;
        if (_lastTriggerCc == cc && now - _lastTriggerTick < 140)
            return false;
        _lastTriggerCc = cc;
        _lastTriggerTick = now;
        return true;
    }

    private void OnMidiMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        int raw = e.RawMessage;
        int status = raw & 0xF0;
        int channel = (raw & 0x0F) + 1;
        int data1 = (raw >> 8) & 0x7F;
        int data2 = (raw >> 16) & 0x7F;

        if (status == 0xB0) // Control Change
        {
            MidiLearnTarget? learnedTarget = null;
            lock (_gate)
            {
                if (_learnTarget != null)
                {
                    learnedTarget = _learnTarget;
                    _learnTarget = null;
                }
            }

            if (learnedTarget != null)
            {
                var binding = new MidiBindingDto
                {
                    TargetKind = learnedTarget.TargetKind,
                    Cc = data1,
                    Channel = 0, // Omni by default for convenience
                    SlotIndex = learnedTarget.SlotIndex,
                    ParamIndex = learnedTarget.ParamIndex,
                    SceneIndex = learnedTarget.SceneIndex,
                };
                BindingLearned?.Invoke(binding);
                return;
            }

            MidiReceived?.Invoke(new MidiEventMessage(false, channel, data1, data2));
        }
        else if (status == 0xC0) // Program Change
        {
            MidiReceived?.Invoke(new MidiEventMessage(true, channel, data1, 0));
        }
    }

    private void CloseInternal()
    {
        var dev = _midiIn;
        _midiIn = null;
        _activeDeviceName = "";
        if (dev != null)
        {
            try { dev.Stop(); } catch { }
            try { dev.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _learnTarget = null;
            CloseInternal();
        }
    }
}
