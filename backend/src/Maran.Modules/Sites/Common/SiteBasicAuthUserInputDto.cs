namespace Maran.Modules.Sites.Common;

/// <summary>One basic-authentication user as a replacement states it.</summary>
/// <remarks>
/// Request-only: a response lists user names and nothing else, because the panel keeps a hash and
/// never a password, and a hash is not something to hand back.
/// </remarks>
/// <param name="Name">The login name.</param>
/// <param name="Password">
/// The new password, or null / empty to keep the one the user already has. A user the site does not
/// have yet must carry one.
/// </param>
public sealed record SiteBasicAuthUserInputDto(string Name, string? Password);
