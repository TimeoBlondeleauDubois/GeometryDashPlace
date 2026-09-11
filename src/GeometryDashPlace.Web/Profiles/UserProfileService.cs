using System.Buffers.Binary;
using GeometryDashPlace.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeometryDashPlace.Web.Profiles;

public enum ProfileAvatarChoice
{
    Google,
    Default,
    Upload
}

public sealed record ProfileAvatarUpload(byte[] Content);

public sealed record UserProfile(
    Guid UserId,
    string GoogleDisplayName,
    string? Username,
    string? AvatarUrl,
    string? GoogleAvatarUrl,
    bool HasUploadedAvatar,
    bool IsCompleted);

public sealed record ProfileSaveResult(
    bool Succeeded,
    string? Error = null,
    UserProfile? Profile = null);

public interface IUserProfileService
{
    Task<UserProfile?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ProfileSaveResult> SaveAsync(
        Guid userId,
        string username,
        ProfileAvatarChoice avatarChoice,
        ProfileAvatarUpload? avatarUpload,
        CancellationToken cancellationToken = default);
}

public sealed class UserProfileService(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory) : IUserProfileService
{
    public async Task<UserProfile?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserProfile(
                user.Id,
                user.DisplayName,
                user.Username,
                user.AvatarUrl,
                user.GoogleAvatarUrl,
                user.AvatarPng != null,
                user.IsProfileCompleted))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ProfileSaveResult> SaveAsync(
        Guid userId,
        string username,
        ProfileAvatarChoice avatarChoice,
        ProfileAvatarUpload? avatarUpload,
        CancellationToken cancellationToken = default)
    {
        var usernameError = UserProfileRules.ValidateUsername(username);
        if (usernameError is not null)
        {
            return new ProfileSaveResult(false, usernameError);
        }

        var cleanedUsername = username.Trim();
        var normalizedUsername = UserProfileRules.NormalizeUsername(cleanedUsername);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);
        if (user is null)
        {
            return new ProfileSaveResult(false, "Your Google account could not be found.");
        }

        var avatarResult = UserProfileRules.ResolveAvatar(
            avatarChoice,
            user.GoogleAvatarUrl,
            user.AvatarUrl,
            user.AvatarPng,
            avatarUpload);
        if (avatarResult.Error is not null)
        {
            return new ProfileSaveResult(false, avatarResult.Error);
        }

        var isTaken = await context.Users.AsNoTracking().AnyAsync(
            candidate => candidate.Id != userId &&
                         candidate.NormalizedUsername == normalizedUsername,
            cancellationToken);
        if (isTaken)
        {
            return new ProfileSaveResult(false, "This username is already taken.");
        }

        user.Username = cleanedUsername;
        user.NormalizedUsername = normalizedUsername;
        user.AvatarUrl = avatarChoice == ProfileAvatarChoice.Upload && avatarUpload is not null
            ? $"/avatars/{user.Id:N}.png?v={Guid.NewGuid():N}"
            : avatarResult.AvatarUrl;
        user.AvatarPng = avatarResult.AvatarPng;
        user.IsProfileCompleted = true;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new ProfileSaveResult(false, "This username is already taken.");
        }

        return new ProfileSaveResult(
            true,
            Profile: new UserProfile(
                user.Id,
                user.DisplayName,
                user.Username,
                user.AvatarUrl,
                user.GoogleAvatarUrl,
                user.AvatarPng is not null,
                user.IsProfileCompleted));
    }

    private static bool IsUniqueViolation(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException is not null && IsUniqueViolation(exception.InnerException);
}

public static class UserProfileRules
{
    public const int MinimumUsernameLength = 3;
    public const int MaximumUsernameLength = 20;
    public const int MaximumAvatarBytes = 1024 * 1024;
    public const int MaximumAvatarDimension = 1024;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static string? ValidateUsername(string? username)
    {
        var value = username?.Trim() ?? string.Empty;
        if (value.Length is < MinimumUsernameLength or > MaximumUsernameLength)
        {
            return $"Your username must contain between {MinimumUsernameLength} and {MaximumUsernameLength} characters.";
        }

        if (value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            return "Use only letters, numbers, underscores and hyphens in your username.";
        }

        return null;
    }

    public static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();

    public static AvatarResolution ResolveAvatar(
        ProfileAvatarChoice choice,
        string? googleAvatarUrl,
        string? currentAvatarUrl,
        byte[]? currentAvatarPng,
        ProfileAvatarUpload? avatarUpload) => choice switch
        {
            ProfileAvatarChoice.Default => new AvatarResolution(null, null),
            ProfileAvatarChoice.Google when !string.IsNullOrWhiteSpace(googleAvatarUrl) =>
                new AvatarResolution(googleAvatarUrl, null),
            ProfileAvatarChoice.Google =>
                new AvatarResolution(null, null, "Google did not provide a profile picture."),
            ProfileAvatarChoice.Upload => ResolveUploadedAvatar(
                currentAvatarUrl,
                currentAvatarPng,
                avatarUpload),
            _ => new AvatarResolution(null, null, "Choose a profile picture.")
        };

    private static AvatarResolution ResolveUploadedAvatar(
        string? currentAvatarUrl,
        byte[]? currentAvatarPng,
        ProfileAvatarUpload? avatarUpload)
    {
        if (avatarUpload is null)
        {
            return currentAvatarPng is not null && !string.IsNullOrWhiteSpace(currentAvatarUrl)
                ? new AvatarResolution(currentAvatarUrl, currentAvatarPng)
                : new AvatarResolution(null, null, "Select a PNG image to upload.");
        }

        var error = ValidatePng(avatarUpload.Content);
        return error is null
            ? new AvatarResolution(null, avatarUpload.Content.ToArray())
            : new AvatarResolution(null, null, error);
    }

    public static string? ValidatePng(byte[]? content)
    {
        if (content is null || content.Length == 0)
        {
            return "Select a PNG image to upload.";
        }

        if (content.Length > MaximumAvatarBytes)
        {
            return "The PNG image must not exceed 1 MB.";
        }

        if (content.Length < 24 ||
            !content.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature) ||
            BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(8, 4)) != 13 ||
            !content.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return "The selected file is not a valid PNG image.";
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(20, 4));
        if (width == 0 || height == 0 ||
            width > MaximumAvatarDimension || height > MaximumAvatarDimension)
        {
            return $"The PNG dimensions must be between 1 and {MaximumAvatarDimension} pixels.";
        }

        var offset = PngSignature.Length;
        var hasImageData = false;
        while (offset <= content.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(offset, 4));
            if (chunkLength > int.MaxValue ||
                chunkLength > content.Length - offset - 12)
            {
                return "The selected PNG file is incomplete or corrupted.";
            }

            var chunkType = content.AsSpan(offset + 4, 4);
            hasImageData |= chunkType.SequenceEqual("IDAT"u8);
            var nextOffset = offset + 12 + (int)chunkLength;
            if (chunkType.SequenceEqual("IEND"u8))
            {
                return chunkLength == 0 && hasImageData && nextOffset == content.Length
                    ? null
                    : "The selected PNG file is incomplete or corrupted.";
            }

            offset = nextOffset;
        }

        return "The selected PNG file is incomplete or corrupted.";
    }
}

public sealed record AvatarResolution(
    string? AvatarUrl,
    byte[]? AvatarPng,
    string? Error = null);
