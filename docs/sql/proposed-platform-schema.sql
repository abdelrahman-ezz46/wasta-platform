-- Wasta platform schema — PROPOSED, for review.
--
-- Derived from the jobseeker.sql draft plus the Figma designs. Not yet
-- authoritative: once this model is agreed, it becomes EF Core entities and
-- generated migrations, and this file is retired.
--
-- Three classes of change from the draft:
--
--   1. Foreign keys were declared backwards (child <- parent instead of
--      child -> parent). Postgres rejected 14 of 21; only 7 were created.
--      All corrected here.
--   2. Surrogate keys are BIGINT identity, with the identity provider's
--      subject kept separately as auth_subject. The draft used TEXT ids,
--      which would have forced a rewrite of the two finished AI modules -
--      they type student ids as int throughout. This also avoids leaking an
--      IdP subject as a primary key.
--   3. Everything the designs need that the draft had no room for:
--      assessment attempts, answers, per-section scores, bands, percentiles,
--      a credit ledger, profile unlocks, company verification, timestamps.
--
-- Conventions: snake_case, timestamptz for every instant, created_at on
-- every mutable row, ON DELETE chosen per relationship rather than by habit.

BEGIN;

-- ============================================================
-- Identity and accounts
-- ============================================================

CREATE TABLE user_account (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    auth_subject    TEXT        NOT NULL,          -- external IdP subject
    email           TEXT        NOT NULL,
    role            TEXT        NOT NULL CHECK (role IN ('seeker', 'company', 'admin')),
    status          TEXT        NOT NULL DEFAULT 'active'
                                CHECK (status IN ('active', 'suspended', 'deleted')),
    email_verified_at TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ                    -- PDPL erasure, keeps FKs intact
);

CREATE UNIQUE INDEX ux_user_account_auth_subject ON user_account (auth_subject);
CREATE UNIQUE INDEX ux_user_account_email_lower  ON user_account (lower(email));

-- ============================================================
-- Reference data
-- ============================================================

CREATE TABLE track (
    id           INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name         TEXT    NOT NULL,
    slug         TEXT    NOT NULL,
    is_active    BOOLEAN NOT NULL DEFAULT true,
    display_order INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_track_slug ON track (slug);

CREATE TABLE skill (
    id   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_skill_name_lower ON skill (lower(name));

CREATE TABLE industry (
    id   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL
);

CREATE TABLE location (
    id           INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    city         TEXT NOT NULL,
    country_code CHAR(2) NOT NULL          -- EG, AE, JO, SA
);

CREATE TABLE employment_type (
    id   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL                     -- Full-time, Internship, Contract
);

CREATE TABLE work_type (
    id   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL                     -- Remote, Hybrid, On-site
);

CREATE TABLE application_state (
    id           INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name         TEXT NOT NULL,            -- Applied, In review, Rejected, Hired, Withdrawn
    is_terminal  BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE payment_method (
    id   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL                     -- Bank transfer (only method in v1)
);

-- ============================================================
-- Job seekers
-- ============================================================

CREATE TABLE job_seeker (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      BIGINT NOT NULL REFERENCES user_account (id) ON DELETE CASCADE,
    full_name    TEXT   NOT NULL,
    phone_number TEXT,
    track_id     INTEGER REFERENCES track (id) ON DELETE RESTRICT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_job_seeker_user ON job_seeker (user_id);
CREATE INDEX ix_job_seeker_track ON job_seeker (track_id);

-- Score deliberately does NOT live here. It is derived from an attempt and
-- lives on attempt_score, so history, percentile, and section breakdown are
-- all representable. The draft's job_seeker.score float could not produce
-- the designed results page.

CREATE TABLE job_seeker_profile (
    job_seeker_id       BIGINT PRIMARY KEY REFERENCES job_seeker (id) ON DELETE CASCADE,
    bio                 TEXT CHECK (char_length(bio) <= 500),
    university          TEXT,
    graduation_year     SMALLINT CHECK (graduation_year BETWEEN 1950 AND 2100),
    availability        TEXT,
    preferred_work_type_id INTEGER REFERENCES work_type (id) ON DELETE SET NULL,
    cv_url              TEXT,
    cv_uploaded_at      TIMESTAMPTZ,
    visible_to_companies BOOLEAN NOT NULL DEFAULT true,
    profile_strength    SMALLINT NOT NULL DEFAULT 0
                        CHECK (profile_strength BETWEEN 0 AND 100),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE job_seeker_skill (
    job_seeker_id BIGINT  NOT NULL REFERENCES job_seeker (id) ON DELETE CASCADE,
    skill_id      INTEGER NOT NULL REFERENCES skill (id)      ON DELETE CASCADE,
    PRIMARY KEY (job_seeker_id, skill_id)
);

-- ============================================================
-- Companies and verification
-- ============================================================

CREATE TABLE company (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id         BIGINT  NOT NULL REFERENCES user_account (id) ON DELETE CASCADE,
    name            TEXT    NOT NULL,
    normalized_name TEXT    NOT NULL,
    website         TEXT,
    company_size    TEXT,
    industry_id     INTEGER REFERENCES industry (id) ON DELETE RESTRICT,
    verification_state TEXT NOT NULL DEFAULT 'pending'
                       CHECK (verification_state IN ('pending', 'approved', 'rejected')),
    verified_at     TIMESTAMPTZ,
    verified_by     BIGINT REFERENCES user_account (id) ON DELETE SET NULL,
    rejection_note  TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_company_user ON company (user_id);
CREATE UNIQUE INDEX ux_company_normalized_name ON company (normalized_name);

-- Credits are NOT a column here. See credit_ledger_entry: a bare counter
-- makes disputes unresolvable and lets concurrent unlocks double-spend.

CREATE TABLE company_document (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id    BIGINT NOT NULL REFERENCES company (id) ON DELETE CASCADE,
    document_type TEXT   NOT NULL
                  CHECK (document_type IN ('commercial_register', 'tax_card', 'linkedin', 'other')),
    file_url      TEXT   NOT NULL,
    uploaded_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_company_document_company ON company_document (company_id);

-- ============================================================
-- Assessment content
-- ============================================================

CREATE TABLE section (
    id            INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    track_id      INTEGER NOT NULL REFERENCES track (id) ON DELETE CASCADE,
    name          TEXT    NOT NULL,      -- Fundamentals, Algorithms, ...
    display_order INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_section_track_name ON section (track_id, lower(name));

-- Multiple interchangeable forms per track are what make a monthly retake
-- possible without showing the same 30 questions again.
CREATE TABLE assessment_form (
    id               INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    track_id         INTEGER NOT NULL REFERENCES track (id) ON DELETE CASCADE,
    version          INTEGER NOT NULL,
    question_count   SMALLINT NOT NULL DEFAULT 30,
    duration_seconds INTEGER  NOT NULL DEFAULT 2700,
    is_active        BOOLEAN  NOT NULL DEFAULT false,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_assessment_form_track_version ON assessment_form (track_id, version);

CREATE TABLE question (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    track_id    INTEGER NOT NULL REFERENCES track (id)   ON DELETE CASCADE,
    section_id  INTEGER NOT NULL REFERENCES section (id) ON DELETE RESTRICT,
    body        JSONB   NOT NULL,        -- prompt + optional code block, markdown
    difficulty  SMALLINT CHECK (difficulty BETWEEN 1 AND 5),
    is_active   BOOLEAN NOT NULL DEFAULT true,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_question_track_section ON question (track_id, section_id);

CREATE TABLE question_option (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    question_id   BIGINT   NOT NULL REFERENCES question (id) ON DELETE CASCADE,
    body          TEXT     NOT NULL,
    is_correct    BOOLEAN  NOT NULL DEFAULT false,
    display_order SMALLINT NOT NULL DEFAULT 0
);
CREATE INDEX ix_question_option_question ON question_option (question_id);

CREATE TABLE assessment_form_question (
    form_id       INTEGER  NOT NULL REFERENCES assessment_form (id) ON DELETE CASCADE,
    question_id   BIGINT   NOT NULL REFERENCES question (id)        ON DELETE RESTRICT,
    display_order SMALLINT NOT NULL,
    PRIMARY KEY (form_id, question_id)
);

-- ============================================================
-- Scoring rules
-- ============================================================

-- Versioned so a historical score stays reproducible after the rubric changes.
CREATE TABLE scoring_rule_version (
    id          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    track_id    INTEGER NOT NULL REFERENCES track (id) ON DELETE CASCADE,
    version     INTEGER NOT NULL,
    notes       TEXT,
    is_active   BOOLEAN NOT NULL DEFAULT false,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_scoring_rule_track_version ON scoring_rule_version (track_id, version);

CREATE TABLE section_weight (
    rule_version_id INTEGER NOT NULL REFERENCES scoring_rule_version (id) ON DELETE CASCADE,
    section_id      INTEGER NOT NULL REFERENCES section (id)              ON DELETE CASCADE,
    weight          NUMERIC(5,4) NOT NULL CHECK (weight >= 0 AND weight <= 1),
    PRIMARY KEY (rule_version_id, section_id)
);

CREATE TABLE score_band (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    rule_version_id INTEGER  NOT NULL REFERENCES scoring_rule_version (id) ON DELETE CASCADE,
    name            TEXT     NOT NULL,
    min_percent     SMALLINT NOT NULL CHECK (min_percent BETWEEN 0 AND 100),
    max_percent     SMALLINT NOT NULL CHECK (max_percent BETWEEN 0 AND 100),
    CHECK (min_percent <= max_percent)
);

-- The fixed, pre-written feedback the results page shows instantly.
CREATE TABLE section_band_feedback (
    section_id INTEGER NOT NULL REFERENCES section (id)    ON DELETE CASCADE,
    band_id    INTEGER NOT NULL REFERENCES score_band (id) ON DELETE CASCADE,
    body       TEXT    NOT NULL,
    PRIMARY KEY (section_id, band_id)
);

-- ============================================================
-- Assessment delivery
-- ============================================================

CREATE TABLE attempt (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    job_seeker_id BIGINT  NOT NULL REFERENCES job_seeker (id)      ON DELETE CASCADE,
    form_id       INTEGER NOT NULL REFERENCES assessment_form (id) ON DELETE RESTRICT,
    track_id      INTEGER NOT NULL REFERENCES track (id)           ON DELETE RESTRICT,
    state         TEXT    NOT NULL DEFAULT 'in_progress'
                  CHECK (state IN ('in_progress', 'submitted', 'expired', 'abandoned')),
    started_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at    TIMESTAMPTZ NOT NULL,
    submitted_at  TIMESTAMPTZ
);
CREATE INDEX ix_attempt_seeker_track ON attempt (job_seeker_id, track_id, started_at DESC);

CREATE TABLE attempt_answer (
    attempt_id         BIGINT  NOT NULL REFERENCES attempt (id)         ON DELETE CASCADE,
    question_id        BIGINT  NOT NULL REFERENCES question (id)        ON DELETE RESTRICT,
    selected_option_id BIGINT           REFERENCES question_option (id) ON DELETE SET NULL,
    flagged_for_review BOOLEAN NOT NULL DEFAULT false,
    answered_at        TIMESTAMPTZ,
    PRIMARY KEY (attempt_id, question_id)
);

CREATE TABLE attempt_score (
    attempt_id      BIGINT PRIMARY KEY REFERENCES attempt (id) ON DELETE CASCADE,
    rule_version_id INTEGER  NOT NULL REFERENCES scoring_rule_version (id) ON DELETE RESTRICT,
    overall_percent SMALLINT NOT NULL CHECK (overall_percent BETWEEN 0 AND 100),
    percentile      SMALLINT CHECK (percentile BETWEEN 0 AND 100),
    computed_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE attempt_section_score (
    attempt_id BIGINT   NOT NULL REFERENCES attempt (id)   ON DELETE CASCADE,
    section_id INTEGER  NOT NULL REFERENCES section (id)   ON DELETE RESTRICT,
    percent    SMALLINT NOT NULL CHECK (percent BETWEEN 0 AND 100),
    band_id    INTEGER           REFERENCES score_band (id) ON DELETE SET NULL,
    PRIMARY KEY (attempt_id, section_id)
);

-- ============================================================
-- Jobs
-- ============================================================

CREATE TABLE job_post (
    id                 BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id         BIGINT  NOT NULL REFERENCES company (id)         ON DELETE CASCADE,
    title              TEXT    NOT NULL,
    track_id           INTEGER NOT NULL REFERENCES track (id)           ON DELETE RESTRICT,
    work_type_id       INTEGER REFERENCES work_type (id)                ON DELETE SET NULL,
    location_id        INTEGER REFERENCES location (id)                 ON DELETE SET NULL,
    employment_type_id INTEGER REFERENCES employment_type (id)          ON DELETE SET NULL,
    salary_min         NUMERIC(12,2),
    salary_max         NUMERIC(12,2),
    salary_currency    CHAR(3),                    -- EGP, AED, JOD, SAR
    salary_period      TEXT CHECK (salary_period IN ('month', 'year')),
    job_description    TEXT    NOT NULL,
    project_brief      TEXT,                       -- brief for the attached project
    project_deadline   DATE,
    is_active          BOOLEAN NOT NULL DEFAULT true,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    closes_at          TIMESTAMPTZ,
    CHECK (salary_min IS NULL OR salary_max IS NULL OR salary_min <= salary_max)
);
CREATE INDEX ix_job_post_company_active ON job_post (company_id) WHERE is_active;
CREATE INDEX ix_job_post_track_active   ON job_post (track_id)   WHERE is_active;

CREATE TABLE job_post_skill (
    job_post_id BIGINT  NOT NULL REFERENCES job_post (id) ON DELETE CASCADE,
    skill_id    INTEGER NOT NULL REFERENCES skill (id)    ON DELETE CASCADE,
    PRIMARY KEY (job_post_id, skill_id)
);

CREATE TABLE job_post_file (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    job_post_id BIGINT NOT NULL REFERENCES job_post (id) ON DELETE CASCADE,
    file_url    TEXT   NOT NULL,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============================================================
-- Applications and projects
-- ============================================================

CREATE TABLE application (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    job_seeker_id  BIGINT  NOT NULL REFERENCES job_seeker (id)        ON DELETE CASCADE,
    job_post_id    BIGINT  NOT NULL REFERENCES job_post (id)          ON DELETE CASCADE,
    state_id       INTEGER NOT NULL REFERENCES application_state (id) ON DELETE RESTRICT,
    project_title  TEXT,
    description    TEXT CHECK (char_length(description) <= 600),
    repo_url       TEXT,
    live_demo_url  TEXT,
    feedback       TEXT,
    submitted_at   TIMESTAMPTZ,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
-- Re-applying creates a NEW application rather than reusing the old one, so
-- there is deliberately no unique constraint on (job_seeker_id, job_post_id).
-- The 6-project cap therefore counts non-withdrawn applications, or a seeker
-- who applied and withdrew six times would be locked out permanently.
CREATE INDEX ix_application_seeker_post ON application (job_seeker_id, job_post_id);
CREATE INDEX ix_application_post ON application (job_post_id);

CREATE TABLE application_file (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    application_id BIGINT NOT NULL REFERENCES application (id) ON DELETE CASCADE,
    file_url       TEXT   NOT NULL,
    file_name      TEXT,
    uploaded_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============================================================
-- Credits and unlocks
-- ============================================================

-- Append-only. Balance is the sum of deltas; balance_after is carried for
-- cheap reads and reconciliation, never as the source of truth.
CREATE TABLE credit_ledger_entry (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id    BIGINT  NOT NULL REFERENCES company (id) ON DELETE CASCADE,
    delta         INTEGER NOT NULL CHECK (delta <> 0),
    reason        TEXT    NOT NULL
                  CHECK (reason IN ('trial_grant', 'topup', 'unlock', 'refund', 'adjustment')),
    balance_after INTEGER NOT NULL CHECK (balance_after >= 0),
    actor_user_id BIGINT REFERENCES user_account (id) ON DELETE SET NULL,
    note          TEXT,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_credit_ledger_company ON credit_ledger_entry (company_id, created_at DESC);

-- Bank transfer only in v1: the company requests, an admin confirms the
-- transfer arrived, then issues credits.
CREATE TABLE credit_topup_request (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id        BIGINT  NOT NULL REFERENCES company (id)        ON DELETE CASCADE,
    credits_requested INTEGER NOT NULL CHECK (credits_requested > 0),
    payment_method_id INTEGER NOT NULL REFERENCES payment_method (id) ON DELETE RESTRICT,
    amount            NUMERIC(12,2),
    currency          CHAR(3),
    state             TEXT    NOT NULL DEFAULT 'pending'
                      CHECK (state IN ('pending', 'approved', 'rejected')),
    reviewed_by       BIGINT REFERENCES user_account (id) ON DELETE SET NULL,
    reviewed_at       TIMESTAMPTZ,
    ledger_entry_id   BIGINT REFERENCES credit_ledger_entry (id) ON DELETE SET NULL,
    note              TEXT,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_topup_request_state ON credit_topup_request (state, created_at);

CREATE TABLE profile_unlock (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT NOT NULL REFERENCES company (id)             ON DELETE CASCADE,
    job_seeker_id   BIGINT NOT NULL REFERENCES job_seeker (id)          ON DELETE CASCADE,
    ledger_entry_id BIGINT NOT NULL REFERENCES credit_ledger_entry (id) ON DELETE RESTRICT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
-- Paying twice for the same candidate is a bug, not a feature.
CREATE UNIQUE INDEX ux_profile_unlock_pair ON profile_unlock (company_id, job_seeker_id);
CREATE INDEX ix_profile_unlock_seeker ON profile_unlock (job_seeker_id, created_at DESC);

-- ============================================================
-- Audit and notifications
-- ============================================================

CREATE TABLE audit_log (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_user_id BIGINT REFERENCES user_account (id) ON DELETE SET NULL,
    action        TEXT   NOT NULL,
    entity_type   TEXT   NOT NULL,
    entity_id     TEXT   NOT NULL,
    detail        JSONB,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_log_entity ON audit_log (entity_type, entity_id, created_at DESC);

CREATE TABLE notification (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id     BIGINT NOT NULL REFERENCES user_account (id) ON DELETE CASCADE,
    kind        TEXT   NOT NULL,
    payload     JSONB  NOT NULL,
    read_at     TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_notification_user_unread ON notification (user_id, created_at DESC)
    WHERE read_at IS NULL;

COMMIT;
