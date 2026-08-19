using Wasta.Domain.Common;

namespace Wasta.Domain.Companies;

public enum VerificationState
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

public class Company : Entity<long>, ICreatedAt
{
    public const int MaxActiveJobPosts = 6;

    /// <summary>Granted once, on approval. Matches the "3 free trial credits" in the designs.</summary>
    public const int TrialCredits = 3;

    private Company() { }

    public Company(long userId, string name, string? website, string? companySize, int? industryId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("company.name_required", "A company name is required.");
        }

        UserId = userId;
        Name = name.Trim();
        NormalizedName = Normalize(name);
        Website = website;
        CompanySize = companySize;
        IndustryId = industryId;
        VerificationState = VerificationState.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public long UserId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Case- and whitespace-folded, uniquely indexed, so the same firm cannot register twice.</summary>
    public string NormalizedName { get; private set; } = null!;

    public string? Website { get; private set; }

    public string? CompanySize { get; private set; }

    public int? IndustryId { get; private set; }

    public VerificationState VerificationState { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public long? VerifiedByUserId { get; private set; }

    public string? RejectionNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Gate for everything except signing in and uploading documents. An
    /// unverified company must never reach the talent pool.
    /// </summary>
    public bool IsVerified => VerificationState == VerificationState.Approved;

    public static string Normalize(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public void Approve(long adminUserId, DateTimeOffset now)
    {
        if (VerificationState == VerificationState.Approved)
        {
            throw new DomainException("company.already_approved", "This company is already approved.");
        }

        VerificationState = VerificationState.Approved;
        VerifiedAt = now;
        VerifiedByUserId = adminUserId;
        RejectionNote = null;
        UpdatedAt = now;
    }

    public void Reject(long adminUserId, string note, DateTimeOffset now)
    {
        VerificationState = VerificationState.Rejected;
        VerifiedByUserId = adminUserId;
        RejectionNote = note;
        UpdatedAt = now;
    }
}

public enum CompanyDocumentType
{
    CommercialRegister = 1,
    TaxCard = 2,
    LinkedIn = 3,
    Other = 4,
}

public class CompanyDocument : Entity<long>, ICreatedAt
{
    private CompanyDocument() { }

    public CompanyDocument(long companyId, CompanyDocumentType documentType, string fileUrl, DateTimeOffset now)
    {
        CompanyId = companyId;
        DocumentType = documentType;
        FileUrl = fileUrl;
        CreatedAt = now;
    }

    public long CompanyId { get; private set; }
    public CompanyDocumentType DocumentType { get; private set; }
    public string FileUrl { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
