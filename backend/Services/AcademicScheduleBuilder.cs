namespace TimetableSync.Api.Services;

public sealed class AcademicScheduleBuilder : IAcademicScheduleBuilder
{
    private static readonly Dictionary<int, (TimeOnly Start, TimeOnly End)> PeriodMap = new()
    {
        [1] = (new TimeOnly(8, 0), new TimeOnly(8, 50)),
        [2] = (new TimeOnly(9, 0), new TimeOnly(9, 50)),
        [3] = (new TimeOnly(10, 0), new TimeOnly(10, 50)),
        [4] = (new TimeOnly(11, 0), new TimeOnly(11, 50)),
        [5] = (new TimeOnly(12, 0), new TimeOnly(12, 50)),
        [6] = (new TimeOnly(13, 0), new TimeOnly(13, 50)),
        [7] = (new TimeOnly(14, 0), new TimeOnly(14, 50)),
        [8] = (new TimeOnly(15, 0), new TimeOnly(15, 50)),
        [9] = (new TimeOnly(16, 0), new TimeOnly(16, 50))
    };

    public (TimeOnly Start, TimeOnly End)? ResolvePeriod(int period)
    {
        return PeriodMap.TryGetValue(period, out var slot) ? slot : null;
    }
}
