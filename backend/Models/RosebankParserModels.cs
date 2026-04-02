using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace TimetableSync.Api.Models;

public sealed class RosebankParseRequest
{
    [FromForm(Name = "student_year")]
    public string? StudentYear { get; set; }

    [FromForm(Name = "student_group")]
    public string? StudentGroup { get; set; }

    [FromForm(Name = "class_schedule_pdf")]
    public IFormFile? ClassSchedulePdf { get; set; }

    [FromForm(Name = "assessment_schedule_pdf")]
    public IFormFile? AssessmentSchedulePdf { get; set; }
}

public sealed class RosebankMissingInputsError
{
    [JsonPropertyName("error")]
    public bool Error { get; init; } = true;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "Parsing cannot begin. Required inputs are missing.";

    [JsonPropertyName("missing_fields")]
    public List<string> MissingFields { get; init; } = new();
}

public sealed class RosebankParseResponse
{
    [JsonPropertyName("parsed_at")]
    public string ParsedAt { get; init; } = string.Empty;

    [JsonPropertyName("student_year")]
    public string StudentYear { get; init; } = string.Empty;

    [JsonPropertyName("student_group")]
    public string StudentGroup { get; init; } = string.Empty;

    [JsonPropertyName("institution")]
    public string Institution { get; init; } = "Rosebank College";

    [JsonPropertyName("campus")]
    public string Campus { get; init; } = "Pretoria CBD";

    [JsonPropertyName("timezone")]
    public string Timezone { get; init; } = "Africa/Johannesburg";

    [JsonPropertyName("term")]
    public string Term { get; init; } = "2026 Term 1";

    [JsonPropertyName("schedules")]
    public RosebankSchedules Schedules { get; init; } = new();

    [JsonPropertyName("warnings")]
    public List<RosebankWarning> Warnings { get; init; } = new();

    [JsonPropertyName("summary")]
    public RosebankSummary Summary { get; init; } = new();
}

public sealed class RosebankSchedules
{
    [JsonPropertyName("class_schedule")]
    public RosebankClassSchedule ClassSchedule { get; init; } = new();

    [JsonPropertyName("assessment_schedule")]
    public RosebankAssessmentSchedule AssessmentSchedule { get; init; } = new();
}

public sealed class RosebankClassSchedule
{
    [JsonPropertyName("source_format")]
    public string SourceFormat { get; init; } = "image";

    [JsonPropertyName("timetable_generated_date")]
    public string? TimetableGeneratedDate { get; init; }

    [JsonPropertyName("events")]
    public List<RosebankClassEvent> Events { get; init; } = new();
}

public sealed class RosebankAssessmentSchedule
{
    [JsonPropertyName("source_format")]
    public string SourceFormat { get; init; } = "table";

    [JsonPropertyName("events")]
    public List<RosebankAssessmentEvent> Events { get; init; } = new();
}

public sealed class RosebankClassEvent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("event_category")]
    public string EventCategory { get; init; } = "class";

    [JsonPropertyName("subject_code")]
    public string SubjectCode { get; init; } = string.Empty;

    [JsonPropertyName("subject_name")]
    public string? SubjectName { get; init; }

    [JsonPropertyName("day_of_week")]
    public string? DayOfWeek { get; init; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; init; }

    [JsonPropertyName("room")]
    public string? Room { get; init; }

    [JsonPropertyName("lecturer")]
    public string? Lecturer { get; init; }

    [JsonPropertyName("recurrence")]
    public string Recurrence { get; init; } = "weekly";

    [JsonPropertyName("reminders")]
    public List<string> Reminders { get; init; } = new() { "15min_before" };

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "high";

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed class RosebankAssessmentEvent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("event_category")]
    public string EventCategory { get; init; } = "assessment";

    [JsonPropertyName("subject_code")]
    public string SubjectCode { get; init; } = string.Empty;

    [JsonPropertyName("subject_name")]
    public string? SubjectName { get; init; }

    [JsonPropertyName("assessment_type")]
    public string AssessmentType { get; init; } = string.Empty;

    [JsonPropertyName("submission_type")]
    public string SubmissionType { get; init; } = string.Empty;

    [JsonPropertyName("requires_turnitin")]
    public bool RequiresTurnitin { get; init; }

    [JsonPropertyName("specific_date")]
    public string? SpecificDate { get; init; }

    [JsonPropertyName("due_time")]
    public string? DueTime { get; init; }

    [JsonPropertyName("is_deferred")]
    public bool IsDeferred { get; init; }

    [JsonPropertyName("recurrence")]
    public string Recurrence { get; init; } = "once";

    [JsonPropertyName("reminders")]
    public List<string> Reminders { get; init; } = new();

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "high";

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed class RosebankWarning
{
    [JsonPropertyName("event_id")]
    public string? EventId { get; init; }

    [JsonPropertyName("issue")]
    public string Issue { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "info";
}

public sealed class RosebankSummary
{
    [JsonPropertyName("total_class_events")]
    public int TotalClassEvents { get; init; }

    [JsonPropertyName("total_assessment_events")]
    public int TotalAssessmentEvents { get; init; }

    [JsonPropertyName("unique_subjects")]
    public List<string> UniqueSubjects { get; init; } = new();

    [JsonPropertyName("days_with_classes")]
    public List<string> DaysWithClasses { get; init; } = new();

    [JsonPropertyName("earliest_assessment_date")]
    public string? EarliestAssessmentDate { get; init; }

    [JsonPropertyName("latest_assessment_date")]
    public string? LatestAssessmentDate { get; init; }
}
