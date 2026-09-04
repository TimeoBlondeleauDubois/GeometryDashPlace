using System.Data;
using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeometryDashPlace.Web.Auth;

public sealed class GoogleUserSynchronizer(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory)
{
    public async Task<GoogleUser> SynchronizeAsync(
        string subject,
        string email,
        string displayName,
        string? avatarUrl,
        bool isEmailVerified,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await SynchronizeOnceAsync(
                    subject, email, displayName, avatarUrl,
                    isEmailVerified, cancellationToken);
            }
            catch (DbUpdateException exception)
                when (attempt == 0 && IsUniqueViolation(exception))
            {
            }
        }

        throw new InvalidOperationException("Unable to synchronize the Google user.");
    }

    private async Task<GoogleUser> SynchronizeOnceAsync(
        string subject,
        string email,
        string displayName,
        string? avatarUrl,
        bool isEmailVerified,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(
            candidate => candidate.GoogleSubject == subject,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (user is null)
        {
            user = new UserAccountEntity
            {
                Id = Guid.NewGuid(),
                GoogleSubject = subject,
                Email = email,
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
                IsEmailVerified = isEmailVerified,
                CreatedAt = now,
                LastLoginAt = now
            };
            context.Users.Add(user);
        }
        else
        {
            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = avatarUrl;
            user.IsEmailVerified = isEmailVerified;
            user.LastLoginAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new GoogleUser(user.Id, user.DisplayName, user.IsBanned);
    }

    private static bool IsUniqueViolation(Exception exception) =>
        exception is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } ||
        exception.InnerException is not null &&
        IsUniqueViolation(exception.InnerException);
}

public sealed record GoogleUser(Guid Id, string DisplayName, bool IsBanned);
