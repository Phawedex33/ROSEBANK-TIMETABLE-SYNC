# Manual Browser Test Checklist

## Preconditions

- Start the API locally: `dotnet run --project backend\TimetableSync.Api.csproj`
- Open `https://localhost:7068`
- Make sure Google OAuth credentials are configured for `https://localhost:7068/oauth/google/callback`
- Have the sample PDFs ready:
  - `timetable_examples\classes\2026-Diploma in Information in Software Development-3rd Year-Gr1-Gr3-AW4-V7.pdf`
  - `timetable_examples\exams\DISD0601 (v1).pdf`

## Auth Flow

1. Open the app home page and confirm the top status area shows Google disconnected.
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
9. In the sync panel, click `Connect Google`.
10. Complete Google sign-in and consent.
11. After the redirect back to the app, confirm the UI shows an authenticated state.
12. Confirm disconnect works and returns the UI to the disconnected state.

## Assessment Sync Flow

1. Reconnect Google if you disconnected during auth testing.
2. Choose `Assessments`.
3. Parse the sample files again.
4. Confirm the event count is `4` for the DIS3 sample and warning messages are understandable.
5. Stay on the same page and use the sync panel.
6. Click `Sync to Calendar`.
7. Open Google Calendar and confirm the created events:
   - use the `[ASSESSMENT]` prefix
   - have the expected dates and times
   - include popup reminders
8. Return to the app and run a cleanup pass if needed.

## Academic Sync Flow

1. Choose `Class Timetable`.
2. Parse the class sample again.
3. Confirm the recurring class rows look correct in the preview panel.
4. Stay on the same page and use the sync panel.
5. Click `Sync to Calendar`.
6. Open Google Calendar and confirm the created events:
   - use the `[CLASS]` prefix
   - repeat weekly
   - include venue and lecturer when available
7. Verify cleanup through `POST /api/calendar/delete-synced` or the smoke script if you want a reset.

## Optional Smoke Script

- Copy the browser `sync_user_id` cookie value after a successful login.
- Run parse-only verification:
  - `pwsh -File .\scripts\Invoke-SmokeSync.ps1`
- Run an academic sync smoke test with cleanup:
  - `pwsh -File .\scripts\Invoke-SmokeSync.ps1 -Mode academic -SyncUserId '<cookie>' -DeleteAfterSync`
- Run an assessment sync smoke test with cleanup:
  - `pwsh -File .\scripts\Invoke-SmokeSync.ps1 -Mode assessment -SyncUserId '<cookie>' -DeleteAfterSync`
