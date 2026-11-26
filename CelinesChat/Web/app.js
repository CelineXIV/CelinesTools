"use strict";

const FLAT_CHANNELS = ["Say", "Party", "Alliance", "Yell", "Shout", "FreeCompany", "PvpTeam", "NoviceNetwork"];

const WATCHDOG_TIMEOUT_MS = 40_000;

const COMPOSE_MAX_HEIGHT_PX = 168;

const MAX_RENDERED_MESSAGES = 500;

const THEME_STORAGE_KEY = "celineschat-theme";
const THEMES = [
  { id: "dark", label: "Dark", bg: "#16181d", accent: "#4a9eff" },
  { id: "light", label: "Light", bg: "#f4f5f7", accent: "#2f6fed" },
  { id: "celines-classic", label: "Celines Classic", bg: "#15100c", accent: "#ff8a2b" },
  { id: "c4t", label: "C4T Blau", bg: "#080c18", accent: "#00e6c8" },
  { id: "c4t-lila", label: "C4T Lila", bg: "#10091b", accent: "#ff3ec8" },
];

function applyTheme(themeId) {
  const theme = THEMES.some((t) => t.id === themeId) ? themeId : "dark";
  document.documentElement.setAttribute("data-theme", theme);
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
  }
}

(function initTheme() {
  let saved = "dark";
  try {
    saved = localStorage.getItem(THEME_STORAGE_KEY) || "dark";
  } catch {
  }

  applyTheme(saved);
})();

const state = {
  source: null,
  lastEventAt: 0,
  watchdogTimer: null,
  view: null,
  channelOptions: null,
  sheetOpen: false,
  themeSheetOpen: false,
  sendErrorTimer: null,
  unread: 0,
  searchTerm: "",
  loadedCount: 0,
  hasMore: false,
  loadingOlder: false,
};

const HISTORY_PAGE_SIZE = 80;

const NOTIFY_SOUND_KEY = "celineschat-notify-sound";
const NOTIFY_VIBRATE_KEY = "celineschat-notify-vibrate";
const BASE_TITLE = document.title;
let audioContext = null;

function readFlag(key, fallback) {
  try {
    const value = localStorage.getItem(key);
    return value === null ? fallback : value === "1";
  } catch {
    return fallback;
  }
}

function writeFlag(key, value) {
  try {
    localStorage.setItem(key, value ? "1" : "0");
  } catch {
  }
}

function pageInBackground() {
  return document.hidden || !document.hasFocus();
}

function updateTitle() {
  document.title = state.unread > 0 ? `(${state.unread}) ${BASE_TITLE}` : BASE_TITLE;
}

function countUnread() {
  if (pageInBackground()) {
    state.unread++;
    updateTitle();
  }
}

function resetUnread() {
  if (!pageInBackground() && state.unread > 0) {
    state.unread = 0;
    updateTitle();
  }
}

function unlockAudio() {
  if (audioContext) {
    if (audioContext.state === "suspended") {
      audioContext.resume().catch(() => {});
    }

    return;
  }

  const Ctx = window.AudioContext || window.webkitAudioContext;
  if (Ctx) {
    audioContext = new Ctx();
  }
}

function playNotifySound(kind) {
  if (!audioContext || audioContext.state !== "running") {
    return;
  }

  const tones = kind === "whisper" ? [880, 1175] : [660, 880];
  const start = audioContext.currentTime;
  tones.forEach((frequency, index) => {
    const osc = audioContext.createOscillator();
    const gain = audioContext.createGain();
    osc.type = "sine";
    osc.frequency.value = frequency;
    const t = start + index * 0.13;
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(0.2, t + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.12);
    osc.connect(gain).connect(audioContext.destination);
    osc.start(t);
    osc.stop(t + 0.13);
  });
}

function onNotify(info) {
  if (!info.inView) {
    countUnread();
  }

  if (readFlag(NOTIFY_SOUND_KEY, true)) {
    playNotifySound(info.kind);
  }

  if (readFlag(NOTIFY_VIBRATE_KEY, true) && navigator.vibrate) {
    navigator.vibrate(info.kind === "whisper" ? [120, 80, 120] : [150]);
  }
}

function messageMatchesSearch(row) {
  return state.searchTerm === "" || row.textContent.toLowerCase().includes(state.searchTerm);
}

function applySearch() {
  const list = el("message-list");
  let shown = 0;
  for (const row of list.children) {
    const match = messageMatchesSearch(row);
    row.classList.toggle("search-hidden", !match);
    if (match) {
      shown++;
    }
  }

  el("search-count").textContent = state.searchTerm === "" ? "" : `${shown}`;
  if (state.searchTerm === "") {
    list.scrollTop = list.scrollHeight;
  }
}

function toggleSearch() {
  const bar = el("search-bar");
  const open = bar.hidden;
  bar.hidden = !open;
  el("search-button").classList.toggle("active", open);
  if (open) {
    el("search-input").focus();
  } else {
    el("search-input").value = "";
    state.searchTerm = "";
    applySearch();
  }
}

const el = (id) => document.getElementById(id);

async function main() {
  setupViewportHandling();

  const session = await fetch("/api/session").then((r) => r.ok).catch(() => false);
  if (session) {
    showChat();
  } else {
    showLogin();
  }

  el("login-form").addEventListener("submit", onLoginSubmit);
  el("compose-form").addEventListener("submit", onSendSubmit);
  el("compose-text").addEventListener("keydown", onComposeKeyDown);
  el("compose-text").addEventListener("input", autoGrowTextarea);
  el("compose-text").addEventListener("focus", () => {
    setTimeout(() => el("compose-text").scrollIntoView({ block: "end" }), 50);
  });

  el("channel-button").addEventListener("click", openChannelSheet);
  el("sheet-close").addEventListener("click", closeChannelSheet);
  el("channel-sheet").addEventListener("click", (ev) => {
    if (ev.target === el("channel-sheet")) {
      closeChannelSheet();
    }
  });

  el("theme-button").addEventListener("click", openThemeSheet);
  el("search-button").addEventListener("click", toggleSearch);
  setupOlderPaging();
  el("bookmarks-button").addEventListener("click", openBookmarkList);
  el("action-sheet-close").addEventListener("click", closeActionSheet);
  el("action-sheet").addEventListener("click", (ev) => {
    if (ev.target === el("action-sheet")) {
      closeActionSheet();
    }
  });
  el("search-input").addEventListener("input", () => {
    state.searchTerm = el("search-input").value.trim().toLowerCase();
    applySearch();
  });
  document.addEventListener("pointerdown", unlockAudio);
  document.addEventListener("keydown", unlockAudio);
  document.addEventListener("visibilitychange", resetUnread);
  window.addEventListener("focus", resetUnread);
  el("theme-sheet-close").addEventListener("click", closeThemeSheet);
  el("theme-sheet").addEventListener("click", (ev) => {
    if (ev.target === el("theme-sheet")) {
      closeThemeSheet();
    }
  });
}

function showLogin() {
  el("login-screen").hidden = false;
  el("chat-screen").hidden = true;
}

function showChat() {
  el("login-screen").hidden = true;
  el("chat-screen").hidden = false;
  renderTabStrip();
  updateChannelLabel();
  connectEvents();
  fetchChannelOptions();
}

async function onLoginSubmit(ev) {
  ev.preventDefault();

  const submitButton = ev.submitter ?? el("login-form").querySelector("button[type=submit]");
  if (submitButton?.disabled) {
    return;
  }

  const code = el("login-code").value.trim();
  const errorEl = el("login-error");
  errorEl.hidden = true;
  if (submitButton) submitButton.disabled = true;

  const timeout = AbortSignal.timeout(8_000);

  try {
    const res = await fetch("/api/auth", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code }),
      signal: timeout,
    });

    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      errorEl.textContent = body.error || "Login failed.";
      errorEl.hidden = false;
      return;
    }

    showChat();
  } catch {
    errorEl.textContent = "Could not reach the server.";
    errorEl.hidden = false;
  } finally {
    if (submitButton) submitButton.disabled = false;
  }
}

function setupViewportHandling() {
  if (!window.visualViewport) {
    return;
  }

  const apply = () => {
    document.documentElement.style.setProperty("--app-height", `${window.visualViewport.height}px`);

    const list = el("message-list");
    if (list && list.scrollTop + list.clientHeight >= list.scrollHeight - 40) {
      list.scrollTop = list.scrollHeight;
    }
  };

  window.visualViewport.addEventListener("resize", apply);
  window.visualViewport.addEventListener("scroll", apply);
  apply();
}

function autoGrowTextarea() {
  const textarea = el("compose-text");
  textarea.style.height = "auto";
  textarea.style.height = `${Math.min(textarea.scrollHeight, COMPOSE_MAX_HEIGHT_PX)}px`;
}

function connectEvents() {
  if (state.source) {
    state.source.close();
  }

  el("message-list").textContent = "";

  const source = new EventSource("/api/events");
  state.source = source;

  source.addEventListener("open", () => {
    markAlive();
    setConnected(true);
  });

  source.addEventListener("message", (ev) => {
    markAlive();
    appendMessage(JSON.parse(ev.data));
    state.loadedCount++;
    countUnread();
  });

  source.addEventListener("notify", (ev) => {
    markAlive();
    onNotify(JSON.parse(ev.data));
  });

  source.addEventListener("view", (ev) => {
    markAlive();
    state.view = JSON.parse(ev.data);
    renderTabStrip();
    updateChannelLabel();
    if (state.sheetOpen) {
      renderChannelSheet();
    }
  });

  source.addEventListener("history", (ev) => {
    markAlive();
    const list = el("message-list");
    list.textContent = "";
    const messages = JSON.parse(ev.data);
    for (const msg of messages) {
      appendMessage(msg);
    }

    state.loadedCount = messages.length;
    state.hasMore = messages.length >= HISTORY_PAGE_SIZE;
    applySearch();
    list.scrollTop = list.scrollHeight;
  });

  source.addEventListener("heartbeat", markAlive);

  source.addEventListener("error", () => {
    setConnected(false);
  });

  startWatchdog();
}

function markAlive() {
  state.lastEventAt = Date.now();
  setConnected(true);
}

function startWatchdog() {
  if (state.watchdogTimer) {
    clearInterval(state.watchdogTimer);
  }

  state.lastEventAt = Date.now();
  state.watchdogTimer = setInterval(() => {
    if (Date.now() - state.lastEventAt > WATCHDOG_TIMEOUT_MS) {
      setConnected(false);
      connectEvents();
    }
  }, 5_000);
}

function setConnected(connected) {
  el("connection-dot").classList.toggle("connected", connected);
}

function appendMessage(msg) {
  const list = el("message-list");
  const wasAtBottom = list.scrollTop + list.clientHeight >= list.scrollHeight - 40;
  const row = buildMessageRow(msg);
  list.appendChild(row);

  while (list.childElementCount > MAX_RENDERED_MESSAGES) {
    list.removeChild(list.firstElementChild);
    state.hasMore = true;
  }

  if (wasAtBottom) {
    list.scrollTop = list.scrollHeight;
  }
}

function buildMessageRow(msg) {
  const row = document.createElement("div");
  row.className = "msg" + (msg.bookmarked ? " bookmarked" : "");
  row.dataset.sequence = String(msg.sequence);

  const time = document.createElement("span");
  time.className = "time";
  time.textContent = msg.time;
  row.appendChild(time);

  const tag = document.createElement("span");
  tag.className = "tag";
  tag.style.color = msg.channelColorCss;
  tag.textContent = msg.channelTag;
  row.appendChild(tag);

  if (msg.relationIcon) {
    const icon = document.createElement("span");
    icon.className = "relation-icon";
    icon.style.color = msg.senderColorCss;
    const icons = { favorite: ["\u2665", "Favourite"], friend: ["\u{1F465}", "Friend"], fc: ["\u{1F6E1}\uFE0F", "FC member"] };
    const [symbol, label] = icons[msg.relationIcon] || icons.fc;
    icon.textContent = symbol;
    icon.title = label;
    row.appendChild(icon);
  }

  if (msg.sender) {
    const sender = document.createElement("span");
    sender.className = "sender";
    sender.style.color = msg.senderColorCss;
    sender.textContent = msg.sender + ":";
    if (msg.note) {
      sender.title = msg.note;
      const noteMark = document.createElement("span");
      noteMark.className = "note-mark";
      noteMark.textContent = " \u270E";
      sender.appendChild(noteMark);
    }

    row.appendChild(sender);
  }

  for (const segment of msg.segments) {
    const span = document.createElement("span");
    span.className = "segment" + (segment.linkIndex !== null && segment.linkIndex !== undefined ? " link" : "");
    span.style.color = segment.colorCss;
    span.textContent = segment.text + " ";
    if (segment.linkIndex !== null && segment.linkIndex !== undefined) {
      span.addEventListener("click", () => clickLink(msg.sequence, segment.linkIndex));
    }

    row.appendChild(span);
  }

  row.addEventListener("click", (ev) => {
    if (ev.target.closest(".link")) {
      return;
    }

    openMessageSheet(msg, row);
  });

  if (!messageMatchesSearch(row)) {
    row.classList.add("search-hidden");
  }

  return row;
}

async function loadOlderMessages() {
  if (state.loadingOlder || !state.hasMore) {
    return;
  }

  state.loadingOlder = true;
  const list = el("message-list");
  const indicator = document.createElement("div");
  indicator.className = "older-loading";
  indicator.textContent = "...";
  list.insertBefore(indicator, list.firstChild);

  try {
    const res = await fetch("/api/history/older", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ skip: list.querySelectorAll(".msg").length, count: 50 }),
    });
    indicator.remove();
    if (!res.ok) {
      return;
    }

    const body = await res.json();
    const before = list.scrollHeight;
    const fragment = document.createDocumentFragment();
    for (const msg of body.messages) {
      fragment.appendChild(buildMessageRow(msg));
    }

    list.insertBefore(fragment, list.firstChild);
    state.loadedCount += body.messages.length;
    state.hasMore = body.hasMore;

    list.scrollTop += list.scrollHeight - before;
  } catch {
    indicator.remove();
  } finally {
    state.loadingOlder = false;
  }
}

function setupOlderPaging() {
  const list = el("message-list");
  list.addEventListener("scroll", () => {
    if (list.scrollTop < 80) {
      loadOlderMessages();
    }
  }, { passive: true });
}

function renderTabStrip() {
  const container = el("tab-strip");
  container.textContent = "";

  if (!state.view) {
    return;
  }

  for (const tab of state.view.tabs) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.textContent = tab.name;
    btn.className = tab.id === state.view.activeTabId ? "active" : "";
    btn.addEventListener("click", () => post("/api/view/tab", { tabId: tab.id }));
    container.appendChild(btn);
  }

  for (const whisper of state.view.whispers) {
    const group = document.createElement("span");
    group.className = "whisper-pill";

    const label = document.createElement("button");
    label.type = "button";
    label.textContent = whisper.displayName;
    label.className = "whisper-pill-label" + (whisper.target === state.view.activeWhisperTarget ? " active" : "");
    label.addEventListener("click", () => post("/api/view/whisper", { target: whisper.target }));
    group.appendChild(label);

    const closeButton = document.createElement("button");
    closeButton.type = "button";
    closeButton.className = "whisper-pill-close";
    closeButton.textContent = "×";
    closeButton.setAttribute("aria-label", `Close whisper with ${whisper.displayName}`);
    closeButton.addEventListener("click", () => post("/api/view/whisper/close", { target: whisper.target }));
    group.appendChild(closeButton);

    container.appendChild(group);
  }
}

function findChannelName(list, number) {
  return list?.find((c) => c.number === number)?.name ?? "";
}

function updateChannelLabel() {
  const label = el("channel-label");
  if (!state.view) {
    label.textContent = "...";
    return;
  }

  const channel = state.view.channel;
  if (channel === "Linkshell") {
    const name = findChannelName(state.channelOptions?.linkshells, state.view.linkshellNumber);
    label.textContent = `Linkshell [${state.view.linkshellNumber}]` + (name ? `: ${name}` : "");
  } else if (channel === "CrossWorldLinkshell") {
    const name = findChannelName(state.channelOptions?.crossWorldLinkshells, state.view.crossWorldLinkshellNumber);
    label.textContent = `Cross-world LS [${state.view.crossWorldLinkshellNumber}]` + (name ? `: ${name}` : "");
  } else {
    label.textContent = channel;
  }
}

async function fetchChannelOptions() {
  try {
    const res = await fetch("/api/channels");
    if (!res.ok) {
      return;
    }

    state.channelOptions = await res.json();
    updateChannelLabel();
    if (state.sheetOpen) {
      renderChannelSheet();
    }
  } catch {
  }
}

function openChannelSheet() {
  state.sheetOpen = true;
  el("channel-sheet").hidden = false;
  renderChannelSheet();
  fetchChannelOptions();
}

function closeChannelSheet() {
  state.sheetOpen = false;
  el("channel-sheet").hidden = true;
}

function selectChannelAndClose(channel, number) {
  post("/api/channel", { channel, number: number ?? null });
  closeChannelSheet();
}

function sheetSectionTitle(text) {
  const h = document.createElement("h3");
  h.className = "sheet-section-title";
  h.textContent = text;
  return h;
}

function renderChannelSheet() {
  const container = el("sheet-content");
  container.textContent = "";

  const whisperInput = document.createElement("div");
  whisperInput.className = "sheet-whisper-input";

  const input = document.createElement("input");
  input.type = "text";
  input.placeholder = "Character Name@World";
  input.maxLength = 100;

  const openWhisper = () => {
    const target = input.value.trim();
    if (!target) {
      return;
    }

    post("/api/view/whisper", { target });
    input.value = "";
    closeChannelSheet();
  };

  input.addEventListener("keydown", (ev) => {
    if (ev.key === "Enter") {
      ev.preventDefault();
      openWhisper();
    }
  });

  const openButton = document.createElement("button");
  openButton.type = "button";
  openButton.textContent = "Open";
  openButton.addEventListener("click", openWhisper);

  whisperInput.appendChild(input);
  whisperInput.appendChild(openButton);
  container.appendChild(sheetSectionTitle("Whisper"));
  container.appendChild(whisperInput);

  const partyMembers = state.channelOptions?.partyMembers ?? [];
  if (partyMembers.length > 0) {
    const list = document.createElement("div");
    list.className = "sheet-suggestion-list";
    for (const name of partyMembers) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = name;
      btn.addEventListener("click", () => {
        post("/api/view/whisper", { target: name });
        closeChannelSheet();
      });
      list.appendChild(btn);
    }

    container.appendChild(list);
  }

  container.appendChild(sheetSectionTitle("Channels"));
  const flatList = document.createElement("div");
  flatList.className = "sheet-option-list";
  for (const channel of FLAT_CHANNELS) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.textContent = channel;
    btn.className = "sheet-option" + (state.view?.channel === channel ? " active" : "");
    btn.addEventListener("click", () => selectChannelAndClose(channel));
    flatList.appendChild(btn);
  }

  container.appendChild(flatList);

  appendNumberedChannelSection(container, "Cross-world Linkshells", "CrossWorldLinkshell", state.channelOptions?.crossWorldLinkshells ?? [], state.view?.crossWorldLinkshellNumber);

  appendNumberedChannelSection(container, "Linkshells", "Linkshell", state.channelOptions?.linkshells ?? [], state.view?.linkshellNumber);
}

function appendNumberedChannelSection(container, title, channel, entries, activeNumber) {
  if (entries.length === 0) {
    return;
  }

  container.appendChild(sheetSectionTitle(title));
  const list = document.createElement("div");
  list.className = "sheet-option-list";
  for (const entry of entries) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.textContent = `[${entry.number}] ${entry.name}`;
    btn.className = "sheet-option" + (state.view?.channel === channel && activeNumber === entry.number ? " active" : "");
    btn.addEventListener("click", () => selectChannelAndClose(channel, entry.number));
    list.appendChild(btn);
  }

  container.appendChild(list);
}

function openThemeSheet() {
  state.themeSheetOpen = true;
  el("theme-sheet").hidden = false;
  renderThemeSheet();
}

function closeThemeSheet() {
  state.themeSheetOpen = false;
  el("theme-sheet").hidden = true;
}

function renderThemeSheet() {
  const container = el("theme-sheet-content");
  container.textContent = "";

  const current = document.documentElement.getAttribute("data-theme") || "dark";
  const list = document.createElement("div");
  list.className = "theme-swatch-list";

  for (const theme of THEMES) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "theme-swatch" + (theme.id === current ? " active" : "");

    const preview = document.createElement("span");
    preview.className = "theme-swatch-preview";
    preview.style.background = theme.bg;
    preview.style.borderColor = theme.accent;
    btn.appendChild(preview);

    const label = document.createElement("span");
    label.textContent = theme.label;
    btn.appendChild(label);

    btn.addEventListener("click", () => {
      applyTheme(theme.id);
      closeThemeSheet();
    });

    list.appendChild(btn);
  }

  container.appendChild(list);

  const options = document.createElement("div");
  options.className = "notify-options";
  options.appendChild(sheetSectionTitle("Notifications (whispers & mentions)"));
  options.appendChild(makeToggle("Play a sound", NOTIFY_SOUND_KEY));
  if (navigator.vibrate) {
    options.appendChild(makeToggle("Vibrate", NOTIFY_VIBRATE_KEY));
  }

  container.appendChild(options);
}

function makeToggle(text, key) {
  const label = document.createElement("label");
  const input = document.createElement("input");
  input.type = "checkbox";
  input.checked = readFlag(key, true);
  input.addEventListener("change", () => {
    writeFlag(key, input.checked);
    if (input.checked) {
      unlockAudio();
    }
  });
  label.appendChild(input);
  label.appendChild(document.createTextNode(text));
  return label;
}

async function onSendSubmit(ev) {
  ev.preventDefault();
  await trySend();
}

function onComposeKeyDown(ev) {
  if (ev.key === "Enter" && !ev.shiftKey) {
    ev.preventDefault();
    trySend();
  }
}

async function trySend() {
  const textarea = el("compose-text");
  const text = textarea.value.trim();
  if (!text) {
    return;
  }

  textarea.value = "";
  autoGrowTextarea();
  await post("/api/send", { text }, { onError: showSendError });
}

function showSendError(message) {
  const banner = el("send-error");
  banner.textContent = message;
  banner.hidden = false;

  if (state.sendErrorTimer) {
    clearTimeout(state.sendErrorTimer);
  }

  state.sendErrorTimer = setTimeout(() => {
    banner.hidden = true;
  }, 4_000);
}

function clickLink(sequence, linkIndex) {
  post("/api/links/click", { sequence, linkIndex });
}

async function post(path, body, options) {
  try {
    const res = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!res.ok && options?.onError) {
      const errorBody = await res.json().catch(() => ({}));
      options.onError(errorBody.error || "Something went wrong.");
    }
  } catch {
  }
}

main();

function openActionSheet(title) {
  el("action-sheet-title").textContent = title;
  const content = el("action-sheet-content");
  content.textContent = "";
  el("action-sheet").hidden = false;
  return content;
}

function closeActionSheet() {
  el("action-sheet").hidden = true;
}

function messagePlainText(msg) {
  return msg.segments.map((segment) => segment.text).join(" ");
}

function actionButton(text, onClick) {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "sheet-option";
  btn.textContent = text;
  btn.addEventListener("click", onClick);
  return btn;
}

function openMessageSheet(msg, row) {
  const content = openActionSheet(msg.sender || msg.channelTag);

  const preview = document.createElement("p");
  preview.className = "action-preview";
  preview.textContent = messagePlainText(msg);
  content.appendChild(preview);

  if (msg.note) {
    content.appendChild(sheetSectionTitle("Your note"));
    const note = document.createElement("p");
    note.className = "action-note";
    note.textContent = msg.note;
    content.appendChild(note);
  }

  const list = document.createElement("div");
  list.className = "sheet-option-list";

  if (msg.canReply) {
    list.appendChild(actionButton(msg.isWhisper ? "Reply (whisper)" : "Reply in " + msg.channelTag.replace(/[\[\]]/g, ""), async () => {
      closeActionSheet();
      await post("/api/reply", { sequence: msg.sequence }, { onError: showSendError });
      el("compose-text")?.focus();
    }));
  }

  if (msg.whisperTarget && !msg.isWhisper) {
    list.appendChild(actionButton("Whisper " + msg.sender, async () => {
      closeActionSheet();
      await post("/api/view/whisper", { target: msg.whisperTarget }, { onError: showSendError });
      el("compose-text")?.focus();
    }));
  }

  const bookmarked = row.classList.contains("bookmarked");
  list.appendChild(actionButton(bookmarked ? "Remove bookmark" : "Bookmark", async () => {
    closeActionSheet();
    try {
      const res = await fetch("/api/bookmark", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ sequence: msg.sequence }),
      });
      const body = await res.json().catch(() => ({}));
      if (!res.ok) {
        showSendError(body.error || "Something went wrong.");
        return;
      }

      msg.bookmarked = body.bookmarked;
      row.classList.toggle("bookmarked", body.bookmarked);
    } catch {
      showSendError("No connection.");
    }
  }));

  list.appendChild(actionButton("Copy text", () => {
    const text = (msg.sender ? msg.sender + ": " : "") + messagePlainText(msg);
    navigator.clipboard?.writeText(text).catch(() => {});
    closeActionSheet();
  }));

  content.appendChild(list);
}

async function openBookmarkList() {
  const content = openActionSheet("Bookmarks");
  const loading = document.createElement("p");
  loading.textContent = "...";
  content.appendChild(loading);

  let bookmarks = [];
  try {
    const res = await fetch("/api/bookmarks");
    bookmarks = res.ok ? await res.json() : [];
  } catch {
  }

  content.textContent = "";
  if (bookmarks.length === 0) {
    const empty = document.createElement("p");
    empty.textContent = "No bookmarks yet - tap a message to bookmark it.";
    content.appendChild(empty);
    return;
  }

  for (const bookmark of bookmarks) {
    const item = document.createElement("div");
    item.className = "bookmark-item";

    const head = document.createElement("div");
    head.className = "bookmark-head";
    const time = document.createElement("span");
    time.className = "time";
    time.textContent = bookmark.time;
    head.appendChild(time);
    const tag = document.createElement("span");
    tag.className = "tag";
    tag.style.color = bookmark.channelColorCss;
    tag.textContent = bookmark.channelTag;
    head.appendChild(tag);
    if (bookmark.sender) {
      const sender = document.createElement("span");
      sender.className = "sender";
      sender.style.color = bookmark.channelColorCss;
      sender.textContent = bookmark.sender + ":";
      head.appendChild(sender);
    }

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "bookmark-remove";
    remove.textContent = "\u00D7";
    remove.title = "Remove bookmark";
    remove.addEventListener("click", async () => {
      await post("/api/bookmark/remove", { key: bookmark.key });
      item.remove();
      for (const row of el("message-list").children) {
        if (row.classList.contains("bookmarked") && row.textContent.includes(bookmark.text)) {
          row.classList.remove("bookmarked");
        }
      }
    });
    head.appendChild(remove);
    item.appendChild(head);

    const text = document.createElement("p");
    text.className = "bookmark-text";
    text.textContent = bookmark.text;
    item.appendChild(text);

    content.appendChild(item);
  }
}
