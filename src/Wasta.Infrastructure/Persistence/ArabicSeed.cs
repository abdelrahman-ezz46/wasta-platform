using Microsoft.EntityFrameworkCore;
using Wasta.Domain.Localization;

namespace Wasta.Infrastructure.Persistence;

/// <summary>
/// Arabic names for the reference data.
///
/// Matched on the English name rather than on an id, because identity columns
/// assign different ids on different databases and a hard-coded id would
/// silently translate the wrong row. Idempotent: a translation that already
/// exists is left alone, so a corrected string is not overwritten on the next
/// boot.
/// </summary>
public static class ArabicSeed
{
    private static readonly Dictionary<string, string> Tracks = new()
    {
        ["Frontend Engineering"] = "هندسة الواجهات الأمامية",
        ["Backend Engineering"] = "هندسة الأنظمة الخلفية",
        ["Data Science"] = "علم البيانات",
        ["UI/UX Design"] = "تصميم واجهات وتجربة المستخدم",
        ["Product Management"] = "إدارة المنتجات",
        ["DevOps"] = "عمليات التطوير",
    };

    private static readonly Dictionary<string, string> Sections = new()
    {
        ["Fundamentals"] = "الأساسيات",
        ["Algorithms"] = "الخوارزميات",
        ["System Design"] = "تصميم الأنظمة",
        ["Frontend"] = "الواجهات الأمامية",
        ["Testing"] = "الاختبار",
        ["Databases"] = "قواعد البيانات",
        ["Python & Data Handling"] = "بايثون ومعالجة البيانات",
        ["Statistics & ML Fundamentals"] = "الإحصاء وأساسيات تعلّم الآلة",
        ["Applied Modelling"] = "النمذجة التطبيقية",
        ["SQL & Data Pipelines"] = "SQL وخطوط البيانات",
        ["Communication"] = "التواصل",
        ["Design Fundamentals"] = "أساسيات التصميم",
        ["Interaction Design"] = "تصميم التفاعل",
        ["Design Systems"] = "أنظمة التصميم",
        ["Research"] = "البحث",
        ["Accessibility"] = "إمكانية الوصول",
        ["Discovery"] = "الاستكشاف",
        ["Prioritisation"] = "تحديد الأولويات",
        ["Analytics"] = "التحليلات",
        ["Execution"] = "التنفيذ",
        ["Linux & Networking"] = "لينكس والشبكات",
        ["CI/CD"] = "التكامل والنشر المستمر",
        ["Infrastructure as Code"] = "البنية التحتية ككود",
        ["Observability"] = "المراقبة",
        ["Security"] = "الأمن",
    };

    private static readonly Dictionary<string, string> Statuses = new()
    {
        ["Applied"] = "تم التقديم",
        ["In review"] = "قيد المراجعة",
        ["Rejected"] = "مرفوض",
        ["Hired"] = "تم التوظيف",
        ["Withdrawn"] = "تم السحب",
    };

    private static readonly Dictionary<string, string> Bands = new()
    {
        ["Developing"] = "قيد التطوير",
        ["Competent"] = "كفء",
        ["Strong"] = "متميز",
    };

    private static readonly Dictionary<string, string> WorkTypes = new()
    {
        ["Remote"] = "عن بُعد",
        ["Hybrid"] = "هجين",
        ["On-site"] = "في الموقع",
    };

    private static readonly Dictionary<string, string> EmploymentTypes = new()
    {
        ["Full-time"] = "دوام كامل",
        ["Part-time"] = "دوام جزئي",
        ["Internship"] = "تدريب",
        ["Contract"] = "عقد",
    };

    private static readonly Dictionary<string, string> Cities = new()
    {
        ["Cairo"] = "القاهرة",
        ["Alexandria"] = "الإسكندرية",
        ["Dubai"] = "دبي",
        ["Amman"] = "عمّان",
        ["Riyadh"] = "الرياض",
    };

    public static async Task SeedAsync(WastaDbContext db, CancellationToken ct = default)
    {
        var existing = await db.LocalizedTexts
            .Where(t => t.Language == Language.Arabic)
            .Select(t => new { t.EntityType, t.EntityId })
            .ToListAsync(ct);

        var already = existing.Select(e => (e.EntityType, e.EntityId)).ToHashSet();

        void Add(string entityType, long id, string value)
        {
            if (already.Add((entityType, id)))
            {
                db.LocalizedTexts.Add(new LocalizedText(
                    entityType, id, LocalizedEntities.NameField, Language.Arabic, value));
            }
        }

        foreach (var row in await db.Tracks.AsNoTracking().ToListAsync(ct))
        {
            if (Tracks.TryGetValue(row.Name, out var arabic))
            {
                Add(LocalizedEntities.Track, row.Id, arabic);
            }
        }

        foreach (var row in await db.Sections.AsNoTracking().ToListAsync(ct))
        {
            if (Sections.TryGetValue(row.Name, out var arabic))
            {
                Add(LocalizedEntities.Section, row.Id, arabic);
            }
        }

        foreach (var row in await db.ApplicationStatuses.AsNoTracking().ToListAsync(ct))
        {
            if (Statuses.TryGetValue(row.Name, out var arabic))
            {
                Add(LocalizedEntities.ApplicationStatus, row.Id, arabic);
            }
        }

        foreach (var row in await db.ScoreBands.AsNoTracking().ToListAsync(ct))
        {
            if (Bands.TryGetValue(row.Name, out var arabic))
            {
                Add(LocalizedEntities.ScoreBand, row.Id, arabic);
            }
        }

        foreach (var row in await db.WorkTypes.AsNoTracking().ToListAsync(ct))
        {
            if (WorkTypes.TryGetValue(row.Name, out var arabic))
            {
                Add(LocalizedEntities.WorkType, row.Id, arabic);
            }
        }

        foreach (var row in await db.EmploymentTypes.AsNoTracking().ToListAsync(ct))
        {
            if (EmploymentTypes.TryGetValue(row.Name, out var arabic))
            {
                Add(LocalizedEntities.EmploymentType, row.Id, arabic);
            }
        }

        foreach (var row in await db.Locations.AsNoTracking().ToListAsync(ct))
        {
            if (Cities.TryGetValue(row.City, out var arabic))
            {
                Add(LocalizedEntities.Location, row.Id, arabic);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
