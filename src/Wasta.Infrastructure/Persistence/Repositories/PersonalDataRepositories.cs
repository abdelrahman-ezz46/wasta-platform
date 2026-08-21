using Microsoft.EntityFrameworkCore;
using Wasta.Application.Features.Auth;
using Wasta.Domain.Companies;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class PersonalDataQueries(WastaDbContext db) : IPersonalDataQueries
{
    public async Task<PersonalDataExport?> ExportAsync(long userId, CancellationToken ct = default)
    {
        var account = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                Role = u.Role.ToString(),
                Status = u.Status.ToString(),
                Language = u.Language.ToString(),
                u.EmailVerifiedAt,
                u.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var seeker = await (
            from s in db.JobSeekers.AsNoTracking()
            where s.UserId == userId
            select new
            {
                s.Id,
                s.FullName,
                s.PhoneNumber,
                s.TrackId,
                s.CreatedAt,
                Profile = db.JobSeekerProfiles.Where(p => p.JobSeekerId == s.Id)
                    .Select(p => new
                    {
                        p.Bio,
                        p.University,
                        p.GraduationYear,
                        p.Availability,
                        p.CvUrl,
                        p.VisibleToCompanies,
                        p.ProfileStrength,
                    })
                    .FirstOrDefault(),
                Skills = db.JobSeekerSkills.Where(js => js.JobSeekerId == s.Id)
                    .Join(db.Skills, js => js.SkillId, sk => sk.Id, (_, sk) => sk.Name)
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        var company = await db.Companies.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Website,
                c.CompanySize,
                VerificationState = c.VerificationState.ToString(),
                c.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        var seekerId = seeker?.Id;

        var attempts = seekerId is null
            ? []
            : await (
                from a in db.Attempts.AsNoTracking()
                where a.JobSeekerId == seekerId
                select new
                {
                    a.Id,
                    a.TrackId,
                    State = a.State.ToString(),
                    a.StartedAt,
                    a.SubmittedAt,
                    Score = db.AttemptScores.Where(s => s.AttemptId == a.Id)
                        .Select(s => new { s.OverallPercent, s.Percentile })
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

        var applications = seekerId is null
            ? []
            : await (
                from ap in db.JobApplications.AsNoTracking()
                where ap.JobSeekerId == seekerId
                select new
                {
                    ap.Id,
                    ap.JobPostId,
                    ap.ProjectTitle,
                    ap.Description,
                    ap.RepoUrl,
                    ap.LiveDemoUrl,
                    ap.Feedback,
                    ap.CreatedAt,
                })
                .ToListAsync(ct);

        // Who has seen this person. Someone exercising a right of access is
        // usually asking exactly this.
        var unlocks = seekerId is null
            ? []
            : await (
                from u in db.ProfileUnlocks.AsNoTracking()
                join c in db.Companies.AsNoTracking() on u.CompanyId equals c.Id
                where u.JobSeekerId == seekerId
                select new { c.Name, u.CreatedAt })
                .ToListAsync(ct);

        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new { n.Kind, n.Payload, n.CreatedAt, n.ReadAt })
            .ToListAsync(ct);

        return new PersonalDataExport(
            DateTimeOffset.UtcNow,
            account,
            seeker,
            company,
            attempts.Cast<object>().ToList(),
            applications.Cast<object>().ToList(),
            unlocks.Cast<object>().ToList(),
            notifications.Cast<object>().ToList());
    }
}

public sealed class PersonalDataEraser(WastaDbContext db) : IPersonalDataEraser
{
    public async Task<string?> EraseAsync(long userId, DateTimeOffset now, CancellationToken ct = default)
    {
        string? cvKey = null;

        var seeker = await db.JobSeekers.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (seeker is not null)
        {
            var profile = await db.JobSeekerProfiles
                .FirstOrDefaultAsync(p => p.JobSeekerId == seeker.Id, ct);

            if (profile is not null)
            {
                cvKey = profile.CvUrl;

                // Out of the talent pool first: an erased profile must not be
                // unlockable, and a company mid-browse should not be able to
                // spend a credit on someone who has just left.
                profile.SetVisibility(false, now);
                profile.Update(null, null, null, null, null, now);
                profile.SetCv(string.Empty, now);
            }

            seeker.UpdateBasics("Deleted user", null, seeker.TrackId, now);

            var skills = await db.JobSeekerSkills.Where(s => s.JobSeekerId == seeker.Id).ToListAsync(ct);
            db.JobSeekerSkills.RemoveRange(skills);

            // Project work is the person's own writing and goes with them. The
            // application rows stay so the company's hiring record is intact.
            var applications = await db.JobApplications
                .Where(a => a.JobSeekerId == seeker.Id).ToListAsync(ct);

            foreach (var application in applications)
            {
                application.UpdateProject(null, null, null, null, now);
            }
        }

        var company = await db.Companies.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (company is not null)
        {
            // A company's documents are its legal paperwork; the account going
            // away does not entitle us to keep the uploads.
            var documents = await db.CompanyDocuments.Where(d => d.CompanyId == company.Id).ToListAsync(ct);
            db.CompanyDocuments.RemoveRange(documents);
        }

        var notifications = await db.Notifications.Where(n => n.UserId == userId).ToListAsync(ct);
        db.Notifications.RemoveRange(notifications);

        return string.IsNullOrEmpty(cvKey) ? null : cvKey;
    }
}
