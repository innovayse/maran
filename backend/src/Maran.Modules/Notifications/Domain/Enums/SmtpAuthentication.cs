namespace Maran.Modules.Notifications.Domain.Enums;

/// <summary>How the panel presents itself to the mail server once connected.</summary>
/// <remarks>
/// The stored mode of <see cref="Entities.SmtpSettings"/>. A user name and password exist only in
/// <see cref="Basic"/>; in <see cref="Anonymous"/> the columns that hold them are <c>null</c>, which
/// the table's check constraint enforces. A relay on the same machine takes no credentials and
/// offering some it did not ask for is how a working relay starts refusing mail, so the anonymous
/// mode is a first-class choice and not a missing value.
/// </remarks>
public enum SmtpAuthentication
{
    /// <summary>The server takes no credentials; the panel never authenticates.</summary>
    Anonymous = 0,

    /// <summary>The server wants a user name and password, and the panel authenticates with them.</summary>
    Basic = 1,
}
