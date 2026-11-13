using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// The lease renewal over its REAL parts: the licence verifier, the lease reader, the file source and the file
/// writer, each on a temporary directory, with a clock the test moves. The only doubles are the licence service
/// (scripted) and the cache the gate reads (counted).
/// </summary>
internal sealed class RenewalHarness : IDisposable
{
    /// <summary>The instant every test starts at.</summary>
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A directory of its own, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "renewal-harness-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The clock the whole harness shares.</summary>
    internal MutableClock Clock { get; }

    /// <summary>The scripted licence service.</summary>
    internal ScriptedLeaseServiceClient Client { get; }

    /// <summary>Counts the times the gate's memory was told to forget.</summary>
    internal CountingInvalidator Invalidator { get; }

    /// <summary>Reads the installed licence's text.</summary>
    internal FileLicenceRawTextSource RawText { get; }

    /// <summary>Reads the stored lease the way the gate does.</summary>
    internal ILicenceLeaseSource Source { get; }

    /// <summary>The renewal under test.</summary>
    internal LeaseRenewer Renewer { get; }

    /// <summary>The gate the renewal stores a lease under; an install under test must use the same one.</summary>
    internal LicenceInstallLock InstallLock { get; } = new();

    /// <summary>Where the licence is stored.</summary>
    internal string LicenceFilePath
    {
        get { return Path.Combine(_directory, "licence.json"); }
    }

    /// <summary>Where the lease is stored.</summary>
    internal string LeaseFilePath
    {
        get { return Path.Combine(_directory, "lease.json"); }
    }

    /// <summary>The text of the stored lease, or null when there is none.</summary>
    internal string? StoredLease
    {
        get { return File.Exists(LeaseFilePath) ? File.ReadAllText(LeaseFilePath) : null; }
    }

    /// <summary>Builds the harness.</summary>
    /// <param name="machineId">The machine id of the host, or null for a host the agent cannot identify.</param>
    /// <param name="writer">Replaces the file writer, to simulate a disk that fails.</param>
    internal RenewalHarness(string? machineId = null, ILeaseWriter? writer = null)
    {
        Clock = new MutableClock(Start);
        Client = new ScriptedLeaseServiceClient();
        Invalidator = new CountingInvalidator();

        var storage = new Microsoft.Extensions.Options.OptionsWrapper<LeaseStorageOptions>(
            new LeaseStorageOptions { LeaseFilePath = LeaseFilePath });
        var leaseVerifier = new Ed25519LeaseSignatureVerifier(TestKeys.TrustedLeaseKey);
        var identity = new FixedServerIdentitySource(machineId);

        RawText = new FileLicenceRawTextSource(
            new Microsoft.Extensions.Options.OptionsWrapper<LicenceStorageOptions>(
                new LicenceStorageOptions { LicenceFilePath = LicenceFilePath }));
        Source = new FileLeaseSource(storage, leaseVerifier);
        Renewer = new LeaseRenewer(
            RawText,
            new LicenceVerifier(new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey), Clock, identity),
            Source,
            identity,
            Client,
            new LeaseEnvelopeReader(leaseVerifier),
            writer ?? new FileLeaseWriter(storage),
            InstallLock,
            Invalidator,
            Clock,
            NullLogger<LeaseRenewer>.Instance);
    }

    /// <summary>Builds a licence signed by the trusted licence key, naming mail, valid for 90 days from the start.</summary>
    /// <param name="id">The licence id.</param>
    /// <param name="offline">Whether the licence is an offline licence.</param>
    /// <returns>The licence file text.</returns>
    internal static string Licence(string id = "lic-001", bool offline = false)
    {
        return LicenceEnvelopeBuilder.Build(
            TestKeys.RealPrivateKeyHex,
            id: id,
            modules: ["mail"],
            expiry: Start.AddDays(90),
            offline: offline ? true : null);
    }

    /// <summary>Builds a lease signed by the trusted lease key.</summary>
    /// <param name="issued">When it was issued.</param>
    /// <param name="validUntil">When it ends; fourteen days after <paramref name="issued"/> when null.</param>
    /// <param name="licence">The licence it names.</param>
    /// <param name="server">The machine it is bound to, or null.</param>
    /// <returns>The lease envelope text.</returns>
    internal static string Lease(
        DateTimeOffset issued,
        DateTimeOffset? validUntil = null,
        string licence = "lic-001",
        string? server = null)
    {
        return LeaseEnvelopeBuilder.Build(
            TestKeys.LeasePrivateKeyHex,
            licence,
            issued,
            validUntil ?? issued.AddDays(14),
            server);
    }

    /// <summary>Installs a licence.</summary>
    /// <param name="text">The licence file text.</param>
    internal void InstallLicence(string text)
    {
        File.WriteAllText(LicenceFilePath, text);
    }

    /// <summary>Stores a lease as an earlier renewal would have.</summary>
    /// <param name="text">The lease envelope text.</param>
    internal void StoreLease(string text)
    {
        File.WriteAllText(LeaseFilePath, text);
    }

    /// <summary>Reads the stored lease through the same source the gate uses.</summary>
    /// <returns>What the file amounts to.</returns>
    internal async Task<LeaseReading> ReadStoredAsync()
    {
        return await Source.ReadAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
