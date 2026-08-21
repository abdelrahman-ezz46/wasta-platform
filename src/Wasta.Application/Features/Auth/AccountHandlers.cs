using Microsoft.Extensions.Options;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Identity;

namespace Wasta.Application.Features.Auth;

public sealed class AccountLinkOptions
{
    public const string SectionName = "AccountLinks";

    /// <summary>Where the emailed links point. The API never renders these pages itself.</summary>
    public string BaseUrl { get; set; } = "https://app.wasta.example";

    public string VerifyEmailPath { get; set; } = "/verify-email";

    public string ResetPasswordPath { get; set; } = "/reset-password";
}

public sealed record ForgotPasswordCommand(string Email);

public sealed record ResetPasswordCommand(string Token, string NewPassword);

public sealed record ConfirmEmailCommand(string Token);

public class RequestEmailVerificationHandler(
    IUserAccountRepository users,
    IAccountTokenRepository tokens,
    ITokenService tokenService,
    INotificationSender sender,
    IUnitOfWork unitOfWork,
    IOptions<AccountLinkOptions> links,
    IClock clock)
{
    public async Task<Result> HandleAsync(long userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || !user.CanSignIn)
        {
            return Result.Failure("user.not_found", "That account does not exist.");
        }

        if (user.IsEmailVerified)
        {
            return Result.Failure("account.already_verified", "This email address is already confirmed.");
        }

        var now = clock.UtcNow;
        await tokens.InvalidateOutstandingAsync(user.Id, AccountTokenPurpose.EmailVerification, now, ct);

        var (raw, hash) = tokenService.CreateOpaqueToken();
        tokens.Add(new AccountToken(user.Id, AccountTokenPurpose.EmailVerification, hash, now));
        await unitOfWork.SaveChangesAsync(ct);

        var link = $"{links.Value.BaseUrl}{links.Value.VerifyEmailPath}?token={raw}";
        await sender.SendAsync(
            AccountEmails.Message(NotificationKinds.EmailVerification, user.Email, link, user.Language), ct);

        return Result.Success();
    }
}

public class ConfirmEmailHandler(
    IAccountTokenRepository tokens,
    IUserAccountRepository users,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(ConfirmEmailCommand command, CancellationToken ct = default)
    {
        var stored = await tokens.FindByHashAsync(tokenService.HashOpaqueToken(command.Token), ct);
        var now = clock.UtcNow;

        // Wrong, expired, already used and already invalidated all report the
        // same thing. Distinguishing them tells whoever is holding a stale link
        // whether it was ever real.
        if (stored is null
            || stored.Purpose != AccountTokenPurpose.EmailVerification
            || !stored.IsUsable(now))
        {
            return Result.Failure("token.invalid", "That link is not valid or has expired.");
        }

        var user = await users.FindByIdAsync(stored.UserId, ct);
        if (user is null || !user.CanSignIn)
        {
            return Result.Failure("token.invalid", "That link is not valid or has expired.");
        }

        stored.MarkUsed(now);
        user.MarkEmailVerified(now);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class ForgotPasswordHandler(
    IUserAccountRepository users,
    IAccountTokenRepository tokens,
    ITokenService tokenService,
    INotificationSender sender,
    IUnitOfWork unitOfWork,
    IOptions<AccountLinkOptions> links,
    IClock clock)
{
    /// <summary>
    /// Always succeeds, whether or not the address is registered.
    ///
    /// Reporting "no such account" here would turn this endpoint into a
    /// membership oracle - the same reason login returns one message for both
    /// a wrong password and an unknown address.
    /// </summary>
    public async Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken ct = default)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email, ct);

        if (user is null || !user.CanSignIn)
        {
            return Result.Success();
        }

        var now = clock.UtcNow;
        await tokens.InvalidateOutstandingAsync(user.Id, AccountTokenPurpose.PasswordReset, now, ct);

        var (raw, hash) = tokenService.CreateOpaqueToken();
        tokens.Add(new AccountToken(user.Id, AccountTokenPurpose.PasswordReset, hash, now));
        await unitOfWork.SaveChangesAsync(ct);

        var link = $"{links.Value.BaseUrl}{links.Value.ResetPasswordPath}?token={raw}";
        await sender.SendAsync(
            AccountEmails.Message(NotificationKinds.PasswordReset, user.Email, link, user.Language), ct);

        return Result.Success();
    }
}

public class ResetPasswordHandler(
    IAccountTokenRepository tokens,
    IUserAccountRepository users,
    ITokenService tokenService,
    IPasswordHasher hasher,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken ct = default)
    {
        var stored = await tokens.FindByHashAsync(tokenService.HashOpaqueToken(command.Token), ct);
        var now = clock.UtcNow;

        if (stored is null
            || stored.Purpose != AccountTokenPurpose.PasswordReset
            || !stored.IsUsable(now))
        {
            return Result.Failure("token.invalid", "That link is not valid or has expired.");
        }

        var user = await users.FindByIdAsync(stored.UserId, ct);
        if (user is null || !user.CanSignIn)
        {
            return Result.Failure("token.invalid", "That link is not valid or has expired.");
        }

        stored.MarkUsed(now);
        user.ChangePassword(hasher.Hash(command.NewPassword), now);

        // Every session ends. A reset is what someone does when they think
        // their account is compromised, so leaving the attacker's refresh token
        // alive would make the reset theatre.
        await tokens.RevokeAllRefreshTokensAsync(user.Id, now, ct);

        audit.Write(user.Id, "account.password_reset", "user_account", user.Id.ToString(), null, now);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record LogoutCommand(string? RefreshToken, bool AllSessions);

/// <summary>
/// Ends a session.
///
/// Without this a user has no way to revoke their own refresh token: signing
/// out on a shared machine would clear the browser and leave a credential valid
/// for another thirty days. The access token still lives out its fifteen
/// minutes - inherent to a stateless token, and the reason that lifetime is
/// short.
/// </summary>
public class LogoutHandler(
    IRefreshTokenRepository refreshTokens,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(long userId, LogoutCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        if (command.AllSessions)
        {
            await refreshTokens.RevokeAllForUserAsync(userId, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure(
                "auth.refresh_token_required",
                "Supply the refresh token to revoke, or ask for all sessions.");
        }

        var stored = await refreshTokens.FindByHashAsync(
            tokenService.HashOpaqueToken(command.RefreshToken), ct);

        // Someone else's token is silently a no-op rather than an error.
        // Telling a caller their guess was wrong is a probe result, and signing
        // out is not somewhere to hand one out.
        if (stored is null || stored.UserId != userId)
        {
            return Result.Success();
        }

        // The whole rotation family, not the single token. Revoking one link
        // leaves its successor alive, which is the opposite of signing out.
        await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
