using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TimetableSync.Api.Models;
using Xunit;

namespace TimetableSync.Api.Tests;

public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task Post_RosebankParser_MissingInputs_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("DIS3"), "student_year" }
        };

        var response = await client.PostAsync("/api/parser/rosebank", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("missing_fields");
        payload.Should().Contain("class_schedule_pdf");
        payload.Should().Contain("assessment_schedule_pdf");
    }

    [Fact]
    public async Task Post_RosebankParser_GoldenFiles_ReturnsClassAndAssessmentData()
    {
        var classPdf = FindExamplePdf("classes", "2026-Diploma in Information");
        var assessmentPdf = FindExamplePdf("exams", "DISD0601");

        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("DIS3"), "student_year" },
            { new StringContent("GR1"), "student_group" }
        };

        form.Add(new StreamContent(File.OpenRead(classPdf)), "class_schedule_pdf", Path.GetFileName(classPdf));
        form.Add(new StreamContent(File.OpenRead(assessmentPdf)), "assessment_schedule_pdf", Path.GetFileName(assessmentPdf));

        var response = await client.PostAsync("/api/parser/rosebank", form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);

        var classEvents = doc.RootElement
            .GetProperty("schedules")
            .GetProperty("class_schedule")
            .GetProperty("events")
            .EnumerateArray()
            .ToList();

        classEvents.Should().NotBeEmpty();
        classEvents.All(x => !string.IsNullOrWhiteSpace(x.GetProperty("day_of_week").GetString())).Should().BeTrue();
        classEvents.All(x => !string.IsNullOrWhiteSpace(x.GetProperty("start_time").GetString())).Should().BeTrue();
        classEvents.All(x => !string.IsNullOrWhiteSpace(x.GetProperty("end_time").GetString())).Should().BeTrue();
        classEvents.Any(x =>
            !string.IsNullOrWhiteSpace(x.GetProperty("room").GetString()) &&
            !string.IsNullOrWhiteSpace(x.GetProperty("lecturer").GetString())).Should().BeTrue();

        var assessmentEvents = doc.RootElement
            .GetProperty("schedules")
            .GetProperty("assessment_schedule")
            .GetProperty("events")
            .EnumerateArray()
            .ToList();

        assessmentEvents.Should().NotBeEmpty();
        var allowedDis3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADDB6311", "OPSC6311", "WEDE6021", "XISD5319"
        };
        assessmentEvents.Select(x => x.GetProperty("subject_code").GetString()).All(code => code is not null && allowedDis3.Contains(code)).Should().BeTrue();
    }

    [Fact]
    public async Task Post_RosebankParser_DifferentYear_FiltersAssessmentRows()
    {
        var classPdf = FindExamplePdf("classes", "2026-Diploma in Information");
        var assessmentPdf = FindExamplePdf("exams", "DISD0601");
        var client = _factory.CreateClient();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("DIS2"), "student_year" },
            { new StringContent("GR1"), "student_group" }
        };
        form.Add(new StreamContent(File.OpenRead(classPdf)), "class_schedule_pdf", Path.GetFileName(classPdf));
        form.Add(new StreamContent(File.OpenRead(assessmentPdf)), "assessment_schedule_pdf", Path.GetFileName(assessmentPdf));

        var response = await client.PostAsync("/api/parser/rosebank", form);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var doc = JsonDocument.Parse(body);
        var assessmentEvents = doc.RootElement
            .GetProperty("schedules")
            .GetProperty("assessment_schedule")
            .GetProperty("events")
            .EnumerateArray()
            .ToList();

        assessmentEvents.Should().NotBeEmpty();
        var allowedDis2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DATA6211", "ISEC6321", "PROG6221", "SAND6221"
        };
        assessmentEvents.Select(x => x.GetProperty("subject_code").GetString()).All(code => code is not null && allowedDis2.Contains(code)).Should().BeTrue();
    }

    [Fact]
    public async Task Post_RosebankParser_AssessmentOnly_AllowsMissingClassFileAndGroup()
    {
        var assessmentPdf = FindExamplePdf("exams", "DISD0601");
        var client = _factory.CreateClient();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("DIS3"), "student_year" }
        };
        form.Add(new StreamContent(File.OpenRead(assessmentPdf)), "assessment_schedule_pdf", Path.GetFileName(assessmentPdf));

        var response = await client.PostAsync("/api/parser/rosebank", form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("student_group").GetString().Should().BeEmpty();
        doc.RootElement.GetProperty("schedules").GetProperty("class_schedule").GetProperty("events").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("schedules").GetProperty("assessment_schedule").GetProperty("events").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Post_RosebankParser_SupplementaryAttempt_ReturnsDeferredAssessmentRowsOnly()
    {
        var assessmentPdf = FindExamplePdf("exams", "DISD0601");
        var client = _factory.CreateClient();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("DIS3"), "student_year" },
            { new StringContent("supplementary"), "assessment_attempt" }
        };
        form.Add(new StreamContent(File.OpenRead(assessmentPdf)), "assessment_schedule_pdf", Path.GetFileName(assessmentPdf));

        var response = await client.PostAsync("/api/parser/rosebank", form);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);

        var assessmentEvents = doc.RootElement
            .GetProperty("schedules")
            .GetProperty("assessment_schedule")
            .GetProperty("events")
            .EnumerateArray()
            .ToList();

        assessmentEvents.Should().NotBeEmpty();
        assessmentEvents.All(x => x.GetProperty("is_deferred").GetBoolean()).Should().BeTrue();
    }

    [Fact]
    public async Task Post_AcademicExport_ReturnsCalendarFile()
    {
        var client = _factory.CreateClient();
        var request = new AcademicSyncRequest
        {
            Group = "GR1",
            TimeZone = "Africa/Johannesburg",
            WeeksDuration = 2,
            Events =
            {
                new ClassEvent
                {
                    Day = DayOfWeek.Monday,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(10, 0),
                    Subject = "Programming 3",
                    Lecturer = "Ms Example",
                    Venue = "Lab 2"
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/academic/export", request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/calendar");
        body.Should().Contain("BEGIN:VCALENDAR");
        body.Should().Contain("SUMMARY:[CLASS] Programming 3");
        body.Should().Contain("RRULE:FREQ=WEEKLY");
    }

    [Fact]
    public async Task Post_AssessmentExport_ReturnsCalendarFile()
    {
        var client = _factory.CreateClient();
        var request = new AssessmentSyncRequest
        {
            TimeZone = "Africa/Johannesburg",
            Events =
            {
                new AssessmentEvent
                {
                    ModuleCode = "XISD5319",
                    ModuleName = "UX Design",
                    AssessmentType = "Task 1",
                    Date = new DateOnly(2026, 4, 23),
                    Time = new TimeOnly(9, 0),
                    DeliveryMode = "Online"
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/assessment/export", request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/calendar");
        body.Should().Contain("BEGIN:VCALENDAR");
        body.Should().Contain("SUMMARY:[ASSESSMENT] XISD5319 - UX Design (Task 1)");
        body.Should().Contain("DTSTART;TZID=Africa/Johannesburg:20260423T090000");
    }

    private static string FindExamplePdf(string folderName, string nameContains)
    {
        var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var root = Path.Combine(baseDir, "timetable_examples", folderName);
        var file = Directory
            .GetFiles(root, "*.pdf", SearchOption.AllDirectories)
            .FirstOrDefault(x => x.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

        file.Should().NotBeNull($"Expected sample PDF containing '{nameContains}' in {root}");
        return file!;
    }
}
