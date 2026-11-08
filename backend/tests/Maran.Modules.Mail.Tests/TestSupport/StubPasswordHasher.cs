using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>A password hasher that is instant and recognisable (<c>hashed:</c> plus the password reversed), never used outside tests.</summary>
public sealed class StubPasswordHasher : IPasswordHasher
{
    /// <summary>The marker every hash starts with.</summary>
    public const string Marker = "hashed:";

    /// <inheritdoc />
    public string Hash(string password)
    {
        return Marker + new string(password.Reverse().ToArray());
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        return hash == Hash(password);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string hash)
    {
        return false;
    }
}
