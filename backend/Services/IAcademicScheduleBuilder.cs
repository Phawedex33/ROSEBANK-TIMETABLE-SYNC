namespace TimetableSync.Api.Services;

public interface IAcademicScheduleBuilder
{
    (TimeOnly Start, TimeOnly End)? ResolvePeriod(int period);
}
