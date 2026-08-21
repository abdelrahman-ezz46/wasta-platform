using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Admin;
using Wasta.Domain.Assessments;
using Wasta.Domain.Catalog;
using Wasta.Domain.Localization;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class AdminContentRepository(WastaDbContext db)
    : IAdminContentRepository, IAdminContentQueries
{
    // ---- tracks and sections ----

    public Task<Track?> FindTrackAsync(int trackId, CancellationToken ct = default) =>
        db.Tracks.FirstOrDefaultAsync(t => t.Id == trackId, ct);

    public Task<bool> TrackSlugExistsAsync(string slug, CancellationToken ct = default) =>
        db.Tracks.AnyAsync(t => t.Slug == slug, ct);

    public void AddTrack(Track track) => db.Tracks.Add(track);

    public Task<Section?> FindSectionAsync(int sectionId, CancellationToken ct = default) =>
        db.Sections.FirstOrDefaultAsync(s => s.Id == sectionId, ct);

    public async Task<IReadOnlyList<int>> SectionIdsForTrackAsync(
        int trackId, CancellationToken ct = default) =>
        await db.Sections.AsNoTracking()
            .Where(s => s.TrackId == trackId).Select(s => s.Id).ToListAsync(ct);

    public void AddSection(Section section) => db.Sections.Add(section);

    // ---- questions ----

    public Task<Question?> FindQuestionWithOptionsAsync(long questionId, CancellationToken ct = default) =>
        db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == questionId, ct);

    public void AddQuestion(Question question) => db.Questions.Add(question);

    public void ReplaceOptions(long questionId, IReadOnlyList<QuestionOption> options)
    {
        var existing = db.QuestionOptions.Local.Where(o => o.QuestionId == questionId).ToList();
        db.QuestionOptions.RemoveRange(
            existing.Count > 0 ? existing : db.QuestionOptions.Where(o => o.QuestionId == questionId));

        db.QuestionOptions.AddRange(options);
    }

    public async Task<IReadOnlyList<long>> ActiveQuestionIdsForTrackAsync(
        int trackId, CancellationToken ct = default) =>
        await db.Questions.AsNoTracking()
            .Where(q => q.TrackId == trackId && q.IsActive).Select(q => q.Id).ToListAsync(ct);

    /// <summary>
    /// Locked once a submitted attempt has been graded against it. In-progress
    /// attempts do not lock: nothing has been published from them yet, and a
    /// question found to be broken mid-window should still be fixable.
    /// </summary>
    public Task<bool> QuestionIsLockedAsync(long questionId, CancellationToken ct = default) =>
        (from answer in db.AttemptAnswers.AsNoTracking()
         join attempt in db.Attempts.AsNoTracking() on answer.AttemptId equals attempt.Id
         where answer.QuestionId == questionId && attempt.State == AttemptState.Submitted
         select answer.AttemptId)
        .AnyAsync(ct);

    // ---- forms ----

    public Task<AssessmentForm?> FindFormAsync(int formId, CancellationToken ct = default) =>
        db.AssessmentForms.FirstOrDefaultAsync(f => f.Id == formId, ct);

    public Task<bool> FormVersionExistsAsync(int trackId, int version, CancellationToken ct = default) =>
        db.AssessmentForms.AnyAsync(f => f.TrackId == trackId && f.Version == version, ct);

    public void AddForm(AssessmentForm form) => db.AssessmentForms.Add(form);

    public async Task<IReadOnlyList<long>> FormQuestionIdsAsync(int formId, CancellationToken ct = default) =>
        await db.AssessmentFormQuestions.AsNoTracking()
            .Where(fq => fq.FormId == formId)
            .OrderBy(fq => fq.DisplayOrder)
            .Select(fq => fq.QuestionId)
            .ToListAsync(ct);

    public async Task ReplaceFormQuestionsAsync(
        int formId, IReadOnlyList<long> questionIds, CancellationToken ct = default)
    {
        var existing = await db.AssessmentFormQuestions.Where(fq => fq.FormId == formId).ToListAsync(ct);
        db.AssessmentFormQuestions.RemoveRange(existing);

        db.AssessmentFormQuestions.AddRange(questionIds.Select((id, index) => new AssessmentFormQuestion
        {
            FormId = formId,
            QuestionId = id,
            DisplayOrder = (short)index,
        }));
    }

    /// <summary>
    /// Locked as soon as anyone opens an attempt on it, submitted or not.
    /// Changing composition mid-window would mean two candidates sat "version 1"
    /// and answered different questions.
    /// </summary>
    public Task<bool> FormIsLockedAsync(int formId, CancellationToken ct = default) =>
        db.Attempts.AsNoTracking().AnyAsync(a => a.FormId == formId, ct);

    public async Task DeactivateOtherFormsAsync(int trackId, int keepFormId, CancellationToken ct = default)
    {
        var others = await db.AssessmentForms
            .Where(f => f.TrackId == trackId && f.Id != keepFormId && f.IsActive)
            .ToListAsync(ct);

        foreach (var form in others)
        {
            form.IsActive = false;
        }
    }

    // ---- scoring ----

    public Task<ScoringRuleVersion?> FindScoringRuleAsync(int ruleVersionId, CancellationToken ct = default) =>
        db.ScoringRuleVersions.FirstOrDefaultAsync(r => r.Id == ruleVersionId, ct);

    public Task<bool> ScoringRuleVersionExistsAsync(
        int trackId, int version, CancellationToken ct = default) =>
        db.ScoringRuleVersions.AnyAsync(r => r.TrackId == trackId && r.Version == version, ct);

    public void AddScoringRule(ScoringRuleVersion rule) => db.ScoringRuleVersions.Add(rule);

    public async Task ReplaceBandsAsync(
        int ruleVersionId, IReadOnlyList<ScoreBand> bands, CancellationToken ct = default)
    {
        var existing = await db.ScoreBands.Where(b => b.RuleVersionId == ruleVersionId).ToListAsync(ct);
        db.ScoreBands.RemoveRange(existing);
        db.ScoreBands.AddRange(bands);
    }

    public async Task ReplaceWeightsAsync(
        int ruleVersionId, IReadOnlyDictionary<int, decimal> weights, CancellationToken ct = default)
    {
        var existing = await db.SectionWeights.Where(w => w.RuleVersionId == ruleVersionId).ToListAsync(ct);
        db.SectionWeights.RemoveRange(existing);

        db.SectionWeights.AddRange(weights.Select(pair => new SectionWeight
        {
            RuleVersionId = ruleVersionId,
            SectionId = pair.Key,
            Weight = pair.Value,
        }));
    }

    public async Task<IReadOnlyList<ScoreBand>> BandsForRuleAsync(
        int ruleVersionId, CancellationToken ct = default) =>
        await db.ScoreBands.AsNoTracking().Where(b => b.RuleVersionId == ruleVersionId).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<int, decimal>> WeightsForRuleAsync(
        int ruleVersionId, CancellationToken ct = default) =>
        await db.SectionWeights.AsNoTracking()
            .Where(w => w.RuleVersionId == ruleVersionId)
            .ToDictionaryAsync(w => w.SectionId, w => w.Weight, ct);

    /// <summary>Locked once a score has been computed with it - that score has to stay reproducible.</summary>
    public Task<bool> ScoringRuleIsLockedAsync(int ruleVersionId, CancellationToken ct = default) =>
        db.AttemptScores.AsNoTracking().AnyAsync(s => s.RuleVersionId == ruleVersionId, ct);

    public async Task DeactivateOtherScoringRulesAsync(
        int trackId, int keepRuleId, CancellationToken ct = default)
    {
        var others = await db.ScoringRuleVersions
            .Where(r => r.TrackId == trackId && r.Id != keepRuleId && r.IsActive)
            .ToListAsync(ct);

        foreach (var rule in others)
        {
            rule.IsActive = false;
        }
    }

    public Task<ScoreBand?> FindBandAsync(int bandId, CancellationToken ct = default) =>
        db.ScoreBands.FirstOrDefaultAsync(b => b.Id == bandId, ct);

    public async Task UpsertSectionFeedbackAsync(
        int sectionId, int bandId, string body, CancellationToken ct = default)
    {
        var existing = await db.SectionBandFeedback
            .FirstOrDefaultAsync(f => f.SectionId == sectionId && f.BandId == bandId, ct);

        if (existing is null)
        {
            db.SectionBandFeedback.Add(new SectionBandFeedback
            {
                SectionId = sectionId,
                BandId = bandId,
                Body = body,
            });
            return;
        }

        existing.Body = body;
    }

    // ---- translations ----

    public async Task UpsertTranslationAsync(
        string entityType, long entityId, Language language, string value, CancellationToken ct = default)
    {
        var existing = await db.LocalizedTexts.FirstOrDefaultAsync(
            t => t.EntityType == entityType
                 && t.EntityId == entityId
                 && t.Field == LocalizedEntities.NameField
                 && t.Language == language,
            ct);

        if (existing is not null)
        {
            db.LocalizedTexts.Remove(existing);
        }

        db.LocalizedTexts.Add(
            new LocalizedText(entityType, entityId, LocalizedEntities.NameField, language, value));
    }

    // ---- read models ----

    public async Task<PagedResult<AdminQuestionView>> ListQuestionsAsync(
        int trackId, PageRequest page, CancellationToken ct = default)
    {
        var query = db.Questions.AsNoTracking().Where(q => q.TrackId == trackId);
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(q => q.SectionId)
            .ThenBy(q => q.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(q => new
            {
                q.Id,
                q.TrackId,
                q.SectionId,
                SectionName = db.Sections.Where(s => s.Id == q.SectionId).Select(s => s.Name).First(),
                q.Body,
                q.Difficulty,
                q.IsActive,
                IsLocked = db.AttemptAnswers.Any(a =>
                    a.QuestionId == q.Id
                    && db.Attempts.Any(at => at.Id == a.AttemptId && at.State == AttemptState.Submitted)),
                Options = db.QuestionOptions
                    .Where(o => o.QuestionId == q.Id)
                    .OrderBy(o => o.DisplayOrder)
                    .Select(o => new AdminOptionView(o.Id, o.Body, o.IsCorrect, o.DisplayOrder))
                    .ToList(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            using var document = JsonDocument.Parse(r.Body);
            var root = document.RootElement;

            string? Read(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            return new AdminQuestionView(
                r.Id, r.TrackId, r.SectionId, r.SectionName,
                Read("prompt") ?? string.Empty, Read("code"),
                r.Difficulty, r.IsActive, r.IsLocked, r.Options);
        }).ToList();

        return new PagedResult<AdminQuestionView>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<AdminFormView>> ListFormsAsync(
        int trackId, CancellationToken ct = default) =>
        await db.AssessmentForms.AsNoTracking()
            .Where(f => f.TrackId == trackId)
            .OrderByDescending(f => f.Version)
            .Select(f => new AdminFormView(
                f.Id, f.TrackId, f.Version, f.QuestionCount, f.DurationSeconds, f.IsActive,
                db.AssessmentFormQuestions.Count(fq => fq.FormId == f.Id),
                db.Attempts.Any(a => a.FormId == f.Id)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AdminScoringRuleView>> ListScoringRulesAsync(
        int trackId, CancellationToken ct = default)
    {
        var rules = await db.ScoringRuleVersions.AsNoTracking()
            .Where(r => r.TrackId == trackId)
            .OrderByDescending(r => r.Version)
            .Select(r => new
            {
                r.Id,
                r.TrackId,
                r.Version,
                r.Notes,
                r.IsActive,
                IsLocked = db.AttemptScores.Any(s => s.RuleVersionId == r.Id),
            })
            .ToListAsync(ct);

        var views = new List<AdminScoringRuleView>(rules.Count);

        foreach (var rule in rules)
        {
            var bands = await db.ScoreBands.AsNoTracking()
                .Where(b => b.RuleVersionId == rule.Id)
                .OrderBy(b => b.MinPercent)
                .Select(b => new BandInput(b.Name, b.MinPercent, b.MaxPercent))
                .ToListAsync(ct);

            var weights = await db.SectionWeights.AsNoTracking()
                .Where(w => w.RuleVersionId == rule.Id)
                .ToDictionaryAsync(w => w.SectionId, w => w.Weight, ct);

            views.Add(new AdminScoringRuleView(
                rule.Id, rule.TrackId, rule.Version, rule.Notes, rule.IsActive, rule.IsLocked,
                bands, weights));
        }

        return views;
    }

    public async Task<IReadOnlyList<TrackReadiness>> ReadinessAsync(CancellationToken ct = default)
    {
        var tracks = await db.Tracks.AsNoTracking().OrderBy(t => t.DisplayOrder).ToListAsync(ct);
        var readiness = new List<TrackReadiness>(tracks.Count);

        foreach (var track in tracks)
        {
            var sections = await db.Sections.CountAsync(s => s.TrackId == track.Id, ct);
            var active = await db.Questions.CountAsync(q => q.TrackId == track.Id && q.IsActive, ct);

            // Placeholders are counted separately and on purpose. Content that
            // exists is not the same as content that is real, and a readiness
            // report that cannot tell the difference is worse than none.
            //
            // Raw SQL with an explicit ::text cast: body is jsonb, and Postgres
            // has no LIKE operator for jsonb, so the obvious .Contains() call
            // compiles and then throws at runtime.
            var marker = $"%{DatabaseSeeder.PlaceholderMarker}%";
            var placeholders = await db.Questions
                .FromSql(
                    $"SELECT * FROM question WHERE track_id = {track.Id} AND is_active AND body::text LIKE {marker}")
                .CountAsync(ct);

            var forms = await db.AssessmentForms.CountAsync(f => f.TrackId == track.Id, ct);
            var hasActiveForm = await db.AssessmentForms.AnyAsync(
                f => f.TrackId == track.Id && f.IsActive, ct);
            var hasActiveRule = await db.ScoringRuleVersions.AnyAsync(
                r => r.TrackId == track.Id && r.IsActive, ct);

            var blockers = new List<string>();

            if (sections == 0) blockers.Add("No sections defined.");
            if (active == 0) blockers.Add("No active questions.");
            if (!hasActiveForm) blockers.Add("No active assessment form.");
            if (!hasActiveRule) blockers.Add("No active scoring rules.");

            if (placeholders > 0)
            {
                blockers.Add(
                    $"{placeholders} of {active} active questions are seeded placeholders. "
                    + "Scores produced from them are meaningless.");
            }

            readiness.Add(new TrackReadiness(
                track.Id, track.Name, sections, active, placeholders, forms,
                hasActiveForm, hasActiveRule, blockers));
        }

        return readiness;
    }
}
