using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

/// <summary>
/// One in-memory run per event and distance. Draft state dies with the process.
/// </summary>
public sealed class SimulationSessionBook
{
    public const int MaxAthletes = 2000;
    public const double InitialPlaybackRate = 1;
    private static readonly double[] PlaybackRates = [1, 5, 20, 60];

    private readonly object _slots = new();
    private readonly Dictionary<(Guid EventId, Guid DistanceId), Session> _bySlot = new();
    private readonly Dictionary<Guid, Session> _byRun = new();
    private readonly ISimulationFieldSource _fields;
    private readonly ISimulationCommitGateway _commits;
    private readonly ISimulationBroadcaster _broadcaster;

    public SimulationSessionBook(
        ISimulationFieldSource fields,
        ISimulationCommitGateway commits,
        ISimulationBroadcaster broadcaster)
    {
        _fields = fields;
        _commits = commits;
        _broadcaster = broadcaster;
    }

    public async Task<SimulationPreviewDto> PreviewAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        var field = await _fields.LoadAsync(eventId, distanceId, cancellationToken);
        return ToPreview(field, ActiveRunId(eventId, distanceId));
    }

    /// <summary>
    /// Current run for the pair, including a finished run. Uses the same locks as Reset and New Run.
    /// </summary>
    private Guid? ActiveRunId(Guid eventId, Guid distanceId)
    {
        lock (_slots)
        {
            if (!_bySlot.TryGetValue((eventId, distanceId), out var session))
            {
                return null;
            }

            lock (session.Gate)
            {
                return session.RunId;
            }
        }
    }

    public async Task<SimulationSnapshotDto> CreateAsync(
        Guid eventId,
        Guid distanceId,
        int? runnerCount,
        int? seed,
        CancellationToken cancellationToken = default)
    {
        var field = await _fields.LoadAsync(eventId, distanceId, cancellationToken);
        SimulationSnapshotDto snapshot;
        var created = false;
        lock (_slots)
        {
            if (_bySlot.TryGetValue((eventId, distanceId), out var existing))
            {
                lock (existing.Gate)
                {
                    return Build(existing);
                }
            }

            var selected = Select(field, runnerCount);
            var session = Session.Create(field, selected, seed ?? Random.Shared.Next(1, int.MaxValue));
            _bySlot[(eventId, distanceId)] = session;
            _byRun[session.RunId] = session;
            created = true;
            lock (session.Gate)
            {
                snapshot = Build(session);
            }
        }

        if (created)
        {
            await _broadcaster.PublishAsync(snapshot, cancellationToken);
        }

        return snapshot;
    }

    public Guid? EventIdOf(Guid runId)
    {
        lock (_slots)
        {
            return _byRun.TryGetValue(runId, out var session) ? session.EventId : null;
        }
    }

    public SimulationSnapshotDto? SnapshotOf(Guid runId)
    {
        lock (_slots)
        {
            if (!_byRun.TryGetValue(runId, out var session))
            {
                return null;
            }

            lock (session.Gate)
            {
                return Build(session);
            }
        }
    }

    public Task<SimulationSnapshotDto> StartAsync(Guid runId, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session => session.Engine.Start(), cancellationToken);

    public Task<SimulationSnapshotDto> PauseAsync(Guid runId, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session => session.Engine.Pause(), cancellationToken);

    public Task<SimulationSnapshotDto> ResumeAsync(Guid runId, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session => session.Engine.Resume(), cancellationToken);

    public Task<SimulationSnapshotDto> SetPlaybackAsync(Guid runId, double rate, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session =>
        {
            if (!PlaybackRates.Any(allowed => Math.Abs(allowed - rate) < 1e-9))
            {
                throw new DomainException("Tốc độ phát chỉ được 1, 5, 20 hoặc 60.");
            }

            session.PlaybackRate = rate;
            session.Engine.Note("PLAYBACK", rate.ToString("0"));
        }, cancellationToken);

    public Task<SimulationSnapshotDto> SetBaseSpeedAsync(
        Guid runId,
        Guid registrationId,
        double kmh,
        CancellationToken cancellationToken = default)
        => MutateAsync(runId, session =>
        {
            if (!double.IsFinite(kmh) || kmh < 0 || kmh > 40)
            {
                throw new DomainException("Tốc độ cơ sở phải từ 0 đến 40 km/h.");
            }

            session.Engine.SetBaseSpeed(registrationId, kmh);
        }, cancellationToken);

    public Task<SimulationSnapshotDto> SosAsync(Guid runId, Guid registrationId, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session => session.Engine.MarkSos(registrationId), cancellationToken);

    public Task<SimulationSnapshotDto> DnfAsync(Guid runId, Guid registrationId, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session => session.Engine.MarkDnf(registrationId), cancellationToken);

    public Task<SimulationSnapshotDto> AssignChipAsync(
        Guid runId,
        Guid registrationId,
        string chipId,
        CancellationToken cancellationToken = default)
        => MutateAsync(runId, session => session.Engine.AssignChip(registrationId, chipId), cancellationToken);

    public Task<SimulationSnapshotDto> InjectPassageAsync(
        Guid runId,
        Guid registrationId,
        string checkpointId,
        CancellationToken cancellationToken = default)
        => MutateAsync(runId, session =>
        {
            if (string.IsNullOrWhiteSpace(checkpointId))
            {
                throw new DomainException("Thiếu checkpoint.");
            }

            session.Engine.InjectPassage(registrationId, checkpointId);
        }, cancellationToken);

    public async Task<SimulationSnapshotDto> ResetAsync(
        Guid runId,
        bool confirmDiscard,
        CancellationToken cancellationToken = default)
    {
        var session = Find(runId);
        if (!await session.CommitLock.WaitAsync(0, cancellationToken))
        {
            throw new SimulationBusyException();
        }

        try
        {
            SimulationSnapshotDto snapshot;
            SimulationRunReplacedDto replaced;
            lock (_slots)
            {
                lock (session.Gate)
                {
                    if (session.RunId != runId)
                    {
                        throw new DomainException("Không tìm thấy lượt giả lập.");
                    }

                    EnsureCanDiscard(session, confirmDiscard);
                    var previousId = session.RunId;
                    var previousGeneration = session.Generation;
                    session.ReplaceEngine(session.Seed, session.Athletes, session.DistanceKm, session.Checkpoints);
                    session.PlaybackRate = InitialPlaybackRate;
                    session.Committed.Clear();
                    session.Unresolved.Clear();
                    session.CommitState = "Idle";
                    _byRun.Remove(previousId);
                    _byRun[session.RunId] = session;
                    snapshot = Build(session);
                    replaced = new SimulationRunReplacedDto(
                        previousId, previousGeneration, session.RunId, session.Generation, session.Version);
                }
            }

            await _broadcaster.PublishReplacedAsync(replaced, cancellationToken);
            await _broadcaster.PublishAsync(snapshot, cancellationToken);
            return snapshot;
        }
        finally
        {
            session.CommitLock.Release();
        }
    }

    public async Task<SimulationSnapshotDto> NewRunAsync(
        Guid runId,
        bool confirmDiscard,
        int? seed,
        int? runnerCount,
        CancellationToken cancellationToken = default)
    {
        var session = Find(runId);
        if (!await session.CommitLock.WaitAsync(0, cancellationToken))
        {
            throw new SimulationBusyException();
        }

        try
        {
            lock (session.Gate)
            {
                if (session.RunId != runId)
                {
                    throw new DomainException("Không tìm thấy lượt giả lập.");
                }

                EnsureCanDiscard(session, confirmDiscard);
            }

            var field = await _fields.LoadAsync(session.EventId, session.DistanceId, cancellationToken);
            SimulationSnapshotDto snapshot;
            SimulationRunReplacedDto replaced;
            lock (_slots)
            {
                lock (session.Gate)
                {
                    if (session.RunId != runId)
                    {
                        throw new DomainException("Không tìm thấy lượt giả lập.");
                    }

                    EnsureCanDiscard(session, confirmDiscard);
                    var selected = Select(field, runnerCount);

                    var previousId = session.RunId;
                    var previousGeneration = session.Generation;
                    session.Title = field.Title;
                    session.DistanceKm = field.DistanceKm;
                    session.Polyline = field.Polyline.ToList();
                    session.Checkpoints = field.Checkpoints.ToList();
                    session.ReplaceEngine(seed ?? Random.Shared.Next(1, int.MaxValue), selected, field.DistanceKm, field.Checkpoints);
                    session.PlaybackRate = InitialPlaybackRate;
                    session.Committed.Clear();
                    session.Unresolved.Clear();
                    session.CommitState = "Idle";
                    _byRun.Remove(previousId);
                    _byRun[session.RunId] = session;
                    snapshot = Build(session);
                    replaced = new SimulationRunReplacedDto(
                        previousId, previousGeneration, session.RunId, session.Generation, session.Version);
                }
            }

            await _broadcaster.PublishReplacedAsync(replaced, cancellationToken);
            await _broadcaster.PublishAsync(snapshot, cancellationToken);
            return snapshot;
        }
        finally
        {
            session.CommitLock.Release();
        }
    }

    public async Task<SimulationCommitResponseDto> CommitAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var session = Find(runId);
        await session.CommitLock.WaitAsync(cancellationToken);
        try
        {
            List<SimulationFinishDraft> drafts;
            List<Guid> alreadyCommitted;
            List<Guid> unresolved;
            string previousState;
            SimulationSnapshotDto committing;
            lock (session.Gate)
            {
                if (session.RunId != runId)
                {
                    throw new DomainException("Không tìm thấy lượt giả lập.");
                }

                var view = session.Engine.Capture();
                if (view.Phase != SimulationPhase.Finished)
                {
                    throw new DomainException("Chỉ ghi nhận kết quả khi lượt giả lập đã kết thúc.");
                }

                drafts = view.Athletes
                    .Where(athlete => athlete.ValidFinish
                                      && athlete.Motion != AthleteMotion.Dnf
                                      && athlete.FinishSimSeconds is not null
                                      && !session.Committed.Contains(athlete.RegistrationId))
                    .Select(athlete => new SimulationFinishDraft(
                        athlete.RegistrationId,
                        athlete.FinishSimSeconds!.Value,
                        athlete.Bib))
                    .ToList();
                if (drafts.Count == 0)
                {
                    session.CommitState = ComputeCommitState(session, view);
                    return new SimulationCommitResponseDto(true, null, CommittedItems(session, view), Build(session));
                }

                previousState = session.CommitState;
                session.CommitState = "Committing";
                session.Version++;
                committing = Build(session);
                alreadyCommitted = session.Committed.ToList();
                unresolved = session.Unresolved.ToList();
            }

            await PublishCommittingAsync(session, previousState, committing, cancellationToken);
            SimulationCommitBatch batch;
            try
            {
                batch = await _commits.CommitAsync(
                    new SimulationCommitCommand(
                        session.EventId, session.DistanceId, drafts, alreadyCommitted, unresolved),
                    cancellationToken);
            }
            catch
            {
                SimulationSnapshotDto failed;
                lock (session.Gate)
                {
                    session.CommitState = previousState;
                    session.Version++;
                    failed = Build(session);
                }

                await _broadcaster.PublishAsync(failed, cancellationToken);
                throw;
            }

            SimulationSnapshotDto snapshot;
            List<SimulationCommitItemDto> items;
            lock (session.Gate)
            {
                var view = session.Engine.Capture();
                if (batch.Accepted)
                {
                    foreach (var item in batch.Items)
                    {
                        if (item.Outcome == SimulationCommitOutcomes.CommittedThisRun)
                        {
                            session.Committed.Add(item.RegistrationId);
                            session.Unresolved.Remove(item.RegistrationId);
                        }
                        else if (item.Outcome == SimulationCommitOutcomes.Unknown)
                        {
                            session.Unresolved.Add(item.RegistrationId);
                        }
                        else
                        {
                            session.Unresolved.Remove(item.RegistrationId);
                        }
                    }

                    session.CommitState = ComputeCommitState(session, view);
                }
                else
                {
                    session.CommitState = previousState;
                }

                session.Version++;
                snapshot = Build(session);
                var already = alreadyCommitted.ToHashSet();
                items = view.Athletes
                    .Where(athlete => athlete.ValidFinish
                                      && athlete.Motion != AthleteMotion.Dnf
                                      && already.Contains(athlete.RegistrationId))
                    .Select(athlete => new SimulationCommitItemDto(
                        athlete.RegistrationId,
                        SimulationCommitOutcomes.CommittedThisRun,
                        null,
                        athlete.FinishSimSeconds is double seconds ? SimulationClockMath.RoundSeconds(seconds) : null))
                    .ToList();
                if (batch.Accepted)
                {
                    items.AddRange(batch.Items.Select(item =>
                        new SimulationCommitItemDto(item.RegistrationId, item.Outcome, item.Reason, item.ResultSeconds)));
                }
            }

            await _broadcaster.PublishAsync(snapshot, cancellationToken);
            return new SimulationCommitResponseDto(batch.Accepted, batch.RefusalReason, items, snapshot);
        }
        finally
        {
            session.CommitLock.Release();
        }
    }

    public async Task AdvanceRunningAsync(double realSeconds, CancellationToken cancellationToken = default)
    {
        List<Session> sessions;
        lock (_slots)
        {
            sessions = _bySlot.Values.ToList();
        }

        foreach (var session in sessions)
        {
            SimulationSnapshotDto? snapshot = null;
            lock (session.Gate)
            {
                if (session.Engine.Phase != SimulationPhase.Running)
                {
                    continue;
                }

                if (!session.Engine.AdvanceReal(realSeconds, session.PlaybackRate))
                {
                    continue;
                }

                session.Version++;
                snapshot = Build(session);
            }

            if (snapshot is not null)
            {
                await _broadcaster.PublishAsync(snapshot, cancellationToken);
            }
        }
    }

    private async Task PublishCommittingAsync(
        Session session,
        string previousState,
        SimulationSnapshotDto committing,
        CancellationToken cancellationToken)
    {
        try
        {
            await _broadcaster.PublishAsync(committing, cancellationToken);
        }
        catch (Exception)
        {
            SimulationSnapshotDto restored;
            lock (session.Gate)
            {
                if (session.CommitState == "Committing")
                {
                    session.CommitState = previousState;
                    session.Version++;
                }

                restored = Build(session);
            }

            try
            {
                await _broadcaster.PublishAsync(restored, CancellationToken.None);
            }
            catch (Exception)
            {
                // The run is already out of Committing. Keep the original broadcast error.
            }

            throw;
        }
    }

    private async Task<SimulationSnapshotDto> MutateAsync(
        Guid runId,
        Action<Session> mutate,
        CancellationToken cancellationToken)
    {
        var session = Find(runId);
        SimulationSnapshotDto snapshot;
        lock (session.Gate)
        {
            if (session.RunId != runId)
            {
                throw new DomainException("Không tìm thấy lượt giả lập.");
            }

            mutate(session);
            session.Version++;
            snapshot = Build(session);
        }

        await _broadcaster.PublishAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private Session Find(Guid runId)
    {
        lock (_slots)
        {
            if (_byRun.TryGetValue(runId, out var session))
            {
                return session;
            }
        }

        throw new DomainException("Không tìm thấy lượt giả lập.");
    }

    private static void EnsureCanDiscard(Session session, bool confirmDiscard)
    {
        var view = session.Engine.Capture();
        if (view.Phase == SimulationPhase.Finished
            && view.Athletes.Any(athlete => athlete.ValidFinish
                                            && athlete.Motion != AthleteMotion.Dnf
                                            && !session.Committed.Contains(athlete.RegistrationId))
            && !confirmDiscard)
        {
            throw new DomainException("Lượt đã có kết quả nháp chưa ghi nhận. Gửi confirmDiscard=true để bỏ lượt này.");
        }
    }

    private static IReadOnlyList<SimulationAthleteSeed> Select(SimulationField field, int? runnerCount)
    {
        if (field.CourseError is not null)
        {
            throw new DomainException(field.CourseError);
        }

        var eligible = field.Eligible.OrderBy(athlete => athlete.RegistrationId).ToList();
        if (eligible.Count == 0)
        {
            throw new DomainException("Không có VĐV hợp lệ (confirmed, có BIB, chưa xuất phát, chưa có kết quả).");
        }

        var count = runnerCount ?? eligible.Count;
        if (count <= 0)
        {
            throw new DomainException("Số VĐV phải lớn hơn 0.");
        }

        if (count > MaxAthletes)
        {
            throw new DomainException($"Tối đa {MaxAthletes} VĐV mỗi lượt.");
        }

        if (count > eligible.Count)
        {
            throw new DomainException($"Số VĐV yêu cầu ({count}) lớn hơn số đăng ký hợp lệ ({eligible.Count}).");
        }

        return eligible.Take(count).ToList();
    }

    private static string ComputeCommitState(Session session, SimulationEngineView view)
    {
        var valid = view.Athletes
            .Where(athlete => athlete.ValidFinish && athlete.Motion != AthleteMotion.Dnf)
            .Select(athlete => athlete.RegistrationId)
            .ToList();
        if (valid.Count == 0)
        {
            return "Idle";
        }

        var committed = valid.Count(id => session.Committed.Contains(id));
        if (committed == 0)
        {
            return "Idle";
        }

        return committed == valid.Count ? "Committed" : "Partial";
    }

    private static List<SimulationCommitItemDto> CommittedItems(Session session, SimulationEngineView view)
    {
        return view.Athletes
            .Where(athlete => athlete.ValidFinish
                              && athlete.Motion != AthleteMotion.Dnf
                              && session.Committed.Contains(athlete.RegistrationId))
            .Select(athlete => new SimulationCommitItemDto(
                athlete.RegistrationId,
                SimulationCommitOutcomes.CommittedThisRun,
                null,
                athlete.FinishSimSeconds is double seconds ? SimulationClockMath.RoundSeconds(seconds) : null))
            .ToList();
    }

    private static SimulationPreviewDto ToPreview(SimulationField field, Guid? activeRunId)
        => new(
            field.EventId,
            field.DistanceId,
            field.Title,
            field.DistanceKm,
            field.Eligible.Count,
            field.CourseError is null,
            field.CourseError,
            field.Polyline.Select(point => new SimulationPointDto(point.Lat, point.Lng)).ToList(),
            field.Checkpoints.Select(ToCheckpoint).ToList(),
            activeRunId);

    private static SimulationSnapshotDto Build(Session session)
    {
        var view = session.Engine.Capture();
        var athletes = view.Athletes.Select(athlete => new SimulationAthleteDto(
            athlete.RegistrationId,
            athlete.Bib,
            athlete.FullName,
            athlete.ChipId,
            athlete.BaseKmh,
            athlete.SpeedKmh,
            athlete.CourseKm,
            athlete.Motion.ToString(),
            athlete.Sos,
            athlete.ValidFinish && athlete.Motion != AthleteMotion.Dnf,
            athlete.ValidFinish && athlete.Motion != AthleteMotion.Dnf && athlete.FinishSimSeconds is double seconds
                ? SimulationClockMath.RoundSeconds(seconds)
                : null,
            athlete.FinishSimSeconds,
            athlete.Splits.Select(split => new SimulationSplitDto(
                split.CheckpointId,
                split.Name,
                split.Km,
                SimulationClockMath.MicrosToSeconds(split.SimMicros))).ToList())).ToList();

        var ranking = athletes
            .Where(athlete => athlete.ValidFinish && athlete.FinishSimSeconds is not null && athlete.ResultSeconds is not null)
            .OrderBy(athlete => athlete.ResultSeconds)
            .ThenBy(athlete => athlete.FinishSimSeconds)
            .ThenBy(athlete => athlete.RegistrationId)
            .Select((athlete, index) => new SimulationRankDto(
                index + 1,
                athlete.RegistrationId,
                athlete.Bib,
                athlete.FullName,
                athlete.ResultSeconds!.Value,
                athlete.FinishSimSeconds!.Value))
            .ToList();

        var leaders = new List<SimulationCheckpointLeaderDto>();
        foreach (var checkpoint in session.Checkpoints)
        {
            var best = view.Athletes
                .Select(athlete => (athlete, split: athlete.Splits.FirstOrDefault(split => split.CheckpointId == checkpoint.Id)))
                .Where(row => row.split is not null)
                .OrderBy(row => row.split!.SimMicros)
                .ThenBy(row => row.athlete.RegistrationId)
                .FirstOrDefault();
            if (best.athlete is null || best.split is null)
            {
                continue;
            }

            leaders.Add(new SimulationCheckpointLeaderDto(
                checkpoint.Id,
                best.athlete.RegistrationId,
                best.athlete.Bib,
                SimulationClockMath.MicrosToSeconds(best.split.SimMicros)));
        }

        var discard = view.Phase == SimulationPhase.Finished
                      && view.Athletes.Any(athlete => athlete.ValidFinish
                                                      && athlete.Motion != AthleteMotion.Dnf
                                                      && !session.Committed.Contains(athlete.RegistrationId));

        return new SimulationSnapshotDto(
            session.RunId,
            session.Generation,
            session.Version,
            session.EventId,
            session.DistanceId,
            view.Phase.ToString(),
            session.CommitState,
            session.Seed,
            session.PlaybackRate,
            view.SimulatedSeconds,
            session.DistanceKm,
            session.Title,
            discard,
            session.Committed.OrderBy(id => id).ToList(),
            session.Polyline.Select(point => new SimulationPointDto(point.Lat, point.Lng)).ToList(),
            session.Checkpoints.Select(ToCheckpoint).ToList(),
            athletes,
            ranking,
            leaders,
            view.Log.TakeLast(100).Select(op => new SimulationLogDto(op.Sequence, op.SimSeconds, op.Kind, op.Subject)).ToList());
    }

    private static SimulationCheckpointDto ToCheckpoint(SimulationCourseCheckpoint checkpoint)
        => new(checkpoint.Id, checkpoint.Name, checkpoint.Km, checkpoint.Lat, checkpoint.Lng, checkpoint.Type);

    private sealed class Session
    {
        public object Gate { get; } = new();
        public SemaphoreSlim CommitLock { get; } = new(1, 1);
        public Guid RunId { get; private set; }
        public int Generation { get; private set; }
        public long Version { get; set; }
        public Guid EventId { get; private set; }
        public Guid DistanceId { get; private set; }
        public string Title { get; set; } = "";
        public double DistanceKm { get; set; }
        public int Seed { get; private set; }
        public double PlaybackRate { get; set; } = InitialPlaybackRate;
        public string CommitState { get; set; } = "Idle";
        public HashSet<Guid> Committed { get; } = [];
        public HashSet<Guid> Unresolved { get; } = [];
        public List<SimulationCoursePoint> Polyline { get; set; } = [];
        public List<SimulationCourseCheckpoint> Checkpoints { get; set; } = [];
        public List<SimulationAthleteSeed> Athletes { get; private set; } = [];
        public RaceSimulationEngine Engine { get; private set; } = null!;

        public static Session Create(
            SimulationField field,
            IReadOnlyList<SimulationAthleteSeed> athletes,
            int seed)
        {
            var session = new Session
            {
                RunId = Guid.NewGuid(),
                Generation = 1,
                Version = 1,
                EventId = field.EventId,
                DistanceId = field.DistanceId,
                Title = field.Title,
                DistanceKm = field.DistanceKm,
                Seed = seed,
                Polyline = field.Polyline.ToList(),
                Checkpoints = field.Checkpoints.ToList(),
                Athletes = athletes.ToList(),
                Engine = new RaceSimulationEngine(seed, field.DistanceKm, field.Polyline, field.Checkpoints, athletes)
            };
            return session;
        }

        public void ReplaceEngine(
            int seed,
            IReadOnlyList<SimulationAthleteSeed> athletes,
            double distanceKm,
            IReadOnlyList<SimulationCourseCheckpoint> checkpoints)
        {
            Seed = seed;
            Athletes = athletes.ToList();
            DistanceKm = distanceKm;
            Checkpoints = checkpoints.ToList();
            Engine = new RaceSimulationEngine(seed, distanceKm, Polyline, Checkpoints, Athletes);
            RunId = Guid.NewGuid();
            Generation++;
            Version = 1;
        }
    }
}
