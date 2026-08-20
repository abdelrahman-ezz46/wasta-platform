using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasta.Domain.Applications;
using Wasta.Domain.Audit;
using Wasta.Domain.Catalog;
using Wasta.Domain.Companies;
using Wasta.Domain.Credits;
using Wasta.Domain.Identity;
using Wasta.Domain.Jobs;
using Wasta.Domain.Seekers;

namespace Wasta.Infrastructure.Persistence.Configurations;

public class JobPostConfiguration : IEntityTypeConfiguration<JobPost>
{
    public void Configure(EntityTypeBuilder<JobPost> b)
    {
        b.ToTable("job_post");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired();
        b.Property(x => x.JobDescription).IsRequired();
        b.Property(x => x.SalaryMin).HasPrecision(12, 2);
        b.Property(x => x.SalaryMax).HasPrecision(12, 2);
        b.Property(x => x.SalaryCurrency).HasMaxLength(3).IsFixedLength();

        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkType>().WithMany().HasForeignKey(x => x.WorkTypeId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<EmploymentType>().WithMany().HasForeignKey(x => x.EmploymentTypeId).OnDelete(DeleteBehavior.SetNull);

        // Partial indexes: the only queries that matter scan live postings, and
        // closed ones accumulate forever.
        b.HasIndex(x => x.CompanyId).HasFilter("is_active");
        b.HasIndex(x => x.TrackId).HasFilter("is_active");
    }
}

public class JobPostSkillConfiguration : IEntityTypeConfiguration<JobPostSkill>
{
    public void Configure(EntityTypeBuilder<JobPostSkill> b)
    {
        b.ToTable("job_post_skill");
        b.HasKey(x => new { x.JobPostId, x.SkillId });
        b.HasOne<JobPost>().WithMany().HasForeignKey(x => x.JobPostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class JobPostFileConfiguration : IEntityTypeConfiguration<JobPostFile>
{
    public void Configure(EntityTypeBuilder<JobPostFile> b)
    {
        b.ToTable("job_post_file");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileUrl).IsRequired();
        b.HasOne<JobPost>().WithMany().HasForeignKey(x => x.JobPostId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable("job_application");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(JobApplication.MaxDescriptionLength);

        b.HasOne<JobSeeker>().WithMany().HasForeignKey(x => x.JobSeekerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<JobPost>().WithMany().HasForeignKey(x => x.JobPostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationStatus>().WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);

        // Deliberately NOT unique: reapplying creates a second application.
        b.HasIndex(x => new { x.JobSeekerId, x.JobPostId });
        b.HasIndex(x => x.JobPostId);
    }
}

public class ApplicationFileConfiguration : IEntityTypeConfiguration<ApplicationFile>
{
    public void Configure(EntityTypeBuilder<ApplicationFile> b)
    {
        b.ToTable("application_file");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileUrl).IsRequired();
        b.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CreditLedgerEntryConfiguration : IEntityTypeConfiguration<CreditLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CreditLedgerEntry> b)
    {
        b.ToTable("credit_ledger_entry");
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasConversion<int>();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.CompanyId, x.CreatedAt });
    }
}

public class CreditTopUpRequestConfiguration : IEntityTypeConfiguration<CreditTopUpRequest>
{
    public void Configure(EntityTypeBuilder<CreditTopUpRequest> b)
    {
        b.ToTable("credit_topup_request");
        b.HasKey(x => x.Id);
        b.Property(x => x.State).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<CreditLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.State, x.CreatedAt });
    }
}

public class ProfileUnlockConfiguration : IEntityTypeConfiguration<ProfileUnlock>
{
    public void Configure(EntityTypeBuilder<ProfileUnlock> b)
    {
        b.ToTable("profile_unlock");
        b.HasKey(x => x.Id);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<JobSeeker>().WithMany().HasForeignKey(x => x.JobSeekerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CreditLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);

        // The database, not the handler, is what ultimately stops a company
        // being charged twice for the same candidate.
        b.HasIndex(x => new { x.CompanyId, x.JobSeekerId }).IsUnique();
        b.HasIndex(x => new { x.JobSeekerId, x.CreatedAt });
    }
}

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("audit_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).IsRequired();
        b.Property(x => x.EntityType).IsRequired();
        b.Property(x => x.EntityId).IsRequired();
        b.Property(x => x.Detail).HasColumnType("jsonb");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notification");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Channel).HasConversion<int>();
        b.Property(x => x.DeliveryState).HasConversion<int>();
        b.Property(x => x.LastError).HasMaxLength(500);

        // Drives the dispatcher's poll: pending rows only, oldest first.
        b.HasIndex(x => new { x.DeliveryState, x.CreatedAt });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasFilter("read_at IS NULL");
    }
}
