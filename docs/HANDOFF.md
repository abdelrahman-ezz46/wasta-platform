# Handoff

Everything a new session needs to continue this work. Written 21 August 2026.

Read this before touching code. The **Traps** section in particular records things that
compiled fine and failed at runtime — each one cost a real debugging cycle.

---

## 1. What this is

**Wasta** is a skills-assessment and hiring platform for MENA. Students take a timed,
track-specific assessment, get a *Wasta Score* with a percentile and per-section breakdown, and
appear **anonymously** in a talent pool. Verified companies browse that pool and spend credits to
unlock a candidate's real identity. Students also apply to job posts, each application carrying a
project they submit work against.

The repo began as two finished AI modules (a Career Coach and a Support Chatbot) with **no platform
underneath them**. The work since has been building that platform and connecting them to it.

### Current state

- Branch **`platform/api-host`**, 13 commits ahead of `main`, **nothing pushed** (no git remote).
- **316 tests, 0 warnings.** `dotnet build --warnaserror` is clean and CI enforces it.
- The API runs in Docker against PostgreSQL and the full user journey works end to end.
- **There is no user interface.** Everything is HTTP.

---

## 2. Hard constraints — do not change these without asking

| | |
|---|---|
| Runtime | **.NET 9**, pinned by `global.json`. The user's deployment host requires it. |
| Database | PostgreSQL 16, EF Core, code-first migrations |
| Auth | Self-issued JWT (access + refresh), roles in claims |
| Payments | **Bank transfer only.** No processor, no card handling anywhere |
| Scope | Backend only. The user explicitly deferred frontend work |

> .NET 9 left support in May 2026 and receives no security patches. The user was told and chose it
> anyway for hosting reasons. Don't relitigate it; do mention it if they ask about security posture.

---

## 3. Layout

```
src/Wasta.Domain           entities, business rules, pure validation. No dependencies at all.
src/Wasta.Application      use cases, one folder per feature. Declares its own interfaces.
src/Wasta.Infrastructure   EF Core, repositories, JWT, hashing, files, notifications, localization
src/Wasta.WebApi           endpoints, DI composition, middleware, and the AI-module adapters

src/Wasta.Ai               shared provider chain (Groq → Gemini → dev fixture)
src/Wasta.CareerCoach      the AI Career Coach module — consumed as a library, not rebuilt
src/Wasta.SupportChat      the Support Chatbot module — same
src/Wasta.DevHost          development harness for the two AI modules. Refuses to start outside Development.
src/frontend               two React widgets (TypeScript) shipped with the AI modules

tests/Wasta.Architecture.Tests   enforces the layer rules — see below
tests/Wasta.Domain.Tests         pure rules: scoring, bands, weights, notification retry
tests/Wasta.Application.Tests    file-upload validation
tests/Wasta.Api.IntegrationTests real Postgres via Testcontainers, real HTTP
```

### The dependency rule

Inward only: `Domain ← Application ← Infrastructure ← WebApi`.

**Eight architecture tests enforce this** by reading the `.csproj` files, not compiled assemblies —
the compiler drops declared-but-unused references, so an assembly scan goes quiet exactly when a
layer has just been violated but not yet leaned on. Domain may reference **no** NuGet package at
all. Application may not reference EF Core or ASP.NET.

If you need Application to reach something outside it, **declare an interface in Application and
implement it further out.** That is how `ICoachPlanTrigger`, `IFileStore`, `INotificationSender`,
`IAuditWriter` and `ILoggerAdapter` all work.

---

## 4. Conventions that are load-bearing

- **Ownership failures return 404, never 403.** A 403 confirms the resource exists and turns any
  id-taking endpoint into an enumeration oracle. This is applied consistently — attempts,
  applications, job posts, notifications, candidates.
- **Role mismatches return 403** (wrong actor type for an endpoint). That is different from
  ownership and is fine.
- Errors are RFC 9457 Problem Details with a stable `code`. All status mapping lives in one place:
  `src/Wasta.WebApi/Endpoints/ProblemMapping.cs`. Add new codes there.
- Every list endpoint is paged (`PageRequest`, hard cap 100).
- Enums serialise as **names**, not integers.
- Handlers return `Result` / `Result<T>` for expected failures. Genuine faults throw.
- `DomainException` maps to 400 with its code — a broken business rule is the caller's problem.
- Money and time: `timestamptz` everywhere, currency stored as an explicit ISO code.

---

## 5. Traps — things that compile and then fail

These each cost real debugging time. They are the most valuable part of this document.

1. **EF cannot filter or order on a projected positional record.** Projecting into a record with a
   constructor and *then* calling `.Where()` or `.OrderBy()` on its properties throws at runtime.
   Object-initializer projections are fine because EF can see through them. **Filter and order on
   entities; project last.**

2. **`jsonb` has no `LIKE` operator.** A LINQ `.Contains()` on a `jsonb` column compiles and throws.
   Use raw SQL with an explicit `::text` cast.

3. **Never string-match a `jsonb` payload in a test.** Postgres normalises it — reorders keys,
   rewrites whitespace. Parse and assert on the value.

4. **The three DbContexts cannot share a migrations-history table.** The platform context uses
   `UseSnakeCaseNamingConvention()`; the two AI modules do not. The platform uses
   `__platform_migrations_history`; the modules keep the default `__EFMigrationsHistory`.

5. **The AI modules' design-time factories hard-code a local connection string** and ignore
   configuration. Applying their migrations needs `--connection` passed explicitly.

6. **`??` does not fall through on an empty string.** Configuration binds an absent value to `""`,
   not `null`. This bug sent an empty model name to Groq and produced a 404 that reads exactly like
   a deprecated model. Fixed in `AiModelResolver`; be alert for the pattern elsewhere.

7. **`WebApplicationFactory` boots the host in `Development`**, where `Program.cs` seeds itself —
   so the app seeded before the test fixture had migrated. Tests run under a `Testing` environment
   and the fixture owns migrate-then-seed.

8. **The coach worker waits 2 seconds between jobs on purpose** (free-tier rate limits). A test that
   waits for a plan to settle is testing that throttle. Don't.

9. **Rate limits throttle the test suite itself** — every test signs in from one IP. Limits are
   configurable under `RateLimits`; the factory raises auth and unlock, and deliberately leaves
   upload low (it partitions per user, so one test can exhaust its own budget safely).

10. **`.slnx` is a .NET 10 format.** The solution is a classic `.sln` because the .NET 9 SDK cannot
    parse `.slnx`.

11. **`verify-guardrails.sh` used to define a shell function named `head`**, shadowing the command
    its assertions use to truncate. Every chat assertion passed while displaying nothing — and a
    pass on an empty reply is indistinguishable from a real one. Fixed, but the lesson generalises:
    **a green run whose evidence you cannot read is not a green run.**

---

## 6. Security decisions worth preserving

- **Passwords**: PBKDF2-SHA256, 210k iterations, fixed-time compare. Stored format carries its own
  iteration count so the cost can be raised later.
- **Refresh tokens**: hashed at rest, rotated on every use. Replaying a spent token revokes the
  **entire family** — revoking one link leaves its successor alive.
- **Login**: a wrong password and an unknown email return byte-identical responses. So does
  `forgot-password`, and a test asserts no email is actually sent to a stranger — an identical
  response is worthless if the side effects differ.
- **Unlocking a candidate** has three independent guards: a `FOR UPDATE` row lock on the company,
  the balance summed from the ledger inside that lock, and a unique index on
  `(company_id, job_seeker_id)`. **Verified by removing the lock**: 6 parallel unlocks then
  succeeded against a 3-credit balance. The unique index alone only catches the same-candidate case.
- **Uploads** are identified by magic bytes, never extension or `Content-Type` — both are attacker
  controlled. Storage keys are server-generated so the uploader's filename never touches a path.
- **Downloads** need an unexpired HMAC token covering the key. Missing, tampered or expired all
  return 404.
- **Request logs redact** `token`, `access_token`, `code`, `password` from query strings. The file
  download route carries its authorisation in the query string; a raw log line would be a working
  download link for every CV fetched. Headers and bodies are never logged.
- **Erasure scrubs, does not delete.** Credit ledger entries and unlock records are the *other
  party's* financial history.
- **Verification and reset emails bypass the notification outbox** — the outbox persists a payload,
  and queueing these would put a bearer credential in a table in plain text.
- **No default admin.** Seeded only when `Seed:AdminEmail` *and* `Seed:AdminPassword` are both set.

### Open security findings — recorded, not fixed

Full detail in `docs/TESTING.md`.

1. **Email verification gates nothing.** The machinery works but `IsEmailVerified` is never checked.
   Recommended gate: the talent pool, so a company never pays to unlock an unconfirmed address.
   Where the gate goes changes the signup funnel — **it is the user's decision, not ours.**
2. An access token outlives logout or erasure by up to 15 minutes. Inherent to stateless tokens.
3. **CORS is not configured**, so no browser on another origin can call the API. Safe default, but
   it blocks any frontend until the user supplies an origin.

---

## 7. Running it

```bash
# database + migrations (all three contexts)
docker compose up -d
dotnet ef database update --project src/Wasta.Infrastructure --startup-project src/Wasta.Infrastructure
CS="Host=localhost;Port=55432;Database=wasta;Username=postgres;Password=wasta_local_dev"
dotnet ef database update --project src/Wasta.CareerCoach --startup-project src/Wasta.CareerCoach --connection "$CS"
dotnet ef database update --project src/Wasta.SupportChat --startup-project src/Wasta.SupportChat --connection "$CS"

# the API
dotnet user-secrets set "Jwt:SigningKey" "<48+ random chars>" --project src/Wasta.WebApi
dotnet run --project src/Wasta.WebApi        # Swagger at /swagger in Development
```

Whole stack in containers: `docker compose -f docker-compose.api.yml up --build`
(requires `JWT_SIGNING_KEY`; set `API_PORT` if 8080 is taken).

Tests: `dotnet test WastaCareerCoach.sln` — needs Docker running (Testcontainers).

**Never use `dotnet run` in the background for a dev server if the harness offers a preview tool.**

---

## 8. The AI modules

Two finished libraries, connected through **five ports** implemented in
`src/Wasta.WebApi/Integration/`. Nothing inside either module was changed to wire them in.

- `IAssessmentDataProvider` → real attempts, scores, sections
- `IJobListingProvider` → live job posts, track-matched, each with a real URL
- `IAuditLogWriter` → the platform's own audit log
- `ICurrentStudentAccessor` (×2, one per module) → from claims

Notes:

- The modules type ids as **`int`**; the platform uses **`long`**. Every crossing goes through
  `PlatformIds`, which throws on overflow rather than casting unchecked.
- The Career Coach endpoints require a policy literally named **`StudentOnly`**. The host supplies
  it as an alias for `SeekerOnly` rather than editing a tested module.
- **`Ai:Enabled` defaults to `false` in the platform API**, and the Groq key currently lives only in
  the **DevHost's** user-secrets. So the AI features work in the dev host but are off in the
  platform API. Closing that is minutes of work — point `scripts/set-ai-key.sh` at `Wasta.WebApi`.

### Guardrails

`./scripts/verify-guardrails.sh` runs the checks a mocked provider cannot prove — whether a *real*
model leaks a percentage, obeys an injection, or invents a job listing.

**Last run: 21 August 2026, Groq, 16 passed / 0 failed.** Models `openai/gpt-oss-120b` (coach) and
`openai/gpt-oss-20b` (chat). Re-run after any prompt change and periodically regardless — that run
found both previously configured model IDs already retired.

---

## 9. What is left

Ordered by what actually blocks a launch.

### Blocks everything — needs people, not code

1. **Assessment questions.** Six tracks, multiple forms each for monthly retakes; realistically
   600+ items. Everything seeded today is marked `[PLACEHOLDER]`. **Every score the platform
   currently produces is meaningless.** Needs a subject-matter expert per track.
2. **Validity study and band cut-points.** Companies spend credits on this number, and in a hiring
   context an unvalidated screen carries discrimination exposure. Needs a psychometrician.
   *Do not generate assessment items with an LLM.* This was raised with the user and stands.
3. **Section feedback copy** — real text per section per band.

`GET /api/admin/content/readiness` reports this per track. The admin content API (commit `576ddd5`)
is the surface those experts load real content through.

### Services to plug in — slots built and waiting

4. **Email delivery.** `LoggingNotificationSender` writes to the log. Password resets, verification
   and unlock alerts reach nobody.
5. **Malware scanning.** `NoOpVirusScanner` reports every file clean without looking. Both warn
   loudly at startup on every boot.

### Needs a decision from the user

6. Where the email-verification gate goes · hosting region (PDPL residency) · CORS origin · the
   **9 unresolved knowledge base TODOs** (`docs/KNOWLEDGE-BASE-QUESTIONNAIRE.md`) · privacy policy URL.

### Bigger pieces

7. **Frontend.** Four prototypes in `~/Desktop/SpaceTech-Front/` with **zero API calls** between
   them. Nothing consumes this API. A thin throwaway demo UI is days; the real thing is months.
8. **Launch hardening.** Load testing, monitoring, incident runbook, external pen test, PDPL review,
   backup/restore drill.

### Housekeeping

9. Nothing is pushed — no remote configured.
10. The blueprint artifact
    (`https://claude.ai/code/artifact/f5fcd806-bf6e-4822-a839-ec690209ad8b`) is **stale**: it still
    lists finished work as pending. Update it or stop citing it.

---

## 10. How the user works

- Wants plain answers. Says "say it simply" when given jargon. Match that.
- Says "continue" / "go on" to mean *keep building* — but they mean genuinely useful work, not
  invented work. When there is nothing left that doesn't need them, **say so** rather than padding.
- Repeatedly asks "what's left" — keep an honest, current picture ready.
- Has been given uncomfortable news several times (their schema didn't run; their frontend was
  mockups; their scope is months not weeks) and responded well each time. **Do not soften findings.**
- They corrected me once — I claimed the AI key was missing without checking, and it was already
  set. **Check before asserting something is blocked on them.**

### Working agreements established

- Verify claims by running things, not by reasoning about them. Nearly every real bug in this repo
  was found by executing, not reading.
- Prove a guard works by removing it and watching the test fail. Done for the architecture tests
  and the unlock row lock.
- Commit messages explain *why*, including bugs found and decisions rejected.
- Update `README.md` and `docs/TESTING.md` in the same commit as the change.
- Secret scan before every commit (CI enforces it too).
- Don't commit or push unless asked.

---

## 11. Key documents

| File | What it is |
|---|---|
| `docs/BACKEND-BRIEF.md` | The build contract — architecture, features, definition of done |
| `docs/TESTING.md` | Acceptance checklist. What is verified, what is blocked, and why |
| `docs/sql/proposed-platform-schema.sql` | The reviewed data model (37 tables, 57 FKs) |
| `docs/KNOWLEDGE-BASE-QUESTIONNAIRE.md` | The 9 questions blocking the chatbot |
| `README.md` | Current status and how to run everything |

> The user's original `jobseeker.sql` draft **did not run** — Postgres rejected 14 of its 21 foreign
> keys because the direction was inverted, plus an integer/text mismatch. The corrected model
> replaced it. Worth knowing if they refer back to the original.
