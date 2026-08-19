using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasta.Domain.Catalog;
using Wasta.Domain.Companies;
using Wasta.Domain.Identity;
using Wasta.Domain.Seekers;

namespace Wasta.Infrastructure.Persistence.Configurations;

public class JobSeekerConfiguration : IEntityTypeConfiguration<JobSeeker>
{
    public void Configure(EntityTypeBuilder<JobSeeker> b)
    {
        b.ToTable("job_seeker");
        b.HasKey(x => x.Id);
        b.Property(x => x.FullName).IsRequired();

        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.TrackId);

        b.HasOne(x => x.Profile).WithOne().HasForeignKey<JobSeekerProfile>(x => x.JobSeekerId);
    }
}

public class JobSeekerProfileConfiguration : IEntityTypeConfiguration<JobSeekerProfile>
{
    public void Configure(EntityTypeBuilder<JobSeekerProfile> b)
    {
        b.ToTable("job_seeker_profile");
        b.HasKey(x => x.JobSeekerId);
        b.Property(x => x.Bio).HasMaxLength(JobSeekerProfile.MaxBioLength);
        b.HasOne<WorkType>().WithMany().HasForeignKey(x => x.PreferredWorkTypeId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class JobSeekerSkillConfiguration : IEntityTypeConfiguration<JobSeekerSkill>
{
    public void Configure(EntityTypeBuilder<JobSeekerSkill> b)
    {
        b.ToTable("job_seeker_skill");
        b.HasKey(x => new { x.JobSeekerId, x.SkillId });
        b.HasOne<JobSeeker>().WithMany().HasForeignKey(x => x.JobSeekerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("company");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.NormalizedName).IsRequired();
        b.Property(x => x.VerificationState).HasConversion<int>();

        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Industry>().WithMany().HasForeignKey(x => x.IndustryId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.NormalizedName).IsUnique();
    }
}

public class CompanyDocumentConfiguration : IEntityTypeConfiguration<CompanyDocument>
{
    public void Configure(EntityTypeBuilder<CompanyDocument> b)
    {
        b.ToTable("company_document");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileUrl).IsRequired();
        b.Property(x => x.DocumentType).HasConversion<int>();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.CompanyId);
    }
}
