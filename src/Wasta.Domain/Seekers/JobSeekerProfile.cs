using Wasta.Domain.Common;

namespace Wasta.Domain.Seekers;

public class JobSeekerProfile
{
    public const int MaxSkills = 12;
    public const int MaxBioLength = 500;

    private JobSeekerProfile() { }

    public JobSeekerProfile(long jobSeekerId) => JobSeekerId = jobSeekerId;

    public long JobSeekerId { get; private set; }

    public string? Bio { get; private set; }

    public string? University { get; private set; }

    public short? GraduationYear { get; private set; }

    public string? Availability { get; private set; }

    public int? PreferredWorkTypeId { get; private set; }

    public string? CvUrl { get; private set; }

    public DateTimeOffset? CvUploadedAt { get; private set; }

    /// <summary>Opt-out of the talent pool. An invisible seeker is never unlockable.</summary>
    public bool VisibleToCompanies { get; private set; } = true;

    /// <summary>Completeness, not ability. Deliberately unrelated to the Wasta Score.</summary>
    public short ProfileStrength { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string? bio,
        string? university,
        short? graduationYear,
        string? availability,
        int? preferredWorkTypeId,
        DateTimeOffset now)
    {
        if (bio is { Length: > MaxBioLength })
        {
            throw new DomainException("profile.bio_too_long", $"Bio must be {MaxBioLength} characters or fewer.");
        }

        if (graduationYear is < 1950 or > 2100)
        {
            throw new DomainException("profile.graduation_year_invalid", "Graduation year is out of range.");
        }

        Bio = bio;
        University = university;
        GraduationYear = graduationYear;
        Availability = availability;
        PreferredWorkTypeId = preferredWorkTypeId;
        UpdatedAt = now;
    }

    public void SetCv(string url, DateTimeOffset now)
    {
        CvUrl = url;
        CvUploadedAt = now;
        UpdatedAt = now;
    }

    public void SetVisibility(bool visible, DateTimeOffset now)
    {
        VisibleToCompanies = visible;
        UpdatedAt = now;
    }

    /// <summary>
    /// Recomputed on every profile write rather than stored by the client, so the
    /// number always reflects what is actually filled in.
    /// </summary>
    public void RecomputeStrength(int skillCount, bool hasTrack)
    {
        var filled = 0;
        if (!string.IsNullOrWhiteSpace(Bio)) filled++;
        if (!string.IsNullOrWhiteSpace(University)) filled++;
        if (GraduationYear is not null) filled++;
        if (!string.IsNullOrWhiteSpace(Availability)) filled++;
        if (PreferredWorkTypeId is not null) filled++;
        if (!string.IsNullOrWhiteSpace(CvUrl)) filled++;
        if (skillCount > 0) filled++;
        if (hasTrack) filled++;

        ProfileStrength = (short)Math.Round(filled * 100.0 / 8);
    }
}

public class JobSeekerSkill
{
    public long JobSeekerId { get; set; }
    public int SkillId { get; set; }
}
