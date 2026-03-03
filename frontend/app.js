const apiBase = "/api/upload";

let parsedAcademicEvents = [];

const fileInput = document.getElementById("fileInput");
const modeInput = document.getElementById("mode");
const textInput = document.getElementById("textInput");
const semesterEndDateInput = document.getElementById("semesterEndDate");
const timeZoneInput = document.getElementById("timeZone");
const previewBtn = document.getElementById("previewBtn");
const previewTextBtn = document.getElementById("previewTextBtn");
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

syncBtn.addEventListener("click", async () => {
  if (modeInput.value !== "Academic") {
    statusOutput.textContent = "Exam sync will be added in the next milestone. Use Academic mode for now.";
    return;
  }

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
});

function applyPreviewResult(data) {
  parsedAcademicEvents = data.academicEvents || [];
  const examEvents = data.examEvents || [];

  textOutput.textContent = data.extractedText || "No text extracted.";
  eventsOutput.textContent = JSON.stringify(
    {
      mode: data.mode,
      academicEvents: data.academicEvents,
      examEvents: data.examEvents,
      warnings: data.warnings
    },
    null,
    2
  );

  statusOutput.textContent = `Preview complete: ${parsedAcademicEvents.length} academic, ${examEvents.length} exam event(s).`;
}
