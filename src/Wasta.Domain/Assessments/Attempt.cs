using Wasta.Domain.Common;

namespace Wasta.Domain.Assessments;

public enum AttemptState
{
    InProgress = 1,
    Submitted = 2,
    Expired = 3,
    Abandoned = 4,
}

/// <summary>
/// One sitting. The clock is authoritative here, not on the client: expiry is
/// stored when the attempt opens and checked server-side on submit, so a paused
/// browser tab or a tampered timer cannot buy extra minutes.
/// </summary>
public class Attempt : Entity<long>
{
    /// <summary>Retakes are per track, once every 30 days.</summary>
    public static readonly TimeSpan RetakeCooldown = TimeSpan.FromDays(30);

    private Attempt() { }

    public Attempt(long jobSeekerId, int formId, int trackId, int durationSeconds, DateTimeOffset now)
    {
        JobSeekerId = jobSeekerId;
        FormId = formId;
        TrackId = trackId;
        State = AttemptState.InProgress;
        StartedAt = now;
        ExpiresAt = now.AddSeconds(durationSeconds);
    }

    public long JobSeekerId { get; private set; }
    public int FormId { get; private set; }
    public int TrackId { get; private set; }
    public AttemptState State { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }

    public List<AttemptAnswer> Answers { get; private set; } = [];

    public bool HasExpired(DateTimeOffset now) => now > ExpiresAt;

    public TimeSpan RemainingTime(DateTimeOffset now) =>
        ExpiresAt > now ? ExpiresAt - now : TimeSpan.Zero;

    public void Submit(DateTimeOffset now)
    {
        if (State != AttemptState.InProgress)
        {
            throw new DomainException("attempt.not_in_progress", "This attempt has already finished.");
        }

        if (HasExpired(now))
        {
            State = AttemptState.Expired;
            throw new DomainException("attempt.expired", "The time limit for this attempt has passed.");
        }

        State = AttemptState.Submitted;
        SubmittedAt = now;
    }

    public void MarkExpired() => State = AttemptState.Expired;

    /// <summary>When the seeker may next start this track, given their last attempt.</summary>
    public DateTimeOffset RetakeAvailableAt() => StartedAt.Add(RetakeCooldown);
}

public class AttemptAnswer
{
    public long AttemptId { get; set; }
    public long QuestionId { get; set; }
    public long? SelectedOptionId { get; set; }
    public bool FlaggedForReview { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
}

public class AttemptScore
{
    public long AttemptId { get; set; }
    public int RuleVersionId { get; set; }
    public short OverallPercent { get; set; }

    /// <summary>
    /// Null until the track's cohort is large enough to make a percentile mean
    /// anything. Showing "94th percentile" out of eleven attempts is a lie the
    /// results page would tell on our behalf.
    /// </summary>
    public short? Percentile { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

public class AttemptSectionScore
{
    public long AttemptId { get; set; }
    public int SectionId { get; set; }
    public short Percent { get; set; }
    public int? BandId { get; set; }
}
