/* Wasta — client application.
   Vanilla JS on purpose: no build step, so what ships is what you read. */

const $ = (s, r = document) => r.querySelector(s);
const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const fmt = n => (n === null || n === undefined) ? "—" : n;

/* ---------------- session ---------------- */
const session = {
  get()  { try { return JSON.parse(localStorage.getItem("wasta.session")); } catch { return null; } },
  set(v) { localStorage.setItem("wasta.session", JSON.stringify(v)); },
  clear(){ localStorage.removeItem("wasta.session"); },
  get role()      { return this.get()?.role ?? null; },
  get seekerId()  { return this.get()?.seekerId ?? null; },
  get companyId() { return this.get()?.companyId ?? null; },
};

/* ---------------- api ---------------- */
let refreshing = null;

async function api(method, path, body, opts = {}) {
  const s = session.get();
  const headers = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (s?.accessToken && !opts.anon) headers["Authorization"] = "Bearer " + s.accessToken;

  const res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });

  // One transparent refresh attempt, then give up and send them to sign in.
  if (res.status === 401 && s?.refreshToken && !opts.retried && !opts.anon) {
    refreshing = refreshing || fetch("/api/auth/refresh", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: s.refreshToken })
    }).then(async r => {
      if (!r.ok) return null;
      const next = await r.json();
      session.set(next);
      return next;
    }).finally(() => { refreshing = null; });

    const next = await refreshing;
    if (next) return api(method, path, body, { ...opts, retried: true });
    session.clear();
    go("#/login");
    return { status: 401, data: null };
  }

  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: res.status, ok: res.ok, data };
}

/** Problem-details carry a stable `code`; prefer the human `detail`. */
function errText(r, fallback = "Something went wrong.") {
  const d = r?.data;
  if (!d) return fallback;
  if (typeof d === "string") return d;
  if (d.errors) return Object.values(d.errors).flat().join(" ");
  return d.detail || d.title || fallback;
}

/* ---------------- reference data ---------------- */
let REF = null;
async function reference() {
  if (!REF) REF = (await api("GET", "/api/reference", undefined, { anon: true })).data;
  return REF;
}
const nameOf = (list, id) => (list || []).find(x => x.id === id)?.name ?? "—";

/* ---------------- routing ---------------- */
const routes = [];
const route = (re, view, guard) => routes.push({ re, view, guard });
const go = h => { location.hash = h; };

async function render() {
  const hash = location.hash || "#/";
  // Match on the path alone. A route like #/verify?token=... still has to
  // reach the /verify view, and an anchored regex would never match with the
  // query string attached.
  const path = hash.slice(1).split("?")[0];
  for (const r of routes) {
    const m = path.match(r.re);
    if (!m) continue;
    if (r.guard && !r.guard()) { go("#/login"); return; }
    chrome();
    const host = $("#view");
    host.innerHTML = `<div class="card center"><span class="spin"></span> <span class="muted small">Loading…</span></div>`;
    try { await r.view(host, ...m.slice(1)); }
    catch (e) { host.innerHTML = `<div class="note note-err">${esc(e.message || String(e))}</div>`; }
    return;
  }
  go(session.get() ? homeFor(session.role) : "#/login");
}

const homeFor = role => role === "Company" ? "#/company" : role === "Admin" ? "#/admin" : "#/me";
const isSeeker  = () => session.role === "Seeker";
const isCompany = () => session.role === "Company";
const isAdmin   = () => session.role === "Admin";
const signedIn  = () => !!session.get();

/* ---------------- chrome ---------------- */
function chrome() {
  const s = session.get();
  const path = location.hash.slice(1).split("?")[0];

  // Signed out - or on a page that belongs to signed-out users - gets a centred
  // card rather than the portal shell. The path check matters: a session can
  // outlive the account it refers to (a reset database, a deleted user), and
  // rendering a sidebar around a sign-in form is the visible symptom of that.
  const signedOutPage = ["/login", "/register", "/forgot", "/verify", "/mailbox"]
    .some(p => path === p || path.startsWith(p + "/"));

  if (!s || signedOutPage) {
    $("#shell").innerHTML = `<div class="auth-page"><div id="view"></div></div>`;
    return;
  }

  const on = p => path.startsWith(p) ? "on" : "";
  const item = (href, icon, label) =>
    `<a href="${href}" class="${on(href.slice(1))}"><span class="ic">${icon}</span>${label}</a>`;

  let nav = "", portal = "Portal", tag = "";
  if (s.role === "Seeker") {
    portal = "Student Portal"; tag = "Student";
    nav = item("#/me", "◈", "Dashboard")
        + item("#/assessment", "✎", "Assessment")
        + item("#/coach", "✦", "Career Coach")
        + item("#/jobs", "▤", "Jobs")
        + item("#/applications", "☰", "Applications");
  } else if (s.role === "Company") {
    portal = "Company Portal"; tag = "Company";
    nav = item("#/company", "◈", "Dashboard")
        + item("#/talent", "☺", "Talent Pool")
        + item("#/company/jobs", "▤", "Jobs")
        + item("#/credits", "◆", "Credits");
  } else {
    portal = "Administration"; tag = "Admin";
    nav = item("#/admin", "☑", "Review queue")
        + item("#/admin/content", "▤", "Content");
  }

  $("#shell").innerHTML = `
    <div class="shell">
      <aside class="side">
        <div class="logo">
          <div class="mark">W</div><span class="word">Wasta</span>
          <span class="tag">${esc(tag)}</span>
        </div>
        <div class="kicker">${esc(portal)}</div>
        <nav>${nav}</nav>
        <div class="foot">
          <div class="who">${esc(s.email || s.role)}</div>
          <button class="btn-ghost btn-sm" id="signout" style="width:100%">Sign out</button>
        </div>
      </aside>
      <div class="main">
        <div class="topstrip">
          <span class="title">${esc(portal)}</span>
          <span class="spacer"></span>
          <a class="small muted" href="#/mailbox">Dev mailbox</a>
        </div>
        <div class="wrap"><div id="view"></div></div>
      </div>
    </div>`;

  $("#signout").onclick = async () => {
    const t = session.get()?.refreshToken;
    if (t) await api("POST", "/api/auth/logout", { refreshToken: t });
    session.clear();
    go("#/login");
  };
}

function head(title, sub) {
  return `<div class="page-head"><h1>${esc(title)}</h1>${sub ? `<p>${esc(sub)}</p>` : ""}</div>`;
}

/* =====================================================================
   AUTH
   ===================================================================== */
route(/^\/login$/, async host => {
  host.innerHTML = `
    <div class="auth-wrap">
      <div class="auth-brand"><div class="mark">W</div><span class="word">Wasta</span></div>
      <div class="page-head center"><h1>Sign in</h1><p>Prove skill, not connections.</p></div>
      <div class="card">
        <div id="msg"></div>
        <div class="field"><label>Email</label><input id="email" type="email" autocomplete="username" /></div>
        <div class="field"><label>Password</label><input id="password" type="password" autocomplete="current-password" /></div>
        <button class="btn" id="submit" style="width:100%">Sign in</button>
        <p class="small center" style="margin:14px 0 0">
          New here? <a href="#/register">Create an account</a><br />
          <a href="#/forgot">Forgotten your password?</a>
        </p>
      </div>
      <p class="tiny center muted">Local testing? The <a href="#/mailbox">dev mailbox</a> shows confirmation links.</p>
    </div>`;

  $("#submit").onclick = async () => {
    const email = $("#email").value.trim(), password = $("#password").value;
    if (!email || !password) return note("#msg", "err", "Enter your email and password.");
    $("#submit").disabled = true;
    const r = await api("POST", "/api/auth/login", { email, password }, { anon: true });
    $("#submit").disabled = false;

    if (r.status === 403 && r.data?.code === "auth.email_not_verified") {
      note("#msg", "warn",
        "Confirm your email address before signing in. We can send a new link.");
      $("#msg").insertAdjacentHTML("beforeend",
        `<button class="btn-ghost btn-sm" id="resend" style="margin-top:8px">Send a new link</button>`);
      $("#resend").onclick = async () => {
        await api("POST", "/api/auth/verify-email/resend", { email }, { anon: true });
        note("#msg", "ok", "If that address is registered, a link is on its way. Check the dev mailbox.");
      };
      return;
    }
    if (!r.ok) return note("#msg", "err", errText(r, "Email or password is incorrect."));

    session.set({ ...r.data, email });
    go(homeFor(r.data.role));
  };
});

route(/^\/register$/, async host => {
  const ref = await reference();
  host.innerHTML = `
    <div class="auth-wrap">
      <div class="auth-brand"><div class="mark">W</div><span class="word">Wasta</span></div>
      <div class="page-head center"><h1>Create an account</h1></div>
      <div class="tabs">
        <button id="t-seeker" class="on">I'm a student</button>
        <button id="t-company">I'm hiring</button>
      </div>
      <div class="card">
        <div id="msg"></div>
        <div id="form"></div>
      </div>
    </div>`;

  const seekerForm = () => `
    <div class="field"><label>Full name</label><input id="fullName" /></div>
    <div class="field"><label>Email</label><input id="email" type="email" /></div>
    <div class="field"><label>Password</label><input id="password" type="password" />
      <div class="hint">At least 8 characters, with a letter and a number.</div></div>
    <div class="field"><label>Track</label>
      <select id="trackId">${ref.tracks.map(t => `<option value="${t.id}">${esc(t.name)}</option>`).join("")}</select>
      <div class="hint">The assessment you'll sit. You can change it later.</div></div>
    <button class="btn" id="submit" style="width:100%">Create account</button>`;

  const companyForm = () => `
    <div class="field"><label>Company name</label><input id="companyName" /></div>
    <div class="field"><label>Work email</label><input id="email" type="email" /></div>
    <div class="field"><label>Password</label><input id="password" type="password" /></div>
    <div class="field"><label>Industry</label>
      <select id="industryId"><option value="">—</option>
        ${(ref.industries || []).map(i => `<option value="${i.id}">${esc(i.name)}</option>`).join("")}</select></div>
    <div class="note note-info small">New companies are reviewed by an administrator before they can browse candidates.</div>
    <button class="btn" id="submit" style="width:100%">Create account</button>`;

  let mode = "seeker";
  const paint = () => {
    $("#form").innerHTML = mode === "seeker" ? seekerForm() : companyForm();
    $("#t-seeker").className = mode === "seeker" ? "on" : "";
    $("#t-company").className = mode === "company" ? "on" : "";
    $("#submit").onclick = submit;
  };
  $("#t-seeker").onclick = () => { mode = "seeker"; paint(); };
  $("#t-company").onclick = () => { mode = "company"; paint(); };

  async function submit() {
    $("#submit").disabled = true;
    const email = $("#email").value.trim();
    const body = mode === "seeker"
      ? { fullName: $("#fullName").value.trim(), email, password: $("#password").value,
          trackId: Number($("#trackId").value) }
      : { companyName: $("#companyName").value.trim(), workEmail: email, password: $("#password").value,
          industryId: $("#industryId").value ? Number($("#industryId").value) : null };

    const r = await api("POST", `/api/auth/register/${mode}`, body, { anon: true });
    $("#submit").disabled = false;
    if (!r.ok) return note("#msg", "err", errText(r, "Could not create the account."));

    session.set({ ...r.data, email, justRegistered: true });
    go(homeFor(r.data.role));
  }
  paint();
});

route(/^\/forgot$/, async host => {
  host.innerHTML = `
    <div class="auth-wrap">
      <div class="auth-brand"><div class="mark">W</div><span class="word">Wasta</span></div>
      <div class="page-head center"><h1>Reset your password</h1></div>
      <div class="card">
        <div id="msg"></div>
        <div class="field"><label>Email</label><input id="email" type="email" /></div>
        <button class="btn" id="submit" style="width:100%">Send a reset link</button>
        <p class="small center" style="margin:13px 0 0"><a href="#/login">Back to sign in</a></p>
      </div>
    </div>`;
  $("#submit").onclick = async () => {
    await api("POST", "/api/auth/forgot-password", { email: $("#email").value.trim() }, { anon: true });
    // Always the same answer, registered or not.
    note("#msg", "ok", "If that address is registered, a reset link has been sent.");
  };
});

route(/^\/verify$/, async host => {
  const token = new URLSearchParams(location.hash.split("?")[1] || "").get("token");
  host.innerHTML = `<div class="auth-wrap"><div class="card" id="box">Confirming…</div></div>`;
  if (!token) return ($("#box").innerHTML = `<div class="note note-err">That link is missing its token.</div>`);
  const r = await api("POST", "/api/auth/verify-email/confirm", { token }, { anon: true });
  $("#box").innerHTML = r.ok
    ? `<div class="note note-ok">Email confirmed. You can sign in now.</div><a class="btn" href="#/login">Sign in</a>`
    : `<div class="note note-err">${esc(errText(r, "That link is no longer valid."))}</div>
       <p class="small">Links expire and can only be used once. Request a fresh one from the sign-in page.</p>`;
});

/* Local mail catcher — Development only; the endpoint does not exist elsewhere. */
route(/^\/mailbox$/, async host => {
  const r = await api("GET", "/api/dev/mailbox", undefined, { anon: true });
  if (r.status === 404) {
    host.innerHTML = head("Dev mailbox") +
      `<div class="note note-warn">Not available. This exists only when the API runs in Development.</div>`;
    return;
  }
  const msgs = r.data || [];
  host.innerHTML = head("Dev mailbox", "Messages the app would have emailed. Development only — never present in a deployment.") +
    (msgs.length ? msgs.map(m => `
      <div class="card">
        <div class="between">
          <div><strong>${esc(m.subject)}</strong><div class="small muted">to ${esc(m.recipient)}</div></div>
          ${m.token ? `<a class="btn btn-sm" href="#/verify?token=${encodeURIComponent(m.token)}">Open the link</a>` : ""}
        </div>
        <pre class="tiny mono" style="white-space:pre-wrap;margin:10px 0 0;color:var(--ink-soft)">${esc(m.body)}</pre>
      </div>`).join("")
    : `<div class="card empty">Nothing captured yet. Register an account or request a reset link.</div>`);
});

/* =====================================================================
   STUDENT
   ===================================================================== */
route(/^\/me$/, async host => {
  const [me, apps] = await Promise.all([
    api("GET", "/api/seekers/me"),
    api("GET", "/api/seekers/me/applications?page=1&pageSize=5"),
  ]);
  const m = me.data || {};
  const ref = await reference();

  // The profile endpoint returns trackId, not a name - resolve it here rather
  // than showing a bare number.
  const trackName = nameOf(ref.tracks, m.trackId);

  // The API deliberately does not report verification state, so this is shown
  // to people who just registered rather than inferred from a missing field.
  const fresh = !!session.get()?.justRegistered;

  host.innerHTML = head(`Welcome${m.fullName ? ", " + m.fullName.split(" ")[0] : ""}`,
                        "Your assessment, your score, and the jobs open to you.") +
    (fresh ? `<div class="note note-warn" id="verify-note">
        <strong>Confirm your email.</strong> You can use everything now, but signing in again needs a
        confirmed address.
        <button class="btn-ghost btn-sm" id="resend" style="margin-left:8px">Send the link</button>
        <button class="btn-ghost btn-sm" id="dismiss">Dismiss</button></div>` : "") +
    `<div class="grid g2">
      <div class="card">
        <h3>Your profile</h3>
        <p class="small muted">Track: <strong>${esc(trackName)}</strong></p>
        <p class="small muted">Visible to companies: <strong>${m.visibleToCompanies === false ? "No" : "Yes"}</strong></p>
        <p class="small muted">Profile strength: <strong>${fmt(m.profileStrength)}</strong></p>
      </div>
      <div class="card">
        <h3>Assessment</h3>
        <p class="small muted">Sit a timed, track-specific assessment to get your Wasta Score.</p>
        <a class="btn" href="#/assessment">Go to assessment</a>
      </div>
    </div>
    <div class="card">
      <div class="between"><h3>Recent applications</h3><a class="small" href="#/applications">See all</a></div>
      ${listApplications((apps.data?.items) || [])}
    </div>`;

  const rs = $("#resend");
  if (rs) rs.onclick = async () => {
    await api("POST", "/api/auth/verify-email/resend", { email: session.get().email }, { anon: true });
    rs.textContent = "Link sent — open the dev mailbox"; rs.disabled = true;
  };
  const dm = $("#dismiss");
  if (dm) dm.onclick = () => {
    const cur = session.get(); delete cur.justRegistered; session.set(cur);
    $("#verify-note").remove();
  };
}, isSeeker);

function listApplications(items) {
  if (!items.length) return `<div class="empty">No applications yet. <a href="#/jobs">Browse jobs</a></div>`;
  return `<table><thead><tr><th>Role</th><th>Company</th><th>Status</th><th></th></tr></thead><tbody>
    ${items.map(a => `<tr>
      <td><strong>${esc(a.jobTitle)}</strong></td>
      <td class="muted">${esc(a.companyName)}</td>
      <td><span class="pill">${esc(a.statusName)}</span></td>
      <td class="right"><a class="small" href="#/applications/${a.applicationId}">Open</a></td>
    </tr>`).join("")}</tbody></table>`;
}

route(/^\/assessment$/, async host => {
  const ref = await reference();
  host.innerHTML = head("Assessment", "Pick a track and begin. The attempt is timed, and answers save as you go.") +
    `<div id="msg"></div>
     <div class="card">
       <div class="field" style="max-width:380px"><label>Track</label>
         <select id="track">${ref.tracks.map(t => `<option value="${t.id}">${esc(t.name)}</option>`).join("")}</select></div>
       <button class="btn" id="start">Start the assessment</button>
       <div class="hint" style="margin-top:10px">
         Once started, the clock runs. You can flag questions and come back to them before submitting.</div>
     </div>`;

  $("#start").onclick = async () => {
    $("#start").disabled = true;
    const r = await api("POST", `/api/assessments/tracks/${$("#track").value}/attempts`);
    $("#start").disabled = false;
    if (!r.ok) return note("#msg", "err", errText(r, "Could not start an attempt."));
    go(`#/attempt/${r.data.attemptId}`);
  };
}, isSeeker);

route(/^\/attempt\/(\d+)$/, async (host, id) => {
  const r = await api("GET", `/api/assessments/attempts/${id}`);
  if (!r.ok) return (host.innerHTML = `<div class="note note-err">${esc(errText(r, "Attempt not found."))}</div>`);
  const a = r.data;
  if (a.state !== "InProgress") return go(`#/results/${id}`);

  const qs = a.questions.slice().sort((x, y) => x.displayOrder - y.displayOrder);
  const answers = new Map(qs.map(q => [q.questionId, q.selectedOptionId ?? null]));
  const flags = new Map(qs.map(q => [q.questionId, !!q.flaggedForReview]));
  let cur = 0, remaining = a.remainingSeconds;

  host.innerHTML = `
    <div class="between page-head">
      <div><h1>Assessment</h1><p>${qs.length} questions · answers save automatically</p></div>
      <div class="timer" id="timer"></div>
    </div>
    <div id="msg"></div>
    <div class="qbar" id="qbar"></div>
    <div class="card" id="q"></div>
    <div class="between">
      <button class="btn-ghost" id="prev">← Previous</button>
      <div class="row">
        <button class="btn-ghost" id="flag">Flag for review</button>
        <button class="btn" id="next">Next →</button>
      </div>
    </div>
    <div class="card" style="margin-top:14px">
      <div class="between">
        <div><strong>Finished?</strong>
          <div class="small muted" id="progress"></div></div>
        <button class="btn" id="submit">Submit assessment</button>
      </div>
    </div>`;

  const tick = setInterval(() => {
    remaining--;
    const el = $("#timer");
    if (!el) return clearInterval(tick);
    const m = Math.max(0, Math.floor(remaining / 60)), s = Math.max(0, remaining % 60);
    el.textContent = `${m}:${String(s).padStart(2, "0")}`;
    el.className = "timer" + (remaining < 300 ? " low" : "");
    if (remaining <= 0) { clearInterval(tick); submit(true); }
  }, 1000);

  function paintBar() {
    $("#qbar").innerHTML = qs.map((q, i) => {
      const cls = [i === cur ? "on" : "", flags.get(q.questionId) ? "flagged" : (answers.get(q.questionId) ? "answered" : "")].join(" ");
      return `<button class="${cls}" data-i="${i}">${i + 1}</button>`;
    }).join("");
    $("#qbar").querySelectorAll("button").forEach(b => b.onclick = () => { cur = Number(b.dataset.i); paint(); });
    const done = [...answers.values()].filter(Boolean).length;
    $("#progress").textContent = `${done} of ${qs.length} answered`;
  }

  function paint() {
    const q = qs[cur];
    let prompt = q.body;
    try { prompt = JSON.parse(q.body).prompt ?? q.body; } catch { /* plain text body */ }
    $("#q").innerHTML = `
      <div class="small muted">Question ${cur + 1} of ${qs.length}</div>
      <h3 style="margin:6px 0 14px">${esc(prompt)}</h3>
      ${q.options.slice().sort((x, y) => x.displayOrder - y.displayOrder).map(o => {
        const on = answers.get(q.questionId) === o.optionId;
        // A div, not a label wrapping a radio. A click on such a label fires
        // twice - once for the label, once for the input bubbling back - which
        // raced two identical saves and made the server 500 on a duplicate key.
        return `<div class="opt ${on ? "sel" : ""}" role="radio" tabindex="0"
                     aria-checked="${on}" data-opt="${o.optionId}">
                  <span class="dot" aria-hidden="true"></span>${esc(o.body)}
                </div>`;
      }).join("")}`;
    $("#q").querySelectorAll(".opt").forEach(el => {
      const choose = () => save(q.questionId, Number(el.dataset.opt));
      el.onclick = choose;
      el.onkeydown = e => { if (e.key === " " || e.key === "Enter") { e.preventDefault(); choose(); } };
    });
    $("#flag").textContent = flags.get(q.questionId) ? "Unflag" : "Flag for review";
    $("#prev").disabled = cur === 0;
    $("#next").disabled = cur === qs.length - 1;
    paintBar();
  }

  async function save(qid, optionId) {
    // Nothing changed: skip the round trip entirely rather than racing an
    // identical write against one already in flight.
    if (answers.get(qid) === optionId) return;
    answers.set(qid, optionId);
    paint();
    const r = await api("PUT", `/api/assessments/attempts/${id}/answers/${qid}`,
      { selectedOptionId: optionId, flaggedForReview: flags.get(qid) });
    if (!r.ok) note("#msg", "err", errText(r, "That answer did not save. Check your connection."));
  }

  $("#prev").onclick = () => { if (cur > 0) { cur--; paint(); } };
  $("#next").onclick = () => { if (cur < qs.length - 1) { cur++; paint(); } };
  $("#flag").onclick = async () => {
    const q = qs[cur];
    flags.set(q.questionId, !flags.get(q.questionId));
    paint();
    await api("PUT", `/api/assessments/attempts/${id}/answers/${q.questionId}`,
      { selectedOptionId: answers.get(q.questionId), flaggedForReview: flags.get(q.questionId) });
  };

  async function submit(auto) {
    const unanswered = qs.length - [...answers.values()].filter(Boolean).length;
    if (!auto && unanswered > 0 &&
        !confirm(`${unanswered} question${unanswered === 1 ? "" : "s"} still unanswered. Submit anyway?`)) return;
    clearInterval(tick);
    $("#submit").disabled = true;
    const r = await api("POST", `/api/assessments/attempts/${id}/submit`);
    if (!r.ok) { $("#submit").disabled = false; return note("#msg", "err", errText(r, "Could not submit.")); }
    go(`#/results/${id}`);
  }
  $("#submit").onclick = () => submit(false);

  paint();
}, isSeeker);

route(/^\/results\/(\d+)$/, async (host, id) => {
  const r = await api("GET", `/api/assessments/attempts/${id}/results`);
  if (!r.ok) return (host.innerHTML = `<div class="note note-err">${esc(errText(r, "No results for that attempt."))}</div>`);
  const d = r.data;
  host.innerHTML = head("Your Wasta Score", "Companies see this score and your percentile — not your name.") +
    scoreCard(d) +
    `<div class="card"><h3>Section feedback</h3>
      ${d.sections.map(s => `<div style="margin:12px 0">
        <div class="between"><strong>${esc(s.sectionName)}</strong>
          <span class="pill ${s.percent >= 80 ? "pill-ok" : s.percent >= 60 ? "pill-accent" : "pill-warn"}">${esc(s.bandName)} · ${s.percent}%</span></div>
        <div class="small muted">${esc(s.feedback || "")}</div></div>`).join("")}
    </div>
    <div class="row"><a class="btn" href="#/coach">See your study plan</a>
      <a class="btn-ghost" href="#/jobs">Browse jobs</a>
      <a class="btn-ghost" href="#/assessment" style="text-decoration:none;display:inline-block">Take another</a></div>`;
}, isSeeker);

function scoreCard(d) {
  return `<div class="score">
    <div><div class="big">${d.overallPercent}<span>%</span></div>
      <div class="plabel">${d.percentile === null ? "Percentile withheld" : d.percentile + "th percentile"}</div></div>
    <div class="bars">${d.sections.map(s => `
      <div class="bar-row"><span>${esc(s.sectionName)}</span>
        <span class="bar"><i style="width:${Math.max(2, s.percent)}%"></i></span>
        <span class="b">${esc(s.bandName)} · ${s.percent}%</span></div>`).join("")}</div>
  </div>
  ${d.percentile === null ? `<div class="note note-warn">
     Your percentile is withheld until at least 50 people have sat this track's assessment. A percentile
     from a handful of attempts would be misleading, so we don't show one.</div>` : ""}`;
}

route(/^\/coach$/, async host => {
  const r = await api("GET", "/api/students/me/coach-plan");
  const d = r.data || {};

  if (d.status !== "ready") {
    // "pending" means it is being written. "unavailable" means there is no
    // scored attempt yet, or the model's answer was rejected by the guardrails.
    const pending = d.status === "pending";
    host.innerHTML = head("Career Coach", "A study plan written from your own section scores.") +
      `<div class="card">
         <div class="note ${pending ? "note-info" : "note-warn"}">
           ${pending
             ? "Your plan is being written now. It is generated once, in the background, so it never holds up your results."
             : "No plan yet. Sit an assessment first — the coach works from your section scores, so it has nothing to work from until then."}
         </div>
         ${pending
           ? `<button class="btn" id="again">Check again</button>`
           : `<a class="btn" href="#/assessment">Go to assessment</a>`}
       </div>`;
    const b = $("#again");
    if (b) b.onclick = () => render();
    return;
  }

  const weeks = (d.weekly_plan || []).map(w => `
    <div class="card" style="margin:0">
      <div class="between">
        <h3>Week ${w.week}</h3>
        ${w.focus ? `<span class="pill pill-accent">${esc(w.focus)}</span>` : ""}
      </div>
      <ul class="plain" style="margin-top:10px">
        ${(w.actions || []).map(a => `<li class="small">• ${esc(a)}</li>`).join("")}
      </ul>
      ${w.checkpoint ? `<div class="small muted" style="margin-top:9px"><strong>Checkpoint:</strong> ${esc(w.checkpoint)}</div>` : ""}
    </div>`).join("");

  const proj = d.project_suggestion || {};
  host.innerHTML = head("Career Coach", "Written from your section scores — and never repeats your score back at you.") +
    `<div class="card" style="background:var(--brand-wash);border-color:var(--brand-200)">
       <h2 style="color:var(--brand-ink)">${esc(d.headline)}</h2>
       <p class="small" style="margin-top:8px">${esc(d.assessment)}</p>
     </div>
     <h3 style="margin:20px 0 10px">Four weeks</h3>
     <div class="grid g2">${weeks}</div>
     ${proj.title ? `
       <h3 style="margin:22px 0 10px">Build this</h3>
       <div class="card">
         <h3>${esc(proj.title)}</h3>
         <p class="small muted" style="margin-top:6px">${esc(proj.description || "")}</p>
         <div class="row" style="margin-top:11px">
           ${(proj.skills_practised || []).map(k => `<span class="pill">${esc(k)}</span>`).join("")}
         </div>
       </div>` : ""}
     ${d.interview_line ? `
       <div class="card">
         <h3>Say this in an interview</h3>
         <p class="small muted" style="margin-top:6px">${esc(d.interview_line)}</p>
       </div>` : ""}
     <p class="tiny muted">Generated by an AI model from your section scores, then checked against rules that reject any
       answer leaking your numeric score, inventing a job listing, or obeying instructions hidden in the input.</p>`;
}, isSeeker);

route(/^\/jobs$/, async host => {
  const r = await api("GET", "/api/jobs?page=1&pageSize=20");
  const items = r.data?.items || [];
  host.innerHTML = head("Jobs", "Roles open to you. 'Recommended' means the post matches your track.") +
    `<div id="msg"></div>` +
    (items.length ? `<div class="grid g2">${items.map(j => `
      <div class="card">
        <div class="between"><h3>${esc(j.title)}</h3>
          ${j.isRecommended ? `<span class="pill pill-accent">Recommended</span>` : ""}</div>
        <p class="small muted">${esc(j.companyName)} · ${esc(j.trackName)}${j.city ? " · " + esc(j.city) : ""}</p>
        <p class="small muted">${[j.workType, j.employmentType].filter(Boolean).map(esc).join(" · ")}</p>
        <div class="row" style="margin-top:10px">
          ${j.hasApplied ? `<span class="pill pill-ok">Applied</span>`
                         : `<button class="btn btn-sm" data-apply="${j.jobPostId}">Apply</button>`}
          <span class="tiny muted">${j.applicantCount} applicant${j.applicantCount === 1 ? "" : "s"}</span>
        </div>
      </div>`).join("")}</div>`
    : `<div class="card empty">No jobs posted yet.</div>`);

  host.querySelectorAll("[data-apply]").forEach(b => b.onclick = async () => {
    b.disabled = true;
    const r2 = await api("POST", `/api/jobs/${b.dataset.apply}/apply`);
    if (!r2.ok) { b.disabled = false; return note("#msg", "err", errText(r2, "Could not apply.")); }
    note("#msg", "ok", "Applied. Add your project from the Applications page.");
    render();
  });
}, isSeeker);

route(/^\/applications$/, async host => {
  const r = await api("GET", "/api/seekers/me/applications?page=1&pageSize=50");
  host.innerHTML = head("Applications", "Each application carries a project you submit work against.") +
    `<div class="card">${listApplications(r.data?.items || [])}</div>`;
}, isSeeker);

route(/^\/applications\/(\d+)$/, async (host, id) => {
  const r = await api("GET", `/api/seekers/me/applications/${id}`);
  if (!r.ok) return (host.innerHTML = `<div class="note note-err">${esc(errText(r, "Not found."))}</div>`);
  const a = r.data;
  const locked = !!a.submittedAt;
  host.innerHTML = head(a.jobTitle, `${a.companyName} · ${a.statusName}`) +
    `<div id="msg"></div>
     ${a.feedback ? `<div class="note note-info"><strong>Feedback:</strong> ${esc(a.feedback)}</div>` : ""}
     <div class="card">
       <h3>Your project</h3>
       ${locked ? `<div class="note note-ok small">Submitted. It can no longer be edited.</div>` : ""}
       <div class="field"><label>Title</label><input id="projectTitle" value="${esc(a.projectTitle || "")}" ${locked ? "disabled" : ""} /></div>
       <div class="field"><label>Description</label><textarea id="description" ${locked ? "disabled" : ""}>${esc(a.description || "")}</textarea></div>
       <div class="grid g2">
         <div class="field"><label>Repository URL</label><input id="repoUrl" value="${esc(a.repoUrl || "")}" ${locked ? "disabled" : ""} /></div>
         <div class="field"><label>Live demo URL</label><input id="liveDemoUrl" value="${esc(a.liveDemoUrl || "")}" ${locked ? "disabled" : ""} /></div>
       </div>
       ${locked ? "" : `<div class="row">
          <button class="btn-ghost" id="save">Save draft</button>
          <button class="btn" id="submit">Submit project</button></div>`}
     </div>`;

  if (locked) return;
  const payload = () => ({
    projectTitle: $("#projectTitle").value.trim() || null,
    description: $("#description").value.trim() || null,
    repoUrl: $("#repoUrl").value.trim() || null,
    liveDemoUrl: $("#liveDemoUrl").value.trim() || null,
  });
  $("#save").onclick = async () => {
    const r2 = await api("PUT", `/api/seekers/me/applications/${id}`, payload());
    note("#msg", r2.ok ? "ok" : "err", r2.ok ? "Draft saved." : errText(r2));
  };
  $("#submit").onclick = async () => {
    if (!confirm("Submit this project? It can't be edited afterwards.")) return;
    await api("PUT", `/api/seekers/me/applications/${id}`, payload());
    const r2 = await api("POST", `/api/seekers/me/applications/${id}/submit`);
    if (!r2.ok) return note("#msg", "err", errText(r2));
    render();
  };
}, isSeeker);

/* =====================================================================
   COMPANY
   ===================================================================== */
route(/^\/company$/, async host => {
  const [me, credits, jobs] = await Promise.all([
    api("GET", "/api/companies/me"),
    api("GET", "/api/companies/me/credits"),
    api("GET", "/api/companies/me/jobs?page=1&pageSize=5"),
  ]);
  const m = me.data || {};
  const approved = m.isVerified === true;

  // Credits, the talent pool and unlocking all answer 403 until an
  // administrator approves the company. That is the platform working, so say
  // so rather than rendering a broken dash.
  const balance = credits.status === 403 ? null : credits.data?.balance;

  host.innerHTML = head(m.name || "Your company", "Find candidates by proven skill.") +
    (approved ? "" : `<div class="note note-warn">
      <strong>Awaiting approval.</strong> An administrator reviews every new company before it can
      browse candidates or hold credits. Everything below stays closed until then.</div>`) +
    `<div class="grid g3">
      <div class="card center"><div style="font-size:38px;font-weight:700;color:var(--accent)">${balance === null ? "—" : balance}</div>
        <div class="small muted">${balance === null ? "available once approved" : "credits available"}</div>
        ${approved ? `<a class="btn btn-sm" href="#/credits" style="margin-top:8px;display:inline-block">Buy more</a>` : ""}</div>
      <div class="card center"><div style="font-size:38px;font-weight:700">${jobs.data?.totalCount ?? (jobs.data?.items || []).length}</div>
        <div class="small muted">job posts</div>
        <a class="btn-sm btn-ghost" href="#/company/jobs" style="margin-top:8px;display:inline-block;text-decoration:none">Manage</a></div>
      <div class="card center"><div style="font-size:38px;font-weight:700">→</div>
        <div class="small muted">browse the talent pool</div>
        <a class="btn btn-sm" href="#/talent" style="margin-top:8px;display:inline-block">Open</a></div>
    </div>`;
}, isCompany);

route(/^\/talent$/, async host => {
  const ref = await reference();
  const r = await api("GET", "/api/talent-pool?page=1&pageSize=24");
  if (r.status === 403) {
    host.innerHTML = head("Talent pool") + `<div class="note note-warn">
      <strong>Awaiting approval.</strong> The talent pool opens once an administrator has approved
      your company. This is deliberate: candidates are real people, and we check who is looking at
      them.</div>`;
    return;
  }
  const items = r.data?.items || [];
  host.innerHTML = head("Talent pool", "Candidates are anonymous until you spend a credit to unlock one.") +
    `<div id="msg"></div>
     <div class="card"><div class="row">
       <div style="flex:1;min-width:200px"><label>Track</label>
         <select id="track"><option value="">All tracks</option>
           ${ref.tracks.map(t => `<option value="${t.id}">${esc(t.name)}</option>`).join("")}</select></div>
       <button class="btn" id="filter" style="margin-top:20px">Filter</button>
     </div></div>
     <div class="grid g3" id="list">${items.map(candCard).join("") || `<div class="card empty">No candidates yet.</div>`}</div>`;

  $("#filter").onclick = async () => {
    const t = $("#track").value;
    const r2 = await api("GET", `/api/talent-pool?page=1&pageSize=24${t ? "&trackId=" + t : ""}`);
    $("#list").innerHTML = (r2.data?.items || []).map(candCard).join("") || `<div class="card empty">No matches.</div>`;
  };
}, isCompany);

function candCard(c) {
  return `<div class="card cand">
    <div class="between"><span class="ref">${esc(c.candidateReference)}</span>
      ${c.isUnlocked ? `<span class="pill pill-ok">Unlocked</span>` : ""}</div>
    <div class="small muted" style="margin-top:5px">${esc(c.trackName)}</div>
    <div class="small" style="margin-top:3px"><strong>${fmt(c.overallPercent)}%</strong>
      ${c.percentile !== null ? `<span class="muted"> · ${c.percentile}th pct</span>` : ""}</div>
    <a class="btn btn-sm" href="#/candidate/${c.seekerId}" style="margin-top:10px;display:inline-block">View</a>
  </div>`;
}

route(/^\/candidate\/(\d+)$/, async (host, id) => {
  const r = await api("GET", `/api/talent-pool/${id}`);
  if (!r.ok) return (host.innerHTML = `<div class="note note-err">${esc(errText(r, "Candidate not found."))}</div>`);
  const c = r.data;
  host.innerHTML = head(c.isUnlocked ? c.fullName : c.candidateReference,
                        `${c.trackName}${c.isUnlocked ? "" : " · identity locked"}`) +
    `<div id="msg"></div>` +
    (c.sections?.length ? scoreCard({ overallPercent: c.overallPercent, percentile: c.percentile, sections: c.sections }) : "") +
    `<div class="card">
      <h3>Contact details</h3>
      ${c.isUnlocked
        ? `<p class="small"><strong>Name:</strong> ${esc(c.fullName)}</p>
           <p class="small"><strong>Email:</strong> ${esc(c.email)}</p>
           <p class="small"><strong>Phone:</strong> ${esc(c.phoneNumber || "—")}</p>
           ${c.cvUrl ? `<a class="btn btn-sm" href="${esc(c.cvUrl)}">Download CV</a>` : ""}`
        : `<div class="note note-info">Locked. Spend one credit to reveal this candidate's name and contact details.
             Unlocking the same candidate again is free.</div>
           <button class="btn" id="unlock">Unlock for 1 credit</button>`}
    </div>`;

  const u = $("#unlock");
  if (u) u.onclick = async () => {
    u.disabled = true;
    const r2 = await api("POST", `/api/talent-pool/${id}/unlock`);
    if (!r2.ok) { u.disabled = false; return note("#msg", "err", errText(r2, "Could not unlock.")); }
    render();
  };
}, isCompany);

route(/^\/company\/jobs$/, async host => {
  const ref = await reference();
  const r = await api("GET", "/api/companies/me/jobs?page=1&pageSize=50");
  const items = r.data?.items || [];
  host.innerHTML = head("My jobs", "Post a role and review who applies.") +
    `<div id="msg"></div>
     <div class="card"><h3>Post a job</h3>
       <div class="grid g2">
         <div class="field"><label>Title</label><input id="title" /></div>
         <div class="field"><label>Track</label>
           <select id="trackId">${ref.tracks.map(t => `<option value="${t.id}">${esc(t.name)}</option>`).join("")}</select></div>
       </div>
       <div class="field"><label>Description</label><textarea id="desc"></textarea></div>
       <div class="grid g2">
         <div class="field"><label>Work type</label><select id="workTypeId"><option value="">—</option>
           ${(ref.workTypes || []).map(w => `<option value="${w.id}">${esc(w.name)}</option>`).join("")}</select></div>
         <div class="field"><label>Location</label><select id="locationId"><option value="">—</option>
           ${(ref.locations || []).map(l => `<option value="${l.id}">${esc(l.city)}</option>`).join("")}</select></div>
       </div>
       <div class="field"><label>Project brief (optional)</label><textarea id="brief"></textarea></div>
       <button class="btn" id="post">Post job</button>
     </div>
     ${items.length ? `<div class="card"><h3>Posted</h3>
       <table><thead><tr><th>Title</th><th>Track</th><th>Applicants</th><th></th></tr></thead><tbody>
       ${items.map(j => `<tr><td><strong>${esc(j.title)}</strong></td><td class="muted">${esc(j.trackName)}</td>
         <td>${j.applicantCount}</td>
         <td class="right"><a class="small" href="#/company/jobs/${j.jobPostId}/applicants">Applicants</a></td></tr>`).join("")}
       </tbody></table></div>` : `<div class="card empty">No jobs posted yet.</div>`}`;

  $("#post").onclick = async () => {
    $("#post").disabled = true;
    const r2 = await api("POST", "/api/companies/me/jobs", {
      title: $("#title").value.trim(),
      trackId: Number($("#trackId").value),
      jobDescription: $("#desc").value.trim(),
      workTypeId: $("#workTypeId").value ? Number($("#workTypeId").value) : null,
      locationId: $("#locationId").value ? Number($("#locationId").value) : null,
      employmentTypeId: null, salary: null,
      projectBrief: $("#brief").value.trim() || null,
      projectDeadline: null, skillIds: null,
    });
    $("#post").disabled = false;
    if (!r2.ok) return note("#msg", "err", errText(r2, "Could not post the job."));
    render();
  };
}, isCompany);

route(/^\/company\/jobs\/(\d+)\/applicants$/, async (host, id) => {
  const ref = await reference();
  const r = await api("GET", `/api/companies/me/jobs/${id}/applicants?page=1&pageSize=50`);
  const items = r.data?.items || [];
  host.innerHTML = head("Applicants", "Scores and submitted work. Names stay hidden until you unlock the candidate.") +
    `<div id="msg"></div>` +
    (items.length ? `<div class="card"><table>
      <thead><tr><th>Candidate</th><th>Score</th><th>Project</th><th>Status</th><th></th></tr></thead><tbody>
      ${items.map(a => `<tr>
        <td><span class="mono"><strong>${esc(a.candidateReference)}</strong></span></td>
        <td>${fmt(a.overallPercent)}%</td>
        <td>${a.projectTitle ? esc(a.projectTitle) : `<span class="muted small">not submitted</span>`}
            ${a.repoUrl ? `<div class="tiny"><a href="${esc(a.repoUrl)}" target="_blank" rel="noopener">repo</a></div>` : ""}</td>
        <td><select data-app="${a.applicationId}" class="small">
          ${(ref.applicationStatuses || []).map(s => `<option value="${s.id}" ${s.id === a.statusId ? "selected" : ""}>${esc(s.name)}</option>`).join("")}
        </select></td>
        <td class="right"><button class="btn-ghost btn-sm" data-save="${a.applicationId}">Save</button></td>
      </tr>`).join("")}</tbody></table></div>`
    : `<div class="card empty">Nobody has applied yet.</div>`);

  host.querySelectorAll("[data-save]").forEach(b => b.onclick = async () => {
    const appId = b.dataset.save;
    const sel = host.querySelector(`[data-app="${appId}"]`);
    const r2 = await api("PUT", `/api/companies/me/applications/${appId}/status`,
      { statusId: Number(sel.value), feedback: null });
    note("#msg", r2.ok ? "ok" : "err", r2.ok ? "Status updated." : errText(r2));
  });
}, isCompany);

route(/^\/credits$/, async host => {
  const [balance, ledger, topups] = await Promise.all([
    api("GET", "/api/companies/me/credits"),
    api("GET", "/api/companies/me/credits/ledger?page=1&pageSize=20"),
    api("GET", "/api/companies/me/credits/topups?page=1&pageSize=10"),
  ]);
  host.innerHTML = head("Credits", "Credits are bought by bank transfer and issued once an administrator confirms it.") +
    `<div id="msg"></div>
     <div class="grid g2">
       <div class="card center"><div style="font-size:44px;font-weight:700;color:var(--accent)">${fmt(balance.data?.balance)}</div>
         <div class="small muted">credits available</div></div>
       <div class="card"><h3>Request credits</h3>
         <div class="field"><label>Credits</label><input id="credits" type="number" value="25" min="1" /></div>
         <div class="field"><label>Amount paid (EGP)</label><input id="amount" type="number" value="2500" min="1" /></div>
         <button class="btn" id="request">Request</button>
         <div class="hint">An administrator confirms the transfer before credits appear.</div>
       </div>
     </div>
     <div class="card"><h3>Pending requests</h3>
       ${(topups.data?.items || []).length ? `<table><thead><tr><th>Credits</th><th>Amount</th><th>Status</th></tr></thead><tbody>
         ${topups.data.items.map(t => `<tr><td>${t.creditsRequested}</td><td>${fmt(t.amount)} ${esc(t.currency || "")}</td>
           <td><span class="pill">${esc(t.status || t.statusName || "Pending")}</span></td></tr>`).join("")}
         </tbody></table>` : `<div class="empty">No requests.</div>`}
     </div>
     <div class="card"><h3>Ledger</h3>
       ${(ledger.data?.items || []).length ? `<table><thead><tr><th>Change</th><th>Reason</th><th>When</th></tr></thead><tbody>
         ${ledger.data.items.map(e => `<tr>
           <td><strong style="color:${e.delta > 0 ? "var(--ok)" : "var(--danger)"}">${e.delta > 0 ? "+" : ""}${e.delta}</strong></td>
           <td class="muted">${esc(e.reason || e.kind || "")}</td>
           <td class="muted small">${new Date(e.createdAt).toLocaleString()}</td></tr>`).join("")}
         </tbody></table>` : `<div class="empty">Nothing yet.</div>`}
     </div>`;

  $("#request").onclick = async () => {
    $("#request").disabled = true;
    const r = await api("POST", "/api/companies/me/credits/topups", {
      creditsRequested: Number($("#credits").value),
      paymentMethodId: 1,                       // Bank transfer — the only method in v1.
      amount: Number($("#amount").value),
      currency: "EGP",
    });
    $("#request").disabled = false;
    if (!r.ok) return note("#msg", "err", errText(r, "Could not submit the request."));
    note("#msg", "ok", "Requested. An administrator will confirm the transfer.");
    render();
  };
}, isCompany);

/* =====================================================================
   ADMIN
   ===================================================================== */
route(/^\/admin$/, async host => {
  const [companies, topups] = await Promise.all([
    api("GET", "/api/admin/companies/pending?page=1&pageSize=50"),
    api("GET", "/api/admin/topups/pending?page=1&pageSize=50"),
  ]);
  host.innerHTML = head("Review queue", "Companies waiting for approval, and bank transfers waiting to be confirmed.") +
    `<div id="msg"></div>
     <div class="card"><h3>Companies awaiting approval</h3>
       ${(companies.data?.items || []).length ? `<table><thead><tr><th>Company</th><th>Email</th><th></th></tr></thead><tbody>
         ${companies.data.items.map(c => `<tr><td><strong>${esc(c.name)}</strong>
             ${c.website ? `<div class="tiny muted">${esc(c.website)}</div>` : ""}</td>
           <td class="muted small">${esc(c.email || "")}
             <div class="tiny">${c.documentCount} document${c.documentCount === 1 ? "" : "s"}</div></td>
           <td class="right"><button class="btn btn-sm" data-approve="${c.companyId}">Approve</button>
             <button class="btn-ghost btn-sm" data-reject="${c.companyId}">Reject</button></td></tr>`).join("")}
         </tbody></table>` : `<div class="empty">Nothing waiting.</div>`}
     </div>
     <div class="card"><h3>Credit requests</h3>
       ${(topups.data?.items || []).length ? `<table><thead><tr><th>Company</th><th>Credits</th><th>Amount</th><th></th></tr></thead><tbody>
         ${topups.data.items.map(t => `<tr><td>${esc(t.companyName || "#" + t.companyId)}</td>
           <td>${t.creditsRequested}</td><td>${fmt(t.amount)} ${esc(t.currency || "")}</td>
           <td class="right"><button class="btn btn-sm" data-issue="${t.requestId || t.id}">Confirm &amp; issue</button></td></tr>`).join("")}
         </tbody></table>` : `<div class="empty">Nothing waiting.</div>`}
     </div>`;

  host.querySelectorAll("[data-approve]").forEach(b => b.onclick = async () => {
    const r = await api("POST", `/api/admin/companies/${b.dataset.approve}/approve`);
    note("#msg", r.ok ? "ok" : "err", r.ok ? "Approved." : errText(r)); render();
  });
  host.querySelectorAll("[data-reject]").forEach(b => b.onclick = async () => {
    const note_ = prompt("Why is this company being rejected?");
    if (!note_) return;
    const r = await api("POST", `/api/admin/companies/${b.dataset.reject}/reject`, { note: note_ });
    note("#msg", r.ok ? "ok" : "err", r.ok ? "Rejected." : errText(r)); render();
  });
  host.querySelectorAll("[data-issue]").forEach(b => b.onclick = async () => {
    const r = await api("POST", `/api/admin/topups/${b.dataset.issue}/review`,
      { approve: true, note: "Bank transfer confirmed." });
    note("#msg", r.ok ? "ok" : "err", r.ok ? "Credits issued." : errText(r)); render();
  });
}, isAdmin);

route(/^\/admin\/content$/, async host => {
  const r = await api("GET", "/api/admin/content/readiness");
  const rows = r.data?.tracks || r.data || [];
  host.innerHTML = head("Content readiness", "Whether each track has real assessment content, or is still on placeholders.") +
    `<div class="note note-warn">Every track below is seeded with placeholder questions. Until a subject-matter
      expert authors real items, the scores this platform produces demonstrate the mechanism, not ability.</div>
     <div class="card">${Array.isArray(rows) && rows.length
       ? `<table><thead><tr><th>Track</th><th>Questions</th><th>Ready</th></tr></thead><tbody>
         ${rows.map(t => `<tr><td><strong>${esc(t.trackName || t.name)}</strong></td>
           <td>${fmt(t.questionCount ?? t.questions)}</td>
           <td>${t.isReady ? `<span class="pill pill-ok">Ready</span>` : `<span class="pill pill-warn">Placeholder</span>`}</td></tr>`).join("")}
         </tbody></table>`
       : `<pre class="tiny mono" style="white-space:pre-wrap">${esc(JSON.stringify(r.data, null, 2))}</pre>`}</div>`;
}, isAdmin);

/* ---------------- helpers ---------------- */
function note(sel, kind, text) {
  const el = $(sel);
  if (el) el.innerHTML = `<div class="note note-${kind}">${esc(text)}</div>`;
}

window.addEventListener("hashchange", render);
render();
