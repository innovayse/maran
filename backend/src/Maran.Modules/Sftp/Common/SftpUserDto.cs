using Maran.Modules.Sftp.Domain.Entities;

namespace Maran.Modules.Sftp.Common;

/// <summary>Outward view of an <see cref="SftpUser"/>: everything a screen shows about one.</summary>
/// <remarks>
/// One DTO for both the list and the single read, because a login has no detail beyond this — there
/// is no larger view to grow into, and a second near-identical type would only be somewhere for the
/// two to drift apart.
///
/// It carries no password, and there is nothing for it to carry one FROM: no column holds one
/// (<see cref="SftpUser"/>). The value is shown exactly once, by
/// <see cref="CreatedSftpUserDto"/> and <see cref="SftpUserPasswordDto"/>, and never again.
/// </remarks>
/// <param name="Id">The login's identity, and the only identifier a request may name.</param>
/// <param name="AccountId">The account that owns this login.</param>
/// <param name="Name">The name the customer asked for, without the account prefix.</param>
/// <param name="FullName">
/// The system login the host holds — the user name the customer actually types into their SFTP
/// client, which is why it is on every read and not only on the one that shows the password.
/// </param>
/// <param name="CreatedAt">The instant the login was created.</param>
public sealed record SftpUserDto(
    Guid Id,
    Guid AccountId,
    string Name,
    string FullName,
    DateTimeOffset CreatedAt);
