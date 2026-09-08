namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>Which kind of storage a backup destination names (spec §11, §209).</summary>
/// <remarks>
/// <para>
/// <b>Both members exist so that a remote destination can be DESCRIBED, and only one of them can be
/// acted on.</b> Which kinds this build may act on is
/// <see cref="Domain.Policies.RemoteDestinationPolicy"/>'s single answer, and it is asked at both
/// boundaries that could reach a destination: the endpoint that would store one, and the resolver
/// that turns a stored row into the destination an agent call names.
/// </para>
/// <para>
/// The alternative — an enum with one member, grown when the remote arm lands — would leave the
/// panel with no way to say "you asked for S3 and this build cannot" other than a validation message
/// about an unknown value, which no screen can branch on and which reads as a typo rather than as
/// the honest state of the product.
/// </para>
/// </remarks>
public enum BackupDestinationKind
{
    /// <summary>A directory on this server, owned and written by the agent.</summary>
    Local,

    /// <summary>An S3-compatible bucket. Describable; refused by every path that would act on it.</summary>
    S3,
}
