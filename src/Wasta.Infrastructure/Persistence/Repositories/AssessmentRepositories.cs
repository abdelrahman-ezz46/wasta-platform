using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Application.Features.Assessments;
using Wasta.Application.Features.Localization;
using Wasta.Domain.Assessments;
using Wasta.Domain.Localization;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class AssessmentRepository(WastaDbContext db) : IAssessmentRepository
{
    public async Task<ActiveFormInfo?> FindActiveFormAsync(int trackId, CancellationToken ct = default)
    {
        // Highest active version wins when several are live, so publishing a new
        // form supersedes the old one without a migration.
        var form = await db.AssessmentForms.AsNoTracking()
            .Where(f => f.TrackId == trackId && f.IsActive)
            .OrderByDescending(f => f.Version)
            .FirstOrDefaultAsync(ct);

        return form is null
            ? null
            : new ActiveFormInfo(form.Id, form.TrackId, form.DurationSeconds, form.QuestionCount);
    }

    public async Task<DateTimeOffset?> FindLastAttemptStartAsync(
        long seekerId, int trackId, CancellationToken ct = default)
    {
        var last = await db.Attempts.AsNoTracking()
            .Where(a => a.JobSeekerId == seekerId && a.TrackId == trackId)
            .OrderByDescending(a => a.StartedAt)
            .Select(a => (DateTimeOffset?)a.StartedAt)
            .FirstOrDefaultAsync(ct);

        return last;
    }

    public async Task<IReadOnlyList<AttemptQuestionView>> GetFormQuestionsForDisplayAsync(
        int formId, long attemptId, CancellationToken ct = default)
    {
        var rows = await (
            from fq in db.AssessmentFormQuestions.AsNoTracking()
            join q in db.Questions.AsNoTracking() on fq.QuestionId equals q.Id
            where fq.FormId == formId
            orderby fq.DisplayOrder
            select new { q.Id, q.Body, fq.DisplayOrder })
            .ToListAsync(ct);

        var questionIds = rows.Select(r => r.Id).ToList();

        // Projected without IsCorrect. The answer key is never loaded on this
        // path at all, so it cannot leak through a serialisation mistake.
        var options = await db.QuestionOptions.AsNoTracking()
            .Where(o => questionIds.Contains(o.QuestionId))
            .OrderBy(o => o.DisplayOrder)
            .Select(o => new { o.Id, o.QuestionId, o.Body, o.DisplayOrder })
            .ToListAsync(ct);

        var answers = await db.AttemptAnswers.AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .ToDictionaryAsync(a => a.QuestionId, a => new { a.SelectedOptionId, a.FlaggedForReview }, ct);

        return rows.Select(r =>
        {
            answers.TryGetValue(r.Id, out var answer);

            return new AttemptQuestionView(
                r.Id,
                r.Body,
                r.DisplayOrder,
                options.Where(o => o.QuestionId == r.Id)
                    .Select(o => new AttemptOptionView(o.Id, o.Body, o.DisplayOrder))
                    .ToList(),
                answer?.SelectedOptionId,
                answer?.FlaggedForReview ?? false);
        }).ToList();
    }

    public async Task<IReadOnlyList<FormQuestionGrading>> GetFormQuestionsForGradingAsync(
        int formId, CancellationToken ct = default)
    {
        return await (
            from fq in db.AssessmentFormQuestions.AsNoTracking()
            join q in db.Questions.AsNoTracking() on fq.QuestionId equals q.Id
            where fq.FormId == formId
            select new FormQuestionGrading(
                q.Id,
                q.SectionId,
                db.QuestionOptions
                    .Where(o => o.QuestionId == q.Id && o.IsCorrect)
                    .Select(o => (long?)o.Id)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    public Task<bool> QuestionBelongsToFormAsync(int formId, long questionId, CancellationToken ct = default) =>
        db.AssessmentFormQuestions.AsNoTracking()
            .AnyAsync(fq => fq.FormId == formId && fq.QuestionId == questionId, ct);

    public Task<bool> OptionBelongsToQuestionAsync(long questionId, long optionId, CancellationToken ct = default) =>
        db.QuestionOptions.AsNoTracking().AnyAsync(o => o.Id == optionId && o.QuestionId == questionId, ct);

    public async Task<IReadOnlyDictionary<int, decimal>> GetSectionWeightsAsync(
        int ruleVersionId, CancellationToken ct = default) =>
        await db.SectionWeights.AsNoTracking()
            .Where(w => w.RuleVersionId == ruleVersionId)
            .ToDictionaryAsync(w => w.SectionId, w => w.Weight, ct);

    public async Task<IReadOnlyList<ScoreBand>> GetBandsAsync(int ruleVersionId, CancellationToken ct = default) =>
        await db.ScoreBands.AsNoTracking().Where(b => b.RuleVersionId == ruleVersionId).ToListAsync(ct);

    public async Task<int?> FindActiveRuleVersionIdAsync(int trackId, CancellationToken ct = default) =>
        await db.ScoringRuleVersions.AsNoTracking()
            .Where(r => r.TrackId == trackId && r.IsActive)
            .OrderByDescending(r => r.Version)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<short>> GetCohortScoresAsync(int trackId, CancellationToken ct = default) =>
        await (
            from s in db.AttemptScores.AsNoTracking()
            join a in db.Attempts.AsNoTracking() on s.AttemptId equals a.Id
            where a.TrackId == trackId && a.State == AttemptState.Submitted
            select s.OverallPercent)
            .ToListAsync(ct);
}

public sealed class AttemptRepository(
    WastaDbContext db, ILocalizer localizer, ICurrentLanguage language) : IAttemptRepository
{
    public Task<Attempt?> FindAsync(long attemptId, CancellationToken ct = default) =>
        db.Attempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct);

    public Task<Attempt?> FindWithAnswersAsync(long attemptId, CancellationToken ct = default) =>
        db.Attempts.Include(a => a.Answers).FirstOrDefaultAsync(a => a.Id == attemptId, ct);

    public void Add(Attempt attempt) => db.Attempts.Add(attempt);

    public async Task UpsertAnswerAsync(
        long attemptId, long questionId, long? selectedOptionId, bool flagged, DateTimeOffset now,
        CancellationToken ct = default)
    {
        var existing = await db.AttemptAnswers
            .FirstOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, ct);

        if (existing is null)
        {
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                SelectedOptionId = selectedOptionId,
                FlaggedForReview = flagged,
                AnsweredAt = selectedOptionId is null ? null : now,
            });
            return;
        }

        existing.SelectedOptionId = selectedOptionId;
        existing.FlaggedForReview = flagged;

        // Only a real selection stamps the time; flagging alone is not answering.
        if (selectedOptionId is not null)
        {
            existing.AnsweredAt = now;
        }
    }

    public void AddScore(AttemptScore score) => db.AttemptScores.Add(score);

    public void AddSectionScore(AttemptSectionScore sectionScore) => db.AttemptSectionScores.Add(sectionScore);

    public async Task<ResultsView?> GetResultsAsync(long attemptId, CancellationToken ct = default)
    {
        var header = await (
            from a in db.Attempts.AsNoTracking()
            join s in db.AttemptScores.AsNoTracking() on a.Id equals s.AttemptId
            where a.Id == attemptId
            select new { a.TrackId, s.OverallPercent, s.Percentile, s.ComputedAt })
            .FirstOrDefaultAsync(ct);

        if (header is null)
        {
            return null;
        }

        var sections = await (
            from ss in db.AttemptSectionScores.AsNoTracking()
            join sec in db.Sections.AsNoTracking() on ss.SectionId equals sec.Id
            where ss.AttemptId == attemptId
            select new
            {
                ss.SectionId,
                sec.Name,
                sec.DisplayOrder,
                ss.Percent,
                ss.BandId,
                BandName = db.ScoreBands.Where(b => b.Id == ss.BandId).Select(b => b.Name).FirstOrDefault(),
                Feedback = db.SectionBandFeedback
                    .Where(f => f.SectionId == ss.SectionId && f.BandId == ss.BandId)
                    .Select(f => f.Body)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var sectionNames = await localizer.NamesAsync(LocalizedEntities.Section, language.Value, ct);
        var bandNames = await localizer.NamesAsync(LocalizedEntities.ScoreBand, language.Value, ct);

        static string Localised(IReadOnlyDictionary<long, string> names, long id, string? fallback) =>
            names.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback ?? string.Empty;

        var views = sections
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SectionId)
            .Select(s => new SectionScoreView(
                s.SectionId,
                Localised(sectionNames, s.SectionId, s.Name),
                s.Percent,
                s.BandId is null ? s.BandName : Localised(bandNames, s.BandId.Value, s.BandName),
                s.Feedback))
            .ToList();

        // Same ordering rule the calculator uses, so the gaps shown here match
        // the gaps it derived.
        var gaps = views
            .OrderBy(s => s.Percent)
            .ThenBy(s => s.SectionId)
            .Take(Domain.Assessments.ScoreCalculator.SkillGapCount)
            .ToList();

        return new ResultsView(
            attemptId, header.TrackId, header.OverallPercent, header.Percentile, header.ComputedAt, views, gaps);
    }
}
