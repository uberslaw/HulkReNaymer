const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

const state = {
  path: "",
  rows: [],
  selected: null,
  sort: "name",
  desc: false,
  mapping: {},
  previewTimer: null,
};

const PRESETS = {
  project: {
    case_mode: "title",
    replace_enabled: true,
    find: " ",
    replace_with: "_",
    add_enabled: true,
    prefix: "PROJECT123_",
  },
  export: {
    replace_enabled: true,
    find: "EXPORT_20260914_",
    replace_with: "",
  },
  photos: {
    name_enabled: true,
    name_mode: "remove",
    date_enabled: true,
    date_source: "exif",
    date_format: "%Y-%m-%d",
    date_position: "prefix",
    date_separator: "_",
    add_enabled: true,
    suffix: "Site-Inspection",
    numbering_enabled: true,
    number_start: 1,
    number_pad: 3,
    number_position: "suffix",
    number_separator: "_",
  },
  "title-underscore": {
    case_mode: "title",
    replace_enabled: true,
    find: " ",
    replace_with: "_",
  },
  folder: {
    folder_enabled: true,
    folder_mode: "prefix",
    folder_separator: "_",
  },
  mp3: {
    name_enabled: true,
    name_mode: "fixed",
    name_fixed: "{id3.artist} - {id3.title}",
  },
};

function readRules() {
  const rules = {};
  $$("[data-field]").forEach((el) => {
    const key = el.dataset.field;
    if (el.type === "checkbox") rules[key] = el.checked;
    else if (el.type === "number") rules[key] = Number(el.value || 0);
    else rules[key] = el.value;
  });
  rules.mapping = state.mapping;
  return rules;
}

function writeRules(rules) {
  $$("[data-field]").forEach((el) => {
    const key = el.dataset.field;
    if (!(key in rules)) return;
    if (el.type === "checkbox") el.checked = Boolean(rules[key]);
    else el.value = rules[key] ?? "";
  });
}

function resetRules() {
  $$("[data-field]").forEach((el) => {
    if (el.type === "checkbox") {
      el.checked = el.dataset.field === "replace_all"
        || el.dataset.field === "replace_case_sensitive"
        || el.dataset.field === "mapping_exclusive"
        || el.dataset.field === "windows_safe";
    } else if (el.type === "number") {
      el.value = el.dataset.field === "number_start" || el.dataset.field === "number_increment" || el.dataset.field === "number_pad"
        ? (el.dataset.field === "number_pad" ? 3 : 1)
        : 0;
    } else if (el.tagName === "SELECT") {
      el.selectedIndex = 0;
    } else if (el.dataset.field === "date_format") {
      el.value = "%Y-%m-%d";
    } else if (el.dataset.field === "date_separator" || el.dataset.field === "folder_separator" || el.dataset.field === "number_separator") {
      el.value = "_";
    } else {
      el.value = "";
    }
  });
  state.mapping = {};
  $("#mapping-text").value = "";
  queuePreview();
}

function scanFields() {
  const extra = {};
  $$("[data-scan]").forEach((el) => {
    extra[el.dataset.scan] = el.type === "checkbox" ? el.checked : (el.type === "number" ? Number(el.value || 0) : el.value);
  });
  return extra;
}

function scanPayload() {
  return {
    path: $("#path").value.trim(),
    recurse: $("#recurse").checked,
    include_files: $("#include-files").checked,
    include_folders: $("#include-folders").checked,
    wildcard: $("#wildcard").value.trim() || "*",
    ...scanFields(),
  };
}

async function api(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    throw new Error(data.detail || data.message || res.statusText);
  }
  return data;
}

function formatSize(bytes) {
  if (!bytes) return "";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function renderRows(rows) {
  state.rows = rows;
  const body = $("#files tbody");
  body.innerHTML = "";
  const selectedPaths = state.selected == null
    ? rows.filter((row) => row.selected).map((row) => row.path)
    : state.selected;
  const selected = new Set(selectedPaths);
  for (const row of rows) {
    const tr = document.createElement("tr");
    tr.className = row.status;
    tr.dataset.path = row.path;
    const checked = selected.has(row.path);
    tr.innerHTML = `
      <td class="check"><input type="checkbox" ${checked ? "checked" : ""} /></td>
      <td class="old" title="${row.path}">${escapeHtml(row.old_name)}</td>
      <td class="new" title="${escapeHtml(row.new_name)}">${escapeHtml(row.new_name)}</td>
      <td>${escapeHtml(row.ext)}</td>
      <td>${formatSize(row.size)}</td>
      <td>${escapeHtml((row.modified || "").replace("T", " ").slice(0, 19))}</td>
      <td>${escapeHtml(row.folder)}</td>
      <td><span class="status-pill ${row.status}" title="${escapeHtml(row.warning || "")}">${row.status}</span></td>
    `;
    body.appendChild(tr);
  }
  $("#select-all").checked = rows.length > 0 && rows.every((row) => selected.has(row.path));
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function currentSelected() {
  return $$("#files tbody tr").filter((tr) => tr.querySelector("input").checked).map((tr) => tr.dataset.path);
}

function updateStats(counts, warning) {
  const bits = [
    `${counts.total || 0} listed`,
    `${counts.selected || 0} selected`,
    `${counts.changed || 0} will change`,
    `${counts.unchanged || 0} unchanged`,
    `${counts.conflicts || 0} conflicts`,
    `${counts.invalid || 0} invalid`,
  ];
  if (warning) bits.push(warning);
  $("#stats").textContent = bits.join(" · ");
  $("#btn-rename").disabled = !counts.changed || counts.conflicts || counts.invalid;
}

async function preview() {
  if (!$("#path").value.trim()) return;
  const payload = {
    ...scanPayload(),
    rules: readRules(),
    selected: state.selected,
    sort_column: state.sort,
    sort_desc: state.desc,
  };
  const data = await api("/api/preview", { method: "POST", body: JSON.stringify(payload) });
  renderRows(data.rows);
  updateStats(data.counts, data.warning);
}

function queuePreview() {
  clearTimeout(state.previewTimer);
  state.previewTimer = setTimeout(() => {
    preview().catch((err) => {
      $("#stats").textContent = err.message;
    });
  }, 180);
}

async function loadTree(path) {
  const data = await api(`/api/tree?path=${encodeURIComponent(path)}`);
  const tree = $("#tree");
  tree.innerHTML = "";
  if (data.parent) {
    const up = document.createElement("button");
    up.textContent = "… parent";
    up.onclick = () => openPath(data.parent);
    tree.appendChild(up);
  }
  for (const child of data.children) {
    const btn = document.createElement("button");
    btn.textContent = child.name;
    btn.onclick = () => openPath(child.path);
    if (child.path === path) btn.classList.add("active");
    tree.appendChild(btn);
  }
}

async function openPath(path) {
  $("#path").value = path;
  state.path = path;
  state.selected = null;
  await loadTree(path);
  $$("#roots button").forEach((btn) => btn.classList.toggle("active", btn.dataset.path === path));
  await preview();
}

async function loadRoots() {
  const data = await api("/api/roots");
  const box = $("#roots");
  box.innerHTML = "";
  for (const root of data.roots) {
    const btn = document.createElement("button");
    btn.textContent = root.name;
    btn.dataset.path = root.path;
    btn.onclick = () => openPath(root.path);
    box.appendChild(btn);
  }
  const start = data.roots.find((r) => r.name === "Demo files")?.path || data.cwd;
  await openPath(start);
}

async function loadFavorites() {
  const data = await api("/api/favorites");
  const sel = $("#favorites");
  sel.innerHTML = `<option value="">Favourites…</option>`;
  for (const fav of data.favorites) {
    const opt = document.createElement("option");
    opt.value = fav.name;
    opt.textContent = fav.name;
    sel.appendChild(opt);
  }
  sel.onchange = () => {
    const fav = data.favorites.find((item) => item.name === sel.value);
    if (fav) {
      resetRules();
      writeRules(fav.rules);
      state.mapping = fav.rules.mapping || {};
      queuePreview();
    }
  };
}

function bind() {
  $("#btn-browse").onclick = () => openPath($("#path").value.trim());
  $("#path").addEventListener("keydown", (ev) => {
    if (ev.key === "Enter") openPath($("#path").value.trim());
  });
  $("#btn-up").onclick = async () => {
    const current = $("#path").value.trim();
    const data = await api(`/api/tree?path=${encodeURIComponent(current)}`);
    if (data.parent) openPath(data.parent);
  };
  $("#btn-seed").onclick = async () => {
    const data = await api("/api/seed", { method: "POST", body: "{}" });
    await loadRoots();
    await openPath(data.path);
  };
  $("#btn-reset").onclick = resetRules;
  $("#btn-mapping").onclick = async () => {
    const data = await api("/api/mapping", {
      method: "POST",
      body: JSON.stringify({ text: $("#mapping-text").value }),
    });
    state.mapping = data.mapping;
    $("#stats").textContent = `Imported ${data.count} name mappings`;
    queuePreview();
  };
  $("#btn-save-fav").onclick = async () => {
    const name = $("#fav-name").value.trim();
    if (!name) return;
    await api("/api/favorites", { method: "POST", body: JSON.stringify({ name, rules: readRules() }) });
    await loadFavorites();
  };
  $("#btn-log").onclick = async () => {
    const data = await api("/api/log");
    $("#log-text").textContent = data.text || "(empty)";
    $("#log-dialog").showModal();
  };
  $("#btn-undo").onclick = async () => {
    const data = await api("/api/undo", { method: "POST", body: "{}" });
    $("#stats").textContent = data.message;
    await preview();
  };
  $("#btn-rename").onclick = () => {
    const changed = state.rows.filter((row) => row.changed && row.status === "ok" && currentSelected().includes(row.path));
    $("#confirm-text").textContent = `${changed.length} item(s) will be renamed.`;
    $("#confirm-dialog").showModal();
  };
  $("#confirm-go").onclick = async (ev) => {
    ev.preventDefault();
    const payload = {
      ...scanPayload(),
      rules: readRules(),
      selected: currentSelected(),
      sort_column: state.sort,
      sort_desc: state.desc,
      confirm: true,
    };
    try {
      const data = await api("/api/rename", { method: "POST", body: JSON.stringify(payload) });
      $("#confirm-dialog").close();
      $("#stats").textContent = data.message;
      state.selected = null;
      await preview();
    } catch (err) {
      $("#confirm-text").textContent = err.message;
    }
  };
  $("#preset").onchange = (ev) => {
    const preset = PRESETS[ev.target.value];
    if (!preset) return;
    resetRules();
    writeRules(preset);
    queuePreview();
  };
  $("#theme-toggle").onchange = (ev) => {
    document.body.classList.toggle("light", ev.target.checked);
    localStorage.setItem("hulk-theme", ev.target.checked ? "light" : "dark");
  };
  if (localStorage.getItem("hulk-theme") === "light") {
    $("#theme-toggle").checked = true;
    document.body.classList.add("light");
  }

  ["recurse", "include-files", "include-folders", "wildcard"].forEach((id) => {
    $(`#${id}`).addEventListener("change", () => {
      state.selected = null;
      queuePreview();
    });
    $(`#${id}`).addEventListener("input", () => {
      state.selected = null;
      queuePreview();
    });
  });
  $$("[data-field], [data-scan]").forEach((el) => {
    el.addEventListener("input", queuePreview);
    el.addEventListener("change", queuePreview);
  });
  $("#files").addEventListener("change", (ev) => {
    if (ev.target.matches("tbody input[type=checkbox]")) {
      state.selected = currentSelected();
      queuePreview();
    }
  });
  $("#select-all").addEventListener("change", (ev) => {
    $$("#files tbody input[type=checkbox]").forEach((box) => { box.checked = ev.target.checked; });
    state.selected = currentSelected();
    queuePreview();
  });
  $$("#files thead th[data-sort]").forEach((th) => {
    th.onclick = () => {
      state.desc = state.sort === th.dataset.sort ? !state.desc : false;
      state.sort = th.dataset.sort;
      queuePreview();
    };
  });
  document.addEventListener("keydown", (ev) => {
    if ((ev.ctrlKey || ev.metaKey) && ev.key === "Enter") {
      ev.preventDefault();
      $("#btn-rename").click();
    }
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === "z" && ev.target.tagName !== "INPUT" && ev.target.tagName !== "TEXTAREA") {
      ev.preventDefault();
      $("#btn-undo").click();
    }
  });
}

bind();
loadFavorites().catch(() => {});
loadRoots().catch((err) => { $("#stats").textContent = err.message; });
