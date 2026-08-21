using Microsoft.EntityFrameworkCore;
using Wasta.CareerCoach.Domain;
using Wasta.Domain.Assessments;
using Wasta.Infrastructure.Persistence;

namespace Wasta.WebApi.Integration;

/// <summary>
/// Feeds the Career Coach from the platform's real assessment tables.
///
/// This is the port the module was written against three slices before the
/// tables existed. Nothing about the module changes to plug it in - which was
/// the point of the port, and is worth stating now that it has actually been
/// tested rather than asserted.
/// </summary>
public sealed class PlatformAssessmentDataProvider(WastaDbContext db) : IAssessmentDataProvider
{
    public async Task<AttemptScoreData?> GetAttemptScoreAsync(int attemptId, CancellationToken ct)
    {
        var header = await (
            from attempt in db.Attempts.AsNoTracking()
            join score in db.AttemptScores.AsNoTracking() on attempt.Id equals score.AttemptId
            join track in db.Tracks.AsNoTracking() on attempt.TrackId equals track.Id
            where attempt.Id == attemptId && attempt.State == AttemptState.Submitted
            select new { attempt.Id, attempt.JobSeekerId, TrackName = track.Name })
            .FirstOrDefaultAsync(ct);

        if (header is null)
        {
            return null;
        }

        var sections = await (
            from sectionScore in db.AttemptSectionScores.AsNoTracking()
            join section in db.Sections.AsNoTracking() on sectionScore.SectionId equals section.Id
            where sectionScore.AttemptId == attemptId
            orderby section.DisplayOrder
            select new SectionScoreData(section.Name, sectionScore.Percent))
            .ToListAsync(ct);

        // The module wants a separate score id; the platform keys a score by its
        // attempt, so they are the same number. Passing the attempt id keeps the
        // module's contract satisfied without inventing a column for it.
        return new AttemptScoreData(
            PlatformIds.ToModuleId(header.JobSeekerId, "Seeker id"),
            attemptId,
            attemptId,
            header.TrackName,
            sections);
    }

    public async Task<StudentContextData> GetStudentContextAsync(int studentId, CancellationToken ct)
    {
        var profile = await db.JobSeekerProfiles.AsNoTracking()
            .Where(p => p.JobSeekerId == studentId)
            .Select(p => new { p.GraduationYear })
            .FirstOrDefaultAsync(ct);

        var skills = await db.JobSeekerSkills.AsNoTracking()
            .Where(s => s.JobSeekerId == studentId)
            .Join(db.Skills.AsNoTracking(), s => s.SkillId, s => s.Id, (_, skill) => skill.Name)
            .OrderBy(name => name)
            .ToListAsync(ct);

        var projects = await db.JobApplications.AsNoTracking()
            .Where(a => a.JobSeekerId == studentId && a.ProjectTitle != null)
            .OrderByDescending(a => a.SubmittedAt)
            .Select(a => a.ProjectTitle!)
            .Distinct()
            .Take(10)
            .ToListAsync(ct);

        // Name, email, university, city and CV are deliberately absent. The
        // module's DTO has no fields for them, so what reaches the model is
        // bounded by shape rather than by remembering not to send them.
        return new StudentContextData(skills, projects, profile?.GraduationYear);
    }

    public async Task<int?> GetCurrentAttemptIdAsync(int studentId, CancellationToken ct)
    {
        // "Current" is the most recent scored attempt: what the results page is
        // showing when the coach card renders beneath it.
        var attemptId = await (
            from attempt in db.Attempts.AsNoTracking()
            join score in db.AttemptScores.AsNoTracking() on attempt.Id equals score.AttemptId
            where attempt.JobSeekerId == studentId && attempt.State == AttemptState.Submitted
            orderby attempt.SubmittedAt descending
            select (long?)attempt.Id)
            .FirstOrDefaultAsync(ct);

        return PlatformIds.TryToModuleId(attemptId);
    }
}
