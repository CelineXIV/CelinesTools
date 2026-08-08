"use strict";

// Chat text (sender names, message segments) is untrusted (any player can type anything) - every
// place it reaches the DOM below goes through textContent/createTextNode, never innerHTML/insertAdjacentHTML.
// Keep it that way if you touch this file - it's the one thing standing between a hostile chat
// message and script execution in whoever's browser is viewing this page.

const CHANNELS = ["Say", "Party", "Whisper", "Yell", "Shout", "FreeCompany", "Alliance", "PvpTeam", "NoviceNetwork", "Linkshell", "CrossWorldLinkshell"];

// How long with no received event (including the server's own heartbeat - see WebServerService)
// before the connection is treated as dead and force-reconnected. Well above the 15s heartbeat
// interval so normal jitter never trips it, but short enough that a silently-stalled connection
// (the "Chat2 sometimes stops updating" failure mode this exists to catch) doesn't sit broken for
// long - a native TCP-level drop is already handled by EventSource's own reconnect logic on its
// own; this specifically covers "the connection looks alive but nothing is actually arriving."
const WATCHDOG_TIMEOUT_MS = 40_000;

const state = {
  source: null,
  lastEventAt: 0,
  watchdogTimer: null,
  view: null, // last WebViewDto received
};

const el = (id) => document.getElementById(id);

async function main() {
  const session = await fetch("/api/session").then((r) => r.ok).catch(() => false);
  if (session) {
    showChat();
  } else {
    showLogin();
  }

  el("login-form").addEventListener("submit", onLoginSubmit);
  el("compose-form").addEventListener("submit", onSendSubmit);
  el("compose-text").addEventListener("keydown", onComposeKeyDown);
}

function showLogin() {
  el("login-screen").hidden = false;
  el("chat-screen").hidden = true;
}

function showChat() {
  el("login-screen").hidden = true;
  el("chat-screen").hidden = false;
  renderChannelPicker();
  connectEvents();
}

async function onLoginSubmit(ev) {
  ev.preventDefault();
  const code = el("login-code").value.trim();
  const errorEl = el("login-error");
  errorEl.hidden = true;

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
  }
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
    renderViewPicker();
    renderChannelPicker();
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

// ----- Rendering -----

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

  if (wasAtBottom) {
    list.scrollTop = list.scrollHeight;
  }
}

function renderViewPicker() {
  const container = el("view-picker");
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
    const btn = document.createElement("button");
    btn.type = "button";
    btn.textContent = whisper.displayName;
    btn.className = whisper.target === state.view.activeWhisperTarget ? "active" : "";
    btn.addEventListener("click", () => post("/api/view/whisper", { target: whisper.target }));
    container.appendChild(btn);
  }
}

function renderChannelPicker() {
  const container = el("channel-picker");
  container.textContent = "";

  const select = document.createElement("select");
  for (const channel of CHANNELS) {
    const option = document.createElement("option");
    option.value = channel;
    option.textContent = channel;
    if (state.view && state.view.channel === channel) {
      option.selected = true;
    }

    select.appendChild(option);
  }

  select.addEventListener("change", () => post("/api/channel", { channel: select.value, number: null }));
  container.appendChild(select);
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
  await post("/api/send", { text });
}

function clickLink(sequence, linkIndex) {
  post("/api/links/click", { sequence, linkIndex });
}

async function post(path, body) {
  try {
    await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    // A failed action request isn't fatal - the SSE watchdog above handles genuine connection
    // loss; a one-off failed POST just means try again.
  }
}

main();
