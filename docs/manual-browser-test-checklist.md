# Manual Browser Test Checklist

## Preconditions

- Start the API locally: `dotnet run --project backend\TimetableSync.Api.csproj`
- Open `https://localhost:7068`
- Have the sample PDFs ready:
  - `timetable_examples\classes\2026-Diploma in Information in Software Development-3rd Year-Gr1-Gr3-AW4-V7.pdf`
  - `timetable_examples\exams\DISD0601 (v1).pdf`

## Parse And Review Flow

1. Open the app home page and confirm the top status area loads without auth prompts.
2. Choose `Assessments` or `Class Timetable`.
3. Select `DIS3` and `GR1`.
4. Upload the class PDF and optionally the assessment PDF.
5. Click `Parse Timetable`.
6. Confirm the diagnostics and preview panels load on the same page.
7. For the DIS3 sample, confirm the assessment preview shows 4 clean rows:
   - `ADDB6311 Practical Assignment 2`
   - `XISD5319 Task 1`
   - `ADDB6311 Assignment 2 Deferred`
   - `XISD5319 Task 1 Deferred`
8. Confirm there are no merged multi-module assessment rows and no duplicate generic `Assessment` rows.
9. Confirm the export panel clearly says no Google sign-in is required.

## Assessment Export Flow

1. Choose `Assessments`.
2. Parse the sample files again.
3. Confirm the event count is `4` for the DIS3 sample and warning messages are understandable.
4. In the export panel, click `Download Calendar (.ics)`.
5. Confirm the browser downloads an `.ics` file.
6. Import that file into Google Calendar, Apple Calendar, Outlook, or your phone calendar app.
7. Confirm the imported events:
   - use the `[ASSESSMENT]` prefix
   - have the expected dates and times
   - include the assessment details in the event description

## Academic Export Flow

1. Choose `Class Timetable`.
2. Parse the class sample again.
3. Confirm the recurring class rows look correct in the preview panel.
4. In the export panel, click `Download Calendar (.ics)`.
5. Confirm the browser downloads an `.ics` file.
6. Import that file into your preferred calendar app and confirm the imported events:
   - use the `[CLASS]` prefix
   - repeat weekly
   - include venue and lecturer when available

## Optional Smoke Script

- Run parse-only verification:
  - `pwsh -File .\scripts\Invoke-SmokeSync.ps1`
- For any follow-up export smoke test, use the API export endpoints directly or the browser download flow.
