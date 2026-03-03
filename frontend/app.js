const apiBase = "/api/upload";

let parsedAcademicEvents = [];
let parsedExamEvents = [];

const fileInput = document.getElementById("fileInput");
const modeInput = document.getElementById("mode");
const textInput = document.getElementById("textInput");
const groupInput = document.getElementById("groupInput");
const academicDraftInput = document.getElementById("academicDraftInput");
const semesterEndDateInput = document.getElementById("semesterEndDate");
const timeZoneInput = document.getElementById("timeZone");
const previewBtn = document.getElementById("previewBtn");
const previewTextBtn = document.getElementById("previewTextBtn");
const buildAcademicBtn = document.getElementById("buildAcademicBtn");
const syncBtn = document.getElementById("syncBtn");
const textOutput = document.getElementById("textOutput");
const eventsOutput = document.getElementById("eventsOutput");
const statusOutput = document.getElementById("statusOutput");

previewBtn.addEventListener("click", async () => {
  const file = fileInput.files[0];
  if (!file) {
    statusOutput.textContent = "Choose a file first.";
    return;
  }

  const form = new FormData();
  form.append("file", file);
  form.append("mode", modeInput.value);

  statusOutput.textContent = "Extracting and parsing...";

  try {
    const res = await fetch(`${apiBase}/preview`, {
      method: "POST",
      body: form
    });

    if (!res.ok) {
      const error = await res.text();
      throw new Error(error || "Preview failed.");
    }

    const data = await res.json();
    applyPreviewResult(data);
  } catch (err) {
    statusOutput.textContent = err.message;
  }
});

previewTextBtn.addEventListener("click", async () => {
  if (!textInput.value.trim()) {
    statusOutput.textContent = "Paste text first for text preview mode.";
    return;
  }

  statusOutput.textContent = "Parsing pasted text...";

  try {
    const res = await fetch(`${apiBase}/preview-text`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        mode: modeInput.value,
        text: textInput.value
      })
    });

    if (!res.ok) {
      const error = await res.text();
      throw new Error(error || "Preview text failed.");
    }

    const data = await res.json();
    applyPreviewResult(data);
  } catch (err) {
    statusOutput.textContent = err.message;
  }
});

buildAcademicBtn.addEventListener("click", async () => {
  if (!academicDraftInput.value.trim()) {
    statusOutput.textContent = "Paste academic draft JSON rows first.";
    return;
  }

  let rows;
  try {
    rows = JSON.parse(academicDraftInput.value);
    if (!Array.isArray(rows)) {
      throw new Error("JSON must be an array.");
    }
  } catch (err) {
    statusOutput.textContent = `Invalid JSON: ${err.message}`;
    return;
  }

  statusOutput.textContent = "Building academic events from period mapping...";

  try {
    const res = await fetch(`${apiBase}/build-academic`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        group: groupInput.value || "",
        rows
      })
    });

    if (!res.ok) {
      const error = await res.text();
      throw new Error(error || "Build academic failed.");
    }

    const data = await res.json();
    parsedAcademicEvents = data.events || [];
    modeInput.value = "Academic";
    eventsOutput.textContent = JSON.stringify(
      {
        mode: "Academic",
        academicEvents: parsedAcademicEvents,
        warnings: data.warnings || []
      },
      null,
      2
    );
    statusOutput.textContent = `Academic build complete: ${parsedAcademicEvents.length} event(s).`;
  } catch (err) {
    statusOutput.textContent = err.message;
  }
});

syncBtn.addEventListener("click", async () => {
  if (modeInput.value === "Academic") {
    if (parsedAcademicEvents.length === 0) {
      statusOutput.textContent = "No parsed academic events to sync. Run preview first.";
      return;
    }

    if (!semesterEndDateInput.value) {
      statusOutput.textContent = "Select semester end date.";
      return;
    }

    statusOutput.textContent = "Syncing weekly events to Google Calendar...";

    try {
      const res = await fetch(`${apiBase}/sync`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          events: parsedAcademicEvents,
          semesterEndDate: semesterEndDateInput.value,
          timeZone: timeZoneInput.value || "Africa/Johannesburg"
        })
      });

      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Sync failed.");
      }

      const data = await res.json();
      statusOutput.textContent = `Success: ${data.created} recurring event(s) created.`;
    } catch (err) {
      statusOutput.textContent = err.message;
    }
    return;
  }

  if (parsedExamEvents.length === 0) {
    statusOutput.textContent = "No parsed exam events to sync. Run preview first.";
    return;
  }

  statusOutput.textContent = "Syncing exam events to Google Calendar...";

  try {
    const res = await fetch(`${apiBase}/sync-exam`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        events: parsedExamEvents,
        timeZone: timeZoneInput.value || "Africa/Johannesburg",
        durationMinutes: 60
      })
    });

    if (!res.ok) {
      const error = await res.text();
      throw new Error(error || "Exam sync failed.");
    }

    const data = await res.json();
    statusOutput.textContent = `Success: ${data.created} exam event(s) created.`;
  } catch (err) {
    statusOutput.textContent = err.message;
  }
});

function applyPreviewResult(data) {
  parsedAcademicEvents = data.academicEvents || [];
  parsedExamEvents = data.examEvents || [];

  textOutput.textContent = data.extractedText || "No text extracted.";
  eventsOutput.textContent = JSON.stringify(
    {
      mode: data.mode,
      academicEvents: data.academicEvents,
      examEvents: parsedExamEvents,
      warnings: data.warnings
    },
    null,
    2
  );

  statusOutput.textContent = `Preview complete: ${parsedAcademicEvents.length} academic, ${parsedExamEvents.length} exam event(s).`;
}
