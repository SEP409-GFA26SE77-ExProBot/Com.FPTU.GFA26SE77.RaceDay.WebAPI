namespace RaceDay.Modules.Simulation.Application;

internal readonly record struct StaffCheckpointScope(double FromKm, double ToKm, bool IsStart)
{
    public bool Contains(double courseKm, bool ready) => ready
        ? IsStart
        : IsStart ? courseKm == ToKm : courseKm > FromKm && courseKm <= ToKm;

    public bool CanReport(double courseKm, bool ready, bool hasPassage) =>
        Contains(courseKm, ready) || hasPassage;
}
