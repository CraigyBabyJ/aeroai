const voiceSelect = document.getElementById("voice-select");
const voiceEngineFilter = document.getElementById("voice-engine-filter");
const radioSelect = document.getElementById("radio-select");
const radioIntensityInput = document.getElementById("radio-intensity");
const radioIntensityDisplay = document.getElementById("radio-intensity-display");
const phrasesetSelect = document.getElementById("phraseset-select");
const textInput = document.getElementById("text-input");
const roleSelect = document.getElementById("role-select");
const speedInput = document.getElementById("speed-input");
const pauseHardInput = document.getElementById("pause-hard");
const pauseSoftInput = document.getElementById("pause-soft");
const ttsForm = document.getElementById("tts-form");
const ttsStatus = document.getElementById("tts-status");
const prefetchStatus = document.getElementById("prefetch-status");
const prefetchBtn = document.getElementById("prefetch-btn");
const prefetchResults = document.getElementById("prefetch-results");
const refreshVoicesBtn = document.getElementById("refresh-voices");
const refreshStatsBtn = document.getElementById("refresh-stats");
const recentList = document.getElementById("recent-list");
const clearCacheBtn = document.getElementById("clear-cache");
const cacheToggle = document.getElementById("cache-toggle");
const cacheToggleStatus = document.getElementById("cache-toggle-status");
const player = document.getElementById("player");
const cacheHitBadge = document.getElementById("cache-hit");
const cacheModeBadge = document.getElementById("cache-mode");
const cacheSegmentsBadge = document.getElementById("cache-segments");
const cacheDelimiterBadge = document.getElementById("cache-delimiter");
const cacheKeyBadge = document.getElementById("cache-key");
const speedDisplay = document.getElementById("speed-display");
const insertPipeBtn = document.getElementById("insert-pipe");
const insertNewlineBtn = document.getElementById("insert-newline");
const segmentPreview = document.getElementById("segment-preview");
let voicesCache = [];
let cacheEnabled = true;

const PUNCT_BREAKS = new Set([".", "?", "!", ";"]);

function formatBytes(bytes) {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const idx = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, idx)).toFixed(1)} ${units[idx]}`;
}

function normalizeSegmentText(text) {
  return String(text || "")
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .join(" ");
}

function splitLongSegment(segment, maxLen = 220, minLen = 160) {
  const normalized = normalizeSegmentText(segment);
  if (!normalized) return [];
  if (normalized.length <= maxLen) return [normalized];

  const words = normalized.split(" ");
  const chunks = [];
  let current = "";

  for (const word of words) {
    if (!current) {
      if (word.length <= maxLen) {
        current = word;
      } else {
        for (let i = 0; i < word.length; i += maxLen) chunks.push(word.slice(i, i + maxLen));
        current = "";
      }
      continue;
    }

    const candidate = `${current} ${word}`;
    if (candidate.length <= maxLen) {
      current = candidate;
    } else {
      chunks.push(current);
      current = word;
    }
  }

  if (current) chunks.push(current);

  if (chunks.length >= 2 && chunks[chunks.length - 1].length < minLen) {
    const merged = `${chunks[chunks.length - 2]} ${chunks[chunks.length - 1]}`;
    if (merged.length <= maxLen) {
      chunks.splice(chunks.length - 2, 2, merged);
    }
  }

  return chunks.map(normalizeSegmentText).filter(Boolean);
}

function segmentText(text) {
  const raw = String(text || "");
  let delimiter = "none";
  let base = [];

  if (raw.includes("|")) {
    delimiter = "pipe";
    base = raw.split("|").map(normalizeSegmentText).filter(Boolean);
  } else if (raw.includes("\n") || raw.includes("\r")) {
    delimiter = "newline";
    base = raw
      .split(/\r?\n+/)
      .map(normalizeSegmentText)
      .filter(Boolean);
  } else {
    delimiter = Array.from(PUNCT_BREAKS).some((ch) => raw.includes(ch)) ? "punct" : "none";
    let buf = "";
    for (const ch of raw) {
      buf += ch;
      if (PUNCT_BREAKS.has(ch)) {
        const seg = normalizeSegmentText(buf);
        if (seg) base.push(seg);
        buf = "";
      }
    }
    const tail = normalizeSegmentText(buf);
    if (tail) base.push(tail);
  }

  const segments = [];
  base.forEach((seg) => segments.push(...splitLongSegment(seg, 220, 160)));
  return { segments, delimiter };
}

function setBadge(el, text, isHidden) {
  el.textContent = text;
  el.classList.toggle("hidden", Boolean(isHidden));
}

function updateSegmentPreview() {
  if (!segmentPreview) return;
  const { segments, delimiter } = segmentText(textInput.value);
  if (!segments.length) {
    segmentPreview.innerHTML = `<div class="seg-row">Segments: 0</div>`;
    return;
  }
  const header = `Segments: ${segments.length} | delimiter: ${delimiter}`;
  const lines = segments.map((s, idx) => `<div class="seg-row">${idx + 1}. ${s}</div>`).join("");
  segmentPreview.innerHTML = `<div class="seg-row">${header}</div>${lines}`;
}

function insertAtCursor(textarea, insertText) {
  const start = textarea.selectionStart || 0;
  const end = textarea.selectionEnd || 0;
  const before = textarea.value.slice(0, start);
  const after = textarea.value.slice(end);
  textarea.value = `${before}${insertText}${after}`;
  const newPos = start + insertText.length;
  textarea.setSelectionRange(newPos, newPos);
  textarea.focus();
  updateSegmentPreview();
}

async function fetchJSON(url, options = {}) {
  const res = await fetch(url, options);
  if (!res.ok) {
    const detail = await res.text();
    throw new Error(detail || res.statusText);
  }
  return res.json();
}

async function loadHealth() {
  try {
    const data = await fetchJSON("/health");
    const engine = data.engine || {};
    document.getElementById("health-model").innerText = engine.model_version || "Unknown";
    document.getElementById("health-cuda").innerText = data.cuda_available ? "Yes" : "No";
    const cacheEnabledFlag = data.cache_enabled !== false;
    const cacheLabel = cacheEnabledFlag
      ? `${data.cache_items} | ${formatBytes(data.cache_bytes)}`
      : `Disabled | ${data.cache_items} | ${formatBytes(data.cache_bytes)}`;
    document.getElementById("health-cache").innerText = cacheLabel;
    if (cacheToggle) applyCacheState(cacheEnabledFlag);
    let engineText = "Placeholder (XTTS missing)";
    if (engine.engine_error) {
      engineText = `Error: ${engine.engine_error}`;
    } else if (engine.mode === "xtts") {
      engineText = "XTTS ready";
    } else if (engine.xtts_available) {
      engineText = "XTTS placeholder (libs available)";
    }
    document.getElementById("health-engine").innerText = engineText;

    const coquiRow = document.getElementById("health-coqui");
    if (coquiRow) {
      const coquiText = engine.coqui_error
        ? `Error: ${engine.coqui_error}`
        : engine.coqui_ready
        ? "Coqui ready"
        : engine.coqui_available
        ? "Coqui available"
        : "Unavailable";
      coquiRow.innerText = coquiText;
    }
  } catch (err) {
    document.getElementById("health-model").innerText = "Error";
    document.getElementById("health-cuda").innerText = "-";
    document.getElementById("health-cache").innerText = "-";
    document.getElementById("health-engine").innerText = "Unavailable";
    const coquiRow = document.getElementById("health-coqui");
    if (coquiRow) coquiRow.innerText = "Unavailable";
  }
}

function normalizeEngine(voice) {
  return String(voice.engine || "xtts").toLowerCase();
}

function formatVoiceLabel(voice) {
  const engine = normalizeEngine(voice);
  const name = voice.name || voice.id;
  return `${name} (${engine})`;
}

function renderVoiceOptions(filterEngine = "all") {
  voiceSelect.innerHTML = "";
  const filtered = voicesCache.filter((v) => filterEngine === "all" || normalizeEngine(v) === filterEngine);
  filtered.forEach((voice) => {
    const opt = document.createElement("option");
    opt.value = voice.id;
    opt.textContent = formatVoiceLabel(voice);
    voiceSelect.appendChild(opt);
  });
  if (!filtered.length) {
    const opt = document.createElement("option");
    opt.value = "";
    opt.textContent = "No voices for this filter";
    voiceSelect.appendChild(opt);
  }
}

async function loadVoices() {
  try {
    const data = await fetchJSON("/voices");
    voicesCache = (data.voices || []).map((v) => ({ ...v, engine: normalizeEngine(v) }));
    renderVoiceOptions(voiceEngineFilter ? voiceEngineFilter.value : "all");

    const roles = data.roles && data.roles.length ? data.roles : ["delivery", "ground", "tower", "approach"];
    roleSelect.innerHTML = '<option value="">None</option>';
    roles.forEach((role) => {
      const opt = document.createElement("option");
      opt.value = role;
      opt.textContent = role;
      roleSelect.appendChild(opt);
    });

    radioSelect.innerHTML = '<option value="">Flat / none</option>';
    data.radio_profiles.forEach((profile) => {
      const opt = document.createElement("option");
      opt.value = profile.id;
      opt.textContent = `${profile.name} - ${profile.description}`;
      radioSelect.appendChild(opt);
    });

    phrasesetSelect.innerHTML = "";
    data.phrasesets.forEach((set) => {
      const opt = document.createElement("option");
      opt.value = set.id;
      opt.textContent = `${set.id} (${set.count})`;
      phrasesetSelect.appendChild(opt);
    });
  } catch (err) {
    ttsStatus.textContent = `Voice load failed: ${err.message}`;
    ttsStatus.classList.add("error");
  }
}

function setStatus(el, msg, isError = false) {
  el.textContent = msg;
  el.classList.toggle("error", isError);
}

function applyCacheState(enabled) {
  cacheEnabled = Boolean(enabled);
  if (cacheToggle) cacheToggle.checked = cacheEnabled;
  if (cacheToggleStatus) {
    cacheToggleStatus.textContent = cacheEnabled ? "Enabled" : "Disabled";
    cacheToggleStatus.classList.remove("error");
  }
  if (prefetchBtn) prefetchBtn.disabled = !cacheEnabled;
  if (clearCacheBtn) clearCacheBtn.disabled = !cacheEnabled;
}

async function loadSettings() {
  try {
    const data = await fetchJSON("/settings");
    applyCacheState(data.cache_enabled !== false);
  } catch (err) {
    applyCacheState(true);
    if (cacheToggleStatus) {
      cacheToggleStatus.textContent = "Unknown";
      cacheToggleStatus.classList.add("error");
    }
  }
}

async function saveSettings(enabled) {
  if (cacheToggleStatus) {
    cacheToggleStatus.textContent = "Saving...";
    cacheToggleStatus.classList.remove("error");
  }
  try {
    const data = await fetchJSON("/settings", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ cache_enabled: Boolean(enabled) }),
    });
    applyCacheState(data.cache_enabled !== false);
    await loadHealth();
  } catch (err) {
    if (cacheToggleStatus) {
      cacheToggleStatus.textContent = "Save failed";
      cacheToggleStatus.classList.add("error");
    }
    if (cacheToggle) cacheToggle.checked = !enabled;
  }
}

async function handleTts(event) {
  event.preventDefault();
  setStatus(ttsStatus, "Generating...");
  [cacheHitBadge, cacheModeBadge, cacheSegmentsBadge, cacheDelimiterBadge, cacheKeyBadge].forEach((el) => {
    el.classList.add("hidden");
    el.textContent = "";
  });

  const hardPauseValue = parseInt(pauseHardInput.value || "", 10);
  const softPauseValue = parseInt(pauseSoftInput.value || "", 10);
  const hardPauseMs = Number.isFinite(hardPauseValue) ? hardPauseValue : 70;
  const softPauseMs = Number.isFinite(softPauseValue) ? softPauseValue : 35;

  const payload = {
    text: textInput.value,
    voice_id: voiceSelect.value,
    role: roleSelect.value || null,
    speed: parseFloat(speedInput.value || "1") || 1.0,
    radio_profile: radioSelect.value || null,
    cache_enabled: cacheEnabled,
    format: "wav",
    hard_pause_ms: hardPauseMs,
    soft_pause_ms: softPauseMs,
    pause_hard_ms: hardPauseMs,
    pause_soft_ms: softPauseMs,
    hardPauseMs: hardPauseMs,
    softPauseMs: softPauseMs,
  };
  if (radioSelect.value) {
    const intensityPercent = parseInt(radioIntensityInput.value || "50", 10);
    const clampedPercent = Math.max(0, Math.min(100, Number.isFinite(intensityPercent) ? intensityPercent : 50));
    payload.radio_intensity = clampedPercent / 100.0;
  }

  try {
    const res = await fetch("/tts", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      const detail = await res.text();
      throw new Error(detail || res.statusText);
    }
    const audioBlob = await res.blob();
    const objectUrl = URL.createObjectURL(audioBlob);
    player.src = objectUrl;
    player.play();
    const cacheHeader = (res.headers.get("x-cache") || "").toUpperCase();
    const cacheMode = res.headers.get("x-cache-mode") || "";
    const segCount = res.headers.get("x-cache-segments") || "";
    const hitCount = res.headers.get("x-cache-hits") || "";
    const segDelimiter = res.headers.get("x-segment-delimiter") || "";
    const cacheKey = res.headers.get("x-cache-key") || "";

    if (cacheHeader) setBadge(cacheHitBadge, `Cache: ${cacheHeader}`, false);
    if (cacheMode) setBadge(cacheModeBadge, `mode: ${cacheMode}`, false);
    if (segCount || hitCount) setBadge(cacheSegmentsBadge, `segments: ${segCount || "?"} hits: ${hitCount || "?"}`, false);
    if (segDelimiter) setBadge(cacheDelimiterBadge, `delim: ${segDelimiter}`, false);
    if (cacheKey) setBadge(cacheKeyBadge, cacheKey, false);

    setStatus(ttsStatus, "Ready");
    await loadHealth();
    await loadRecent();
  } catch (err) {
    setStatus(ttsStatus, err.message || "Failed to generate", true);
  }
}

async function handlePrefetch() {
  if (!cacheEnabled) {
    setStatus(prefetchStatus, "Cache disabled.", true);
    return;
  }
  setStatus(prefetchStatus, "Prefetching...");
  prefetchResults.innerHTML = "";

  const payload = {
    voice_id: voiceSelect.value,
    role: roleSelect.value || null,
    radio_profile: radioSelect.value || null,
    phraseset: phrasesetSelect.value,
    speed: parseFloat(speedInput.value || "1") || 1.0,
    limit: parseInt(document.getElementById("prefetch-limit").value || "0", 10) || null,
  };

  try {
    const data = await fetchJSON("/prefetch", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    setStatus(prefetchStatus, `Cached ${data.count} phrase(s).`);
    const list = document.createElement("ul");
    data.items.forEach((item) => {
      const li = document.createElement("li");
      li.textContent = `${item.from_cache ? "HIT" : "+"} ${item.text}`;
      list.appendChild(li);
    });
    prefetchResults.appendChild(list);
    await loadHealth();
    await loadRecent();
  } catch (err) {
    setStatus(prefetchStatus, err.message || "Prefetch failed", true);
  }
}

async function loadRecent() {
  recentList.textContent = "Loading...";
  try {
    const data = await fetchJSON("/cache/recent?limit=20");
    if (!data.length) {
      recentList.textContent = "No cache entries yet.";
      return;
    }
    const list = document.createElement("ul");
    data.forEach((row) => {
      const created = row.created_at ? ` | ${row.created_at}` : "";
      const hits = typeof row.hit_count !== "undefined" ? ` (hits: ${row.hit_count || 0})` : "";
      const li = document.createElement("li");
      const radio = row.radio_profile ? ` | ${row.radio_profile}` : "";
      const role = row.role ? ` | ${row.role}` : "";
      const speed = typeof row.speed === "number" ? ` | x${Number(row.speed).toFixed(1)}` : "";
      li.textContent = `${row.voice_id}${role}${radio}${speed} | ${row.text_norm}${created}${hits}`;
      list.appendChild(li);
    });
    recentList.innerHTML = "";
    recentList.appendChild(list);
  } catch (err) {
    recentList.textContent = "Failed to load cache history.";
  }
}

async function clearCache() {
  if (!cacheEnabled) {
    alert("Cache is disabled.");
    return;
  }
  if (!confirm("Clear cached audio files and index?")) return;
  try {
    await fetchJSON("/cache/clear", { method: "POST" });
    await loadHealth();
    await loadRecent();
  } catch (err) {
    alert("Could not clear cache.");
  }
}

document.getElementById("prefetch-btn").addEventListener("click", (e) => {
  e.preventDefault();
  handlePrefetch();
});

ttsForm.addEventListener("submit", handleTts);
refreshVoicesBtn.addEventListener("click", loadVoices);
refreshStatsBtn.addEventListener("click", () => {
  loadHealth();
  loadRecent();
});
clearCacheBtn.addEventListener("click", clearCache);
speedInput.addEventListener("input", () => {
  speedDisplay.textContent = `${parseFloat(speedInput.value).toFixed(1)}x`;
});
if (radioIntensityInput && radioIntensityDisplay) {
  radioIntensityInput.addEventListener("input", () => {
    const pct = parseInt(radioIntensityInput.value || "50", 10);
    const clamped = Math.max(0, Math.min(100, Number.isFinite(pct) ? pct : 50));
    radioIntensityDisplay.textContent = `${clamped}%`;
  });
}
textInput.addEventListener("input", updateSegmentPreview);
if (insertPipeBtn) insertPipeBtn.addEventListener("click", () => insertAtCursor(textInput, " | "));
if (insertNewlineBtn) insertNewlineBtn.addEventListener("click", () => insertAtCursor(textInput, "\n"));

window.addEventListener("DOMContentLoaded", async () => {
  await Promise.all([loadVoices(), loadHealth(), loadRecent(), loadSettings()]);
  textInput.value =
    "Easy one two three | radio check | wind two seven zero at one two knots";
  speedDisplay.textContent = `${parseFloat(speedInput.value).toFixed(1)}x`;
  if (radioIntensityInput && radioIntensityDisplay) {
    const pct = parseInt(radioIntensityInput.value || "50", 10);
    const clamped = Math.max(0, Math.min(100, Number.isFinite(pct) ? pct : 50));
    radioIntensityDisplay.textContent = `${clamped}%`;
  }
  updateSegmentPreview();

  if (voiceEngineFilter) {
    voiceEngineFilter.addEventListener("change", () => {
      renderVoiceOptions(voiceEngineFilter.value);
    });
  }
  if (cacheToggle) {
    cacheToggle.addEventListener("change", () => {
      saveSettings(cacheToggle.checked);
    });
  }
});
