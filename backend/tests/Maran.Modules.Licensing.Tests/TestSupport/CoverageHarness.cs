using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// Wires the licence window over a temporary licence file, a lease source the test chooses and a clock the
/// test moves: the real verifier, source, cache and window, with only the three edges replaced.
/// </summary>
internal sealed class CoverageHarness : IDisposable
{
    /// <summary>The instant the harness clock starts at.</summary>
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A directory of its own, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "coverage-harness-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Creates the harness.</summary>
    /// <param name="lease">The lease source the window reads.</param>
    /// <param name="machineId">This host's machine id, or null.</param>
    internal CoverageHarness(CountingLeaseSource lease, string? machineId = null)
    {
        Clock = new MutableClock(Start);
        Lease = lease;
        var options = new Microsoft.Extensions.Options.OptionsWrapper<LicenceStorageOptions>(
            new LicenceStorageOptions { LicenceFilePath = LicenceFilePath });
        RawText = new FileLicenceRawTextSource(options);
        var verifier = new LicenceVerifier(
            new Ed25519LicenceSignatureVerifier(TestKeys.TrustedKey),
            Clock,
            new FixedServerIdentitySource(machineId));
        Source = new LicenceCoverageSource(RawText, verifier, lease, new FixedServerIdentitySource(machineId), Clock);
        Cache = new LicenceCoverageCache(Source, Clock);
        Window = new LicenceEntitlements(Cache, NullLogger<LicenceEntitlements>.Instance);
    }

    /// <summary>The clock the harness reads.</summary>
    internal MutableClock Clock { get; }

    /// <summary>The lease source.</summary>
    internal CountingLeaseSource Lease { get; }

    /// <summary>The licence file reader.</summary>
    internal FileLicenceRawTextSource RawText { get; }

    /// <summary>The uncached reader.</summary>
    internal LicenceCoverageSource Source { get; }

    /// <summary>The cache the window reads through.</summary>
    internal LicenceCoverageCache Cache { get; }

    /// <summary>The window under test.</summary>
    internal ILicenceEntitlements Window { get; }

    /// <summary>The licence file the harness reads.</summary>
    internal string LicenceFilePath
    {
        get { return Path.Combine(_directory, "licence.json"); }
    }

    /// <summary>Writes a licence file.</summary>
    /// <param name="text">The file's text.</param>
    internal void Install(string text)
    {
        File.WriteAllText(LicenceFilePath, text);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
