# Manual Browser Test Checklist

## Preconditions

- Start the API locally: `dotnet run --project backend\TimetableSync.Api.csproj`
- Open `https://localhost:7068`
- Make sure Google OAuth credentials are configured for `https://localhost:7068/oauth/google/callback`
- Have the sample PDFs ready:
  - `timetable_examples\classes\2026-Diploma in Information in Software Development-3rd Year-Gr1-Gr3-AW4-V7.pdf`
  - `timetable_examples\exams\DISD0601 (v1).pdf`

## Auth Flow

1. Open the app home page and confirm the status badge shows Google disconnected.
2. Choose `Assessments` or `Class Timetable`.
3. Select `DIS3` and `GR1`.
4. Upload the class PDF and optionally the assessment PDF.
5. Click `Parse Timetable`.
6. Confirm the review screen loads and no obviously garbled merged assessment rows appear.
7. Continue to the auth step.
8. Click `Sign in with Google`.
9. Complete Google sign-in and consent.
10. After the redirect back to the app, confirm the UI shows an authenticated state.
11. Confirm disconnect works and returns the UI to the disconnected state.

## Assessment Sync Flow

1. Reconnect Google if you disconnected during auth testing.
2. Choose `Assessments`.
3. Parse the sample files again.
4. Confirm the event count looks reasonable and warning messages are understandable.
5. Continue to sync.
6. Click `Sync to Calendar`.
7. Open Google Calendar and confirm the created events:
   - use the `[ASSESSMENT]` prefix
   - have the expected dates and times
   - include popup reminders
8. Return to the app and run a cleanup pass if needed.

## Academic Sync Flow

1. Choose `Class Timetable`.
2. Parse the class sample again.
3. Confirm the recurring class rows look correct.
4. Continue to sync.
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
