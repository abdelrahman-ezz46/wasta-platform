using Microsoft.EntityFrameworkCore;
using Wasta.Domain.Applications;
using Wasta.Domain.Assessments;
using Wasta.Domain.Audit;
using Wasta.Domain.Catalog;
using Wasta.Domain.Companies;
using Wasta.Domain.Credits;
using Wasta.Domain.Identity;
using Wasta.Domain.Jobs;
using Wasta.Domain.Localization;
using Wasta.Domain.Seekers;

namespace Wasta.Infrastructure.Persistence;

/// <summary>
/// The platform's write model. Table and column names are snake_case to match
/// the reviewed SQL schema, so a DBA reading the database and a developer
/// reading the code are looking at the same names.
/// </summary>
public class WastaDbContext(DbContextOptions<WastaDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Industry> Industries => Set<Industry>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EmploymentType> EmploymentTypes => Set<EmploymentType>();
    public DbSet<WorkType> WorkTypes => Set<WorkType>();
    public DbSet<ApplicationStatus> ApplicationStatuses => Set<ApplicationStatus>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    public DbSet<JobSeeker> JobSeekers => Set<JobSeeker>();
    public DbSet<JobSeekerProfile> JobSeekerProfiles => Set<JobSeekerProfile>();
    public DbSet<JobSeekerSkill> JobSeekerSkills => Set<JobSeekerSkill>();

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyDocument> CompanyDocuments => Set<CompanyDocument>();

    public DbSet<Section> Sections => Set<Section>();
    public DbSet<AssessmentForm> AssessmentForms => Set<AssessmentForm>();
    public DbSet<AssessmentFormQuestion> AssessmentFormQuestions => Set<AssessmentFormQuestion>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<ScoringRuleVersion> ScoringRuleVersions => Set<ScoringRuleVersion>();
    public DbSet<SectionWeight> SectionWeights => Set<SectionWeight>();
    public DbSet<ScoreBand> ScoreBands => Set<ScoreBand>();
    public DbSet<SectionBandFeedback> SectionBandFeedback => Set<SectionBandFeedback>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<AttemptScore> AttemptScores => Set<AttemptScore>();
    public DbSet<AttemptSectionScore> AttemptSectionScores => Set<AttemptSectionScore>();

    public DbSet<JobPost> JobPosts => Set<JobPost>();
    public DbSet<JobPostSkill> JobPostSkills => Set<JobPostSkill>();
    public DbSet<JobPostFile> JobPostFiles => Set<JobPostFile>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationFile> ApplicationFiles => Set<ApplicationFile>();

    public DbSet<CreditLedgerEntry> CreditLedgerEntries => Set<CreditLedgerEntry>();
    public DbSet<CreditTopUpRequest> CreditTopUpRequests => Set<CreditTopUpRequest>();
    public DbSet<ProfileUnlock> ProfileUnlocks => Set<ProfileUnlock>();

    public DbSet<LocalizedText> LocalizedTexts => Set<LocalizedText>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WastaDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
