namespace Maran.Modules.Ftp.Domain.Policies;

/// <summary>
/// The one spelling of the daemon this module's logins authenticate against, as it travels to the
/// SPA.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the label is a fact the BACKEND owns (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). The SPA merges this module's logins with the Sftp module's onto
/// one "File transfer" screen, and a row on that screen must say which daemon it belongs to — a
/// customer pointed at the wrong client by a guessed label is the failure this closes. The SPA does
/// not derive it from which endpoint it called, so the value has to be on the wire.
/// </para>
/// <para>
/// It is NOT a column on <c>FtpUser</c>. Every row this module stores is an FTPS login by
/// construction, so a column could hold exactly one value; what varies is not the row but the screen
/// the row is rendered on, and that is a boundary concern rather than a stored fact.
/// </para>
/// <para>
/// A stateless rule over a value, so <c>Domain/Policies/</c> rather than <c>Common/</c>, which holds
/// <c>*Dto.cs</c> and nothing else (rules/csharp.md) — the same reasoning that put
/// <see cref="FtpsDefaults"/> here.
/// </para>
/// </remarks>
public static class FtpsProtocolName
{
    /// <summary>The token every login this module reports carries.</summary>
    /// <remarks>
    /// Spelled in the panel's own PascalCase, which is how the SPA's narrowing function documents
    /// that it reads the server's spelling. The SPA matches it case-insensitively, so a JSON
    /// converter camel-casing it changes nothing; a token this constant does not produce would be
    /// rendered as absence rather than as the other protocol, which is why the constant exists in
    /// one place rather than being typed at each call site.
    /// </remarks>
    public const string Ftps = "Ftps";
}
