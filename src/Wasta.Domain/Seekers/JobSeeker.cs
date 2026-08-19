using Wasta.Domain.Common;

namespace Wasta.Domain.Seekers;

public class JobSeeker : Entity<long>, ICreatedAt
{
    private JobSeeker() { }

    public JobSeeker(long userId, string fullName, int? trackId, string? phoneNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("seeker.name_required", "A full name is required.");
        }

        UserId = userId;
        FullName = fullName.Trim();
        TrackId = trackId;
        PhoneNumber = phoneNumber;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public long UserId { get; private set; }

    public string FullName { get; private set; } = null!;

    public string? PhoneNumber { get; private set; }

    public int? TrackId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public JobSeekerProfile? Profile { get; private set; }

    public void UpdateBasics(string fullName, string? phoneNumber, int? trackId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("seeker.name_required", "A full name is required.");
        }

        FullName = fullName.Trim();
        PhoneNumber = phoneNumber;
        TrackId = trackId;
        UpdatedAt = now;
    }
}
