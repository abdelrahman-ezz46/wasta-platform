# Wasta AI features

Two AI features for the Wasta talent platform, built as drop-in modules rather than a standalone
application:

- **AI Career Coach** — turns a student's assessment section scores into a personalized 4-week study
  plan, generated once in a background job and stored. Never blocks the results page, never touches
  the Wasta Score.
- **Support Chatbot** — answers "how does this work" questions from a curated knowledge base, with
  cross-visit memory for logged-in students and job recommendations sourced from the host app.

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

Swagger is at `/swagger` in Development. The API refuses to start without a signing key — a
predictable default would let anyone mint an admin token.

**Runtime is .NET 9**, pinned by `global.json`, because the deployment host requires it. Note that
.NET 9 left support in May 2026 and receives no security patches; moving to .NET 10 LTS is the
single highest-value change available if the host constraint ever lifts.

## Status

169 tests passing, 0 warnings. **Not yet production-ready** — the platform API covers authentication,
authorization, assessment delivery and scoring, and jobs, applications and projects; the talent pool,
unlocks, credits, file uploads, notifications and the admin portal are not built. The knowledge base has unresolved TODOs, the seeded assessment items
are placeholders rather than a validated instrument, and the AI guardrail rows have not been run
against a real model.
[docs/TESTING.md](docs/TESTING.md) tracks exactly what is verified and what is not.
