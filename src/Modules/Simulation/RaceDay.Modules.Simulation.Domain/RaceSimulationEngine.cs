using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Domain;

/// <summary>
/// Deterministic race clock. One step is <see cref="StepMicros"/> (0.1s). Playback only scales how many
/// steps <see cref="AdvanceReal"/> consumes; the step sequence is independent of the scale.
/// Not thread-safe — the session book serializes access.
/// </summary>
public sealed class RaceSimulationEngine
{
    public const long StepMicros = 100_000;
    public const double PeriodSeconds = 30;
    public const long PeriodMicros = 30_000_000;
    public const double DefaultAmplitude = 0.10;
    public const double MaxSlewKmhPerSecond = 0.8;
    public const double MaxBaseKmh = 200_000;
    public const int MaxLog = 1000;

    private readonly int _seed;
    private readonly double _distanceKm;
    private readonly double _amplitude;
    private readonly List<SimulationCourseCheckpoint> _checkpoints;
    private readonly List<Athlete> _athletes;
    private readonly List<SimulationManualOp> _log = [];
    private long _sequence;
    private long _simMicros;
    private long _residualMicros;

    public RaceSimulationEngine(
        int seed,
        double distanceKm,
        IReadOnlyList<SimulationCoursePoint> polyline,
        IReadOnlyList<SimulationCourseCheckpoint> checkpoints,
        IReadOnlyList<SimulationAthleteSeed> athletes,
        double amplitude = DefaultAmplitude)
    {
        var error = SimulationCourseRules.Validate(distanceKm, polyline, checkpoints);
        if (error is not null)
        {
            throw new DomainException(error);
        }

        if (athletes is null || athletes.Count == 0)
        {
            throw new DomainException("Không có VĐV hợp lệ (confirmed, có BIB, chưa xuất phát, chưa có kết quả).");
        }

        if (amplitude < 0 || amplitude > 1 || !double.IsFinite(amplitude))
        {
            throw new DomainException("Biên độ tốc độ không hợp lệ.");
        }

        _seed = seed;
        _distanceKm = distanceKm;
        _amplitude = amplitude;
        _checkpoints = checkpoints.ToList();
        _athletes = athletes.Select(seedAthlete => new Athlete
        {
            RegistrationId = seedAthlete.RegistrationId,
            Bib = seedAthlete.Bib,
            FullName = seedAthlete.FullName,
            ChipId = seedAthlete.ChipId,
            BaseKmh = BaseKmh(seed, seedAthlete.RegistrationId)
        }).ToList();

        foreach (var athlete in _athletes)
        {
            athlete.CurrentKmh = TargetKmh(athlete, 0);
        }
    }

    public SimulationPhase Phase { get; private set; } = SimulationPhase.Ready;

    public double SimulatedSeconds => SimulationClockMath.MicrosToSeconds(_simMicros);

    public static double BaseKmh(int seed, Guid registrationId)
        => 8 + Unit(seed, registrationId, -1) * 6;

    public static string ChipFor(Guid registrationId)
        => "sim-" + registrationId.ToString("N")[..12];

    public void Start()
    {
        if (Phase != SimulationPhase.Ready)
        {
            throw new DomainException("Chỉ xuất phát được từ trạng thái sẵn sàng.");
        }

        Phase = SimulationPhase.Running;
        Log("START", "run");
        foreach (var athlete in _athletes)
        {
            if (athlete.Motion != AthleteMotion.Racing)
            {
                continue;
            }

            TryAccept(athlete, athlete.NextIndex, _simMicros, injected: false);
        }

        FinishIfComplete();
    }

    public void Pause()
    {
        if (Phase != SimulationPhase.Running)
        {
            throw new DomainException("Chỉ tạm dừng khi lượt đang chạy.");
        }

        Phase = SimulationPhase.Paused;
        Log("PAUSE", "run");
    }

    public void Resume()
    {
        if (Phase != SimulationPhase.Paused)
        {
            throw new DomainException("Chỉ tiếp tục khi lượt đang tạm dừng.");
        }

        Phase = SimulationPhase.Running;
        Log("RESUME", "run");
        FinishIfComplete();
    }

    /// <summary>
    /// Adds <paramref name="realSeconds"/> × <paramref name="rate"/> of simulated time.
    /// No partial step is applied. Returns false when paused, finished, or the residual does not fill a step.
    /// </summary>
    public bool AdvanceReal(double realSeconds, double rate)
    {
        if (realSeconds < 0 || rate < 0 || !double.IsFinite(realSeconds) || !double.IsFinite(rate))
        {
            throw new DomainException("Bước thời gian không hợp lệ.");
        }

        var micros = (long)Math.Round((decimal)realSeconds * (decimal)rate * 1_000_000m, MidpointRounding.AwayFromZero);
        return AdvanceMicros(micros);
    }

    public bool AdvanceMicros(long micros)
    {
        if (micros < 0)
        {
            throw new DomainException("Bước thời gian không hợp lệ.");
        }

        if (Phase != SimulationPhase.Running || micros == 0)
        {
            return false;
        }

        _residualMicros += micros;
        var stepped = false;
        while (_residualMicros >= StepMicros && Phase == SimulationPhase.Running)
        {
            StepOnce();
            _residualMicros -= StepMicros;
            stepped = true;
        }

        return stepped;
    }

    public void AdvanceSteps(int steps)
    {
        if (steps < 0)
        {
            throw new DomainException("Bước thời gian không hợp lệ.");
        }

        for (var i = 0; i < steps && Phase == SimulationPhase.Running; i++)
        {
            StepOnce();
        }
    }

    /// <summary>
    /// Sets an athlete's base speed. Current speed slews toward the new target unless <paramref name="immediate"/> is set.
    /// </summary>
    /// <param name="immediate">Test hook. The slider path slews and does not jump.</param>
    public void SetBaseSpeed(Guid registrationId, double kmh, bool immediate = false)
    {
        if (!double.IsFinite(kmh) || kmh < 0 || kmh > MaxBaseKmh)
        {
            throw new DomainException("Tốc độ cơ sở không hợp lệ.");
        }

        var athlete = Require(registrationId);
        athlete.BaseKmh = kmh;
        athlete.LastAutoFraudCheckpoint = null;
        if (immediate)
        {
            athlete.CurrentKmh = TargetKmh(athlete, PeriodIndex(_simMicros));
        }

        Log("SPEED", athlete.Bib + " " + kmh.ToString("0.###"));
    }

    public void MarkSos(Guid registrationId)
    {
        var athlete = Require(registrationId);
        athlete.Sos = true;
        Log("SOS", athlete.Bib);
    }

    public void MarkDnf(Guid registrationId)
    {
        var athlete = Require(registrationId);
        if (athlete.Motion == AthleteMotion.Dnf)
        {
            return;
        }

        athlete.Motion = AthleteMotion.Dnf;
        Log("DNF", athlete.Bib);
        FinishIfComplete();
    }

    public void AssignChip(Guid registrationId, string chipId)
    {
        if (string.IsNullOrWhiteSpace(chipId))
        {
            throw new DomainException("Thiếu chip.");
        }

        var athlete = Require(registrationId);
        athlete.ChipId = chipId.Trim();
        athlete.LastAutoFraudCheckpoint = null;
        Log("CHIP", athlete.Bib + " " + athlete.ChipId);
    }

    public void Note(string kind, string subject) => Log(kind, subject);

    public bool InjectPassage(Guid registrationId, string checkpointId)
    {
        var athlete = Require(registrationId);
        var index = _checkpoints.FindIndex(x => x.Id == checkpointId);
        if (index < 0)
        {
            Log("FRAUD", athlete.Bib + " checkpoint không tồn tại");
            return false;
        }

        var accepted = TryAccept(athlete, index, _simMicros, injected: true);
        if (accepted)
        {
            Log("PASSAGE", athlete.Bib + " " + checkpointId);
        }

        return accepted;
    }

    public SimulationEngineView Capture()
    {
        return new SimulationEngineView
        {
            Phase = Phase,
            SimulatedSeconds = SimulatedSeconds,
            SimulatedMicros = _simMicros,
            ResidualMicros = _residualMicros,
            Athletes = _athletes.Select(athlete => new SimulationAthleteView
            {
                RegistrationId = athlete.RegistrationId,
                Bib = athlete.Bib,
                FullName = athlete.FullName,
                ChipId = athlete.ChipId,
                BaseKmh = athlete.BaseKmh,
                SpeedKmh = athlete.CurrentKmh,
                CourseKm = athlete.CourseKm,
                Motion = athlete.Motion,
                Sos = athlete.Sos,
                ValidFinish = athlete.ValidFinish,
                FinishSimSeconds = athlete.FinishSimMicros is long micros
                    ? SimulationClockMath.MicrosToSeconds(micros)
                    : null,
                Splits = athlete.Splits.ToList()
            }).ToList(),
            Log = _log.ToList()
        };
    }

    private void StepOnce()
    {
        var t0 = _simMicros;
        foreach (var athlete in _athletes)
        {
            if (athlete.Motion != AthleteMotion.Racing)
            {
                continue;
            }

            var speed = athlete.CurrentKmh;
            if (!double.IsFinite(speed) || speed < 0)
            {
                speed = 0;
            }

            var deltaKm = speed <= 0 ? 0 : speed * StepMicros / 1_000_000d / 3600d;
            var startKm = athlete.CourseKm;
            var unclamped = startKm + deltaKm;
            if (deltaKm > 0)
            {
                while (athlete.NextIndex < _checkpoints.Count)
                {
                    var checkpoint = _checkpoints[athlete.NextIndex];
                    if (checkpoint.Km > unclamped + 1e-9)
                    {
                        break;
                    }

                    long cross;
                    if (checkpoint.Km <= startKm + 1e-9)
                    {
                        cross = t0;
                    }
                    else
                    {
                        var fraction = (checkpoint.Km - startKm) / deltaKm;
                        if (fraction < 0)
                        {
                            fraction = 0;
                        }
                        else if (fraction > 1)
                        {
                            fraction = 1;
                        }

                        cross = t0 + (long)Math.Round(fraction * StepMicros, MidpointRounding.AwayFromZero);
                    }

                    if (!TryAccept(athlete, athlete.NextIndex, cross, injected: false))
                    {
                        break;
                    }
                }
            }

            athlete.CourseKm = unclamped > _distanceKm ? _distanceKm : unclamped;
            if (athlete.CourseKm >= _distanceKm - 1e-9)
            {
                athlete.CourseKm = _distanceKm;
                athlete.Motion = AthleteMotion.Finished;
            }
        }

        _simMicros = t0 + StepMicros;
        var period = PeriodIndex(_simMicros);
        foreach (var athlete in _athletes)
        {
            if (athlete.Motion != AthleteMotion.Racing)
            {
                continue;
            }

            Slew(athlete, period);
        }

        FinishIfComplete();
    }

    private void Slew(Athlete athlete, long period)
    {
        var target = TargetKmh(athlete, period);
        var delta = target - athlete.CurrentKmh;
        var max = MaxSlewKmhPerSecond * (StepMicros / 1_000_000d);
        if (Math.Abs(delta) <= max)
        {
            athlete.CurrentKmh = target;
        }
        else
        {
            athlete.CurrentKmh += Math.Sign(delta) * max;
        }
    }

    private bool TryAccept(Athlete athlete, int index, long simMicros, bool injected)
    {
        if (index < 0 || index >= _checkpoints.Count)
        {
            return false;
        }

        var checkpoint = _checkpoints[index];
        if (Owners(athlete.ChipId) != 1)
        {
            LogFraudOnce(athlete, checkpoint.Id, injected, athlete.Bib + " chip không xác định duy nhất");
            return false;
        }

        if (index != athlete.NextIndex)
        {
            LogFraudOnce(athlete, checkpoint.Id, injected, athlete.Bib + " " + checkpoint.Id + " sai thứ tự");
            return false;
        }

        if (athlete.Splits.Any(split => split.CheckpointId == checkpoint.Id))
        {
            if (injected)
            {
                Log("FRAUD", athlete.Bib + " " + checkpoint.Id + " đã ghi");
            }

            return false;
        }

        athlete.Splits.Add(new SimulationSplit(checkpoint.Id, checkpoint.Name, checkpoint.Km, simMicros));
        athlete.NextIndex++;
        athlete.LastAutoFraudCheckpoint = null;
        if (checkpoint.Type.Equals("finish", StringComparison.OrdinalIgnoreCase))
        {
            athlete.ValidFinish = true;
            athlete.FinishSimMicros = simMicros;
        }

        return true;
    }

    private void LogFraudOnce(Athlete athlete, string checkpointId, bool injected, string subject)
    {
        if (!injected && athlete.LastAutoFraudCheckpoint == checkpointId)
        {
            return;
        }

        athlete.LastAutoFraudCheckpoint = checkpointId;
        Log("FRAUD", subject);
    }

    private int Owners(string chipId)
        => _athletes.Count(athlete => string.Equals(athlete.ChipId, chipId, StringComparison.Ordinal));

    private double TargetKmh(Athlete athlete, long period)
    {
        if (athlete.BaseKmh <= 0)
        {
            return 0;
        }

        var unit = Unit(_seed, athlete.RegistrationId, period);
        return athlete.BaseKmh * (1 + _amplitude * (unit * 2 - 1));
    }

    private static long PeriodIndex(long simMicros)
        => simMicros < 0 ? 0 : simMicros / PeriodMicros;

    private Athlete Require(Guid registrationId)
        => _athletes.FirstOrDefault(athlete => athlete.RegistrationId == registrationId)
           ?? throw new DomainException("Không có VĐV này trong lượt giả lập.");

    private void FinishIfComplete()
    {
        if (_athletes.Count > 0 && _athletes.All(athlete =>
                athlete.Motion is AthleteMotion.Finished or AthleteMotion.Dnf))
        {
            Phase = SimulationPhase.Finished;
        }
    }

    private void Log(string kind, string subject)
    {
        _sequence++;
        if (_log.Count >= MaxLog)
        {
            _log.RemoveAt(0);
        }

        _log.Add(new SimulationManualOp(_sequence, SimulatedSeconds, kind, subject));
    }

    internal static double Unit(int seed, Guid registrationId, long salt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}|{registrationId:N}|{salt}"));
        var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        return value / (double)ulong.MaxValue;
    }

    private sealed class Athlete
    {
        public Guid RegistrationId { get; init; }
        public string Bib { get; init; } = "";
        public string FullName { get; init; } = "";
        public string ChipId { get; set; } = "";
        public double BaseKmh { get; set; }
        public double CurrentKmh { get; set; }
        public double CourseKm { get; set; }
        public AthleteMotion Motion { get; set; } = AthleteMotion.Racing;
        public bool Sos { get; set; }
        public bool ValidFinish { get; set; }
        public long? FinishSimMicros { get; set; }
        public int NextIndex { get; set; }
        public string? LastAutoFraudCheckpoint { get; set; }
        public List<SimulationSplit> Splits { get; } = [];
    }
}
