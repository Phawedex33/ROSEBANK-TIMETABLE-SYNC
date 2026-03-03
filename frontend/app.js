const academicApi = "/api/academic";
const assessmentApi = "/api/assessment";

const days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

let academicRows = [];
let assessmentRows = [];
let availableModules = [];
let selectedModules = new Set();

const modeInput = document.getElementById("mode");
const fileInput = document.getElementById("fileInput");
const assessmentTextInput = document.getElementById("assessmentText");
const assessmentTextWrap = document.getElementById("assessmentTextWrap");
const yearInput = document.getElementById("yearInput");
const groupInput = document.getElementById("groupInput");
const durationInput = document.getElementById("durationInput");
const timeZoneInput = document.getElementById("timeZone");
const academicFields = document.getElementById("academicFields");
const assessmentFields = document.getElementById("assessmentFields");
const academicTableWrap = document.getElementById("academicTableWrap");
const assessmentTableWrap = document.getElementById("assessmentTableWrap");
const academicBody = document.getElementById("academicBody");
const assessmentBody = document.getElementById("assessmentBody");
const previewBtn = document.getElementById("previewBtn");
const syncBtn = document.getElementById("syncBtn");
const addAcademicRowBtn = document.getElementById("addAcademicRowBtn");
const addAssessmentRowBtn = document.getElementById("addAssessmentRowBtn");
const selectAllModulesBtn = document.getElementById("selectAllModulesBtn");
const modulesWrap = document.getElementById("modulesWrap");
const textOutput = document.getElementById("textOutput");
const diagnosticsOutput = document.getElementById("diagnosticsOutput");
const statusOutput = document.getElementById("statusOutput");

modeInput.addEventListener("change", applyMode);
previewBtn.addEventListener("click", onPreview);
syncBtn.addEventListener("click", onSync);
selectAllModulesBtn.addEventListener("click", () => {
  selectedModules = new Set(availableModules);
  renderModules();
});
addAcademicRowBtn.addEventListener("click", () => {
  academicRows.push({ day: "Monday", startTime: "08:00", endTime: "08:50", subject: "" });
  refreshModulePicker();
  renderAcademicTable();
});
addAssessmentRowBtn.addEventListener("click", () => {
  assessmentRows.push({
    moduleCode: "",
    moduleName: "",
    assessmentType: "",
    date: "",
    time: "23:59",
    deliveryMode: "Unspecified",
    sitting: null
  });
  refreshModulePicker();
  renderAssessmentTable();
});

applyMode();
renderAcademicTable();
renderAssessmentTable();
refreshModulePicker();

function applyMode() {
  const isAcademic = modeInput.value === "academic";

  academicFields.classList.toggle("hidden", !isAcademic);
  academicTableWrap.classList.toggle("hidden", !isAcademic);

  assessmentFields.classList.toggle("hidden", isAcademic);
  assessmentTableWrap.classList.toggle("hidden", isAcademic);
  assessmentTextWrap.classList.toggle("hidden", isAcademic);

  refreshModulePicker();
}

async function onPreview() {
  try {
    statusOutput.textContent = "Parsing file...";

    if (modeInput.value === "academic") {
      await previewAcademic();
      return;
    }

    await previewAssessment();
  } catch (err) {
    statusOutput.textContent = err.message;
  }
}

async function previewAcademic() {
  const file = fileInput.files[0];
  if (!file) {
    throw new Error("Choose a class timetable file first.");
  }

  const form = new FormData();
  form.append("file", file);
  form.append("year", yearInput.value);
  form.append("group", groupInput.value);

  const res = await fetch(`${academicApi}/preview`, {
    method: "POST",
    body: form
  });

  if (!res.ok) {
    throw new Error(await getErrorText(res, "Class timetable preview failed."));
  }

  const data = await res.json();
  academicRows = (data.events || []).map((eventItem) => ({
    day: eventItem.day || "Monday",
    startTime: normalizeTime(eventItem.startTime),
    endTime: normalizeTime(eventItem.endTime),
    subject: eventItem.subject || ""
  }));

  textOutput.textContent = data.extractedText || "No extracted text.";
  updateDiagnostics(data.diagnostics || [], data.warnings || []);
  refreshModulePicker();
  renderAcademicTable();

  const warningCount = (data.warnings || []).length;
  statusOutput.textContent = `Class preview complete: ${academicRows.length} row(s), ${warningCount} warning(s).`;
}

async function previewAssessment() {
  const file = fileInput.files[0];
  const pastedText = assessmentTextInput.value.trim();

  if (!file && !pastedText) {
    throw new Error("Provide an assessment file or paste assessment text.");
  }

  const form = new FormData();
  if (file) {
    form.append("file", file);
  }
  if (pastedText) {
    form.append("text", pastedText);
  }

  const res = await fetch(`${assessmentApi}/preview`, {
    method: "POST",
    body: form
  });

  if (!res.ok) {
    throw new Error(await getErrorText(res, "Assessment preview failed."));
  }

  const data = await res.json();
  assessmentRows = (data.events || []).map((eventItem) => ({
    moduleCode: eventItem.moduleCode || "",
    moduleName: eventItem.moduleName || "",
    assessmentType: eventItem.assessmentType || "",
    date: eventItem.date || "",
    time: normalizeTime(eventItem.time),
    deliveryMode: eventItem.deliveryMode || "Unspecified",
    sitting: eventItem.sitting ?? null
  }));

  textOutput.textContent = data.extractedText || "No extracted text.";
  updateDiagnostics(data.diagnostics || [], data.warnings || []);
  refreshModulePicker();
  renderAssessmentTable();

  const warningCount = (data.warnings || []).length;
  statusOutput.textContent = `Assessment preview complete: ${assessmentRows.length} row(s), ${warningCount} warning(s).`;
}

async function onSync() {
  try {
    if (modeInput.value === "academic") {
      await syncAcademic();
      return;
    }

    await syncAssessment();
  } catch (err) {
    statusOutput.textContent = err.message;
  }
}

async function syncAcademic() {
  if (academicRows.length === 0) {
    throw new Error("No class rows to sync.");
  }

  const filtered = academicRows.filter((row) => selectedModules.has(getAcademicModuleKey(row.subject)));
  if (filtered.length === 0) {
    throw new Error("Select at least one module to sync.");
  }

  statusOutput.textContent = "Syncing class events...";

  const payload = {
    year: Number(yearInput.value),
    group: groupInput.value,
    weeksDuration: 16,
    timeZone: timeZoneInput.value || "Africa/Johannesburg",
    events: filtered.map((row) => ({
      day: row.day,
      startTime: toApiTime(row.startTime),
      endTime: toApiTime(row.endTime),
      subject: row.subject
    }))
  };

  const res = await fetch(`${academicApi}/sync`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!res.ok) {
    throw new Error(await getErrorText(res, "Class sync failed."));
  }

  const data = await res.json();
  statusOutput.textContent = `Class sync complete: ${data.created} recurring event(s) created.`;
}

async function syncAssessment() {
  if (assessmentRows.length === 0) {
    throw new Error("No assessment rows to sync.");
  }

  const filtered = assessmentRows.filter((row) => selectedModules.has(getAssessmentModuleKey(row)));
  if (filtered.length === 0) {
    throw new Error("Select at least one module to sync.");
  }

  statusOutput.textContent = "Syncing assessment events...";

  const duration = Number(durationInput.value) || 60;
  const payload = {
    timeZone: timeZoneInput.value || "Africa/Johannesburg",
    durationMinutes: duration,
    events: filtered.map((row) => ({
      moduleCode: row.moduleCode,
      moduleName: row.moduleName,
      assessmentType: row.assessmentType,
      date: row.date,
      time: toApiTime(row.time),
      deliveryMode: row.deliveryMode,
      sitting: row.sitting === "" || row.sitting === null ? null : Number(row.sitting)
    }))
  };

  const res = await fetch(`${assessmentApi}/sync`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!res.ok) {
    throw new Error(await getErrorText(res, "Assessment sync failed."));
  }

  const data = await res.json();
  statusOutput.textContent = `Assessment sync complete: ${data.created} event(s) created.`;
}

function renderAcademicTable() {
  academicBody.innerHTML = "";

  for (let i = 0; i < academicRows.length; i += 1) {
    const row = academicRows[i];
    const tr = document.createElement("tr");

    tr.innerHTML = `
      <td>
        <select data-field="day" data-index="${i}">
          ${days.map((d) => `<option value="${d}" ${d === row.day ? "selected" : ""}>${d}</option>`).join("")}
        </select>
      </td>
      <td><input type="time" data-field="startTime" data-index="${i}" value="${escapeAttr(row.startTime)}" /></td>
      <td><input type="time" data-field="endTime" data-index="${i}" value="${escapeAttr(row.endTime)}" /></td>
      <td><input type="text" data-field="subject" data-index="${i}" value="${escapeAttr(row.subject)}" /></td>
      <td class="td-actions"><button type="button" data-delete="academic" data-index="${i}">Delete</button></td>
    `;

    academicBody.appendChild(tr);
  }

  bindTableInputs(academicBody, academicRows);
}

function renderAssessmentTable() {
  assessmentBody.innerHTML = "";

  for (let i = 0; i < assessmentRows.length; i += 1) {
    const row = assessmentRows[i];
    const tr = document.createElement("tr");

    tr.innerHTML = `
      <td><input type="text" data-field="moduleCode" data-index="${i}" value="${escapeAttr(row.moduleCode)}" /></td>
      <td><input type="text" data-field="moduleName" data-index="${i}" value="${escapeAttr(row.moduleName)}" /></td>
      <td><input type="text" data-field="assessmentType" data-index="${i}" value="${escapeAttr(row.assessmentType)}" /></td>
      <td><input type="number" min="1" max="2" data-field="sitting" data-index="${i}" value="${escapeAttr(row.sitting ?? "")}" /></td>
      <td><input type="date" data-field="date" data-index="${i}" value="${escapeAttr(row.date)}" /></td>
      <td><input type="time" data-field="time" data-index="${i}" value="${escapeAttr(row.time)}" /></td>
      <td>
        <select data-field="deliveryMode" data-index="${i}">
          <option value="Unspecified" ${row.deliveryMode === "Unspecified" ? "selected" : ""}>Unspecified</option>
          <option value="Campus Sitting" ${row.deliveryMode === "Campus Sitting" ? "selected" : ""}>Campus Sitting</option>
          <option value="Online Submission" ${row.deliveryMode === "Online Submission" ? "selected" : ""}>Online Submission</option>
        </select>
      </td>
      <td class="td-actions"><button type="button" data-delete="assessment" data-index="${i}">Delete</button></td>
    `;

    assessmentBody.appendChild(tr);
  }

  bindTableInputs(assessmentBody, assessmentRows);
}

function bindTableInputs(container, rows) {
  container.querySelectorAll("input, select").forEach((el) => {
    el.addEventListener("input", onCellInput);
    el.addEventListener("change", onCellInput);
  });

  container.querySelectorAll("button[data-delete]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const mode = btn.getAttribute("data-delete");
      const index = Number(btn.getAttribute("data-index"));
      if (mode === "academic") {
        academicRows.splice(index, 1);
        refreshModulePicker();
        renderAcademicTable();
      } else {
        assessmentRows.splice(index, 1);
        refreshModulePicker();
        renderAssessmentTable();
      }
    });
  });

  function onCellInput(event) {
    const index = Number(event.target.getAttribute("data-index"));
    const field = event.target.getAttribute("data-field");
    rows[index][field] = event.target.value;
    refreshModulePicker();
  }
}

function refreshModulePicker() {
  if (modeInput.value === "academic") {
    availableModules = [...new Set(academicRows.map((row) => getAcademicModuleKey(row.subject)).filter(Boolean))].sort();
  } else {
    availableModules = [...new Set(assessmentRows.map(getAssessmentModuleKey).filter(Boolean))].sort();
  }

  selectedModules = new Set(availableModules.filter((m) => selectedModules.has(m) || selectedModules.size === 0));
  if (selectedModules.size === 0) {
    selectedModules = new Set(availableModules);
  }

  renderModules();
}

function renderModules() {
  modulesWrap.innerHTML = "";

  if (availableModules.length === 0) {
    const empty = document.createElement("span");
    empty.textContent = "No modules detected yet. Preview a timetable first.";
    modulesWrap.appendChild(empty);
    return;
  }

  for (const module of availableModules) {
    const label = document.createElement("label");
    label.className = "chip";

    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = selectedModules.has(module);
    checkbox.addEventListener("change", () => {
      if (checkbox.checked) {
        selectedModules.add(module);
      } else {
        selectedModules.delete(module);
      }
    });

    const text = document.createElement("span");
    text.textContent = module;

    label.appendChild(checkbox);
    label.appendChild(text);
    modulesWrap.appendChild(label);
  }
}

function getAcademicModuleKey(subject) {
  const m = String(subject || "").match(/[A-Z]{4}\d{4}/);
  return m ? m[0] : String(subject || "").trim();
}

function getAssessmentModuleKey(row) {
  return String(row.moduleCode || row.moduleName || "").trim();
}

function normalizeTime(value) {
  if (!value) {
    return "";
  }

  return value.length >= 5 ? value.slice(0, 5) : value;
}

function toApiTime(value) {
  if (!value) {
    return "00:00:00";
  }

  if (value.length === 5) {
    return `${value}:00`;
  }

  return value;
}

function escapeAttr(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("\"", "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

async function getErrorText(res, fallback) {
  const text = await res.text();
  return text || fallback;
}

function updateDiagnostics(diagnostics, warnings) {
  const lines = [];
  if (diagnostics.length > 0) {
    lines.push("Diagnostics:");
    for (const item of diagnostics) {
      lines.push(`- ${item}`);
    }
  } else {
    lines.push("Diagnostics: none");
  }

  if (warnings.length > 0) {
    lines.push("");
    lines.push("Warnings:");
    for (const item of warnings) {
      lines.push(`- ${item}`);
    }
  }

  diagnosticsOutput.textContent = lines.join("\n");
}
