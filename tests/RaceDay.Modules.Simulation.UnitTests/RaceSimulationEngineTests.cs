using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.UnitTests;

public class RaceSimulationEngineTests
{
    private static readonly Guid AthleteA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid AthleteB = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    [Fact]
    public void Same_seed_at_1x_and_60x_matches_at_step_boundaries()
    {
        var slow = Create();
        var fast = Create();
        slow.Start();
        fast.Start();
        Assert.True(slow.AdvanceReal(60, 1));
        Assert.True(fast.AdvanceReal(1, 60));

        var left = slow.Capture();
        var right = fast.Capture();
        Assert.Equal(left.SimulatedMicros, right.SimulatedMicros);
        Assert.Equal(60_000_000, left.SimulatedMicros);
        Assert.Equal(0, left.ResidualMicros);
        Assert.Equal(0, right.ResidualMicros);
        Assert.Equal(left.Athletes.Count, right.Athletes.Count);
        for (var i = 0; i < left.Athletes.Count; i++)
        {
            Assert.Equal(left.Athletes[i].CourseKm, right.Athletes[i].CourseKm);
            Assert.Equal(left.Athletes[i].SpeedKmh, right.Athletes[i].SpeedKmh);
            Assert.Equal(left.Athletes[i].BaseKmh, right.Athletes[i].BaseKmh);
            Assert.Equal(left.Athletes[i].Splits.Count, right.Athletes[i].Splits.Count);
            for (var s = 0; s < left.Athletes[i].Splits.Count; s++)
            {
                Assert.Equal(left.Athletes[i].Splits[s].CheckpointId, right.Athletes[i].Splits[s].CheckpointId);
                Assert.Equal(left.Athletes[i].Splits[s].SimMicros, right.Athletes[i].Splits[s].SimMicros);
            }
        }
    }

    [Fact]
    public void Pause_freezes_residual_and_simulated_time()
    {
        var engine = Create();
        engine.Start();
        Assert.False(engine.AdvanceMicros(50_000));
        Assert.Equal(50_000, engine.Capture().ResidualMicros);
        engine.Pause();
        Assert.False(engine.AdvanceMicros(100_000));
        Assert.Equal(50_000, engine.Capture().ResidualMicros);
        Assert.Equal(0, engine.Capture().SimulatedMicros);
        engine.Resume();
        Assert.True(engine.AdvanceMicros(50_000));
        Assert.Equal(RaceSimulationEngine.StepMicros, engine.Capture().SimulatedMicros);
        Assert.Equal(0, engine.Capture().ResidualMicros);
    }

    [Fact]
    public void Speed_change_keeps_distance_and_splits_already_recorded()
    {
        var changed = Create();
        var steady = Create();
        changed.Start();
        steady.Start();
        changed.AdvanceSteps(100);
        steady.AdvanceSteps(100);
        var before = changed.Capture().Athletes[0];
        changed.SetBaseSpeed(AthleteA, 20);
        var after = changed.Capture().Athletes[0];
        Assert.Equal(before.CourseKm, after.CourseKm);
        Assert.Equal(before.Splits.Count, after.Splits.Count);
        changed.AdvanceSteps(100);
        steady.AdvanceSteps(100);
        Assert.True(changed.Capture().Athletes[0].CourseKm > steady.Capture().Athletes[0].CourseKm);
    }

    [Fact]
    public void Speed_zero_does_not_move_or_throw()
    {
        var engine = Create();
        engine.Start();
        engine.SetBaseSpeed(AthleteA, 0, immediate: true);
        var km = engine.Capture().Athletes[0].CourseKm;
        engine.AdvanceSteps(10);
        Assert.Equal(km, engine.Capture().Athletes[0].CourseKm);
        Assert.Equal(0, engine.Capture().Athletes[0].SpeedKmh);
    }

    [Fact]
    public void One_step_can_cross_several_checkpoints_and_finish_inside_the_step()
    {
        var engine = new RaceSimulationEngine(
            1,
            1,
            [new SimulationCoursePoint(0, 0), new SimulationCoursePoint(1, 1)],
            [
                new SimulationCourseCheckpoint("s", "Start", 0, 0, 0, "start"),
                new SimulationCourseCheckpoint("a", "A", 0.3, 0, 0, "cp"),
                new SimulationCourseCheckpoint("b", "B", 0.6, 0, 0, "cp"),
                new SimulationCourseCheckpoint("f", "Finish", 1, 1, 1, "finish")
            ],
            [new SimulationAthleteSeed(AthleteA, "A001", "An", "chip-a")],
            amplitude: 0);
        engine.SetBaseSpeed(AthleteA, 72_000, immediate: true);
        engine.Start();
        Assert.Equal(0, engine.Capture().Athletes[0].Splits[0].SimMicros);
        engine.AdvanceSteps(1);
        var athlete = engine.Capture().Athletes[0];
        Assert.Equal(["s", "a", "b", "f"], athlete.Splits.Select(split => split.CheckpointId).ToArray());
        Assert.Equal(0.05, athlete.FinishSimSeconds);
        Assert.True(athlete.FinishSimSeconds < 0.1);
        Assert.Equal(AthleteMotion.Finished, athlete.Motion);
        Assert.True(athlete.ValidFinish);
        Assert.Equal(SimulationPhase.Finished, engine.Capture().Phase);
    }

    [Fact]
    public void Injected_duplicate_and_out_of_order_passage_do_not_change_splits()
    {
        var engine = Create();
        engine.Start();
        var before = engine.Capture().Athletes[0].Splits.Count;
        Assert.False(engine.InjectPassage(AthleteA, "start"));
        Assert.Equal(before, engine.Capture().Athletes[0].Splits.Count);
        Assert.Contains(engine.Capture().Log, op => op.Kind == "FRAUD");

        Assert.False(engine.InjectPassage(AthleteA, "finish"));
        var athlete = engine.Capture().Athletes[0];
        Assert.False(athlete.ValidFinish);
        Assert.Equal(0, athlete.CourseKm);
        Assert.DoesNotContain(athlete.Splits, split => split.CheckpointId == "finish");
    }

    [Fact]
    public void Default_chips_keep_the_full_registration_id_and_both_pass_the_same_checkpoint()
    {
        var first = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
        var second = Guid.Parse("00000000-0000-4000-8000-0000000000a2");
        var engine = new RaceSimulationEngine(
            1,
            5,
            [new SimulationCoursePoint(0, 0), new SimulationCoursePoint(1, 1)],
            [
                new SimulationCourseCheckpoint("start", "Start", 0, 0, 0, "start"),
                new SimulationCourseCheckpoint("mid", "Mid", 2.5, 0, 0, "cp"),
                new SimulationCourseCheckpoint("finish", "Finish", 5, 1, 1, "finish")
            ],
            [
                new SimulationAthleteSeed(first, "A001", "An", RaceSimulationEngine.ChipFor(first)),
                new SimulationAthleteSeed(second, "A002", "Ba", RaceSimulationEngine.ChipFor(second))
            ]);

        var athletes = engine.Capture().Athletes;
        Assert.Equal("sim-000000000000400080000000000000a1", athletes[0].ChipId);
        Assert.Equal("sim-000000000000400080000000000000a2", athletes[1].ChipId);
        Assert.Contains(first.ToString("N"), athletes[0].ChipId);
        Assert.Contains(second.ToString("N"), athletes[1].ChipId);
        Assert.NotEqual(athletes[0].ChipId, athletes[1].ChipId);

        engine.Start();
        var started = engine.Capture().Athletes;
        Assert.Contains(started[0].Splits, split => split.CheckpointId == "start");
        Assert.Contains(started[1].Splits, split => split.CheckpointId == "start");
    }

    [Fact]
    public void Shared_chip_rejects_passage()
    {
        var engine = Create();
        engine.Start();
        var chip = engine.Capture().Athletes[0].ChipId;
        engine.AssignChip(AthleteB, chip);
        var splits = engine.Capture().Athletes[0].Splits.Count;
        Assert.False(engine.InjectPassage(AthleteA, "mid"));
        Assert.Equal(splits, engine.Capture().Athletes[0].Splits.Count);
        Assert.Contains(engine.Capture().Log, op => op.Kind == "FRAUD");
    }

    [Fact]
    public void Dnf_stops_progress_and_sos_is_only_a_marker()
    {
        var engine = Create();
        engine.Start();
        engine.AdvanceSteps(20);
        var km = engine.Capture().Athletes[0].CourseKm;
        var splits = engine.Capture().Athletes[0].Splits.Count;
        engine.MarkDnf(AthleteA);
        engine.AdvanceSteps(20);
        var after = engine.Capture().Athletes[0];
        Assert.Equal(AthleteMotion.Dnf, after.Motion);
        Assert.Equal(km, after.CourseKm);
        Assert.Equal(splits, after.Splits.Count);

        engine.MarkSos(AthleteB);
        var beforeSos = engine.Capture().Athletes[1].CourseKm;
        engine.AdvanceSteps(20);
        var sos = engine.Capture().Athletes[1];
        Assert.True(sos.Sos);
        Assert.True(sos.CourseKm > beforeSos);
        Assert.Contains(engine.Capture().Log, op => op.Kind == "SOS");
        Assert.Contains(engine.Capture().Log, op => op.Kind == "DNF");
    }

    [Fact]
    public void Display_rounding_matches_event_result_seconds()
    {
        double[] samples = [0.4, 0.5, 1.5, 2.5, 90.4, 90.5];
        foreach (var sample in samples)
        {
            Assert.Equal(ResultTime.RoundElapsed(sample), SimulationClockMath.RoundSeconds(sample));
        }

        Assert.Equal(1, SimulationClockMath.RoundSeconds(0.5));
        Assert.Equal(2, SimulationClockMath.RoundSeconds(1.5));
        Assert.Equal(3, SimulationClockMath.RoundSeconds(2.5));
        Assert.Equal(90, SimulationClockMath.RoundSeconds(90.4));
        Assert.Equal(91, SimulationClockMath.RoundSeconds(90.5));
    }

    [Fact]
    public void Course_must_increase_and_have_start_and_finish()
    {
        var error = Assert.Throws<DomainException>(() => Create(checkpoints:
        [
            new SimulationCourseCheckpoint("s", "Start", 0, 0, 0, "start"),
            new SimulationCourseCheckpoint("m", "Mid", 3, 0, 0, "cp"),
            new SimulationCourseCheckpoint("f", "Finish", 1, 0, 0, "finish")
        ]));
        Assert.Contains("tăng", error.Message);
    }

    private static RaceSimulationEngine Create(
        IReadOnlyList<SimulationCourseCheckpoint>? checkpoints = null,
        double amplitude = RaceSimulationEngine.DefaultAmplitude)
    {
        checkpoints ??=
        [
            new SimulationCourseCheckpoint("start", "Start", 0, 0, 0, "start"),
            new SimulationCourseCheckpoint("mid", "Mid", 2.5, 0, 0, "cp"),
            new SimulationCourseCheckpoint("finish", "Finish", 5, 1, 1, "finish")
        ];
        return new RaceSimulationEngine(
            42,
            5,
            [new SimulationCoursePoint(0, 0), new SimulationCoursePoint(1, 1)],
            checkpoints,
            [
                new SimulationAthleteSeed(AthleteA, "A001", "An", "chip-a"),
                new SimulationAthleteSeed(AthleteB, "A002", "Ba", "chip-b")
            ],
            amplitude);
    }
}
