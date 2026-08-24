# Wasta AI features — acceptance checklist

Covers the AI Career Coach and the Support Chatbot.

**Sign off only when every item is verified. Flag anything uncertain rather than assuming pass.**

Status keys used below:
- **[auto]** — covered by the automated suite; `dotnet test` proves it
- **[verified]** — checked by hand against a running app
- **[blocked]** — cannot be verified yet, and why

> **The guardrail script's own output was misleading.** It defined a shell function named `head`,
> which shadowed the `head` command the assertions use to truncate. Replies printed as `-c` in bold
> instead of their text, so a pass looked identical to a pass on an empty reply. Renamed to
> `section`. Worth remembering: a green run whose evidence you cannot read is not a green run.

> **The standing rule:** mocked-provider runs prove the plumbing, never the model. Rows marked
> *needs a real key* are false-green under mocks — a stub returns whatever string it was told to,
> so it cannot tell you whether Groq or Gemini would leak a percentage or fall for an injection.
> Re-run those periodically, since provider-side model updates change behaviour silently.

---

## Setup

- [auto] `dotnet build WastaCareerCoach.sln` clean — 0 warnings, 0 errors (CI enforces `--warnaserror`)
- [auto] `dotnet test WastaCareerCoach.sln` — 312 passing (59 Career Coach, 36 Support Chat,
  154 platform API integration, 39 domain, 16 application, 8 architecture)
- [auto] Platform API integration tests run against a real PostgreSQL container via Testcontainers,
  not the in-memory provider — unique indexes and `jsonb` columns are actually exercised
- [auto] Architecture tests fail the build if a layer gains a forbidden dependency

## Assessment delivery and scoring

- [auto] The answer key never reaches the candidate — asserted on the raw response payload, and the
  display query never loads `is_correct` at all
- [auto] A skipped question costs the same as a wrong one; every question on the form is graded
- [auto] Section percentages average per section, not per question, so a section with more questions
  does not quietly count for more
- [auto] Section weights are renormalised over the sections actually on the form
- [auto] Retake cooldown is 30 days **per track** — blocked on the same track, allowed on another,
  lifts after 31 days
- [auto] A submission or an answer save after the deadline is rejected server-side; the client clock
  is never trusted
- [auto] The percentile is withheld below the configured minimum cohort (default 50)
- [auto] Another seeker's attempt returns **404, not 403**, on read, submit, and results
- [auto] An option belonging to a different question is refused
- [auto] Submitting twice is refused; results are unavailable until submission
- [verified] The whole flow exercised against the running API and real Postgres: start, fetch,
  answer, submit, score 100% with bands and per-section feedback, percentile suppressed

## Jobs, applications and projects

- [auto] Only a **verified** company can post; an unapproved one is refused
- [auto] The active-post cap is 6 per company, and closing a post frees a slot
- [auto] A salary amount without a currency is refused — four currencies are in play, so a bare
  number would be read as whichever the reader assumes
- [auto] Another company's post reports **404** on edit, close, and applicants
- [auto] A post on the seeker's own track is flagged `isRecommended` and sorted first
- [auto] Search and paging work; a closed post disappears from browsing but stays reachable by id
- [auto] Applying to a closed post is refused
- [auto] The live-application cap is 6 per seeker; withdrawing frees a slot
- [auto] Re-applying creates a **second** application rather than reusing the first, so earlier
  submitted work survives
- [auto] Another seeker's application reports **404** on read, edit, and withdraw
- [auto] **Applicants are anonymous until unlocked** — the response carries a derived reference
  (`#A7DC`), and the candidate's real name appears nowhere in the payload
- [auto] A company cannot review another company's applicant (404), and cannot mark an applicant
  withdrawn — that is the applicant's to do
- [auto] A broken domain rule returns **400 with its code**, never a 500
- [verified] Flow exercised against the running API: post, browse, apply, and review, with the
  applicant list confirmed to leak no name

## Talent pool, unlocks and credits *(treat as the money path)*

- [auto] An unverified company cannot see the talent pool at all
- [auto] Candidates appear anonymised with their score; the real name is absent from the payload,
  not merely nulled in one field
- [auto] Identity appears only after an unlock, and the section scores and projects are visible
  either way — only the identity costs a credit
- [auto] A seeker who opted out is absent from the pool, cannot be unlocked, and no credit is spent
- [auto] Unlocking spends exactly one credit and writes one ledger row
- [auto] Unlocking the same candidate twice charges once and returns the existing unlock
- [auto] Running out of credits refuses the unlock and leaves the balance at zero, never negative
- [auto] **8 parallel unlocks of one candidate charge exactly one credit**
- [auto] **6 parallel unlocks against a 3-credit balance succeed exactly 3 times**
- [verified] Both concurrency guarantees confirmed by removing the guard: with the `FOR UPDATE`
  row lock removed, 6 unlocks succeeded on a 3-credit balance. The unique index alone catches the
  same-candidate case; only the row lock prevents overspending across different candidates
- [auto] Approving a company grants exactly 3 trial credits, and approving twice is refused
  without granting again
- [auto] A top-up request adds nothing until an admin confirms the transfer arrived; a rejected
  request cannot later be approved
- [auto] A company cannot reach the admin endpoints

> **No default admin.** The seeder creates one only when both `Seed:AdminEmail` and
> `Seed:AdminPassword` are supplied by configuration. A seeded admin with a known password is a
> backdoor that ships the first time someone forgets to override it.

## File uploads and downloads

- [auto] Files are identified by **signature, not extension or Content-Type** — both are attacker
  controlled. An executable renamed `cv.pdf` and declared `application/pdf` is refused
- [auto] A CV must be a PDF and under 5 MB; project attachments also accept images and Office files
- [auto] The storage key is generated server-side, so the uploader's filename is never part of a
  path; traversal sequences in a name are flattened before the name is echoed back
- [auto] Downloads need an unexpired signed token. Without one, tampered, or expired: **404**, so
  probing for keys reveals nothing
- [auto] A token minted for one file does not open another — the signature covers the key
- [auto] Replacing a CV deletes the previous file rather than orphaning it
- [auto] Another seeker's application cannot be given files (404)
- [auto] Uploads are rate limited **per user**, so one user exhausting their budget leaves everyone
  else unaffected
- [verified] Upload, signed download, unsigned 404, and the renamed-executable rejection all
  exercised against the running API
- [verified] **ClamAV scanning behind `IVirusScanner`**, off by default and enabled with
  `VirusScanning:Enabled`. The INSTREAM framing, byte-for-byte delivery across chunk boundaries, and
  reply parsing are all exercised against a fake clamd; a real-clamd test covering EICAR is gated on
  `WASTA_CLAMD_PORT` and **skips** rather than passing when it is unset
- [verified] **The scanner fails closed.** An unreachable clamd raises
  `VirusScannerUnavailableException`, which the host maps to 503 `file.scanner_unavailable` — never
  to "clean", and never to `file.infected`, because "the scanner is down" and "your CV is malware"
  are different things to tell a student
- [verified] A clamd `ERROR` reply — including its own stream size limit — is never read as clean.
  The one file too large to inspect is exactly the one that must not slip through
- [blocked] **Scanning is off until it is switched on in a deployment.** `NoOpVirusScanner` still
  stands in by default and the host warns on every boot. *Turn it on before public launch*

## Rate limiting

- [auto] Per-user upload limits are enforced and return 429
- [verified] Auth is limited per IP and unlocks per company, both configurable under `RateLimits`.
  Behind a proxy these depend on `UseForwardedHeaders`, which is wired

## Notifications

- [auto] Submitting an assessment, being unlocked, an application status change, company approval
  and rejection, and credits being issued each raise the right notification with the right payload
- [auto] **A refused unlock leaves no notification behind** — it is written inside the unlock
  transaction, so a rolled-back charge cannot tell a student they were viewed when nobody paid
- [auto] The status in an application-status notification is resolved from the new status id, not
  re-read from the database before the save — a stale read would have reported the previous status
- [auto] A user sees only their own notifications; someone else's returns 404 on mark-read
- [auto] Unread counts, mark-one-read and mark-all-read behave
- [auto] The dispatcher delivers pending notifications and marks them Sent; a second pass does not
  resend, because a delivered row is no longer pending
- [auto] Retry and backoff: the first attempt is immediate, each failure widens the delay, the row
  stays Pending until the attempt cap and only then becomes Failed
- [verified] End to end against the running API: submitting an assessment created the notification,
  and the background dispatcher picked it up on its own timer and marked it Sent
- [blocked] **Notifications are not actually delivered** — `LoggingNotificationSender` writes to the
  log. *Needs a real email/SMS provider before launch*

> **Never string-match a `jsonb` payload in a test.** Postgres normalises jsonb: it reorders keys and
> rewrites whitespace, so a substring assertion tests Postgres's formatter rather than your code.
> Parse the payload and assert on the value.

## Language

> **Right-to-left layout is a frontend concern and is out of scope for this backend.** What the
> server owns is which language it renders in.

- [auto] `/api/reference` is anonymous — the sign-up form needs the track list before anyone has an
  account to sign in with
- [auto] `Accept-Language: ar` returns Arabic tracks, statuses, cities and work types
- [auto] A regional tag (`ar-EG`) resolves to its primary language
- [auto] An unsupported language (`fr-FR`) falls back to English rather than failing
- [auto] An explicit `?lang=` beats the header
- [auto] Skills are deliberately **not** translated — React and TypeScript are proper nouns, and
  transliterating them would make them harder to recognise
- [auto] An untranslated row falls back to its English name rather than vanishing, so a partially
  translated database stays usable
- [auto] Results come back with Arabic section and band names when asked for
- [auto] A language preference is stored per account; an unsupported value is **refused** rather
  than silently stored as English
- [auto] Notifications render in the recipient's stored language, and data inside them — a company's
  own name — survives translation untouched
- [verified] Confirmed against the running API: `Accept-Language: ar-EG` returned
  `هندسة الواجهات الأمامية`, `قيد المراجعة`, `القاهرة`, with skills left as `AWS`, `C#`, `Docker`

## Account lifecycle, and PDPL access and erasure

- [auto] A new account starts unverified and is confirmed from the emailed link
- [auto] Verification and reset tokens are **single use**, and issuing a new one kills the previous
  one — requesting twice must not leave a spare valid link in an inbox
- [auto] A made-up token, an expired one, a used one and an invalidated one all report the same
  thing, so a stale link never reveals whether it was ever real
- [auto] `forgot-password` returns a **byte-identical** response for a registered and an unregistered
  address, and no email is sent to the stranger — the identical response must not come at the cost
  of mailing people who never signed up
- [auto] A reset changes the password, the old one stops working, and **every existing session ends**
  — a reset is what someone does when they think they are compromised
- [auto] The reset path enforces the same password policy as registration
- [auto] `/api/me/export` returns everything held about the account; it needs authentication
- [auto] Erasure scrubs the identity, blanks the password hash and ends sign-in, while keeping the
  row so foreign keys hold
- [auto] **Erasure leaves a company's purchase history intact** — the unlock record and credit ledger
  survive, because erasing one party must not erase the other party's financial record
- [auto] A password reset writes an audit row
- [verified] Whole flow exercised against the running API: link emitted, reset accepted (204), old
  password refused (401), new password accepted (200), token reuse refused (400)

## Admin content management

This is the surface a subject-matter expert and a psychometrician use to load real content.

- [auto] Only an admin can reach it
- [auto] A question with **no** correct option, or **two**, is refused — one makes it unscoreable for
  everybody, the other makes it ambiguous and only one right answer earns the mark
- [auto] Bands must tile 0–100: a gap drops a score into no band at all, an overlap makes the label
  depend on row order. The error names the uncovered range
- [auto] Weights must sum to 1 **and** cover every section on the track. A missing section is the
  dangerous one: the calculator renormalises over what it is handed, so an omitted section silently
  vanishes from the score instead of failing
- [auto] A form must hold exactly its declared number of questions, all from its own track, no repeats
- [auto] Publishing a form retires the track's previous one — two live forms would make which one a
  candidate sits depend on ordering
- [auto] **Content used to score a submitted attempt is immutable.** Editing the question, changing
  the form's composition, or changing the bands all return 409. Retiring a question stays allowed,
  because it removes it from future forms without touching past scores
- [auto] A track built entirely through the admin API can be sat by a seeker and scored correctly
- [auto] Readiness counts seeded placeholders separately from real questions
- [auto] A corrected translation takes effect immediately — the localizer cache is invalidated
- [auto] Content changes are audited
- [verified] Readiness against the running API reports all six tracks blocked on the same thing:
  *"5 of 5 active questions are seeded placeholders. Scores produced from them are meaningless."*

## AI modules wired into the platform

These prove the five ports actually connect, which until now was asserted rather than tested.

- [auto] The coach reads a real scored attempt through `IAssessmentDataProvider` — right seeker,
  right track, right section scores
- [auto] The student context DTO still has exactly three properties. Name, email, university, city
  and CV have nowhere to go, so what reaches the model is bounded by shape rather than by
  remembering — and this test fails if the shape ever grows a fourth
- [auto] The chatbot is offered real, track-matched job posts, each with a real URL
- [auto] Submitting a scored assessment creates a coach plan row, triggered from the platform's own
  submit handler
- [auto] The coach plan endpoint is reachable by a seeker through the `StudentOnly` alias, and a
  company gets 403
- [auto] The support chat endpoints are mounted; an unknown session returns an empty history rather
  than an error

> **Do not assert the settled coach-plan status in a test.** The module's worker waits two seconds
> between jobs on purpose, so a burst of submissions cannot trip a free-tier AI rate limit. With a
> suite-wide queue the settled value can be half a minute away, and waiting for it tests the
> throttle rather than the wiring.

> **The module contexts need `--connection` passed explicitly** when applying migrations. Their
> design-time factories hard-code a local connection string and ignore configuration.

## Deployability and observability

- [auto] Sensitive query values (`token`, `access_token`, `code`, `password`) are redacted before
  logging, in any casing, while harmless ones survive — dropping the credential, not the context
- [auto] CI builds both container images, so a Dockerfile that only works on one laptop fails the
  pipeline
- [auto] CI applies migrations to an empty PostgreSQL **twice**, proving they work from clean and
  that a re-run is a no-op — a deploy re-runs this step, so "already applied" has to be success
- [verified] The full stack runs in containers: database, one-shot migrations, API. Health live and
  ready both 200, Arabic reference data served anonymously, seeded admin signs in, wrong password
  still 401
- [verified] `X-Correlation-Id` is echoed when supplied and generated when not
- [verified] A request to `?token=SUPERSECRET…&lang=ar` logged as
  `token=[redacted]&lang=ar`, with the raw token appearing **zero** times in the container logs

> **The API does not migrate on boot.** Migrations are a separate one-shot service so a rolling
> deploy does not have every replica racing to migrate, and a bad migration fails a deploy step
> rather than taking the running app down.

> **Immutability is the reproducibility guarantee.** A published score has to stay explainable, so
> anything it was computed from is frozen once it has been used. The remedy for a mistake is always a
> new version, never an edit — which is also why forms and scoring rules are versioned per track.

## Security review findings

A pass over the branch's own code — auth, credit spending, uploads, erasure, ownership.

**Fixed:**

- [auto] **There was no way to sign out.** A user could not revoke their own refresh token, so
  clearing a browser on a shared machine left a credential valid for another 30 days. `POST
  /api/auth/logout` now revokes a session or all of them, and revokes the whole rotation family
  rather than one link — revoking a single token leaves its successor alive
- [auto] Logging out with someone else's token is a silent no-op, not an error. Telling a caller
  their guess was wrong is a probe result

**Checked and sound:**

- Both raw-SQL sites use interpolated `FromSql`, which EF parameterises — not injection
- Every anonymous endpoint is deliberately anonymous: login, register, password reset, email
  confirmation, confirmation resend, signed file download, and reference data for the sign-up form
- Ownership is checked against the database on every resource route, and reports 404 rather than 403
- **Sign-in requires a confirmed email address.** Checked after the password, so naming the reason
  reveals nothing a wrong guess would not have. Returns 403 with `auth.email_not_verified`, not 401:
  the credentials were correct, so a client that reads 401 as "prompt again" would loop
- **An unconfirmed account can always get back in.** Gating sign-in puts the authenticated resend
  endpoint out of reach of the people who need it, so `POST /api/auth/verify-email/resend` takes an
  address and no token. It answers 202 whether or not the address is registered, and a test asserts
  no mail reaches a stranger — the same standard `forgot-password` is held to

**Open, and needing a decision rather than a fix:**

- **An access token outlives a logout or an erasure by up to 15 minutes.** Inherent to a stateless
  token; the short lifetime is the mitigation. Revoking sooner needs a denylist and a lookup on
  every request
- **CORS is not configured at all**, so no browser on another origin can call this API. That is the
  safe default rather than a hole, but it blocks the frontend until an allowed origin is set

**Deliberately off, pending a legal position:**

- **The two AI modules are disabled in the platform API** (`Ai:Enabled: false`), and this is a
  decision rather than an unfinished wire-up. The Career Coach sends assessment scores and student
  context; Support Chat sends conversation history. Groq and Gemini are US services, so either is a
  cross-border transfer of hiring-relevant personal data out of Egypt, which Egypt's PDPL (Law
  151/2020, executive regulations from 1 November 2025) requires a prior PDPC licence or the data
  subject's specific consent to do. Enforcement is expected from **31 October 2026**
- Both modules degrade gracefully with it off: the coach records the plan as skipped, and chat
  returns its static "provider unavailable" reply. Neither invents an answer, and the dev fixture
  provider lives in `Wasta.DevHost` only, so it cannot serve stand-in text from the platform API

**Verified when a real mail provider is wired in:**

- [verified] **A mail outage does not reveal who is registered.** Mail is only ever attempted for an
  address that exists, so a send failure escaping `forgot-password` or `verify-email/resend` would
  answer 500 for registered addresses and 202 for everyone else — reopening the membership oracle
  the uniform 202 exists to close, and only while the provider is down. Both handlers swallow send
  failures and log them; the token is already stored, so requesting another link still works.
  **Verified by removing the catch**: the two responses then differed
- [verified] The SES sender logs the SES message id and never the recipient. The development
  stand-in logs addresses deliberately; a sender that runs where logs are kept must not

> **Verification and reset emails bypass the notification outbox on purpose.** The outbox persists a
> payload, and the payload would have to carry the raw token — queueing these would put a bearer
> credential in a database table in plain text, defeating the point of storing only its hash. They
> are sent inline instead; if delivery fails, requesting another link is already the normal path.

> **Request language and notification language are different on purpose.** `Accept-Language` decides
> what a response renders in; the stored preference decides what an email renders in. A notification
> is sent long after any request header is gone, and a company acting in English must not cause an
> Arabic-reading student to be emailed in English.
- [auto] `npx tsc --noEmit` clean in `src/frontend/coach-card` and `src/frontend/chat-widget`
- [verified] Test doubles live under `tests/`. One deliberate exception: `NullJobListingProvider`
  ships in `src/` as a production null-object default so the chatbot runs before the jobs
  integration exists.

## AI Career Coach — functional

- [verified] Submit returns in **~140ms**, far under the 3s budget, with the row left `Pending`
- [auto][verified] `StudentCoachPlan` row is `Pending` before generation finishes
- [verified] Card shows all four pieces: assessment, 4-week plan (weeks 1–4, 2–3 actions each),
  project suggestion, interview line
- [blocked] Score/percentile/static feedback render independently — *needs the real results page;
  the dev host has no static-feedback blurbs*
- [auto] `POST /api/admin/coach-plans/{attemptId}/regenerate` resets `AttemptCount`, re-enqueues,
  writes an audit row

## AI Career Coach — guardrails

- [auto] Validator rejects a numeric percentage, `percentile`, `41 percent`, `forty-one percent`,
  `41 out of 100`, `41/100`
- [auto] Validator rejects `hire`/`hired`/`hiring`/`salary`/`job offer`/`you will get`
- [auto] Validator does **not** false-reject legitimate text (`Hampshire`, `higher-order functions`,
  `score each model and compare 3 runs`)
- [auto] Outbound `student_context` carries no name, email, university, city, or CV — the DTO has no
  fields for them, so it is structurally impossible
- [auto] A prompt-injection string in `skills` does not change the output shape or land in the
  stored plan
- [verified] **A real model obeys all of the above** — Groq, 21 Aug 2026: plan reached ready and
  passed every validator rule, with no percentage, percentile, N-out-of-M or hiring language

## AI Career Coach — failure modes

- [auto] Groq 429 → Gemini serves. Groq 400 → Gemini **not** tried (non-retryable)
- [auto] Both providers down → `Failed`, `AttemptCount` increments, results page unaffected
- [auto] Malformed response → exactly one retry, then `Failed`
- [verified] `Ai:Enabled = false` → every plan `Skipped`, endpoint `unavailable`, **zero** errors logged
- [auto] Sweeper retries `Failed` plans under the attempt cap, and rescues plans abandoned in
  `Pending` by a full queue or a restart
- [auto] Sweeper leaves recent `Pending` and all `Ready` plans alone

## Support Chatbot — functional

- [verified] Anonymous visitor chats with no auth wall; a logged-in student's id attaches to the session
- [auto] Unknown session: `GET messages` → empty list (never an error); `POST messages` → 404
- [verified] Anonymous session creation without a `visitorId` is refused (400) — it would be unreachable
- [blocked] Page reload keeps the conversation — *needs the React widget mounted in a real app*

## Support Chatbot — cross-visit memory *(treat as a privacy surface)*

- [auto] Returning student's new session is seeded with context from earlier sessions
- [auto] **Student A's history never reaches Student B's session**
- [auto] Anonymous history never carries across sessions, even reusing the same `visitorId`
- [auto] `CrossSessionMemoryTurns` bounds how much is pulled — no unbounded growth

## Support Chatbot — session authorization

- [auto][verified] A stolen session id alone leaks nothing: history returns empty, send returns 404
- [auto][verified] A student's session cannot be read or continued by another student
- [verified] The rightful owner is unaffected
- [auto] Unauthorized reports **404, not 403**, so the API cannot be used to enumerate session ids

## Support Chatbot — job recommendations

- [auto][verified] Listings match the provider's output verbatim (title, employer, URL)
- [auto] No `OPEN_OPPORTUNITIES` block in the prompt when there are no listings
- [auto][verified] The provider receives the correct `studentId` (or null) — personalization varies
  by identity
- [verified] **A real model only raises jobs when relevant, and never invents a listing or URL** —
  Groq, 21 Aug 2026: surfaced exactly the listings supplied and invented no URLs

## Support Chatbot — abuse guardrails

- [auto] Over-length, too-fast, and past-cap messages are all rejected with **no AI call**
- [verified] Per-IP rate limits return 429 on session creation and messages
- [auto] The user's message is a separate chat turn, never spliced into the system prompt
- [auto][verified] Unresolved `[TODO:]` drafts and editor notes are stripped before the model sees
  the knowledge base; a startup warning counts what remains
- [auto] Both providers down mid-chat → friendly fallback, user's message still saved, no exception
  reaches the client
- [verified] **A real model declines account questions and refuses injection** — Groq, 21 Aug 2026:
  declined without inventing a score, refused to reveal the system prompt, redirected off-topic asks

## Cross-cutting

- [verified] 360px mobile: no horizontal scroll (measured), nothing clipped
- [verified] Dark mode: bubbles, plan, and disclaimer all legible
- [auto] No secrets committed — CI fails on tracked `.env`/`appsettings.Development|Local|Production`
  files or committed key patterns
- [verified] `Ai:Enabled = false` disables both features from one flag

---

## How to run the blocked rows

Every row above marked *needs a real key* is automated in `scripts/verify-guardrails.sh`:

```bash
# 1. Set a key. Never paste one into chat, a commit, or a config file.
./scripts/set-ai-key.sh          # prompts for the key; nothing typed as an argument

# 2. Run the host (leave it running)
dotnet run --project src/Wasta.DevHost

# 3. In another terminal
./scripts/verify-guardrails.sh
```

The provider chain is `[groq, gemini, dev]` and skips unconfigured providers, so a real key takes
over automatically. **The script exits with code 3 if the `dev` fixture served the request** — it
refuses to report a pass on anything but a real model, because a green run against a stub is worse
than no run at all.

What it checks: the plan survives validation; no percentage, percentile, `N out of M`, or
employment-prospect language reaches the stored plan; a prompt injection planted in the student's
skills is neither obeyed nor echoed; the chatbot declines account questions without inventing a
score, refuses to reveal its system prompt, mentions only the job listings actually supplied and
invents no URLs, and redirects off-topic requests.

Re-run it after any prompt change, and periodically regardless — providers update models without
notice, and these are properties of the model, not of our code.

**Last real-provider run:** **21 August 2026 — Groq. 16 passed, 0 failed.**
Models: `openai/gpt-oss-120b` (Career Coach, strict JSON) and `openai/gpt-oss-20b` (support chat).

Two things it caught that no unit test could:

1. **The configured model IDs were dead.** `llama-3.3-70b-versatile` and `llama-3.1-8b-instant`
   both returned 404 `model_not_found`. Exactly what the README warns about — providers retire model
   IDs without notice, so this run is not a one-off.
2. **`"Model": ""` did not mean what the README said it meant.** Both providers resolved the
   per-feature override with `??`, which only falls through on `null`. Configuration binds an absent
   value to `""`, so the documented "empty = use the provider default" sent an *empty model name*
   and got a 404 that reads exactly like a deprecated model. Fixed in both providers, with tests for
   the empty and whitespace cases — the existing tests only covered `null`.

## Database schema

- [verified] Both modules' migrations apply cleanly to **PostgreSQL 16**
- [verified] `WeeklyPlan` and `ProjectSkills` are genuine `jsonb` — queryable with `->` and
  `jsonb_array_length`, not text blobs
- [verified] `UNIQUE(AttemptId)` is enforced by the database: a duplicate insert is rejected
- [verified] All indexes from the spec exist, with the right uniqueness flags
- [verified] The app round-trips real data through Postgres — coach plan written and read back,
  chat sessions and messages persisted
- [verified] `scripts/apply-migrations.sh` is idempotent — a second run is a clean no-op

> Worth repeating for anyone changing the schema: the in-memory EF provider **ignores column types
> entirely**. A `jsonb` column and a `text` column look identical to it, and unique indexes are not
> enforced. Run `docker compose up -d && ./scripts/apply-migrations.sh` before trusting a change.

---

## Known gaps before launch

1. **The knowledge base has 9 unresolved TODOs.** The chatbot cannot answer account, retake,
   unlock, or privacy-policy questions until a product owner fills them in. The app warns about
   this on every boot, and
   [docs/KNOWLEDGE-BASE-QUESTIONNAIRE.md](KNOWLEDGE-BASE-QUESTIONNAIRE.md) turns each gap into a
   specific question.
2. **No production host.** `Wasta.DevHost` is a harness and refuses to start outside Development.
   A real host needs real authentication and real implementations of the five ports. The database
   half is now proven.
3. **No real-provider run yet.** Every row above marked *needs a real key* is genuinely unverified.
