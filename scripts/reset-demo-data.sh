#!/usr/bin/env bash
# Clears everything people created, and keeps everything experts authored.
#
# Wiped:  accounts, seekers, companies, attempts and scores, jobs and
#         applications, unlocks, credit ledgers, notifications, tokens, audit
#         log, and the two AI modules' plans and chat history.
#
# Kept:   tracks, sections, questions, options, assessment forms, scoring rules,
#         bands, section feedback, translations, and the reference lookups.
#         That content is the expensive part and nothing here should touch it.
#
# The seeded administrator is removed with everything else. It is recreated on
# the next start from Seed:AdminEmail / Seed:AdminPassword.
#
#   ./scripts/reset-demo-data.sh            # asks first
#   ./scripts/reset-demo-data.sh --yes      # no prompt
set -euo pipefail

CONTAINER="${WASTA_PG_CONTAINER:-wasta-postgres}"
DB="${WASTA_DB:-wasta}"
USER="${WASTA_DB_USER:-postgres}"

if ! docker exec "$CONTAINER" pg_isready -U "$USER" >/dev/null 2>&1; then
  echo "Postgres container '$CONTAINER' is not reachable. Start it with: docker compose up -d" >&2
  exit 1
fi

count() { docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -c "select count(*) from $1;" 2>/dev/null || echo "?"; }

echo "About to delete:"
echo "  user accounts   $(count user_account)"
echo "  job seekers     $(count job_seeker)"
echo "  companies       $(count company)"
echo "  attempts        $(count attempt)"
echo "  job posts       $(count job_post)"
echo "  applications    $(count job_application)"
echo
echo "Keeping: $(count track) tracks and $(count question) questions, plus scoring rules and lookups."
echo

if [ "${1:-}" != "--yes" ]; then
  read -rp "Type 'reset' to continue: " ANSWER
  [ "$ANSWER" = "reset" ] || { echo "Nothing changed."; exit 1; }
fi

# One TRUNCATE so foreign keys never see a half-cleared graph, and RESTART
# IDENTITY so a fresh run starts at id 1 rather than continuing from 83.
docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -v ON_ERROR_STOP=1 -q -c '
TRUNCATE TABLE
    account_token, refresh_token, audit_log, notification,
    attempt_answer, attempt_section_score, attempt_score, attempt,
    application_file, job_application,
    job_post_file, job_post_skill, job_post,
    profile_unlock, credit_ledger_entry, credit_topup_request,
    company_document, company,
    job_seeker_skill, job_seeker_profile, job_seeker,
    user_account,
    "StudentCoachPlans", "ChatMessages", "ChatSessions"
RESTART IDENTITY CASCADE;'

echo
echo "Done. Every account is gone."
echo "Restart the API to recreate the seeded administrator, then optionally:"
echo "    WASTA_ADMIN_PASSWORD=... python3 scripts/seed-demo.py"
