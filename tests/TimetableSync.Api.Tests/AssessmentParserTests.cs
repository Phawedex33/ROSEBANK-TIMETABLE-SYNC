using FluentAssertions;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;
using Xunit;

namespace TimetableSync.Api.Tests;

public class AssessmentParserTests
{
    private readonly AssessmentParser _sut = new();

    [Fact]
    public void Parse_PasSegmentRegex_MatchesCorrectly()
    {
        var input = "DIS1 PROG1234 Programming 1 Practical Assignment 1 Campus Sitting 10-Mar-26 09:00";
        var result = _sut.Parse(input);
        result.Events.Should().HaveCount(1);
        var ev = result.Events[0];
        ev.ModuleCode.Should().Be("PROG1234");
        ev.AssessmentType.Should().Be("Practical Assignment 1");
    }

    [Fact]
    public void Parse_CompactText_ExpandsAndMatches()
    {
        var input = "DIS1PROG1234Programming1PracticalAssignment1CampusSitting10-Mar-2609:00";
        var result = _sut.Parse(input);
        result.Events.Should().NotBeEmpty();
        result.Events[0].ModuleCode.Should().Be("PROG1234");
    }

    [Fact]
    public void Parse_ModuleBlockFallback_MatchesMultipleDates()
    {
        var input = @"PROG1234 Programming 1
                      Practical Test 1 (Sitting 1)
                      15-Mar-26 09:00
                      Practical Test 1 (Sitting 2)
                      16-Mar-26 14:00";
        var result = _sut.Parse(input);
        result.Events.Should().HaveCount(2);
        result.Events.Should().Contain(e => e.Date == new DateOnly(2026, 3, 15) && e.Sitting == 1);
        result.Events.Should().Contain(e => e.Date == new DateOnly(2026, 3, 16) && e.Sitting == 2);
    }

    [Fact]
    public void Parse_OnlineSubmission_DefaultsTo2359()
    {
        var input = "PROG1234 Programming 1 Online Submission 20-Mar-26";
        var result = _sut.Parse(input);
        result.Events.Should().NotBeEmpty();
        result.Events[0].Time.Should().Be(new TimeOnly(23, 59));
    }

    [Fact]
    public void Parse_SalvagesMergedModuleBlocks_WithoutLeakingOtherCodesIntoNames()
    {
        var input = @"OPSC6311 Open Source Coding (Introduction) Part 1 Online Submission Turnitin 24-Mar-26 23:59
DIS3 OPSC6311 Open Source Coding (Introduction) Part 2 Online Submission Turnitin 28-Apr-26 23:59
DIS3 WEDE6021 Web Development (Intermediate) Part 1 Online Submission Turnitin 14-Apr-26 23:59
DIS3 WEDE6021 Web Development (Intermediate) Part 2 Online Submission Turnitin 04-May-26 23:59
DIS3 XISD5319 Work Integrated Learning 3 A Task 1 Online Submission Turnitin 23-Apr-26 23:59
Replacement Resubmission Supplemental Exam 15-May-26 14:00";

        var result = _sut.Parse(input);

        result.Events.Should().NotBeEmpty();
        result.Events.Should().Contain(e => e.ModuleCode == "OPSC6311");
        result.Events.Should().Contain(e => e.ModuleCode == "WEDE6021");
        result.Events.Should().Contain(e => e.ModuleCode == "OPSC6311" && e.AssessmentType == "Part 1");
        result.Events.Should().Contain(e => e.ModuleCode == "OPSC6311" && e.AssessmentType == "Part 2");
        result.Events.Should().Contain(e => e.ModuleCode == "WEDE6021" && e.AssessmentType == "Part 1");
        result.Events.Should().Contain(e => e.ModuleCode == "WEDE6021" && e.AssessmentType == "Part 2");
        result.Events.Should().NotContain(e =>
            e.ModuleCode == "OPSC6311" &&
            e.ModuleName.Contains("WEDE6021", StringComparison.OrdinalIgnoreCase));
        result.Events.Should().OnlyContain(e =>
            !e.ModuleName.Contains("DIS3", StringComparison.OrdinalIgnoreCase) &&
            !e.ModuleName.Contains("23:59", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_PrunesLowerQualityDuplicateAssessmentRows_ForSameSlot()
    {
        var input = @"DIS3 XISD5319 Work Integrated Learning 3 A Task 1 Online Submission Turnitin 23-Apr-26 23:59
XISD5319 Work Integrated Learning 3 A Task 1 23-Apr-26 23:59
XISD5319 Work Integrated Learning 3 A Task 1 Assessment 23-Apr-26 23:59";

        var result = _sut.Parse(input);

        result.Events.Should().ContainSingle(e =>
            e.ModuleCode == "XISD5319" &&
            e.Date == new DateOnly(2026, 4, 23) &&
            e.Time == new TimeOnly(23, 59));
        result.Events[0].AssessmentType.Should().NotBe("Assessment");
    }

    [Fact]
    public void Parse_ModuleBlockFallback_PreservesDeferredAssessmentType_PerDateContext()
    {
        var input = @"XISD5319 Work Integrated Learning 3 A
Task 1 Online Submission 23-Apr-26 23:59
Task 1 Deferred Online Submission 30-Apr-26 23:59";

        var result = _sut.Parse(input);

        result.Events.Should().HaveCount(2);
        result.Events.Should().Contain(e =>
            e.Date == new DateOnly(2026, 4, 23) &&
            e.AssessmentType == "Task 1");
        result.Events.Should().Contain(e =>
            e.Date == new DateOnly(2026, 4, 30) &&
            e.AssessmentType == "Task 1 Deferred");
    }

    [Fact]
    public void Parse_ModuleBlockFallback_MapsReplacementResubmissionSection_ToDeferredEvent()
    {
        var input = @"XISD5319 Work Integrated Learning 3 A
Task 1 Online Submission 23-Apr-26 23:59
Replacement Resubmission Supplemental 15-May-26 14:00";

        var result = _sut.Parse(input);

        result.Events.Should().Contain(e =>
            e.Date == new DateOnly(2026, 5, 15) &&
            e.AssessmentType == "Task 1 Deferred");
    }

    [Fact]
    public void Parse_MainAttempt_ExcludesDeferredRows()
    {
        var input = @"XISD5319 Work Integrated Learning 3 A
Task 1 Online Submission 23-Apr-26 23:59
Replacement Resubmission Supplemental 15-May-26 14:00";

        var result = _sut.Parse(input, new AssessmentParseOptions
        {
            Attempt = "main"
        });

        result.Events.Should().ContainSingle();
        result.Events[0].AssessmentType.Should().Be("Task 1");
    }

    [Fact]
    public void Parse_SupplementaryAttempt_OnlyReturnsDeferredRows()
    {
        var input = @"XISD5319 Work Integrated Learning 3 A
Task 1 Online Submission 23-Apr-26 23:59
Replacement Resubmission Supplemental 15-May-26 14:00";

        var result = _sut.Parse(input, new AssessmentParseOptions
        {
            Attempt = "supplementary"
        });

        result.Events.Should().ContainSingle();
        result.Events[0].AssessmentType.Should().Be("Task 1 Deferred");
        result.Events[0].Date.Should().Be(new DateOnly(2026, 5, 15));
    }
}
