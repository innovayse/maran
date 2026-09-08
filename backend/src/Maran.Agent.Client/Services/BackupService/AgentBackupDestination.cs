using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Where a backup artifact is written to, or read back from.</summary>
/// <remarks>
/// The credentials travel in this record, per call: the agent reads no credential file and holds no
/// bucket configuration, and one panel serves several destinations. They are therefore held in
/// <see cref="SensitiveString"/>, which is what keeps them out of a log — this type is a
/// <c>record</c>, whose compiler-generated <see cref="object.ToString"/> prints every property, so a
/// bare <c>string</c> here would leak the secret access key the first time anything formatted a
/// destination. The wrapper makes that rendering <c>[redacted]</c> instead, and the named test
/// <c>Formatting_a_destination_never_prints_the_secret</c> holds it to that.
///
/// <para>
/// For <see cref="AgentBackupDestinationKind.Local"/>, every S3 member is empty and
/// <see cref="Path"/> MUST be empty as well: the local root is the agent's own, and the agent
/// refuses a non-empty path rather than ignoring it.
/// </para>
/// </remarks>
/// <param name="Kind">Which storage kind this destination targets.</param>
/// <param name="Path">S3: the object key prefix, empty for the bucket root. LOCAL: must be empty.</param>
/// <param name="S3Bucket">S3 only: bucket name. Empty for local.</param>
/// <param name="S3Region">S3 only: region, e.g. <c>eu-central-1</c>. Empty for local.</param>
/// <param name="S3Endpoint">
/// S3 only: base URL of an S3-compatible provider; empty means the provider's own endpoint for the
/// region. The agent requires <c>https://</c>.
/// </param>
/// <param name="S3AccessKeyId">S3 only: access key id. A secret; empty for local.</param>
/// <param name="S3SecretAccessKey">S3 only: secret access key. A secret; empty for local.</param>
/// <param name="S3PathStyle">S3 only: address the bucket as a path segment rather than a subdomain.</param>
public sealed record AgentBackupDestination(
    AgentBackupDestinationKind Kind,
    string Path,
    string S3Bucket,
    string S3Region,
    string S3Endpoint,
    SensitiveString S3AccessKeyId,
    SensitiveString S3SecretAccessKey,
    bool S3PathStyle);
