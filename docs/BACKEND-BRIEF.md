# Wasta backend — build brief

The working contract for the platform backend. Written to be handed to a developer, pasted
into a tool as a prompt, or used as the definition of done. Decisions here are settled unless
listed under **Open questions**.

---

## 1. Context

Wasta is a skills-assessment and hiring platform for MENA. Students ("job seekers") take a
timed, track-specific assessment, receive a Wasta Score with a percentile and per-section
breakdown, and appear anonymously in a talent pool. Verified companies browse that pool and
spend credits to unlock a candidate's real identity and contact details. Students also apply to
job posts, and each application carries a project they submit work against.

**What already exists:** two finished .NET class libraries — an AI Career Coach and a Support
Chatbot — with 95 passing tests. They are consumed as libraries, not rebuilt.

**What does not exist:** everything else. This brief covers building it.

---

## 2. Hard constraints

| | |
|---|---|
| Runtime | **.NET 9** — mandated by the deployment host |
| Language | C#, nullable enabled, warnings as errors |
| Database | PostgreSQL 16, EF Core, code-first migrations |
| Auth | Self-issued JWT (access + refresh), roles in claims |
| Architecture | Clean architecture, four projects |
| API style | ASP.NET Core Web API, REST, OpenAPI documented |
| Scope | Backend only. No frontend work in this engagement |

> **.NET 9 note.** The two existing AI modules target `net10.0` and must be downgraded to
> `net9.0`, including EF Core packages from 10.0.4 to the 9.x line, with all 95 tests re-run and
> green before they are wired in. .NET 9 left support in May 2026, so the host runs without
> security patches — accepted deliberately as a hosting constraint, and worth revisiting.

---

## 3. Architecture

Four projects. Dependencies point inward only; the arrow never reverses.

```
Wasta.Domain          entities, value objects, domain rules, domain events.
                      Zero dependencies. No EF, no attributes, no framework types.

Wasta.Application     use cases, one vertical slice per feature. Defines the interfaces it
                      needs (IJobSeekerRepository, IFileStore, IClock, ICreditService).
                      References Domain only.

Wasta.Infrastructure  EF Core DbContext, configurations, migrations, repository
                      implementations, file storage, email, JWT issuing, external providers.
                      References Application and Domain.

Wasta.WebApi          controllers or minimal-API endpoints, DI composition, middleware,
                      auth wiring, OpenAPI. References Application and Infrastructure.
```

Rules that get enforced in review:

- Domain never references EF Core, ASP.NET, or any NuGet package beyond the BCL.
- Application never references `DbContext` directly — only its own interfaces.
- Entities never leave the Application layer. Endpoints accept and return DTOs.
- Business rules live in Domain or Application, never in a controller and never in SQL.
- One folder per feature under Application (`Features/JobSeekers/Applications/ApplyToJob/`),
  holding request, handler, validator, and response together.

---

## 4. Data model

Authoritative source: [`docs/sql/proposed-platform-schema.sql`](sql/proposed-platform-schema.sql)
— 37 tables, 57 foreign keys, verified to apply cleanly against PostgreSQL 16.

It supersedes the original `jobseeker.sql` draft, which Postgres rejected: 14 of its 21 foreign
keys failed because the direction was inverted, plus a type mismatch between `company_files.company`
(integer) and `company.id` (text).

Build EF Core entities and migrations **from the corrected schema**, keeping its naming. Notable
decisions carried over:

- Surrogate keys are `BIGINT` identity; the identity subject is a separate `auth_subject` column.
- Scores live on `attempt_score` and `attempt_section_score`, never denormalised onto the seeker.
- Company credits are an append-only `credit_ledger_entry`, never a counter column.
- Every instant is `timestamptz`. Every mutable row carries `created_at`.

---

## 5. Authentication and authorization

**Authentication.** The API issues its own tokens.

- `POST /auth/register/seeker`, `POST /auth/register/company`, `POST /auth/login`,
  `POST /auth/refresh`, `POST /auth/logout`, `POST /auth/verify-email`,
  `POST /auth/forgot-password`, `POST /auth/reset-password`.
- Passwords hashed with ASP.NET Core Identity's hasher or Argon2id. Never anything faster.
- Short-lived access token (15 min), long-lived rotating refresh token persisted and revocable.
- Refresh token reuse must be detected and revoke the whole family.
- Claims carry `sub`, `role`, and the actor's own id (`seeker_id` or `company_id`).

**Authorization — two layers, both required.**

1. **Role-based**, via policies: `SeekerOnly`, `CompanyOnly`, `AdminOnly`. Also
   `VerifiedCompanyOnly` — an unapproved company can sign in and upload documents, nothing else.
2. **Resource-based**, via `IAuthorizationHandler`: a company may only edit its own job posts and
   see applicants for its own posts; a seeker may only read and modify their own application,
   profile, and attempt. Ownership is checked against the database, never inferred from the route.

Cross-tenant access returns **404, not 403** — a 403 confirms the resource exists and turns the
API into an enumeration oracle. The existing chat module already follows this convention.

---

## 6. Features

Build as vertical slices, one at a time, each ending in a working endpoint with tests. Order
below is the intended build order.

### Job seeker

1. Register, verify email, sign in, refresh, sign out
2. Profile builder — basic info, bio, university, graduation year, availability, preferred work
   type, skills (max 12), CV upload (PDF, 5 MB), computed profile strength
3. Start assessment — enforce the monthly retake rule per track, pick an active form, create an
   attempt with a server-side expiry
4. Take assessment — fetch questions for the attempt, save an answer, flag for review, resume
   after a reload. The timer is authoritative on the server; a late submission is rejected
5. Submit assessment — score against the active rule version, write overall, percentile, and
   per-section scores, assign bands, derive skill gaps
6. View results — score, percentile, section breakdown, band feedback, skill gaps
7. Browse jobs — track-matched feed, search, "recommended" flag, filters
8. Apply to a job — creates an application and its project, enforcing the 6-project cap
9. Manage a project — description (600 chars), repo URL, live URL, attachments, submit, withdraw
10. See which companies unlocked the profile, and when
11. Visibility controls — opt out of the talent pool
12. Account — change password, export data, delete account (PDPL)

### Company

1. Register, upload verification documents, await approval
2. Sign in, refresh, sign out
3. Post a job — enforce the 6-active cap; edit; close
4. View applicants per job post
5. Browse the talent pool — anonymised cards, score-ranked, filter by track, score, and skills
6. View a locked candidate — masked identity, section scores, project titles
7. **Unlock a candidate** — spend one credit atomically; idempotent, so a retry or double-click
   never double-charges; already-unlocked returns the existing unlock without a new charge
8. View an unlocked candidate — contact details, CV download
9. Credits — balance, ledger history, request a top-up
10. Review an application — change state, leave feedback

### Admin

1. Review company verifications; approve (granting 3 trial credits) or reject with a note
2. Review top-up requests; confirm the transfer arrived and issue credits
3. Manage content — tracks, sections, questions, forms, scoring rule versions, bands, feedback
4. Read the audit log
5. Regenerate a coach plan (endpoint already exists in the AI module)

---

## 7. Cross-cutting requirements

- **Validation** — FluentValidation on every request, returning RFC 9457 Problem Details.
- **Errors** — one exception-handling middleware. No stack traces past the boundary. Every
  response body is Problem Details, including 401 and 403.
- **Pagination** — every list endpoint. Cursor or offset, but consistent, with a hard page cap.
- **Concurrency** — credit spending and unlocking use a transaction with row-level locking or
  an optimistic-concurrency token. A test must prove two parallel unlocks charge exactly once.
- **Idempotency** — `Idempotency-Key` honoured on unlock and top-up request.
- **File storage** — behind `IFileStore`. Content-type and magic-byte checks, size caps,
  virus scan, access through short-lived signed URLs. Never serve user files from the API origin.
- **Rate limiting** — on auth, unlock, and assessment submission. Behind a proxy this needs
  `UseForwardedHeaders`, or every caller shares one bucket.
- **Logging** — structured, with a correlation id per request. Never log tokens, passwords,
  or personal data.
- **Health** — `/health/live` and `/health/ready`, the latter checking the database.
- **OpenAPI** — every endpoint documented with response types and examples.
- **Migrations** — applied by the deploy pipeline, not at app start.
- **Seed data** — tracks, sections, skills, locations, employment types, work types,
  application states, payment methods, and an admin account. Placeholder assessment items so the
  flow is exercisable end to end before real content exists.
- **Localisation** — API returns machine-readable codes; display strings belong to the client.
  Store currency as an explicit code, never a bare number.

---

## 8. Testing — the definition of "not buggy"

- **Unit tests** for domain rules and application handlers, dependencies faked.
- **Integration tests** against real PostgreSQL via Testcontainers — the in-memory provider
  ignores column types and does not enforce unique indexes, so it cannot prove a schema change.
- **Authorization tests** are mandatory, one per resource: another seeker's application, another
  company's job post, another company's applicants, an unverified company hitting a gated
  endpoint. Each must return 404.
- **A concurrency test** proving parallel unlocks charge exactly one credit.
- **A retake test** proving the monthly rule holds at the boundary.
- **A timer test** proving a submission after expiry is rejected server-side.
- CI runs build, tests, and a migration apply against a real Postgres service. Warnings are errors.

---

## 9. Definition of done

The API runs in Docker against PostgreSQL, applies migrations from clean, serves every feature in
section 6 through documented endpoints, passes the full test suite in CI, and ships with a README
covering configuration, secrets, migrations, and deployment.

---

## 10. Explicitly out of scope

- Frontend work of any kind
- Card payments or a payment processor — top-ups are offline bank transfer, admin-issued
- Student subscription tiers, and the CV Builder and Interview Practice features tied to them
- Authoring assessment content — the engine is content-agnostic and ships with placeholders

---

## 11. Open questions

1. **Application states** — proposed: Applied, In review, Rejected, Hired, Withdrawn. Confirm.
2. **Re-applying** — `(job_seeker_id, job_post_id)` is unique, so re-applying reuses the row
   rather than creating a second application. Confirm.
3. **Caps** — 6 projects per seeker, 6 active posts per company, 12 skills. Enforced in the
   application layer as policy rather than as database constraints. Confirm the numbers.
4. **Retake granularity** — the designs say a student "can retake once a month". Per track, or
   across the whole platform?
5. **Percentile at launch** — with few attempts, a percentile is misleading. Suppress it below a
   threshold, or show it regardless?
