#!/usr/bin/env python3
"""
Builds a presentable demo dataset by driving the real HTTP API.

Every candidate is registered over HTTP, sits a real attempt, answers real
questions and is scored by the real pipeline - so what a demo shows is the
product working, not fixtures pasted into tables.

The one exception is email confirmation. Sign-in requires a confirmed address,
and confirming means clicking a link that only exists in an email nobody sends
on a laptop. Rather than open a "mark this account verified" endpoint - a
backdoor that would then exist in production too - the script confirms the
demo accounts with a direct UPDATE, and only ever for @*.demo addresses.

The cohort size is the point. A percentile is withheld below 50 scored attempts
on a track (see MinimumCohortForPercentile), because a percentile drawn from a
handful of attempts is a lie. Seeding fewer than that leaves the score card
showing a blank percentile, which looks like a bug and is not.

    WASTA_ADMIN_PASSWORD=... python3 scripts/seed-demo.py [--base http://localhost:5280]

Registering dozens of accounts in seconds trips the sign-in rate limit, which
defaults to 10/minute. appsettings.Development.json is deliberately untracked
(it is where secrets would land), so raise the limit locally before running:

    "RateLimits": { "AuthPerMinute": 2000, "UnlockPerMinute": 2000 }
"""
import argparse
import json
import os
import random
import sys
import urllib.error
import subprocess
import urllib.request

FIRST = ["Layla", "Omar", "Nour", "Youssef", "Salma", "Karim", "Hana", "Tarek",
         "Mariam", "Ahmed", "Farida", "Hassan", "Yasmin", "Khaled", "Dina",
         "Mostafa", "Rana", "Amir", "Habiba", "Seif", "Malak", "Zeyad",
         "Nada", "Adham", "Jana", "Marwan", "Aya", "Bassel", "Rowan", "Fady"]
LAST = ["Hassan", "Ibrahim", "Mahmoud", "Fouad", "Nasser", "Adel", "Sabry",
        "Zaki", "Rashad", "Halim", "Mansour", "Gaber", "Shafik", "Roshdy"]


def call(base, method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, raw
    except urllib.error.URLError as e:
        # Nothing listening, DNS failure, refused connection. Reported as a
        # status so callers handle it like any other failure instead of
        # dying with a traceback.
        return 0, f"could not reach {base}: {e.reason}"


def sit_assessment(base, token, track_id, correct_count):
    """Registers a real attempt and answers `correct_count` questions correctly."""
    status, attempt = call(base, "POST", f"/api/assessments/tracks/{track_id}/attempts", token=token)
    if status >= 400:
        return None
    attempt_id = attempt["attemptId"]

    status, view = call(base, "GET", f"/api/assessments/attempts/{attempt_id}", token=token)
    if status >= 400:
        return None

    for index, question in enumerate(view["questions"]):
        right = [o for o in question["options"] if "Correct" in o["body"]]
        wrong = [o for o in question["options"] if "Correct" not in o["body"]]
        pick = (right[0] if index < correct_count else wrong[0])["optionId"]
        call(base, "PUT",
             f"/api/assessments/attempts/{attempt_id}/answers/{question['questionId']}",
             {"selectedOptionId": pick}, token)

    status, result = call(base, "POST", f"/api/assessments/attempts/{attempt_id}/submit", token=token)
    return result if status < 400 else None


def confirm_demo_emails():
    """
    Confirms the seeded demo accounts so they can sign in.

    Scoped to .demo addresses so it can never touch a real one, and kept in the
    seeding script rather than the application: an endpoint that marks an
    account verified would be a backdoor that shipped to production along with
    everything else.
    """
    sql = ("update user_account set email_verified_at = now() "
           "where email like '%@wasta.demo' or email like '%@niletech.demo';")
    try:
        done = subprocess.run(
            ["docker", "exec", "wasta-postgres", "psql", "-U", "postgres", "-d", "wasta", "-c", sql],
            capture_output=True, text=True, timeout=60)
        if done.returncode == 0:
            print("demo accounts confirmed for sign-in")
            return True
        print(f"  could not confirm demo emails: {done.stderr.strip()[:120]}")
    except (OSError, subprocess.SubprocessError) as e:
        print(f"  could not confirm demo emails: {e}")

    print("  demo accounts exist but cannot sign in until their email is confirmed.")
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="http://localhost:5280")
    parser.add_argument("--cohort", type=int, default=64)
    parser.add_argument("--admin-email", default="admin@wasta.demo")
    parser.add_argument(
        "--admin-password",
        default=os.environ.get("WASTA_ADMIN_PASSWORD"),
        help="Admin password. Defaults to $WASTA_ADMIN_PASSWORD; no built-in default, "
             "so nothing usable as a credential is committed to this repo.")
    args = parser.parse_args()
    base = args.base.rstrip("/")

    if not args.admin_password:
        sys.exit("No admin password. Set WASTA_ADMIN_PASSWORD, or pass --admin-password.\n"
                 "It must match the Seed:AdminPassword the API was started with.")

    status, health = call(base, "GET", "/health/live")
    if status != 200:
        sys.exit(f"API not reachable at {base}: {health}\n"
                 f"Start it with: dotnet run --project src/Wasta.WebApi --urls {base}")

    status, admin = call(base, "POST", "/api/auth/login",
                         {"email": args.admin_email, "password": args.admin_password})
    if status >= 400:
        sys.exit(f"Admin login failed ({status}): {admin}\n"
                 "Set Seed:AdminEmail and Seed:AdminPassword, then restart the API.")
    admin_token = admin["accessToken"]
    print(f"admin signed in")

    # A believable spread rather than a uniform one: most candidates land in the
    # middle, a few at each tail. Five questions per form, so 0-5 correct.
    weights = [3, 7, 16, 26, 30, 18]
    track_id = 2  # Backend Engineering carries the cohort, so it has a percentile.

    seeded = 0
    random.seed(20260824)
    for i in range(args.cohort):
        name = f"{random.choice(FIRST)} {random.choice(LAST)}"
        email = f"demo.candidate{i:03d}@wasta.demo"
        status, seeker = call(base, "POST", "/api/auth/register/seeker", {
            "fullName": name,
            "email": email,
            "password": "Passw0rd123",
            "trackId": track_id,
        })
        if status >= 400:
            if status == 409:
                continue  # Already seeded; the script is safe to re-run.
            print(f"  register failed ({status}): {seeker}")
            continue

        correct = random.choices(range(6), weights=weights)[0]
        if sit_assessment(base, seeker["accessToken"], track_id, correct):
            seeded += 1
        if seeded and seeded % 16 == 0:
            print(f"  {seeded} candidates scored")

    print(f"cohort: {seeded} candidates scored on track {track_id}")

    # Before the company block, not after: a re-run finds the company already
    # registered and falls back to signing in, which is gated on confirmation.
    confirm_demo_emails()

    # The hiring side: one approved company holding credits it can actually spend.
    company_email = "hiring@niletech.demo"
    status, company = call(base, "POST", "/api/auth/register/company", {
        "companyName": "Nile Tech",
        "workEmail": company_email,
        "password": "Passw0rd123",
        "industryId": 5,
    })
    if status == 409:
        status, company = call(base, "POST", "/api/auth/login",
                               {"email": company_email, "password": "Passw0rd123"})
    if status >= 400:
        sys.exit(f"Company setup failed ({status}): {company}")

    company_token, company_id = company["accessToken"], company["companyId"]
    call(base, "POST", f"/api/admin/companies/{company_id}/approve", token=admin_token)

    status, topup = call(base, "POST", "/api/companies/me/credits/topups", {
        "creditsRequested": 25,
        "paymentMethodId": 1,          # Bank transfer - the only method in v1.
        "amount": 2500,
        "currency": "EGP",
    }, token=company_token)
    if status < 400 and topup:
        call(base, "POST", f"/api/admin/topups/{topup['requestId']}/review",
             {"approve": True, "note": "Demo seed - bank transfer confirmed."}, token=admin_token)

    print("company 'Nile Tech' approved, credits issued")

    # Again, for a company registered during this run.
    confirm_demo_emails()
    print()
    print("Demo sign-ins")
    print(f"  admin    {args.admin_email} / (the password you supplied)")
    print(f"  company  {company_email} / Passw0rd123")
    print()
    print(f"Open {base}/ to run the walkthrough.")


if __name__ == "__main__":
    main()
