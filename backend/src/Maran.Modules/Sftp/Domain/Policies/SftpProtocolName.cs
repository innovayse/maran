namespace Maran.Modules.Sftp.Domain.Policies;

/// <summary>
/// The one spelling of the daemon this module's logins authenticate against, as it travels to the
/// SPA.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the label is a fact the BACKEND owns (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). The SPA merges this module's logins with the Ftp module's onto
/// one "File transfer" screen, and until this constant existed the SFTP half of that table said
/// what it was only because of which URL had answered it. A row labelled by its ORIGIN rather than
/// by its content is right exactly as long as nobody adds a third source, and the failure it
/// produces is a customer pointed at the wrong client for a login that is not the protocol the
/// label claims.
/// </para>
/// <para>
/// It is NOT a column on <c>SftpUser</c>. Every row this module stores is an SFTP login by
/// construction, so a column could hold exactly one value; what varies is not the row but the screen
/// the row is rendered on, and that is a boundary concern rather than a stored fact.
/// </para>
/// <para>
/// A stateless rule over a value, so <c>Domain/Policies/</c> rather than <c>Common/</c>, which holds
/// <c>*Dto.cs</c> and nothing else (rules/csharp.md).
/// </para>
/// <para>
/// The vocabulary is deliberately the same one the Ftp module already ships in its own
/// <c>FtpsProtocolName</c> — a bare token in the panel's PascalCase, not a shared enum. A module may
/// reference only <c>Maran.Sdk</c> and <c>Maran.SharedKernel</c> (rules/architecture.md), so the two
/// protocol modules cannot share a type without promoting the label into the Sdk, and the label is
/// not a cross-module contract: it is each module's own answer to "what am I". Two constants naming
/// the two halves of one union is the shape the boundary permits, and the union itself is composed
/// in the layer that does the composing, which is the SPA.
/// </para>
/// </remarks>
public static class SftpProtocolName
{
    /// <summary>The token every login this module reports carries.</summary>
    /// <remarks>
    /// Spelled in the panel's own PascalCase, which is how the SPA's narrowing function documents
    /// that it reads the server's spelling. The SPA matches it case-insensitively, so a JSON
    /// converter camel-casing it changes nothing; a token this constant does not produce would be
    /// rendered as absence rather than as the other protocol, which is why the constant exists in
    /// one place rather than being typed at each call site.
    /// </remarks>
    public const string Sftp = "Sftp";
}
