using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// Something the last discovery of a remote server could not read or chose not to report in full. Administrator
/// bookkeeping, replaced as a whole by the next discovery.
/// </summary>
public sealed class DiscoveryWarning
{
    /// <summary>The longest subject kept.</summary>
    public const int SubjectMaxLength = 256;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The server whose discovery raised it.</summary>
    public Guid RemoteServerId { get; private set; }

    /// <summary>The closed kind of the warning.</summary>
    public DiscoveryWarningKind Kind { get; private set; }

    /// <summary>What it is about (an account, database or path), as the source named it.</summary>
    public string Subject { get; private set; }

    /// <summary>Creates the warning, shortening the subject the source reported to the column's width.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="remoteServerId">The server whose discovery raised it.</param>
    /// <param name="kind">The closed kind.</param>
    /// <param name="subject">What it is about; may be empty.</param>
    public DiscoveryWarning(Guid id, Guid remoteServerId, DiscoveryWarningKind kind, string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        Id = id;
        RemoteServerId = remoteServerId;
        Kind = kind;
        Subject = ColumnText.Fit(subject, SubjectMaxLength);
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DiscoveryWarning()
    {
        Subject = string.Empty;
    }
}
