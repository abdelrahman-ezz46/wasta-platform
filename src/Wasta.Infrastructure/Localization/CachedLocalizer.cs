using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Features.Localization;
using Wasta.Domain.Localization;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Infrastructure.Localization;

/// <summary>
/// Loads a language's translations once and keeps them.
///
/// The whole set is a few hundred short strings, so caching it costs almost
/// nothing and removes a join from every list endpoint. Invalidate() exists for
/// when an admin edits reference data - without it, a corrected translation
/// would sit unused until the process restarted.
/// </summary>
public sealed class CachedLocalizer(IServiceScopeFactory scopeFactory, IMemoryCache cache) : ILocalizer
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static string CacheKey(Language language) => $"localized-text:{language}";

    public async Task<IReadOnlyDictionary<long, string>> NamesAsync(
        string entityType, Language language, CancellationToken ct = default)
    {
        var all = await LoadAsync(language, ct);

        return all.TryGetValue(entityType, out var names)
            ? names
            : new Dictionary<long, string>();
    }

    public async Task<string> NameAsync(
        string entityType, long entityId, string fallback, Language language, CancellationToken ct = default)
    {
        var names = await NamesAsync(entityType, language, ct);

        // Falling back to the base name keeps a partially translated database
        // usable: an untranslated row shows in English rather than vanishing.
        return names.TryGetValue(entityId, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    public void Invalidate(Language language) => cache.Remove(CacheKey(language));

    private async Task<IReadOnlyDictionary<string, Dictionary<long, string>>> LoadAsync(
        Language language, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(language), out IReadOnlyDictionary<string, Dictionary<long, string>>? cached)
            && cached is not null)
        {
            return cached;
        }

        // A scope of its own: the localizer is a singleton, and capturing a
        // scoped DbContext in one is how a disposed-context bug gets shipped.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();

        var rows = await db.LocalizedTexts.AsNoTracking()
            .Where(t => t.Language == language && t.Field == LocalizedEntities.NameField)
            .Select(t => new { t.EntityType, t.EntityId, t.Value })
            .ToListAsync(ct);

        var map = rows
            .GroupBy(r => r.EntityType)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(r => r.EntityId, r => r.Value));

        cache.Set(CacheKey(language), (IReadOnlyDictionary<string, Dictionary<long, string>>)map, Lifetime);
        return map;
    }
}
