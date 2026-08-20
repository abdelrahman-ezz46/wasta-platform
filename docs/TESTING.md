# Wasta AI features — acceptance checklist

Covers the AI Career Coach and the Support Chatbot.

**Sign off only when every item is verified. Flag anything uncertain rather than assuming pass.**

Status keys used below:
- **[auto]** — covered by the automated suite; `dotnet test` proves it
- **[verified]** — checked by hand against a running app
- **[blocked]** — cannot be verified yet, and why

> **The standing rule:** mocked-provider runs prove the plumbing, never the model. Rows marked
> *needs a real key* are false-green under mocks — a stub returns whatever string it was told to,
> so it cannot tell you whether Groq or Gemini would leak a percentage or fall for an injection.
> Re-run those periodically, since provider-side model updates change behaviour silently.

---

## Setup

- [auto] `dotnet build WastaCareerCoach.sln` clean — 0 warnings, 0 errors (CI enforces `--warnaserror`)
- [auto] `dotnet test WastaCareerCoach.sln` — 240 passing (59 Career Coach, 36 Support Chat,
  103 platform API integration, 18 domain, 16 application, 8 architecture)
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
- [blocked] **Uploads are not scanned for malware** — `NoOpVirusScanner` reports every file clean.
  The host logs a warning on every boot. *Needs a real scanner before public launch*

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
- [blocked] **A real model obeys all of the above** — *needs a real key*

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
- [blocked] **A real model only raises jobs when relevant, and never invents a listing or URL** —
  *needs a real key*

## Support Chatbot — abuse guardrails

- [auto] Over-length, too-fast, and past-cap messages are all rejected with **no AI call**
- [verified] Per-IP rate limits return 429 on session creation and messages
- [auto] The user's message is a separate chat turn, never spliced into the system prompt
- [auto][verified] Unresolved `[TODO:]` drafts and editor notes are stripped before the model sees
  the knowledge base; a startup warning counts what remains
- [auto] Both providers down mid-chat → friendly fallback, user's message still saved, no exception
  reaches the client
- [blocked] **A real model declines account questions and refuses injection** — *needs a real key*

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

**Last real-provider run:** _never_ — record the date and provider here when you run it.

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
