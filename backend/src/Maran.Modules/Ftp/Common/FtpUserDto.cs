using Maran.Modules.Ftp.Domain.Entities;

namespace Maran.Modules.Ftp.Common;

/// <summary>Outward view of an <see cref="FtpUser"/>: everything a screen shows about one.</summary>
/// <remarks>
/// One DTO for both the list and the single read, because a login has no detail beyond this — there
/// is no larger view to grow into, and a second near-identical type would only be somewhere for the
/// two to drift apart.
///
/// It carries no password, and there is nothing for it to carry one FROM: no column holds one
/// (<see cref="FtpUser"/>). The value is shown exactly once, by <see cref="CreatedFtpUserDto"/> and
/// <see cref="FtpUserPasswordDto"/>, and never again.
/// </remarks>
/// <param name="Id">The login's identity, and the only identifier a request may name.</param>
/// <param name="AccountId">The account that owns this login.</param>
/// <param name="Name">The name the customer asked for, without the account prefix.</param>
/// <param name="FullName">
/// The system login the host holds — the user name the customer actually types into their FTPS
/// client, which is why it is on every read and not only on the one that shows the password.
/// </param>
/// <param name="Protocol">
/// Which daemon accepts this login, as the backend spells it
/// (<c>FtpsProtocolName.Ftps</c>). On the wire rather than assumed by the SPA because the merged
/// "File transfer" screen carries logins from two modules and must render a backend-supplied fact,
/// not one inferred from which URL was called.
/// </param>
/// <param name="CreatedAt">The instant the login was created.</param>
public sealed record FtpUserDto(
    Guid Id,
    Guid AccountId,
    string Name,
    string FullName,
    string Protocol,
    DateTimeOffset CreatedAt);
