"use strict";

// Chat text (sender names, message segments, whisper/party names, linkshell names) is untrusted
// (any player, or the player's own game data, can contain arbitrary text) - every place it reaches
// the DOM below goes through textContent/createTextNode, never innerHTML/insertAdjacentHTML. Keep it
// that way if you touch this file - it's the one thing standing between a hostile chat message (or a
// maliciously-named linkshell) and script execution in whoever's browser is viewing this page.

// Flat (non-numbered, non-whisper) channels, in the same order the in-game channel picker uses
// (ChatWindow.DrawChannelPickerButton) - Whisper gets its own section (free text + suggestions) and
// Linkshell/CrossWorldLinkshell get their own numbered sections, both rendered separately below.
const FLAT_CHANNELS = ["Say", "Party", "Alliance", "Yell", "Shout", "FreeCompany", "PvpTeam", "NoviceNetwork"];

// How long with no received event (including the server's own heartbeat - see WebServerService)
// before the connection is treated as dead and force-reconnected. Well above the 15s heartbeat
// interval so normal jitter never trips it, but short enough that a silently-stalled connection
// (the "Chat2 sometimes stops updating" failure mode this exists to catch) doesn't sit broken for
// long - a native TCP-level drop is already handled by EventSource's own reconnect logic on its
// own; this specifically covers "the connection looks alive but nothing is actually arriving."
const WATCHDOG_TIMEOUT_MS = 40_000;

// Textarea auto-grow cap, in CSS pixels - roughly 6 lines at the compose box's own font size/line
// height, matching the in-game compose box's own general feel without letting one huge paste push
// the whole layout (and therefore the message list) around unpredictably.
const COMPOSE_MAX_HEIGHT_PX = 168;

// Oldest messages are dropped from the page beyond this - in a long session the list otherwise
// grew without bound, slowly eating memory and making scrolling sluggish on a phone. The plugin
// keeps its own full history; this is only what this page keeps rendered.
const MAX_RENDERED_MESSAGES = 500;

// Saved design presets - purely a per-device browser preference (localStorage), not something the
// plugin's own Configuration knows or needs to know about. bg/accent here are only used to paint
// each option's own preview swatch in the design sheet; the actual page colours come from the
// matching html[data-theme="..."] block in styles.css.
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
    // Private browsing / storage disabled - the choice just won't persist across reloads.
  }
}

// Applied immediately at script load (before main()/DOMContentLoaded) so the very first paint
// already uses the saved theme instead of flashing the default dark palette first.
(function initTheme() {
  let saved = "dark";
  try {
    saved = localStorage.getItem(THEME_STORAGE_KEY) || "dark";
  } catch {
    // Same as above - falls back to the default theme silently.
  }

  applyTheme(saved);
})();

const state = {
  source: null,
  lastEventAt: 0,
  watchdogTimer: null,
  view: null, // last WebViewDto received
  channelOptions: null, // last WebChannelOptionsDto received (linkshells/CWLS/party members)
  sheetOpen: false,
  themeSheetOpen: false,
  sendErrorTimer: null,
};

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
    // Last-resort safety net on top of the visualViewport handling below - some browsers still
    // leave the freshly-focused box a little off-screen for a frame or two while the keyboard
    // animates in.
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

  // The server rate-limits /api/auth per IP (10s cooldown) regardless of whether an attempt
  // succeeds - a double-click/double-tap firing two overlapping submits would always get the
  // second one rejected with a confusing "rate limit" error for what the user experienced as one
  // single login attempt. Guarding here (rather than only rate-limiting server-side) means that
  // never happens in the first place.
  const submitButton = ev.submitter ?? el("login-form").querySelector("button[type=submit]");
  if (submitButton?.disabled) {
    return;
  }

  const code = el("login-code").value.trim();
  const errorEl = el("login-error");
  errorEl.hidden = true;
  if (submitButton) submitButton.disabled = true;

  // fetch() has no built-in timeout - without this, a request that never gets a response (a
  // server-side hang, a dropped connection that doesn't reset cleanly) leaves the user staring at
  // a form that visibly does nothing forever, with no error to explain why.
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

// ----- Viewport / on-screen-keyboard handling -----

// The viewport meta tag's interactive-widget=resizes-content already tells modern Chrome/Android
// WebView to shrink the visual viewport (and therefore 100dvh) for the on-screen keyboard instead
// of overlaying it - but that hint isn't honoured everywhere (older WebViews, some iOS Safari
// versions), which is exactly the "keyboard covers the compose box/Send button" bug reported. This
// listens to window.visualViewport directly (supported far more broadly than the meta hint) and
// drives an --app-height custom property as a JS-level fallback, so .screen tracks the *actual*
// visible area either way.
function setupViewportHandling() {
  if (!window.visualViewport) {
    return;
  }

  const apply = () => {
    document.documentElement.style.setProperty("--app-height", `${window.visualViewport.height}px`);

    // The visible area just changed size (keyboard opened/closed) - re-anchor the message list to
    // the bottom if it was already effectively at the bottom, matching appendMessage's own
    // "only follow if already following" behaviour rather than yanking a scrolled-up reader down.
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

// ----- SSE -----

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

  // Sent right after "view" whenever the active tab/channel/whisper changes (from this device or
  // any other) - replaces the message list with that view's own history, since "message" events
  // only ever cover newly-arriving traffic from here on.
  source.addEventListener("history", (ev) => {
    markAlive();
    const list = el("message-list");
    list.textContent = "";
    for (const msg of JSON.parse(ev.data)) {
      appendMessage(msg);
    }

    list.scrollTop = list.scrollHeight;
  });

  source.addEventListener("heartbeat", markAlive);

  source.addEventListener("error", () => {
    setConnected(false);
    // EventSource retries connecting on its own - the watchdog below is the backstop for the
    // "looks connected but nothing arrives" case a native error event doesn't catch.
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

// ----- Rendering: messages -----

function appendMessage(msg) {
  const list = el("message-list");
  const wasAtBottom = list.scrollTop + list.clientHeight >= list.scrollHeight - 40;

  const row = document.createElement("div");
  row.className = "msg";

  const time = document.createElement("span");
  time.className = "time";
  time.textContent = msg.time;
  row.appendChild(time);

  const tag = document.createElement("span");
  tag.className = "tag";
  tag.style.color = msg.channelColorCss;
  tag.textContent = msg.channelTag;
  row.appendChild(tag);

  if (msg.sender) {
    const sender = document.createElement("span");
    sender.className = "sender";
    sender.style.color = msg.senderColorCss;
    sender.textContent = msg.sender + ":";
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

  list.appendChild(row);

  while (list.childElementCount > MAX_RENDERED_MESSAGES) {
    list.removeChild(list.firstElementChild);
  }

  if (wasAtBottom) {
    list.scrollTop = list.scrollHeight;
  }
}

// ----- Rendering: tab strip -----

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

// ----- Rendering: channel label + sheet -----

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
    // Best-effort - the sheet still works with whatever it last had (or nothing, showing just bare
    // numbers with no linkshell names) if this fails.
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

  // ----- Whisper -----
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

  // ----- Flat channels -----
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

  // ----- Cross-world Linkshells -----
  appendNumberedChannelSection(container, "Cross-world Linkshells", "CrossWorldLinkshell", state.channelOptions?.crossWorldLinkshells ?? [], state.view?.crossWorldLinkshellNumber);

  // ----- Linkshells -----
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

// ----- Rendering: design/theme sheet -----

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
}

// ----- Actions -----

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
    // A failed action request isn't fatal - the SSE watchdog above handles genuine connection
    // loss; a one-off failed POST just means try again. onError above only fires for responses
    // the server actually sent back (e.g. validation failures), not network-level failures.
  }
}

main();
