const state = {
  assets: [],
  selectedAssetId: "",
};

const $ = (id) => document.getElementById(id);

const apiState = $("api-state");
const assetSelect = $("asset");
const manualAsset = $("manual-asset");
const tokenInput = $("token");
const runIdInput = $("run-id");

function setApiState(ok) {
  apiState.className = ok ? "state state-ok" : "state state-failed";
  apiState.textContent = ok ? "API ok" : "API failed";
}

function fmt(value) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }
  return String(value);
}

function fmtDate(value) {
  if (!value) {
    return "-";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString();
}

async function request(path, options = {}) {
  const response = await fetch(path, {
    headers: {
      "accept": "application/json",
      ...(options.headers || {}),
    },
    ...options,
  });
  const text = await response.text();
  const payload = text ? JSON.parse(text) : null;
  if (!response.ok) {
    const error = new Error(payload?.error || response.statusText);
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function setFacts(id, entries) {
  const node = $(id);
  node.replaceChildren();
  for (const [label, value] of entries) {
    const dt = document.createElement("dt");
    const dd = document.createElement("dd");
    dt.textContent = label;
    dd.textContent = fmt(value);
    node.append(dt, dd);
  }
}

function selectedAssetId() {
  return manualAsset.value.trim() || assetSelect.value || state.selectedAssetId;
}

function applyAsset(assetId) {
  state.selectedAssetId = assetId;
  $("asset-title").textContent = assetId || "-";
  const asset = state.assets.find((item) => item.asset_id === assetId);
  $("asset-capacity").textContent = asset ? `${asset.capacity_kwh} kWh` : "-";
}

async function loadAssets() {
  const body = await request("/assets");
  state.assets = body.assets || [];
  assetSelect.replaceChildren();

  if (state.assets.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = "manual";
    assetSelect.append(option);
    applyAsset(manualAsset.value.trim());
    return;
  }

  for (const asset of state.assets) {
    const option = document.createElement("option");
    option.value = asset.asset_id;
    option.textContent = asset.asset_id;
    assetSelect.append(option);
  }

  const first = state.assets[0].asset_id;
  assetSelect.value = state.selectedAssetId || first;
  applyAsset(assetSelect.value);
}

async function loadHealth() {
  const health = await request("/health");
  $("health-status").textContent = health.status || "unknown";
  $("health-at").textContent = fmtDate(health.at);

  const regelleistung = await request("/health/regelleistung");
  $("regelleistung-status").textContent = regelleistung.production_gate || "unknown";
  $("regelleistung-at").textContent = fmtDate(regelleistung.at);
}

async function loadAssetStatus(assetId) {
  if (!assetId) {
    setFacts("status-facts", [["Asset", "not selected"]]);
    setFacts("command-facts", [["Command", "not selected"]]);
    return;
  }

  try {
    const status = await request(`/battery/${encodeURIComponent(assetId)}/status`);
    const telemetry = status.telemetry;
    const quality = status.quality;
    setFacts("status-facts", [
      ["SOC", telemetry ? `${telemetry.soc_percent}%` : "-"],
      ["Power", telemetry ? `${telemetry.active_power_kw} kW` : "-"],
      ["Available", telemetry ? telemetry.available : "-"],
      ["Fault", telemetry ? telemetry.fault_status : "-"],
      ["Quality", quality ? `${quality.flag} (${quality.reason})` : "-"],
      ["Observed", fmtDate(status.observed_at)],
    ]);
    const command = status.last_command;
    setFacts("command-facts", [
      ["Mode", command?.mode],
      ["Power", command ? `${command.active_power_kw} kW` : "-"],
      ["Reason", command?.reason],
      ["Valid until", fmtDate(command?.valid_until)],
      ["Source", command?.source],
    ]);
  } catch (error) {
    if (error.status === 404) {
      setFacts("status-facts", [["Status", "no telemetry"]]);
      setFacts("command-facts", [["Command", "none"]]);
      return;
    }
    throw error;
  }
}

async function loadSchedules(assetId) {
  const tbody = $("schedules");
  tbody.replaceChildren();
  if (!assetId) {
    appendScheduleEmpty("No asset selected");
    return;
  }

  const body = await request(`/markets/schedules/current?assetId=${encodeURIComponent(assetId)}`);
  const schedules = body.schedules || [];
  if (schedules.length === 0) {
    appendScheduleEmpty("No active schedules");
    return;
  }

  for (const schedule of schedules) {
    const tr = document.createElement("tr");
    for (const value of [
      schedule.type,
      schedule.version,
      `${fmtDate(schedule.horizon_start)} - ${fmtDate(schedule.horizon_end)}`,
      schedule.windows?.length ?? 0,
    ]) {
      const td = document.createElement("td");
      td.textContent = fmt(value);
      tr.append(td);
    }
    tbody.append(tr);
  }
}

function appendScheduleEmpty(message) {
  const tr = document.createElement("tr");
  const td = document.createElement("td");
  td.colSpan = 4;
  td.className = "empty";
  td.textContent = message;
  tr.append(td);
  $("schedules").append(tr);
}

async function loadStop(assetId) {
  if (!assetId) {
    $("stop-state").textContent = "inactive";
    $("stop-detail").textContent = "-";
    return;
  }
  const body = await request(`/operator/stops/current?assetId=${encodeURIComponent(assetId)}`);
  if (!body.stop) {
    $("stop-state").textContent = "inactive";
    $("stop-detail").textContent = "-";
    return;
  }
  $("stop-state").textContent = "active";
  $("stop-detail").textContent = `${body.stop.reason} by ${body.stop.operator}`;
}

async function loadRun() {
  const runId = runIdInput.value.trim();
  if (!runId) {
    setFacts("run-facts", [["Run", "not selected"]]);
    return;
  }
  const run = await request(`/optimization/runs/${encodeURIComponent(runId)}`);
  setFacts("run-facts", [
    ["Status", run.status],
    ["Asset", run.asset_id],
    ["Solver", run.solver_name],
    ["Reason", run.termination_reason],
    ["Runtime", `${run.solver_runtime_seconds}s`],
    ["Created", fmtDate(run.created_at)],
  ]);
}

async function refresh() {
  try {
    await loadAssets();
    const assetId = selectedAssetId();
    applyAsset(assetId);
    await loadHealth();
    await loadAssetStatus(assetId);
    await loadSchedules(assetId);
    await loadStop(assetId);
    setApiState(true);
  } catch (error) {
    setApiState(false);
    $("action-result").textContent = error.message || "request failed";
  }
}

async function activateStop() {
  const assetId = selectedAssetId();
  const token = tokenInput.value.trim();
  const reason = $("stop-reason").value.trim();
  if (!assetId || !token || !reason) {
    $("action-result").textContent = "asset, token, and reason required";
    return;
  }

  try {
    await request("/operator/stop", {
      method: "POST",
      headers: {
        "authorization": `Bearer ${token}`,
        "content-type": "application/json",
      },
      body: JSON.stringify({ asset_id: assetId, reason }),
    });
    $("action-result").textContent = "operator stop active";
    await loadStop(assetId);
    setApiState(true);
  } catch (error) {
    setApiState(false);
    $("action-result").textContent = error.message || "stop failed";
  }
}

assetSelect.addEventListener("change", () => {
  manualAsset.value = "";
  applyAsset(assetSelect.value);
  refresh();
});

manualAsset.addEventListener("change", () => {
  applyAsset(manualAsset.value.trim());
  refresh();
});

$("refresh").addEventListener("click", refresh);
$("load-run").addEventListener("click", () => {
  loadRun().catch((error) => {
    setApiState(false);
    setFacts("run-facts", [["Error", error.message]]);
  });
});
$("stop").addEventListener("click", activateStop);

setFacts("status-facts", [["Status", "loading"]]);
setFacts("command-facts", [["Command", "loading"]]);
setFacts("run-facts", [["Run", "not selected"]]);
refresh();
