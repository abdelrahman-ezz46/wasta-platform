using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Domain.Applications;
using Wasta.Domain.Companies;
using Wasta.Domain.Seekers;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class UploadRepository(WastaDbContext db) : IUploadRepository
{
    public Task<JobSeekerProfile?> FindProfileAsync(long seekerId, CancellationToken ct = default) =>
        db.JobSeekerProfiles.FirstOrDefaultAsync(p => p.JobSeekerId == seekerId, ct);

    public Task<int> CountSkillsAsync(long seekerId, CancellationToken ct = default) =>
        db.JobSeekerSkills.CountAsync(s => s.JobSeekerId == seekerId, ct);

    public Task<bool> SeekerHasTrackAsync(long seekerId, CancellationToken ct = default) =>
        db.JobSeekers.AnyAsync(s => s.Id == seekerId && s.TrackId != null, ct);

    public Task<JobApplication?> FindApplicationAsync(long applicationId, CancellationToken ct = default) =>
        db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);

    public Task<int> CountApplicationFilesAsync(long applicationId, CancellationToken ct = default) =>
        db.ApplicationFiles.CountAsync(f => f.ApplicationId == applicationId, ct);

    public void AddApplicationFile(ApplicationFile file) => db.ApplicationFiles.Add(file);

    public Task<Company?> FindCompanyAsync(long companyId, CancellationToken ct = default) =>
        db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);

    public void AddCompanyDocument(CompanyDocument document) => db.CompanyDocuments.Add(document);
}
