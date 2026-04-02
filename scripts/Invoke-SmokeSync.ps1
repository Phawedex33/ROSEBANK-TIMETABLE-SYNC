param(
    [string]$BaseUrl = "https://localhost:7068",
    [ValidateSet("parse", "academic", "assessment", "cleanup")]
    [string]$Mode = "parse",
    [string]$SyncUserId = "",
    [string]$Year = "DIS3",
    [string]$Group = "GR1",
    [string]$ClassPdf = ".\timetable_examples\classes\2026-Diploma in Information in Software Development-3rd Year-Gr1-Gr3-AW4-V7.pdf",
    [string]$AssessmentPdf = ".\timetable_examples\exams\DISD0601 (v1).pdf",
    [switch]$DeleteAfterSync
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-SmokeSession {
    param([string]$BaseUrl, [string]$SyncUserId)

    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    if (-not [string]::IsNullOrWhiteSpace($SyncUserId)) {
        $uri = [System.Uri]$BaseUrl
        $cookie = New-Object System.Net.Cookie("sync_user_id", $SyncUserId, "/", $uri.Host)
        $session.Cookies.Add($cookie)
    }

    return $session
}

function Invoke-JsonRequest {
    param(
        [string]$Method,
        [string]$Uri,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session,
        [object]$Body
    )

    $jsonBody = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 8 }
    $response = Invoke-WebRequest -UseBasicParsing -SkipHttpErrorCheck -Method $Method -Uri $Uri -WebSession $Session -ContentType "application/json" -Body $jsonBody
    return [pscustomobject]@{
        StatusCode = $response.StatusCode
        Json = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
        Raw = $response.Content
    }
}

function Invoke-ParsePreview {
    param(
        [string]$BaseUrl,
        [string]$Year,
        [string]$Group,
        [string]$ClassPdf,
        [string]$AssessmentPdf,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session
    )

    if (-not (Test-Path $ClassPdf)) {
        throw "Class PDF not found: $ClassPdf"
    }

    $form = @{
        student_year = $Year
        student_group = $Group
        class_schedule_pdf = Get-Item $ClassPdf
    }

    if (Test-Path $AssessmentPdf) {
        $form.assessment_schedule_pdf = Get-Item $AssessmentPdf
    }

    $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$BaseUrl/api/parser/rosebank" -Form $form -WebSession $Session
    return $response.Content | ConvertFrom-Json
}

function New-AcademicPayload {
    param(
        [object]$ParseResult,
        [string]$Year,
        [string]$Group
    )

    $events = @()
    foreach ($event in $ParseResult.schedules.class_schedule.events) {
        if ([string]::IsNullOrWhiteSpace($event.day_of_week)) { continue }

        $events += [pscustomobject]@{
            day = $event.day_of_week
            startTime = $event.start_time
            endTime = $event.end_time
            subject = if ($event.subject_name) { $event.subject_name } else { $event.subject_code }
            lecturer = if ($event.lecturer) { $event.lecturer } else { "" }
            venue = if ($event.room) { $event.room } else { "" }
        }
    }

    return [pscustomobject]@{
        year = [int](($Year -replace "\D", ""))
        group = $Group
        timeZone = "Africa/Johannesburg"
        events = $events
    }
}

function New-AssessmentPayload {
    param([object]$ParseResult)

    $events = @()
    foreach ($event in $ParseResult.schedules.assessment_schedule.events) {
        if ([string]::IsNullOrWhiteSpace($event.specific_date)) { continue }

        $events += [pscustomobject]@{
            moduleCode = $event.subject_code
            moduleName = if ($event.subject_name) { $event.subject_name } else { $event.subject_code }
            assessmentType = $event.assessment_type
            sitting = $null
            date = $event.specific_date
            time = if ($event.due_time) { $event.due_time } else { "23:59" }
            deliveryMode = if ($event.submission_type -eq "online") { "Online Submission" } else { "Campus Sitting" }
        }
    }

    return [pscustomobject]@{
        timeZone = "Africa/Johannesburg"
        events = $events
    }
}

$session = New-SmokeSession -BaseUrl $BaseUrl -SyncUserId $SyncUserId
$parseResult = Invoke-ParsePreview -BaseUrl $BaseUrl -Year $Year -Group $Group -ClassPdf $ClassPdf -AssessmentPdf $AssessmentPdf -Session $session

Write-Host "Parse summary"
Write-Host "  Classes: $($parseResult.summary.total_class_events)"
Write-Host "  Assessments: $($parseResult.summary.total_assessment_events)"
Write-Host "  Warnings: $($parseResult.warnings.Count)"

if ($Mode -eq "parse") {
    return
}

if ([string]::IsNullOrWhiteSpace($SyncUserId)) {
    throw "Sync modes require -SyncUserId copied from the authenticated browser session cookie."
}

switch ($Mode) {
    "academic" {
        $payload = New-AcademicPayload -ParseResult $parseResult -Year $Year -Group $Group
        $result = Invoke-JsonRequest -Method Post -Uri "$BaseUrl/api/academic/sync" -Session $session -Body $payload
    }
    "assessment" {
        $payload = New-AssessmentPayload -ParseResult $parseResult
        $result = Invoke-JsonRequest -Method Post -Uri "$BaseUrl/api/assessment/sync" -Session $session -Body $payload
    }
    "cleanup" {
        $result = Invoke-JsonRequest -Method Post -Uri "$BaseUrl/api/calendar/delete-synced" -Session $session -Body @{
            mode = "all"
        }
    }
}

Write-Host "HTTP $($result.StatusCode)"
Write-Host $result.Raw

if ($DeleteAfterSync -and ($Mode -eq "academic" -or $Mode -eq "assessment") -and $result.StatusCode -ge 200 -and $result.StatusCode -lt 300) {
    $cleanup = Invoke-JsonRequest -Method Post -Uri "$BaseUrl/api/calendar/delete-synced" -Session $session -Body @{
        mode = if ($Mode -eq "academic") { "academic" } else { "assessment" }
    }
    Write-Host "Cleanup HTTP $($cleanup.StatusCode)"
    Write-Host $cleanup.Raw
}
