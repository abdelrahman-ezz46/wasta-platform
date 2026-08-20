using Microsoft.EntityFrameworkCore;
using Wasta.Application.Features.Localization;
using Wasta.Domain.Localization;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Infrastructure.Localization;

/// <summary>
/// Every lookup list a client needs to render, already translated.
///
/// One endpoint rather than seven, because a client rendering a form needs all
/// of them at once and seven round trips to fill a set of dropdowns is seven
/// chances to render half a form.
/// </summary>
public sealed class ReferenceDataQueries(WastaDbContext db, ILocalizer localizer) : IReferenceDataQueries
{
    public async Task<ReferenceData> GetAsync(Language language, CancellationToken ct = default)
    {
        var tracks = await db.Tracks.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder).Select(t => new { t.Id, t.Name }).ToListAsync(ct);
        var workTypes = await db.WorkTypes.AsNoTracking()
            .OrderBy(w => w.Id).Select(w => new { w.Id, w.Name }).ToListAsync(ct);
        var employmentTypes = await db.EmploymentTypes.AsNoTracking()
            .OrderBy(e => e.Id).Select(e => new { e.Id, e.Name }).ToListAsync(ct);
        var statuses = await db.ApplicationStatuses.AsNoTracking()
            .OrderBy(s => s.Id).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking()
            .OrderBy(l => l.Id).Select(l => new { l.Id, l.City, l.CountryCode }).ToListAsync(ct);
        var industries = await db.Industries.AsNoTracking()
            .OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync(ct);

        // Skills are proper nouns - React, TypeScript, Docker - so they are not
        // translated. Transliterating them would make them harder to recognise,
        // not easier.
        var skills = await db.Skills.AsNoTracking()
            .OrderBy(s => s.Name).Select(s => new ReferenceItem(s.Id, s.Name)).ToListAsync(ct);

        var trackNames = await localizer.NamesAsync(LocalizedEntities.Track, language, ct);
        var workTypeNames = await localizer.NamesAsync(LocalizedEntities.WorkType, language, ct);
        var employmentNames = await localizer.NamesAsync(LocalizedEntities.EmploymentType, language, ct);
        var statusNames = await localizer.NamesAsync(LocalizedEntities.ApplicationStatus, language, ct);
        var cityNames = await localizer.NamesAsync(LocalizedEntities.Location, language, ct);

        static string Localised(IReadOnlyDictionary<long, string> names, long id, string fallback) =>
            names.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

        return new ReferenceData(
            language.ToCode(),
            tracks.Select(t => new ReferenceItem(t.Id, Localised(trackNames, t.Id, t.Name))).ToList(),
            workTypes.Select(w => new ReferenceItem(w.Id, Localised(workTypeNames, w.Id, w.Name))).ToList(),
            employmentTypes
                .Select(e => new ReferenceItem(e.Id, Localised(employmentNames, e.Id, e.Name))).ToList(),
            statuses.Select(s => new ReferenceItem(s.Id, Localised(statusNames, s.Id, s.Name))).ToList(),
            locations
                .Select(l => new LocationItem(l.Id, Localised(cityNames, l.Id, l.City), l.CountryCode.Trim()))
                .ToList(),
            industries.Select(i => new ReferenceItem(i.Id, i.Name)).ToList(),
            skills);
    }
}
