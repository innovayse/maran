using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Domain.ValueObjects;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One server an administrator registered as a source to read from: where to connect, the key the panel
/// connects with, the one host key it trusts, and what the last test and discovery found. The row records what
/// the administrator decided and what the panel last observed; it is never a live measurement.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pin is the security property.</b> A server is created <see cref="RemoteServerState.HostKeyUnconfirmed"/>
/// and nothing connects to it until <see cref="PinHostKey"/> accepted a fingerprint the administrator TYPED, and
/// the match is exact (ordinal, case-sensitive) against a key the server itself offered. The pin belongs to one
/// target: <see cref="Retarget"/> clears it, together with every observation, whenever the host, port or login
/// changes, so a key trusted for one machine is never carried to another.
/// </para>
/// <para>
/// <b>One field, one meaning.</b> <see cref="HostKeyAlgorithm"/>, <see cref="HostKey"/>,
/// <see cref="HostKeyFingerprint"/> and <see cref="HostKeyConfirmedAt"/> are all null or all set. The
/// observation fields (<see cref="Kind"/> to <see cref="MailFound"/>) describe the last SUCCESSFUL test and are null
/// otherwise; <see cref="LastFailure"/> is set only while the last test failed.
/// </para>
/// <para>
/// <see cref="PrivateKeyCipher"/> holds the panel's private key for this server. In memory it is the OpenSSH
/// key document; in the column it is ciphertext, through the panel's encryption converter. It is never part
/// of a DTO, an audit entry, a log line or an error. The row carries no account id and is not tenant data:
/// only an administrator reaches it.
/// </para>
/// </remarks>
public sealed class RemoteServer
{
    /// <summary>The length of a host key fingerprint: <c>SHA256:</c> and 43 base64 characters.</summary>
    public const int FingerprintLength = 50;

    /// <summary>The most host keys one scan may offer.</summary>
    public const int MaxHostKeyCandidates = 8;

    /// <summary>The longest public key line: an ed25519 key is about 80 characters.</summary>
    public const int PublicKeyMaxLength = 512;

    /// <summary>The longest private key document held in memory: an ed25519 OpenSSH key is about 420 characters.</summary>
    public const int PrivateKeyMaxLength = 1024;

    /// <summary>The widest the private key column is: ciphertext of <see cref="PrivateKeyMaxLength"/> in base64.</summary>
    public const int PrivateKeyColumnLength = 2048;

    /// <summary>The longest host key algorithm name.</summary>
    public const int HostKeyAlgorithmMaxLength = 32;

    /// <summary>The longest host key (base64): the agent bounds it at 2048 characters.</summary>
    public const int HostKeyMaxLength = 2048;

    /// <summary>The longest operating system release text kept.</summary>
    public const int SystemReleaseMaxLength = 128;

    /// <summary>The longest panel version text kept.</summary>
    public const int MaranVersionMaxLength = 64;

    /// <summary>The longest database or web server name kept.</summary>
    public const int ServerNameMaxLength = 16;

    /// <summary>The host key candidates of the last scan, changed only through this entity.</summary>
    private readonly List<RemoteServerHostKeyCandidate> _hostKeyCandidates = [];

    /// <summary>The row's identity, and the only identifier a request may name for a server.</summary>
    public Guid Id { get; private set; }

    /// <summary>The label the administrator knows the server by. Unique.</summary>
    public string Name { get; private set; }

    /// <summary>The DNS name or IP address the panel connects to.</summary>
    public string Host { get; private set; }

    /// <summary>The TCP port of the server's SSH daemon.</summary>
    public int Port { get; private set; }

    /// <summary>The login the panel's key is installed for.</summary>
    public string SshUser { get; private set; }

    /// <summary>The addresses the installed key is restricted to, as the administrator wrote them; null for anywhere.</summary>
    public string? AllowedFrom { get; private set; }

    /// <summary>The panel's public key for this server, an OpenSSH public key line.</summary>
    public string PublicKey { get; private set; }

    /// <summary>The panel's private key for this server: plain in memory, encrypted in the column. Never leaves the module.</summary>
    public string PrivateKeyCipher { get; private set; }

    /// <summary>The algorithm of the pinned host key; null until one is pinned.</summary>
    public string? HostKeyAlgorithm { get; private set; }

    /// <summary>The pinned host key (base64); null until one is pinned.</summary>
    public string? HostKey { get; private set; }

    /// <summary>The fingerprint of the pinned host key (<c>SHA256:...</c>); null until one is pinned.</summary>
    public string? HostKeyFingerprint { get; private set; }

    /// <summary>When the administrator confirmed the pinned key; null until one is pinned.</summary>
    public DateTimeOffset? HostKeyConfirmedAt { get; private set; }

    /// <summary>Where the server stands; see <see cref="RemoteServerState"/>.</summary>
    public RemoteServerState State { get; private set; }

    /// <summary>When a connection was last tried; null if never.</summary>
    public DateTimeOffset? LastTestedAt { get; private set; }

    /// <summary>Why the last attempt failed; null while the last attempt succeeded or none was made.</summary>
    public RemoteFailureKind? LastFailure { get; private set; }

    /// <summary>What kind of system the last successful test found; null when none did or it could not tell.</summary>
    public RemoteSourceKind? Kind { get; private set; }

    /// <summary>The operating system release found by the last successful test.</summary>
    public string? SystemRelease { get; private set; }

    /// <summary>The panel version found on the server by the last successful test.</summary>
    public string? MaranVersion { get; private set; }

    /// <summary>Whether the login is the superuser, per the last successful test.</summary>
    public bool? IsRoot { get; private set; }

    /// <summary>Free space under the home directories, per the last successful test.</summary>
    public long? HomeFreeBytes { get; private set; }

    /// <summary>Size of the filesystem holding the home directories, per the last successful test.</summary>
    public long? HomeTotalBytes { get; private set; }

    /// <summary>The database server found (<c>MariaDb</c> or <c>MySql</c>), per the last successful test.</summary>
    public string? DatabaseServer { get; private set; }

    /// <summary>The web server found (<c>Nginx</c> or <c>Apache</c>), per the last successful test.</summary>
    public string? WebServer { get; private set; }

    /// <summary>Whether mail storage was found, per the last successful test.</summary>
    public bool? MailFound { get; private set; }

    /// <summary>When the last discovery finished; null if none has.</summary>
    public DateTimeOffset? DiscoveredAt { get; private set; }

    /// <summary>How many accounts the last discovery found; null if none has run.</summary>
    public int? AccountsFound { get; private set; }

    /// <summary>When the server was registered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The host keys the server offered at the last scan; empty until loaded. Load them with the server before changing them.</summary>
    public IReadOnlyCollection<RemoteServerHostKeyCandidate> HostKeyCandidates
    {
        get { return _hostKeyCandidates; }
    }

    /// <summary>True when a host key is pinned, so the panel may connect.</summary>
    public bool IsHostKeyPinned
    {
        get { return HostKeyFingerprint is not null; }
    }

    /// <summary>True when a discovery may run: a key is pinned and the last attempt did not end in a refusal that needs a decision first.</summary>
    public bool MayDiscover
    {
        get { return State is RemoteServerState.Pinned or RemoteServerState.Ready; }
    }

    /// <summary>Registers a server, <see cref="RemoteServerState.HostKeyUnconfirmed"/>, with no host key and no observation.</summary>
    /// <param name="id">The new server's identity.</param>
    /// <param name="name">The label; not blank, at most <see cref="RemoteServerFieldLimits.NameMaxLength"/> long.</param>
    /// <param name="host">The DNS name or IP address; already normalised and validated by the command.</param>
    /// <param name="port">The SSH port.</param>
    /// <param name="sshUser">The login the key is installed for; already validated.</param>
    /// <param name="allowedFrom">The addresses the installed key is restricted to, or null.</param>
    /// <param name="publicKey">The OpenSSH public key line the agent generated.</param>
    /// <param name="privateKeyCipher">The OpenSSH private key document the agent generated (encrypted when stored).</param>
    /// <param name="now">When the server is registered.</param>
    /// <remarks>
    /// The arguments are checked here as well as by the command's validator, but a failure here is a bug in the
    /// caller and is an exception, not a result: a person's mistake is the validator's 400.
    /// </remarks>
    public RemoteServer(
        Guid id,
        string name,
        string host,
        int port,
        string sshUser,
        string? allowedFrom,
        string publicKey,
        string privateKeyCipher,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A server has an identity.", nameof(id));
        }

        RequireTarget(name, host, port, sshUser, allowedFrom);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(publicKey.Length, PublicKeyMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyCipher);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(privateKeyCipher.Length, PrivateKeyMaxLength);

        Id = id;
        Name = name;
        Host = host;
        Port = port;
        SshUser = sshUser;
        AllowedFrom = allowedFrom;
        PublicKey = publicKey;
        PrivateKeyCipher = privateKeyCipher;
        State = RemoteServerState.HostKeyUnconfirmed;
        CreatedAt = now;
    }

    /// <summary>EF Core's constructor; nothing else may build an entity in an undefined state.</summary>
    private RemoteServer()
    {
        Name = string.Empty;
        Host = string.Empty;
        SshUser = string.Empty;
        PublicKey = string.Empty;
        PrivateKeyCipher = string.Empty;
    }

    /// <summary>
    /// Changes what the administrator typed: the label, the address, the port, the login and the key
    /// restriction. A change of host, port or login means a different machine or account, so the pin, the
    /// candidates and every observation of the old target are dropped and the server is
    /// <see cref="RemoteServerState.HostKeyUnconfirmed"/> again. A change of label or restriction alone keeps them.
    /// </summary>
    /// <param name="name">The label; not blank, at most <see cref="RemoteServerFieldLimits.NameMaxLength"/> long.</param>
    /// <param name="host">The DNS name or IP address, normalised and validated.</param>
    /// <param name="port">The SSH port.</param>
    /// <param name="sshUser">The login, validated.</param>
    /// <param name="allowedFrom">The key restriction, or null.</param>
    /// <returns>
    /// True when the target (host, port or login) changed, so the pin and the observations were dropped; the
    /// caller then also discards the rows a discovery of the old target left.
    /// </returns>
    public bool Retarget(string name, string host, int port, string sshUser, string? allowedFrom)
    {
        RequireTarget(name, host, port, sshUser, allowedFrom);

        var targetChanged = !string.Equals(Host, host, StringComparison.Ordinal)
            || Port != port
            || !string.Equals(SshUser, sshUser, StringComparison.Ordinal);

        Name = name;
        Host = host;
        Port = port;
        SshUser = sshUser;
        AllowedFrom = allowedFrom;

        if (targetChanged)
        {
            _hostKeyCandidates.Clear();
            ClearPin();
            ClearObservations();
            LastTestedAt = null;
            LastFailure = null;
            DiscoveredAt = null;
            AccountsFound = null;
            State = RemoteServerState.HostKeyUnconfirmed;
        }

        return targetChanged;
    }

    /// <summary>
    /// Replaces the host keys on offer with what the server offered at a scan. It never pins: only
    /// <see cref="PinHostKey"/> does, and only for a fingerprint the administrator typed. An existing pin is
    /// left alone, so a server that rotated its key is rescanned, compared and re-pinned by a decision.
    /// </summary>
    /// <param name="keys">What the server offered: algorithm, key (base64) and fingerprint of each.</param>
    public void OfferHostKeys(IEnumerable<(string Algorithm, string Key, string Fingerprint)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var offered = new List<RemoteServerHostKeyCandidate>();
        foreach (var (algorithm, key, fingerprint) in keys)
        {
            if (offered.Any(candidate => { return string.Equals(candidate.Fingerprint, fingerprint, StringComparison.Ordinal); }))
            {
                continue;
            }

            offered.Add(new RemoteServerHostKeyCandidate(Guid.NewGuid(), Id, algorithm, key, fingerprint));
        }

        if (offered.Count == 0)
        {
            throw new ArgumentException("A scan that offered no key has nothing to confirm.", nameof(keys));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(offered.Count, MaxHostKeyCandidates);

        _hostKeyCandidates.Clear();
        _hostKeyCandidates.AddRange(offered);
    }

    /// <summary>
    /// Pins the offered host key whose fingerprint equals the one the administrator typed. The match is exact:
    /// ordinal and case-sensitive, so a near miss pins nothing.
    /// </summary>
    /// <param name="fingerprint">The fingerprint typed by the administrator.</param>
    /// <param name="now">When the administrator confirmed it.</param>
    /// <returns>True when a candidate matched and is pinned; false, with nothing changed, when none did.</returns>
    public bool PinHostKey(string fingerprint, DateTimeOffset now)
    {
        var match = _hostKeyCandidates.FirstOrDefault(candidate =>
        {
            return string.Equals(candidate.Fingerprint, fingerprint, StringComparison.Ordinal);
        });
        if (match is null)
        {
            return false;
        }

        var sameKey = string.Equals(HostKeyFingerprint, match.Fingerprint, StringComparison.Ordinal);

        HostKeyAlgorithm = match.Algorithm;
        HostKey = match.Key;
        HostKeyFingerprint = match.Fingerprint;
        HostKeyConfirmedAt = now;

        // Confirming the key already trusted changes nothing about what was observed; confirming a
        // different one invalidates every observation made through the old one.
        if (!sameKey || State is RemoteServerState.HostKeyUnconfirmed or RemoteServerState.HostKeyChanged)
        {
            ClearObservations();
            LastFailure = null;
            State = RemoteServerState.Pinned;
        }

        return true;
    }

    /// <summary>Records a connection test that succeeded with the pinned key.</summary>
    /// <param name="snapshot">What the test established.</param>
    /// <param name="now">When the test finished.</param>
    /// <returns>Success, or <c>RemoteServerHostKeyUnconfirmed</c> when no key is pinned (nothing could have been tested).</returns>
    public Result RecordTestSucceeded(TestedSourceSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!IsHostKeyPinned)
        {
            return Unconfirmed();
        }

        IsRoot = snapshot.IsRoot;
        HomeFreeBytes = snapshot.HomeFreeBytes;
        HomeTotalBytes = snapshot.HomeTotalBytes;
        SystemRelease = Shorten(snapshot.SystemRelease, SystemReleaseMaxLength);
        Kind = snapshot.Kind;
        MaranVersion = Shorten(snapshot.MaranVersion, MaranVersionMaxLength);
        DatabaseServer = Shorten(snapshot.DatabaseServer, ServerNameMaxLength);
        WebServer = Shorten(snapshot.WebServer, ServerNameMaxLength);
        MailFound = snapshot.MailFound;
        LastFailure = null;
        MarkTested(now);
        State = RemoteServerState.Ready;
        return Result.Ok();
    }

    /// <summary>
    /// Records an attempt to connect that failed. The observations of an earlier success are dropped, because they
    /// no longer describe what the panel can reach.
    /// </summary>
    /// <param name="failure">Why it failed.</param>
    /// <param name="now">When the attempt finished.</param>
    /// <returns>Success, or <c>RemoteServerHostKeyUnconfirmed</c> when no key is pinned.</returns>
    public Result RecordTestFailed(RemoteFailureKind failure, DateTimeOffset now)
    {
        if (!IsHostKeyPinned)
        {
            return Unconfirmed();
        }

        ClearObservations();
        LastFailure = failure;
        MarkTested(now);
        State = failure switch
        {
            RemoteFailureKind.HostKeyChanged => RemoteServerState.HostKeyChanged,
            RemoteFailureKind.AuthRefused => RemoteServerState.AuthRefused,
            RemoteFailureKind.Unreachable => RemoteServerState.Unreachable,
            RemoteFailureKind.AddressRefused => RemoteServerState.Unreachable,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "A failure kind this server has no state for."),
        };
        return Result.Ok();
    }

    /// <summary>Records that a discovery of the server finished.</summary>
    /// <param name="accounts">How many accounts it found; not negative.</param>
    /// <param name="now">When it finished.</param>
    /// <returns>Success, or <c>RemoteServerNotReady</c> when the server is not pinned and untroubled.</returns>
    public Result RecordDiscovery(int accounts, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accounts);

        if (!MayDiscover)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.RemoteServerNotReady), ErrorType.Conflict));
        }

        DiscoveredAt = now;
        AccountsFound = accounts;
        return Result.Ok();
    }

    /// <summary>Sets the time of the last attempt.</summary>
    /// <param name="now">When the attempt finished.</param>
    private void MarkTested(DateTimeOffset now)
    {
        LastTestedAt = now;
    }

    /// <summary>Forgets the pinned host key.</summary>
    private void ClearPin()
    {
        HostKeyAlgorithm = null;
        HostKey = null;
        HostKeyFingerprint = null;
        HostKeyConfirmedAt = null;
    }

    /// <summary>Forgets what the last successful test established.</summary>
    private void ClearObservations()
    {
        IsRoot = null;
        HomeFreeBytes = null;
        HomeTotalBytes = null;
        SystemRelease = null;
        Kind = null;
        MaranVersion = null;
        DatabaseServer = null;
        WebServer = null;
        MailFound = null;
    }

    /// <summary>The failure for an operation that needs a pinned host key and has none.</summary>
    /// <returns>A conflict: the request is well formed and the server's state is what is wrong for it.</returns>
    private static Result Unconfirmed()
    {
        return Result.Fail(Error.Of(nameof(ErrorMessages.RemoteServerHostKeyUnconfirmed), ErrorType.Conflict));
    }

    /// <summary>Shortens text the source reported to the column that holds it.</summary>
    /// <param name="value">What the source reported, or null.</param>
    /// <param name="maxCharacters">The column's width.</param>
    /// <returns>The value cut to the width, or null.</returns>
    private static string? Shorten(string? value, int maxCharacters)
    {
        return value is null ? null : ColumnText.Fit(value, maxCharacters);
    }

    /// <summary>Refuses a target no caller should have passed.</summary>
    /// <param name="name">The label.</param>
    /// <param name="host">The address.</param>
    /// <param name="port">The port.</param>
    /// <param name="sshUser">The login.</param>
    /// <param name="allowedFrom">The key restriction.</param>
    private static void RequireTarget(string name, string host, int port, string sshUser, string? allowedFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, RemoteServerFieldLimits.NameMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(host.Length, RemoteServerFieldLimits.HostMaxLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, RemoteServerFieldLimits.PortMinimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, RemoteServerFieldLimits.PortMaximum);
        ArgumentException.ThrowIfNullOrWhiteSpace(sshUser);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sshUser.Length, RemoteServerFieldLimits.SshUserMaxLength);

        if (allowedFrom is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(allowedFrom);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(allowedFrom.Length, RemoteServerFieldLimits.AllowedFromMaxLength);
        }
    }
}
