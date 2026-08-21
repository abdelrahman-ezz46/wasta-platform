using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Localization;
using Wasta.Domain.Assessments;
using Wasta.Domain.Localization;

namespace Wasta.Application.Features.Admin;

public class CreateFormHandler(IAdminContentRepository content, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<int>> HandleAsync(CreateFormCommand command, CancellationToken ct = default)
    {
        if (await content.FindTrackAsync(command.TrackId, ct) is null)
        {
            return ContentErrors.NotFound<int>("track");
        }

        if (await content.FormVersionExistsAsync(command.TrackId, command.Version, ct))
        {
            return Result.Failure<int>(
                "form.version_taken", "That version already exists for this track.");
        }

        if (command.QuestionCount <= 0 || command.DurationSeconds <= 0)
        {
            return Result.Failure<int>(
                "form.invalid", "A form needs a positive question count and duration.");
        }

        // Created inactive. A form with no questions attached must never be
        // reachable by a candidate.
        var form = new AssessmentForm
        {
            TrackId = command.TrackId,
            Version = command.Version,
            QuestionCount = command.QuestionCount,
            DurationSeconds = command.DurationSeconds,
            IsActive = false,
            CreatedAt = clock.UtcNow,
        };

        content.AddForm(form);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(form.Id);
    }
}

public class SetFormQuestionsHandler(IAdminContentRepository content, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(SetFormQuestionsCommand command, CancellationToken ct = default)
    {
        var form = await content.FindFormAsync(command.FormId, ct);
        if (form is null)
        {
            return ContentErrors.NotFound("form");
        }

        // Once anyone has sat this form, its composition is fixed. Changing it
        // would mean two candidates took "version 1" and answered different
        // questions.
        if (await content.FormIsLockedAsync(command.FormId, ct))
        {
            return ContentErrors.Locked("form");
        }

        var onTrack = await content.ActiveQuestionIdsForTrackAsync(form.TrackId, ct);
        var violation = ContentRules.ValidateFormComposition(
            form.QuestionCount, command.QuestionIds, onTrack);

        if (violation is not null)
        {
            return ContentErrors.From(violation.Value);
        }

        await content.ReplaceFormQuestionsAsync(command.FormId, command.QuestionIds, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class ActivateFormHandler(
    IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(int formId, long adminUserId, CancellationToken ct = default)
    {
        var form = await content.FindFormAsync(formId, ct);
        if (form is null)
        {
            return ContentErrors.NotFound("form");
        }

        // Re-validated at activation, not trusted from when the questions were
        // set: a question could have been retired in between.
        var assigned = await content.FormQuestionIdsAsync(formId, ct);
        var onTrack = await content.ActiveQuestionIdsForTrackAsync(form.TrackId, ct);

        var violation = ContentRules.ValidateFormComposition(form.QuestionCount, assigned, onTrack);
        if (violation is not null)
        {
            return ContentErrors.From(violation.Value);
        }

        form.IsActive = true;

        // Exactly one live form per track. Two would make which one a candidate
        // sits depend on ordering.
        await content.DeactivateOtherFormsAsync(form.TrackId, formId, ct);

        audit.Write(
            adminUserId, "content.form_activated", "assessment_form", formId.ToString(),
            new { trackId = form.TrackId, version = form.Version }, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class CreateScoringRuleHandler(IAdminContentRepository content, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<int>> HandleAsync(
        CreateScoringRuleCommand command, CancellationToken ct = default)
    {
        if (await content.FindTrackAsync(command.TrackId, ct) is null)
        {
            return ContentErrors.NotFound<int>("track");
        }

        if (await content.ScoringRuleVersionExistsAsync(command.TrackId, command.Version, ct))
        {
            return Result.Failure<int>(
                "scoring.version_taken", "That version already exists for this track.");
        }

        var rule = new ScoringRuleVersion
        {
            TrackId = command.TrackId,
            Version = command.Version,
            Notes = command.Notes,
            IsActive = false,
            CreatedAt = clock.UtcNow,
        };

        content.AddScoringRule(rule);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(rule.Id);
    }
}

public class SetBandsHandler(IAdminContentRepository content, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(SetBandsCommand command, CancellationToken ct = default)
    {
        var rule = await content.FindScoringRuleAsync(command.RuleVersionId, ct);
        if (rule is null)
        {
            return ContentErrors.NotFound("scoring rule");
        }

        if (await content.ScoringRuleIsLockedAsync(command.RuleVersionId, ct))
        {
            return ContentErrors.Locked("scoring rule");
        }

        var violation = ContentRules.ValidateBands(
            command.Bands.Select(b => (b.MinPercent, b.MaxPercent)).ToList());

        if (violation is not null)
        {
            return ContentErrors.From(violation.Value);
        }

        await content.ReplaceBandsAsync(
            command.RuleVersionId,
            command.Bands.Select(b => new ScoreBand
            {
                RuleVersionId = command.RuleVersionId,
                Name = b.Name.Trim(),
                MinPercent = b.MinPercent,
                MaxPercent = b.MaxPercent,
            }).ToList(),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class SetWeightsHandler(IAdminContentRepository content, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(SetWeightsCommand command, CancellationToken ct = default)
    {
        var rule = await content.FindScoringRuleAsync(command.RuleVersionId, ct);
        if (rule is null)
        {
            return ContentErrors.NotFound("scoring rule");
        }

        if (await content.ScoringRuleIsLockedAsync(command.RuleVersionId, ct))
        {
            return ContentErrors.Locked("scoring rule");
        }

        var sectionIds = await content.SectionIdsForTrackAsync(rule.TrackId, ct);
        var violation = ContentRules.ValidateWeights(command.Weights, sectionIds);

        if (violation is not null)
        {
            return ContentErrors.From(violation.Value);
        }

        await content.ReplaceWeightsAsync(command.RuleVersionId, command.Weights, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class SetSectionFeedbackHandler(IAdminContentRepository content, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(
        SetSectionFeedbackCommand command, CancellationToken ct = default)
    {
        var section = await content.FindSectionAsync(command.SectionId, ct);
        var band = await content.FindBandAsync(command.BandId, ct);

        if (section is null || band is null)
        {
            return ContentErrors.NotFound("section or band");
        }

        if (string.IsNullOrWhiteSpace(command.Body))
        {
            return Result.Failure("feedback.empty", "Feedback text cannot be blank.");
        }

        // Feedback is not locked with the rule version. It is prose shown to a
        // student, not an input to the score, so correcting a typo changes
        // nothing about what anyone was awarded.
        await content.UpsertSectionFeedbackAsync(
            command.SectionId, command.BandId, command.Body.Trim(), ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class ActivateScoringRuleHandler(
    IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(int ruleVersionId, long adminUserId, CancellationToken ct = default)
    {
        var rule = await content.FindScoringRuleAsync(ruleVersionId, ct);
        if (rule is null)
        {
            return ContentErrors.NotFound("scoring rule");
        }

        // Both are re-checked here rather than trusted from when they were set.
        // A section added to the track since then would leave the weights
        // incomplete, and the calculator would quietly renormalise around it.
        var bands = await content.BandsForRuleAsync(ruleVersionId, ct);
        var bandViolation = ContentRules.ValidateBands(
            bands.Select(b => (b.MinPercent, b.MaxPercent)).ToList());

        if (bandViolation is not null)
        {
            return ContentErrors.From(bandViolation.Value);
        }

        var weights = await content.WeightsForRuleAsync(ruleVersionId, ct);
        var sectionIds = await content.SectionIdsForTrackAsync(rule.TrackId, ct);
        var weightViolation = ContentRules.ValidateWeights(weights, sectionIds);

        if (weightViolation is not null)
        {
            return ContentErrors.From(weightViolation.Value);
        }

        rule.IsActive = true;
        await content.DeactivateOtherScoringRulesAsync(rule.TrackId, ruleVersionId, ct);

        audit.Write(
            adminUserId, "content.scoring_rule_activated", "scoring_rule_version", ruleVersionId.ToString(),
            new { trackId = rule.TrackId, version = rule.Version }, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class SetTranslationHandler(
    IAdminContentRepository content, ILocalizer localizer, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(SetTranslationCommand command, CancellationToken ct = default)
    {
        var known = new[]
        {
            LocalizedEntities.Track, LocalizedEntities.Section, LocalizedEntities.ApplicationStatus,
            LocalizedEntities.ScoreBand, LocalizedEntities.WorkType, LocalizedEntities.EmploymentType,
            LocalizedEntities.Location,
        };

        if (!known.Contains(command.EntityType))
        {
            return Result.Failure(
                "translation.entity_not_translatable",
                $"Translatable types are: {string.Join(", ", known)}.");
        }

        if (string.IsNullOrWhiteSpace(command.Value))
        {
            return Result.Failure("translation.empty", "A translation cannot be blank.");
        }

        var language = Languages.Parse(command.LanguageTag);

        await content.UpsertTranslationAsync(
            command.EntityType, command.EntityId, language, command.Value.Trim(), ct);

        await unitOfWork.SaveChangesAsync(ct);

        // The localizer caches a whole language. Without this the correction sits
        // unused until the process restarts.
        localizer.Invalidate(language);

        return Result.Success();
    }
}

public class TrackReadinessHandler(IAdminContentRepository content)
{
    public Task<IReadOnlyList<TrackReadiness>> HandleAsync(CancellationToken ct = default) =>
        content.ReadinessAsync(ct);
}
