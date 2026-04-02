using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
        payload.Should().Contain("student_group");
        payload.Should().Contain("class_schedule_pdf");
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
