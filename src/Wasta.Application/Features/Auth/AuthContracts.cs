namespace Wasta.Application.Features.Auth;

// Primitives only, deliberately: the web layer serialises these straight out,
// and an architecture test stops it referencing Domain, so no domain enum or
// entity may appear here.

public sealed record RegisterSeekerCommand(
    string FullName,
    string Email,
    string Password,
    string? PhoneNumber,
    int? TrackId);

public sealed record RegisterCompanyCommand(
    string CompanyName,
    string WorkEmail,
    string Password,
    string? Website,
    string? CompanySize,
    int? IndustryId);

public sealed record LoginCommand(string Email, string Password);

public sealed record RefreshCommand(string RefreshToken);

public sealed record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string Role,
    long UserId,
    long? SeekerId,
    long? CompanyId);
