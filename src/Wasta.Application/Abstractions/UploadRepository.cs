using Wasta.Domain.Applications;
using Wasta.Domain.Companies;
using Wasta.Domain.Seekers;

namespace Wasta.Application.Abstractions;

public interface IUploadRepository
{
    Task<JobSeekerProfile?> FindProfileAsync(long seekerId, CancellationToken ct = default);

    Task<int> CountSkillsAsync(long seekerId, CancellationToken ct = default);

    Task<bool> SeekerHasTrackAsync(long seekerId, CancellationToken ct = default);

    Task<JobApplication?> FindApplicationAsync(long applicationId, CancellationToken ct = default);

    Task<int> CountApplicationFilesAsync(long applicationId, CancellationToken ct = default);

    void AddApplicationFile(ApplicationFile file);

    Task<Company?> FindCompanyAsync(long companyId, CancellationToken ct = default);

    void AddCompanyDocument(CompanyDocument document);
}
