using FluentAssertions;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;
using Xunit;

namespace TimetableSync.Api.Tests;

public class TimetableParserTests
{
    private readonly TimetableParser _sut = new();

    [Fact]
    public void Parse_AcademicStandardLine_ReturnsEvent()
    {
        var input = "Monday 08:00-09:00 Mathematics";
        var result = _sut.Parse(input, ParseMode.Academic);
        result.AcademicEvents.Should().HaveCount(1);
        var ev = result.AcademicEvents[0];
        ev.Day.Should().Be(DayOfWeek.Monday);
        ev.StartTime.Should().Be(new TimeOnly(8, 0));
        ev.EndTime.Should().Be(new TimeOnly(9, 0));
        ev.Subject.Should().Be("Mathematics");
    }

    [Fact]
    public void Parse_AcademicGridHead_ReturnsEvent()
    {
        var input = "Mo 3 ADDB6311 Advanced Database";
        var result = _sut.Parse(input, ParseMode.Academic);
        result.AcademicEvents.Should().HaveCount(1);
        var ev = result.AcademicEvents[0];
        ev.Day.Should().Be(DayOfWeek.Monday);
        ev.StartTime.Should().Be(new TimeOnly(10, 0));
        ev.Subject.Should().Contain("ADDB6311");
    }

    [Fact]
    public void Parse_AcademicGridTail_ReturnsEvent()
    {
        var input = "ADDB6311 Advanced Database We 2";
        var result = _sut.Parse(input, ParseMode.Academic);
        result.AcademicEvents.Should().HaveCount(1);
        var ev = result.AcademicEvents[0];
        ev.Day.Should().Be(DayOfWeek.Wednesday);
        ev.StartTime.Should().Be(new TimeOnly(9, 0));
        ev.Subject.Should().Contain("ADDB6311");
    }

    [Fact]
    public void Parse_NoiseLines_AreIgnored()
    {
        var input = @"Rosebank College
                      Timetable 2026
                      Monday 08:00-09:00 Mathematics
                      Page 1";
        var result = _sut.Parse(input, ParseMode.Academic);
        result.AcademicEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_AssessmentKeywords_TriggersWarningInAcademicMode()
    {
        var input = "Assessment Timetable Campus Sitting";
        var result = _sut.Parse(input, ParseMode.Academic);
        result.Warnings.Should().Contain(w => w.Contains("Switch Timetable Type"));
    }
}
