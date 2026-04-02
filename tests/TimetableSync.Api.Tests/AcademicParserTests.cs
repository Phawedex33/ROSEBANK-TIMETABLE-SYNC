using FluentAssertions;
using Moq;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;
using Xunit;

namespace TimetableSync.Api.Tests;

public class AcademicParserTests
{
    private readonly Mock<ITimetableParser> _mockInnerParser = new();
    private readonly Mock<IRosebankReferenceService> _mockRefService = new();
    private readonly AcademicParser _sut;

    public AcademicParserTests()
    {
        _sut = new AcademicParser(_mockInnerParser.Object, _mockRefService.Object);
    }

    [Fact]
    public void Parse_FiltersByGroup_WhenGroupTagIsPresent()
    {
        var events = new List<ClassEvent>
        {
            new() { Subject = "MATH101 | GR1", Day = DayOfWeek.Monday },
            new() { Subject = "PHYS101 | GR2", Day = DayOfWeek.Tuesday }
        };
        _mockInnerParser.Setup(p => p.Parse(It.IsAny<string>(), ParseMode.Academic))
            .Returns(new TimetableParseResult { AcademicEvents = events });

        var result = _sut.Parse("some input", 1, "GR1");
        result.Events.Should().HaveCount(1);
        result.Events[0].Subject.Should().Be("MATH101");
    }

    [Fact]
    public void Parse_DoesNotFilter_WhenNoGroupTagsInPdf()
    {
        var events = new List<ClassEvent>
        {
            new() { Subject = "MATH101", Day = DayOfWeek.Monday },
            new() { Subject = "PHYS101", Day = DayOfWeek.Tuesday }
        };
        _mockInnerParser.Setup(p => p.Parse(It.IsAny<string>(), ParseMode.Academic))
            .Returns(new TimetableParseResult { AcademicEvents = events });

        var result = _sut.Parse("some input", 1, "GR1");
        result.Events.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_AppliesReferenceLookup_Priority1Slot()
    {
        var events = new List<ClassEvent>
        {
            new() { Subject = "MATH101", Day = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0) }
        };
        _mockInnerParser.Setup(p => p.Parse(It.IsAny<string>(), ParseMode.Academic))
            .Returns(new TimetableParseResult { AcademicEvents = events });

        string outCode = "MATH101-NEW", outLecturer = "Dr. Smith", outVenue = "Lab 1";
        _mockRefService.Setup(s => s.TryGetSlotDetails(1, "GR1", DayOfWeek.Monday, 1, out outCode, out outLecturer, out outVenue))
            .Returns(true);

        var result = _sut.Parse("some input", 1, "GR1");
        result.Events[0].Subject.Should().Be("MATH101-NEW");
        result.Events[0].Lecturer.Should().Be("Dr. Smith");
        result.Events[0].Venue.Should().Be("Lab 1");
    }

    [Fact]
    public void Parse_YearMismatch_AddsWarning()
    {
        _mockInnerParser.Setup(p => p.Parse(It.IsAny<string>(), ParseMode.Academic))
            .Returns(new TimetableParseResult { AcademicEvents = new() });

        var result = _sut.Parse("This is a 2nd Year timetable", 1, "GR1");
        result.Warnings.Should().Contain(w => w.Contains("Year mismatch") && w.Contains("2nd Year"));
    }
}
