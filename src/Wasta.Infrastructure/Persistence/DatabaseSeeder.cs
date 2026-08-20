using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wasta.Domain.Assessments;
using Wasta.Domain.Catalog;

namespace Wasta.Infrastructure.Persistence;

/// <summary>
/// Reference data plus a placeholder assessment.
///
/// Idempotent: safe to run on every boot and on an existing database. The
/// assessment content here is scaffolding, not a real instrument - real items
/// have to be authored by a subject-matter expert and validated before any
/// score is shown to an employer. It exists so the flow can be exercised end to
/// end while that work happens in parallel.
/// </summary>
public static class DatabaseSeeder
{
    public const string PlaceholderMarker = "[PLACEHOLDER]";

    public static async Task SeedAsync(WastaDbContext db, CancellationToken ct = default)
    {
        await SeedLookupsAsync(db, ct);
        await SeedTracksAndSectionsAsync(db, ct);
        await SeedPlaceholderAssessmentAsync(db, ct);
    }

    /// <summary>
    /// Creates the first admin, and only when both an address and a password are
    /// supplied by configuration. There is deliberately no default: a seeded
    /// admin with a known password is a backdoor that ships to production the
    /// first time someone forgets to override it.
    /// </summary>
    public static async Task SeedAdminAsync(
        WastaDbContext db,
        Wasta.Application.Abstractions.IPasswordHasher hasher,
        string? email,
        string? password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var normalised = email.Trim().ToLowerInvariant();
        if (await db.UserAccounts.AnyAsync(u => u.Email == normalised, ct))
        {
            return;
        }

        var admin = new Domain.Identity.UserAccount(
            normalised, hasher.Hash(password), Domain.Identity.UserRole.Admin);

        admin.MarkEmailVerified(DateTimeOffset.UtcNow);
        db.UserAccounts.Add(admin);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLookupsAsync(WastaDbContext db, CancellationToken ct)
    {
        if (!await db.ApplicationStatuses.AnyAsync(ct))
        {
            // Ids are pinned because Domain.Catalog.ApplicationStatuses refers to
            // them by constant; letting identity assign them would break that.
            db.ApplicationStatuses.AddRange(
                new ApplicationStatus { Name = "Applied" },
                new ApplicationStatus { Name = "In review" },
                new ApplicationStatus { Name = "Rejected", IsTerminal = true },
                new ApplicationStatus { Name = "Hired", IsTerminal = true },
                new ApplicationStatus { Name = "Withdrawn", IsTerminal = true });
        }

        if (!await db.PaymentMethods.AnyAsync(ct))
        {
            // Bank transfer only in v1. No card processing anywhere in the system.
            db.PaymentMethods.Add(new PaymentMethod { Name = "Bank transfer" });
        }

        if (!await db.WorkTypes.AnyAsync(ct))
        {
            db.WorkTypes.AddRange(
                new WorkType { Name = "Remote" },
                new WorkType { Name = "Hybrid" },
                new WorkType { Name = "On-site" });
        }

        if (!await db.EmploymentTypes.AnyAsync(ct))
        {
            db.EmploymentTypes.AddRange(
                new EmploymentType { Name = "Full-time" },
                new EmploymentType { Name = "Part-time" },
                new EmploymentType { Name = "Internship" },
                new EmploymentType { Name = "Contract" });
        }

        if (!await db.Locations.AnyAsync(ct))
        {
            db.Locations.AddRange(
                new Location { City = "Cairo", CountryCode = "EG" },
                new Location { City = "Alexandria", CountryCode = "EG" },
                new Location { City = "Dubai", CountryCode = "AE" },
                new Location { City = "Amman", CountryCode = "JO" },
                new Location { City = "Riyadh", CountryCode = "SA" });
        }

        if (!await db.Industries.AnyAsync(ct))
        {
            db.Industries.AddRange(
                new Industry { Name = "Software" },
                new Industry { Name = "Financial services" },
                new Industry { Name = "Telecommunications" },
                new Industry { Name = "Healthcare" },
                new Industry { Name = "Education" },
                new Industry { Name = "Retail" });
        }

        if (!await db.Skills.AnyAsync(ct))
        {
            string[] skills =
            [
                "React", "TypeScript", "Node.js", "Python", "SQL", "Machine Learning",
                "Product Strategy", "Figma", "AWS", "Docker", "GraphQL", "Tailwind",
                "Kubernetes", "Terraform", "C#", "Java", "Go", "Data Modelling",
            ];

            db.Skills.AddRange(skills.Select(s => new Skill { Name = s }));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedTracksAndSectionsAsync(WastaDbContext db, CancellationToken ct)
    {
        if (await db.Tracks.AnyAsync(ct))
        {
            return;
        }

        // The six tracks named in the designs.
        (string Name, string Slug, string[] Sections)[] tracks =
        [
            ("Frontend Engineering", "frontend-engineering",
                ["Fundamentals", "Algorithms", "System Design", "Frontend", "Testing"]),
            ("Backend Engineering", "backend-engineering",
                ["Fundamentals", "Algorithms", "System Design", "Databases", "Testing"]),
            ("Data Science", "data-science",
                ["Python & Data Handling", "Statistics & ML Fundamentals", "Applied Modelling",
                 "SQL & Data Pipelines", "Communication"]),
            ("UI/UX Design", "ui-ux-design",
                ["Design Fundamentals", "Interaction Design", "Design Systems", "Research", "Accessibility"]),
            ("Product Management", "product-management",
                ["Discovery", "Prioritisation", "Analytics", "Execution", "Communication"]),
            ("DevOps", "devops",
                ["Linux & Networking", "CI/CD", "Infrastructure as Code", "Observability", "Security"]),
        ];

        var order = 0;
        foreach (var (name, slug, sections) in tracks)
        {
            var track = new Track { Name = name, Slug = slug, IsActive = true, DisplayOrder = order++ };
            db.Tracks.Add(track);
            await db.SaveChangesAsync(ct);

            var sectionOrder = 0;
            foreach (var sectionName in sections)
            {
                db.Sections.Add(new Section
                {
                    TrackId = track.Id,
                    Name = sectionName,
                    DisplayOrder = sectionOrder++,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPlaceholderAssessmentAsync(WastaDbContext db, CancellationToken ct)
    {
        if (await db.AssessmentForms.AnyAsync(ct))
        {
            return;
        }

        foreach (var track in await db.Tracks.OrderBy(t => t.DisplayOrder).ToListAsync(ct))
        {
            var sections = await db.Sections
                .Where(s => s.TrackId == track.Id)
                .OrderBy(s => s.DisplayOrder)
                .ToListAsync(ct);

            if (sections.Count == 0)
            {
                continue;
            }

            var rules = new ScoringRuleVersion
            {
                TrackId = track.Id,
                Version = 1,
                Notes = $"{PlaceholderMarker} Equal weighting. Replace once a psychometrician sets the rubric.",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ScoringRuleVersions.Add(rules);
            await db.SaveChangesAsync(ct);

            var weight = Math.Round(1m / sections.Count, 4);
            db.SectionWeights.AddRange(sections.Select(s => new SectionWeight
            {
                RuleVersionId = rules.Id,
                SectionId = s.Id,
                Weight = weight,
            }));

            // Three bands. Thresholds are placeholders - real cut-points come out
            // of a validity study, not out of round numbers.
            db.ScoreBands.AddRange(
                new ScoreBand { RuleVersionId = rules.Id, Name = "Developing", MinPercent = 0, MaxPercent = 59 },
                new ScoreBand { RuleVersionId = rules.Id, Name = "Competent", MinPercent = 60, MaxPercent = 79 },
                new ScoreBand { RuleVersionId = rules.Id, Name = "Strong", MinPercent = 80, MaxPercent = 100 });
            await db.SaveChangesAsync(ct);

            foreach (var band in await db.ScoreBands.Where(b => b.RuleVersionId == rules.Id).ToListAsync(ct))
            {
                foreach (var section in sections)
                {
                    db.SectionBandFeedback.Add(new SectionBandFeedback
                    {
                        SectionId = section.Id,
                        BandId = band.Id,
                        Body = $"{PlaceholderMarker} {band.Name} in {section.Name}. "
                             + "Replace with real written feedback before launch.",
                    });
                }
            }

            // One placeholder question per section, so a full attempt exercises
            // every section of the score. Real forms are 30 questions.
            var form = new AssessmentForm
            {
                TrackId = track.Id,
                Version = 1,
                QuestionCount = (short)sections.Count,
                DurationSeconds = 2700,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AssessmentForms.Add(form);
            await db.SaveChangesAsync(ct);

            short displayOrder = 0;
            foreach (var section in sections)
            {
                var body = JsonSerializer.Serialize(new
                {
                    prompt = $"{PlaceholderMarker} Sample question for {section.Name}. "
                           + "Which option is marked correct?",
                    code = (string?)null,
                    language = (string?)null,
                });

                var question = new Question
                {
                    TrackId = track.Id,
                    SectionId = section.Id,
                    Body = body,
                    Difficulty = 3,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Questions.Add(question);
                await db.SaveChangesAsync(ct);

                db.QuestionOptions.AddRange(
                    new QuestionOption { QuestionId = question.Id, Body = "Correct option", IsCorrect = true, DisplayOrder = 0 },
                    new QuestionOption { QuestionId = question.Id, Body = "Wrong option A", IsCorrect = false, DisplayOrder = 1 },
                    new QuestionOption { QuestionId = question.Id, Body = "Wrong option B", IsCorrect = false, DisplayOrder = 2 },
                    new QuestionOption { QuestionId = question.Id, Body = "Wrong option C", IsCorrect = false, DisplayOrder = 3 });

                db.AssessmentFormQuestions.Add(new AssessmentFormQuestion
                {
                    FormId = form.Id,
                    QuestionId = question.Id,
                    DisplayOrder = displayOrder++,
                });

                await db.SaveChangesAsync(ct);
            }
        }
    }
}
