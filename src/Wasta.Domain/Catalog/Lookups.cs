using Wasta.Domain.Common;

namespace Wasta.Domain.Catalog;

/// <summary>
/// Reference data. These are administered, not user-generated, and every one of
/// them is small enough to cache. They live in tables rather than as C# enums
/// because an admin adds a track or a city without a deployment.
/// </summary>
public class Track : Entity<int>
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class Skill : Entity<int>
{
    public string Name { get; set; } = null!;
}

public class Industry : Entity<int>
{
    public string Name { get; set; } = null!;
}

public class Location : Entity<int>
{
    public string City { get; set; } = null!;

    /// <summary>ISO 3166-1 alpha-2. EG, AE, JO, SA.</summary>
    public string CountryCode { get; set; } = null!;
}

public class EmploymentType : Entity<int>
{
    public string Name { get; set; } = null!;
}

public class WorkType : Entity<int>
{
    public string Name { get; set; } = null!;
}

public class ApplicationStatus : Entity<int>
{
    public string Name { get; set; } = null!;

    /// <summary>Terminal states stop counting against the seeker's project cap.</summary>
    public bool IsTerminal { get; set; }
}

public class PaymentMethod : Entity<int>
{
    public string Name { get; set; } = null!;
}

/// <summary>Well-known ids, seeded. Referenced by rules that must not depend on row order.</summary>
public static class ApplicationStatuses
{
    public const int Applied = 1;
    public const int InReview = 2;
    public const int Rejected = 3;
    public const int Hired = 4;
    public const int Withdrawn = 5;
}
