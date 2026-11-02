using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>An S3-compatible object store.</summary>
/// <remarks>
/// <para>
/// The agent that ships today REFUSES this kind at its service boundary with a not-implemented
/// failure; it is never quietly served from the local directory instead, and the panel must show
/// that refusal as itself.
/// </para>
/// <para>
/// The credentials travel in this record, per call: the agent reads no credential file and holds no
/// bucket configuration, and one panel serves several destinations. They are therefore held in
/// <see cref="SensitiveString"/>, which is what keeps them out of a log — this type is a
/// <c>record</c>, whose compiler-generated <see cref="object.ToString"/> prints every property, so a
/// bare <c>string</c> here would leak the secret access key the first time anything formatted a
/// destination. The wrapper makes that rendering <c>[redacted]</c> instead, and the named test
/// <c>Formatting_a_destination_never_prints_the_secret</c> holds it to that.
/// </para>
/// </remarks>
/// <param name="Path">The object key prefix, empty for the bucket root.</param>
/// <param name="Bucket">The bucket name.</param>
/// <param name="Region">The region, e.g. <c>eu-central-1</c>.</param>
/// <param name="Endpoint">
/// Base URL of an S3-compatible provider; empty means the provider's own endpoint for the region.
/// The agent requires <c>https://</c>.
/// </param>
/// <param name="AccessKeyId">The access key id. A secret.</param>
/// <param name="SecretAccessKey">The secret access key. A secret.</param>
/// <param name="PathStyle">Address the bucket as a path segment rather than a subdomain.</param>
public sealed record AgentS3BackupDestination(
    string Path,
    string Bucket,
    string Region,
    string Endpoint,
    SensitiveString AccessKeyId,
    SensitiveString SecretAccessKey,
    bool PathStyle) : AgentBackupDestination;
