using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Common;

/// <summary>
/// The one and only answer that carries a new database's password: what creating it produced,
/// including the credential the customer will never be shown again.
/// </summary>
/// <remarks>
/// <para>
/// A separate type from <see cref="DatabaseDto"/> precisely so that "the response with the password
/// in it" is one named type a reviewer can find every use of, rather than an optional field on the
/// shape every read returns. Nothing else in this module has a password-shaped member.
/// </para>
/// <para>
/// The password is a <see cref="SensitiveString"/> and not a <see cref="string"/>. This is a
/// <c>record</c>, whose compiler-generated <c>ToString()</c> prints every property, so a plain
/// string here would put the credential into the first log line, exception message or interpolation
/// that ever touched this object — with nothing at the call site looking wrong. Wrapped, all of
/// those render <c>[redacted]</c>, and only the JSON serializer, asked explicitly through
/// <c>SensitiveStringJsonConverter</c>, produces the real value on this one response.
/// </para>
/// </remarks>
/// <param name="Id">The new database's identity.</param>
/// <param name="AccountId">The account that owns it.</param>
/// <param name="Name">The name the customer asked for, without the account prefix.</param>
/// <param name="FullName">The fully-qualified name MySQL holds — what the connection string needs.</param>
/// <param name="DbUserName">The fully-qualified dedicated user name.</param>
/// <param name="Password">
/// The generated password, shown once. Nothing in this system keeps a copy — not the panel, not the
/// agent — so a customer who loses it recovers by having a new one set, never by being shown this
/// one again.
/// </param>
/// <param name="CreatedAt">The instant the database was created.</param>
public sealed record CreatedDatabaseDto(
    Guid Id,
    Guid AccountId,
    string Name,
    string FullName,
    string DbUserName,
    SensitiveString Password,
    DateTimeOffset CreatedAt);
