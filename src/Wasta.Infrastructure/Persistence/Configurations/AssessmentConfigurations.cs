using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasta.Domain.Assessments;
using Wasta.Domain.Catalog;
using Wasta.Domain.Seekers;

namespace Wasta.Infrastructure.Persistence.Configurations;

public class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> b)
    {
        b.ToTable("section");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TrackId, x.Name }).IsUnique();
    }
}

public class AssessmentFormConfiguration : IEntityTypeConfiguration<AssessmentForm>
{
    public void Configure(EntityTypeBuilder<AssessmentForm> b)
    {
        b.ToTable("assessment_form");
        b.HasKey(x => x.Id);
        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TrackId, x.Version }).IsUnique();
    }
}

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.ToTable("question");
        b.HasKey(x => x.Id);

        // jsonb, not text: the prompt carries structure (markdown body plus an
        // optional code block and language) and admin tooling queries into it.
        b.Property(x => x.Body).HasColumnType("jsonb").IsRequired();

        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TrackId, x.SectionId });
        b.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionOptionConfiguration : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> b)
    {
        b.ToTable("question_option");
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).IsRequired();
        b.HasIndex(x => x.QuestionId);
    }
}

public class AssessmentFormQuestionConfiguration : IEntityTypeConfiguration<AssessmentFormQuestion>
{
    public void Configure(EntityTypeBuilder<AssessmentFormQuestion> b)
    {
        b.ToTable("assessment_form_question");
        b.HasKey(x => new { x.FormId, x.QuestionId });
        b.HasOne<AssessmentForm>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ScoringRuleVersionConfiguration : IEntityTypeConfiguration<ScoringRuleVersion>
{
    public void Configure(EntityTypeBuilder<ScoringRuleVersion> b)
    {
        b.ToTable("scoring_rule_version");
        b.HasKey(x => x.Id);
        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TrackId, x.Version }).IsUnique();
    }
}

public class SectionWeightConfiguration : IEntityTypeConfiguration<SectionWeight>
{
    public void Configure(EntityTypeBuilder<SectionWeight> b)
    {
        b.ToTable("section_weight");
        b.HasKey(x => new { x.RuleVersionId, x.SectionId });
        b.Property(x => x.Weight).HasPrecision(5, 4);
        b.HasOne<ScoringRuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ScoreBandConfiguration : IEntityTypeConfiguration<ScoreBand>
{
    public void Configure(EntityTypeBuilder<ScoreBand> b)
    {
        b.ToTable("score_band");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.HasOne<ScoringRuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SectionBandFeedbackConfiguration : IEntityTypeConfiguration<SectionBandFeedback>
{
    public void Configure(EntityTypeBuilder<SectionBandFeedback> b)
    {
        b.ToTable("section_band_feedback");
        b.HasKey(x => new { x.SectionId, x.BandId });
        b.Property(x => x.Body).IsRequired();
        b.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ScoreBand>().WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> b)
    {
        b.ToTable("attempt");
        b.HasKey(x => x.Id);
        b.Property(x => x.State).HasConversion<int>();
        b.HasOne<JobSeeker>().WithMany().HasForeignKey(x => x.JobSeekerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AssessmentForm>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Track>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Restrict);

        // Drives the retake check, which reads the seeker's most recent attempt
        // for a track on every start.
        b.HasIndex(x => new { x.JobSeekerId, x.TrackId, x.StartedAt });

        b.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AttemptAnswerConfiguration : IEntityTypeConfiguration<AttemptAnswer>
{
    public void Configure(EntityTypeBuilder<AttemptAnswer> b)
    {
        b.ToTable("attempt_answer");
        b.HasKey(x => new { x.AttemptId, x.QuestionId });
        b.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<QuestionOption>().WithMany().HasForeignKey(x => x.SelectedOptionId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class AttemptScoreConfiguration : IEntityTypeConfiguration<AttemptScore>
{
    public void Configure(EntityTypeBuilder<AttemptScore> b)
    {
        b.ToTable("attempt_score");
        b.HasKey(x => x.AttemptId);
        b.HasOne<Attempt>().WithOne().HasForeignKey<AttemptScore>(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ScoringRuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AttemptSectionScoreConfiguration : IEntityTypeConfiguration<AttemptSectionScore>
{
    public void Configure(EntityTypeBuilder<AttemptSectionScore> b)
    {
        b.ToTable("attempt_section_score");
        b.HasKey(x => new { x.AttemptId, x.SectionId });
        b.HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ScoreBand>().WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.SetNull);
    }
}
