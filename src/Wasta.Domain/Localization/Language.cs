namespace Wasta.Domain.Localization;

public enum Language
{
    English = 1,
    Arabic = 2,
}

public static class Languages
{
    public const string EnglishCode = "en";
    public const string ArabicCode = "ar";

    public static Language Default => Language.English;

    public static string ToCode(this Language language) =>
        language == Language.Arabic ? ArabicCode : EnglishCode;

    /// <summary>
    /// Parses a language tag, accepting a full one like "ar-EG" by its primary
    /// subtag. Anything unrecognised falls back rather than failing - a browser
    /// sending something unexpected should still get a usable response.
    /// </summary>
    public static Language Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Default;
        }

        var primary = tag.Trim().Split('-')[0].ToLowerInvariant();

        return primary switch
        {
            ArabicCode => Language.Arabic,
            EnglishCode => Language.English,
            _ => Default,
        };
    }
}

/// <summary>
/// A translated string for one reference row.
///
/// Reference data is administered rather than deployed - an admin adds a track
/// or a city without a release - so its translations live beside it in the
/// database rather than in resource files that would need a rebuild.
/// </summary>
public class LocalizedText
{
    private LocalizedText() { }

    public LocalizedText(string entityType, long entityId, string field, Language language, string value)
    {
        EntityType = entityType;
        EntityId = entityId;
        Field = field;
        Language = language;
        Value = value;
    }

    public string EntityType { get; private set; } = null!;

    public long EntityId { get; private set; }

    /// <summary>Which property is translated. Only "name" today; the column exists so adding one is not a migration.</summary>
    public string Field { get; private set; } = null!;

    public Language Language { get; private set; }

    public string Value { get; private set; } = null!;
}

/// <summary>Entity types carrying translations. Constants, so a typo is a compile error rather than a silent miss.</summary>
public static class LocalizedEntities
{
    public const string Track = "track";
    public const string Section = "section";
    public const string ApplicationStatus = "application_status";
    public const string ScoreBand = "score_band";
    public const string WorkType = "work_type";
    public const string EmploymentType = "employment_type";
    public const string Location = "location";

    public const string NameField = "name";
}
