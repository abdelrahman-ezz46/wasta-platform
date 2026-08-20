using Wasta.Domain.Localization;

namespace Wasta.Application.Features.Localization;

/// <summary>
/// The language for the current request: the signed-in user's stored preference
/// if there is one, otherwise Accept-Language, otherwise English.
///
/// The stored preference wins because it is a deliberate choice, and a browser
/// on a shared machine is not.
/// </summary>
public interface ICurrentLanguage
{
    Language Value { get; }
}

/// <summary>
/// Resolves display names for reference rows. Reference data is small and
/// changes rarely, so a whole language is loaded and cached rather than joined
/// per row - which would otherwise be one join on every list endpoint.
/// </summary>
public interface ILocalizer
{
    Task<IReadOnlyDictionary<long, string>> NamesAsync(
        string entityType, Language language, CancellationToken ct = default);

    /// <summary>Falls back to the base name when no translation exists, never to an empty string.</summary>
    Task<string> NameAsync(
        string entityType, long entityId, string fallback, Language language, CancellationToken ct = default);

    void Invalidate(Language language);
}

public sealed record ReferenceItem(long Id, string Name);

public sealed record LocationItem(long Id, string City, string CountryCode);

public sealed record ReferenceData(
    string Language,
    IReadOnlyList<ReferenceItem> Tracks,
    IReadOnlyList<ReferenceItem> WorkTypes,
    IReadOnlyList<ReferenceItem> EmploymentTypes,
    IReadOnlyList<ReferenceItem> ApplicationStatuses,
    IReadOnlyList<LocationItem> Locations,
    IReadOnlyList<ReferenceItem> Industries,
    IReadOnlyList<ReferenceItem> Skills);

public interface IReferenceDataQueries
{
    Task<ReferenceData> GetAsync(Language language, CancellationToken ct = default);
}

public sealed record SetLanguageCommand(long UserId, string LanguageTag);
