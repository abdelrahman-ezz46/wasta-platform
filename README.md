# Wasta AI features

Two AI features for the Wasta talent platform, built as drop-in modules rather than a standalone
application:

- **AI Career Coach** — turns a student's assessment section scores into a personalized 4-week study
  plan, generated once in a background job and stored. Never blocks the results page, never touches
  the Wasta Score.
- **Support Chatbot** — answers "how does this work" questions from a curated knowledge base, with
  cross-visit memory for logged-in students and job recommendations sourced from the host app.

## Run the board demo

A guided walkthrough of the whole product, served by the API itself at `/`. Every step is a real
HTTP call against the running service — a student registers, sits a scored assessment, appears
anonymously in the talent pool, and a company spends a credit to unlock her.

```bash
docker compose up -d
dotnet ef database update --project src/Wasta.Infrastructure --startup-project src/Wasta.Infrastructure
dotnet user-secrets set "Jwt:SigningKey" "<48+ random chars>" --project src/Wasta.WebApi
dotnet user-secrets set "Seed:AdminEmail" "admin@wasta.demo" --project src/Wasta.WebApi
dotnet user-secrets set "Seed:AdminPassword" "<choose one>" --project src/Wasta.WebApi
dotnet run --project src/Wasta.WebApi --urls http://localhost:5280
```

Then, in a second terminal, build the demo dataset and open the console:

```bash
python3 scripts/seed-demo.py
```

`seed-demo.py` registers ~64 candidates and puts each through a real scored attempt. **The cohort
size is the point**: a percentile is withheld below 50 scored attempts on a track, so seeding fewer
leaves the score card blank — which looks like a bug and is not.

Open <http://localhost:5280/> and press **Run the full journey**.

> The demo console is served in Development automatically. Anywhere else it needs an explicit
> `Demo:Enabled`, so a deployment does not put a walkthrough of the whole product on its front page.
>
> **Assessment content is still placeholder**, and the console says so on screen. The platform and
> scoring pipeline are real; the questions are seeded stand-ins awaiting a subject-matter expert.

## Try it now

```bash
dotnet run --project src/Wasta.DevHost
```

No database and no API keys required — EF runs in memory and a fixture provider stands in for the
model. See [src/Wasta.DevHost/README.md](src/Wasta.DevHost/README.md).

### See it visually

For demos and stakeholder reviews, there's a Streamlit preview that renders the real API responses:

```bash
dotnet run --project src/Wasta.DevHost     # terminal 1
streamlit run streamlit/app.py             # terminal 2
```

See [streamlit/README.md](streamlit/README.md).

### Against real Postgres

The in-memory provider ignores column types, so `jsonb` columns and unique indexes are never
exercised by it. Before trusting a schema change, run against the real thing:

```bash
docker compose up -d
./scripts/apply-migrations.sh
ConnectionStrings__Wasta="Host=localhost;Port=55432;Database=wasta;Username=postgres;Password=wasta_local_dev" \
  dotnet run --project src/Wasta.DevHost
```

The migration script is idempotent — the same command works on a fresh or existing database.

## Layout

| Path | What it is |
|---|---|
| `src/Wasta.Ai` | Shared provider chain (Groq → Gemini) with fallthrough on 429/5xx/timeout |
| `src/Wasta.CareerCoach` | Career Coach: entity, generation service, validator, background jobs, endpoints |
| `src/Wasta.SupportChat` | Chatbot: sessions, memory, knowledge base, rate limiting, endpoints |
| `src/Wasta.DevHost` | Runnable harness. Development only — refuses to start elsewhere |
| `src/frontend/coach-card` | React results-page card |
| `src/frontend/chat-widget` | React floating chat widget |
| `src/Wasta.Domain` | Platform domain: entities and business rules. No dependencies at all |
| `src/Wasta.Application` | Platform use cases, one folder per feature. Defines its own interfaces |
| `src/Wasta.Infrastructure` | EF Core, repositories, JWT issuing, password hashing |
| `src/Wasta.WebApi` | The production API. JWT auth, role and resource authorization, OpenAPI |
| `streamlit/` | Visual preview app for demos (calls the real API) |
| [docs/HANDOFF.md](docs/HANDOFF.md) | Continuing this work in a new session: decisions, traps, and what is left |
| `docs/TESTING.md` | Acceptance checklist with current verified/blocked status |
| `docs/KNOWLEDGE-BASE-QUESTIONNAIRE.md` | The questions a product owner must answer to unblock the chatbot |

## Integrating into the real app

Both modules are self-contained and depend on the host only through small port interfaces. Register
your implementations **before** calling the `Add*` extensions:

| Interface | Purpose |
|---|---|
| `IAssessmentDataProvider` | Reads attempt scores and student context from your scoring tables |
| `ICurrentStudentAccessor` | Resolves the caller's student id (each module declares its own) |
| `IAuditLogWriter` | Writes the regenerate audit entry |
| `IJobListingProvider` | Supplies job listings; optional, defaults to a no-op |

```csharp
builder.Services.AddCareerCoach(builder.Configuration, connectionString);
builder.Services.AddSupportChat(builder.Configuration, connectionString);

app.MapCareerCoachEndpoints();
app.MapSupportChatEndpoints();
```

Then call `CoachPlanTrigger.EnqueueGenerationAsync(...)` from your submit flow, after scoring —
without awaiting generation.

## Configuration

Merge the `Ai`, `CareerCoach`, and `SupportChat` sections from
[src/Wasta.DevHost/appsettings.json](src/Wasta.DevHost/appsettings.json). The `Ai` section is shared
by both modules — merge it once.

**Never commit API keys.** Use environment variables or `dotnet user-secrets`; `.env` and
`appsettings.Development|Local|Production.json` are gitignored, and CI fails the build if a
credential pattern is committed.

`Ai:Enabled = false` disables both features cleanly from a single flag: plans go to `Skipped`,
endpoints report `unavailable`, and the UI renders nothing. That is the launch-day escape hatch.

### Choosing models

Each feature can name its own model, because their needs are opposite:

| | Runs | Needs | Suggested (Groq) |
|---|---|---|---|
| Career Coach | once per assessment | strict JSON matching a schema — a miss is rejected and retried | `llama-3.3-70b-versatile` |
| Support chat | once per **message** | a few plain sentences; latency is user-visible | `llama-3.1-8b-instant` |

```jsonc
"Ai":          { "Providers": { "groq": { "Model": "llama-3.3-70b-versatile" } } },  // required default
"CareerCoach": { "Model": "" },                  // empty = use the provider default
"SupportChat": { "Model": "llama-3.1-8b-instant" }
```

The provider's own `Model` is still required — a feature override alone does not make a provider
usable, so a missing base configuration is caught rather than silently half-working. Model IDs are
deprecated without much notice; check the provider's console when wiring this up.

## The AI modules are wired in

The Career Coach and Support Chatbot now run inside the platform API, connected through the five
ports they were written against. Nothing in either module changed to make that happen, which was the
point of the ports — and is now tested rather than asserted.

- Submitting a scored assessment enqueues a coach plan. The trigger fires from the platform's own
  submit handler, so there is no separate call for a host to remember, and a failure there cannot
  cost a student their score.
- The chatbot is offered real job posts, track-matched for a signed-in seeker, each with a real URL
  it can quote rather than invent.
- What reaches the model about a student is bounded by the port's DTO: skills, project titles and
  graduation year. Name, email, university, city and CV have nowhere to go.
- The Career Coach's endpoints require a policy named `StudentOnly`. The host supplies that name as
  an alias for `SeekerOnly` rather than editing a tested module to match platform vocabulary.

**`Ai:Enabled` defaults to `false`.** Both features are add-ons — the results page and the help
widget render without them — so the platform does not attempt AI calls until a key is deliberately
configured. Turning it on without a working provider means plans fail rather than generate.

> The two modules keep their own `DbContext`s in the same database, and their own
> `__EFMigrationsHistory`. The platform context uses a separate history table, because it applies the
> snake_case naming convention and they do not — one shared table cannot have columns that are
> snake_case to one context and PascalCase to the others.

## The platform backend

Beyond the two AI modules, this repo now holds the platform itself, built in four clean-architecture
layers with dependencies pointing inward only. [docs/BACKEND-BRIEF.md](docs/BACKEND-BRIEF.md) is the
build contract; [docs/sql/proposed-platform-schema.sql](docs/sql/proposed-platform-schema.sql) is the
reviewed data model.

```bash
docker compose up -d
dotnet ef database update --project src/Wasta.Infrastructure --startup-project src/Wasta.Infrastructure
dotnet user-secrets set "Jwt:SigningKey" "<48+ random characters>" --project src/Wasta.WebApi
dotnet run --project src/Wasta.WebApi
```

### Or the whole stack in containers

```bash
export JWT_SIGNING_KEY=$(openssl rand -base64 48)
export SEED_ADMIN_EMAIL=you@example.com SEED_ADMIN_PASSWORD='<a real password>'
docker compose -f docker-compose.api.yml up --build
```

Database, migrations and API. Set `API_PORT` if 8080 is taken. `JWT_SIGNING_KEY` is required with
no default — the compose file refuses to start without it, and so does the API, because a
predictable signing key means anyone can mint an admin token.

**Migrations run as their own one-shot service, not at app start.** An API that migrates on boot
means every replica races to migrate during a rolling deploy, and a failed migration takes the app
down instead of failing a deploy step. Re-running is a no-op — EF skips what is already recorded.

Requests carry an `X-Correlation-Id`, accepted from the caller when supplied and generated
otherwise, echoed on the response and attached to every log line. Query values named `token`,
`access_token`, `code` or `password` are redacted before logging: a signed file URL is the whole
authorisation for that file, so a raw query string in a log is a working download link for every CV
that was fetched.

Swagger is at `/swagger` in Development. The API refuses to start without a signing key — a
predictable default would let anyone mint an admin token.

**Runtime is .NET 9**, pinned by `global.json`, because the deployment host requires it. Note that
.NET 9 left support in May 2026 and receives no security patches; moving to .NET 10 LTS is the
single highest-value change available if the host constraint ever lifts.

## Status

316 tests passing, 0 warnings. **Not yet production-ready** — the platform API covers authentication,
authorization, assessment delivery and scoring, jobs and applications, the talent pool and unlocks,
credits, admin verification and top-up review, file uploads, rate limiting, notifications,
English/Arabic, the account lifecycle — email verification, password reset, and PDPL data export and
erasure — and admin content management.

`GET /api/admin/content/readiness` reports, per track, what still stands between the platform and a
meaningful score. Today every track answers the same way: its questions are seeded placeholders.

**Right-to-left layout is a frontend concern and is not part of this backend.** What the server owns
is language: `Accept-Language` (or `?lang=`) selects the language of anything it renders, a stored
per-account preference drives notifications, and reference data carries translations in the database
rather than in resource files, so an admin adding a track or a city does not need a release.

Two stand-ins are wired in place of real providers, and the host logs a warning about each on every
boot:

- **Uploads are not scanned for malware _by default_.** `NoOpVirusScanner` reports every file clean
  without looking at it. A real ClamAV scanner ships behind the same interface — turn it on with
  `VirusScanning:Enabled`, and start clamd alongside the API:

  ```bash
  VIRUS_SCANNING=true docker compose -f docker-compose.api.yml --profile scanning up --build
  ```

  It **fails closed**: with scanning on and clamd unreachable, uploads answer 503 rather than being
  stored unscanned. clamd takes a minute or two to load signatures on first boot, and uploads are
  refused until it is ready — that is the design working, not a fault.
- **Notifications are not delivered _by default_.** `LoggingNotificationSender` writes to the log
  instead of sending. An Amazon SES sender ships behind the same interface — set `Email:Enabled`,
  `Email:Region` and an SES-verified `Email:FromAddress`.

  **Credentials are never configured.** The SES client uses the default AWS credential chain, so a
  deployment supplies an IAM role and nothing long-lived is stored in the repo, an environment
  variable, or a secrets file. Enabling email without a `FromAddress` fails at startup rather than
  at the first password reset.

To exercise verification or password reset locally, set `Notifications:LogBodies=true` — the emailed
link is then written to the log in full. It is off by default and must stay off anywhere that keeps
logs, because those links are bearer credentials. The knowledge base has unresolved TODOs, the seeded assessment items
are placeholders rather than a validated instrument, and the seeded assessment items are placeholders rather than a validated instrument.

The AI guardrail rows **have** now been run against a real model — Groq, 21 August 2026, 16 passed,
0 failed. Re-run `./scripts/verify-guardrails.sh` after any prompt change and periodically
regardless: providers retire model IDs without notice, and that run found two already dead.
[docs/TESTING.md](docs/TESTING.md) tracks exactly what is verified and what is not.
