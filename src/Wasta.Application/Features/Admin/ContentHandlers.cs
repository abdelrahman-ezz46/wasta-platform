using System.Text.Json;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Localization;
using Wasta.Domain.Assessments;
using Wasta.Domain.Catalog;
using Wasta.Domain.Localization;

namespace Wasta.Application.Features.Admin;

internal static class ContentErrors
{
    public static Result<T> From<T>(RuleViolation violation) =>
        Result.Failure<T>(violation.Code, violation.Message);

    public static Result From(RuleViolation violation) =>
        Result.Failure(violation.Code, violation.Message);

    public static Result<T> NotFound<T>(string what) =>
        Result.Failure<T>("content.not_found", $"That {what} does not exist.");

    public static Result NotFound(string what) =>
        Result.Failure("content.not_found", $"That {what} does not exist.");

    /// <summary>
    /// Refused because a published score depends on this content. The remedy is
    /// always to create a new version rather than to edit the old one.
    /// </summary>
    public static Result Locked(string what) =>
        Result.Failure(
            "content.locked",
            $"That {what} has already been used to score an attempt and cannot be changed. "
            + "Create a new version instead.");
}

public class CreateTrackHandler(IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<int>> HandleAsync(
        CreateTrackCommand command, long adminUserId, CancellationToken ct = default)
    {
        var slug = command.Slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure<int>("track.invalid", "A track needs a name and a slug.");
        }

        if (await content.TrackSlugExistsAsync(slug, ct))
        {
            return Result.Failure<int>("track.slug_taken", "A track with that slug already exists.");
        }

        // Inactive on creation. A track with no questions and no scoring rules
        // would otherwise appear on the sign-up form the moment it is made.
        var track = new Track
        {
            Name = command.Name.Trim(),
            Slug = slug,
            DisplayOrder = command.DisplayOrder,
            IsActive = false,
        };

        content.AddTrack(track);
        await unitOfWork.SaveChangesAsync(ct);

        audit.Write(adminUserId, "content.track_created", "track", track.Id.ToString(), new { slug }, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(track.Id);
    }
}

public class UpdateTrackHandler(IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(
        UpdateTrackCommand command, long adminUserId, CancellationToken ct = default)
    {
        var track = await content.FindTrackAsync(command.TrackId, ct);
        if (track is null)
        {
            return ContentErrors.NotFound("track");
        }

        track.Name = command.Name.Trim();
        track.DisplayOrder = command.DisplayOrder;
        track.IsActive = command.IsActive;

        audit.Write(
            adminUserId, "content.track_updated", "track", track.Id.ToString(),
            new { isActive = command.IsActive }, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class CreateSectionHandler(IAdminContentRepository content, IUnitOfWork unitOfWork)
{
    public async Task<Result<int>> HandleAsync(CreateSectionCommand command, CancellationToken ct = default)
    {
        if (await content.FindTrackAsync(command.TrackId, ct) is null)
        {
            return ContentErrors.NotFound<int>("track");
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<int>("section.invalid", "A section needs a name.");
        }

        var section = new Section
        {
            TrackId = command.TrackId,
            Name = command.Name.Trim(),
            DisplayOrder = command.DisplayOrder,
        };

        content.AddSection(section);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(section.Id);
    }
}

public class CreateQuestionHandler(
    IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<long>> HandleAsync(
        CreateQuestionCommand command, long adminUserId, CancellationToken ct = default)
    {
        var section = await content.FindSectionAsync(command.SectionId, ct);
        if (section is null || section.TrackId != command.TrackId)
        {
            return ContentErrors.NotFound<long>("section");
        }

        if (string.IsNullOrWhiteSpace(command.Prompt))
        {
            return Result.Failure<long>("question.no_prompt", "A question needs a prompt.");
        }

        var violation = ContentRules.ValidateOptions(
            command.Options.Select(o => (o.Body, o.IsCorrect)).ToList());

        if (violation is not null)
        {
            return ContentErrors.From<long>(violation.Value);
        }

        var question = new Question
        {
            TrackId = command.TrackId,
            SectionId = command.SectionId,
            Body = JsonSerializer.Serialize(new
            {
                prompt = command.Prompt.Trim(),
                code = command.Code,
                language = command.CodeLanguage,
            }),
            Difficulty = command.Difficulty,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            Options = command.Options.Select(o => new QuestionOption
            {
                Body = o.Body.Trim(),
                IsCorrect = o.IsCorrect,
                DisplayOrder = o.DisplayOrder,
            }).ToList(),
        };

        content.AddQuestion(question);
        await unitOfWork.SaveChangesAsync(ct);

        audit.Write(
            adminUserId, "content.question_created", "question", question.Id.ToString(),
            new { trackId = command.TrackId, sectionId = command.SectionId }, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(question.Id);
    }
}

public class UpdateQuestionHandler(
    IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(
        UpdateQuestionCommand command, long adminUserId, CancellationToken ct = default)
    {
        var question = await content.FindQuestionWithOptionsAsync(command.QuestionId, ct);
        if (question is null)
        {
            return ContentErrors.NotFound("question");
        }

        // Frozen once a submitted attempt has been graded against it. Editing
        // would change what an already-published score meant.
        if (await content.QuestionIsLockedAsync(command.QuestionId, ct))
        {
            return ContentErrors.Locked("question");
        }

        var violation = ContentRules.ValidateOptions(
            command.Options.Select(o => (o.Body, o.IsCorrect)).ToList());

        if (violation is not null)
        {
            return ContentErrors.From(violation.Value);
        }

        question.Body = JsonSerializer.Serialize(new
        {
            prompt = command.Prompt.Trim(),
            code = command.Code,
            language = command.CodeLanguage,
        });
        question.Difficulty = command.Difficulty;

        content.ReplaceOptions(question.Id, command.Options.Select(o => new QuestionOption
        {
            QuestionId = question.Id,
            Body = o.Body.Trim(),
            IsCorrect = o.IsCorrect,
            DisplayOrder = o.DisplayOrder,
        }).ToList());

        audit.Write(
            adminUserId, "content.question_updated", "question", question.Id.ToString(), null, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class DeactivateQuestionHandler(
    IAdminContentRepository content, IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(long questionId, long adminUserId, CancellationToken ct = default)
    {
        var question = await content.FindQuestionWithOptionsAsync(questionId, ct);
        if (question is null)
        {
            return ContentErrors.NotFound("question");
        }

        // Retiring is always allowed, even when locked: it takes the question out
        // of future forms without touching what past attempts were scored on.
        question.IsActive = false;

        audit.Write(
            adminUserId, "content.question_retired", "question", questionId.ToString(), null, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
