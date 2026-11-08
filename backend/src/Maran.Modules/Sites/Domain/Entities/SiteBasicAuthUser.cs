namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>
/// One basic-authentication user of a site's options: a login name and the bcrypt hash of its password.
/// Created and removed with <see cref="SiteOptions"/> and by nothing else.
/// </summary>
/// <remarks>
/// The plain password is never stored; only its hash is, and no response, log line or audit subject
/// carries it. It is the one value in this module that is credential material at rest.
/// </remarks>
public sealed class SiteBasicAuthUser
{
    /// <summary>The user's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The site whose options hold this user.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The options this user belongs to, as the relationship EF Core cascades on and the tenant filter reads through.</summary>
    public SiteOptions Options { get; private set; }

    /// <summary>The user's place in the rendered credential file, from zero.</summary>
    public int Position { get; private set; }

    /// <summary>The login name.</summary>
    public string Name { get; private set; }

    /// <summary>The bcrypt hash of the user's password. Never returned by the API.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Creates a user for <paramref name="options"/>.</summary>
    /// <param name="options">The options that hold the user.</param>
    /// <param name="position">The user's place in the credential file.</param>
    /// <param name="name">The login name.</param>
    /// <param name="passwordHash">The bcrypt hash of the password.</param>
    public SiteBasicAuthUser(SiteOptions options, int position, string name, string passwordHash)
    {
        Id = Guid.NewGuid();
        Options = options;
        SiteId = options.SiteId;
        Position = position;
        Name = name;
        PasswordHash = passwordHash;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteBasicAuthUser()
    {
        Options = null!;
        Name = string.Empty;
        PasswordHash = string.Empty;
    }
}
