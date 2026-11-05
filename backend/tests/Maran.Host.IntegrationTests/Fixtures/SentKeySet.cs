namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>One key-set request the panel sent to the SFTP agent double.</summary>
/// <param name="AccountUsername">The account the request addressed.</param>
/// <param name="SftpUsername">The login the request addressed.</param>
/// <param name="Keys">The complete set sent, one <c>&lt;algorithm&gt; &lt;base64&gt;</c> line per key.</param>
public sealed record SentKeySet(string AccountUsername, string SftpUsername, IReadOnlyList<string> Keys);
