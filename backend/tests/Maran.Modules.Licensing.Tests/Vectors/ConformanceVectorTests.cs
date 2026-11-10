using System.Globalization;
using System.Text.Json;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Vectors;

/// <summary>
/// Holds the open licence window to <c>docs/licensing/conformance-vectors.json</c>, the one artefact any
/// other implementation of the window (the closed one included) is told to match. Every vector is run
/// through the REAL file sources, verifier, lease check and coverage policy with the clock set to the
/// vector's instant, and must give exactly the verdict and end date the file lists.
/// </summary>
/// <remarks>
/// The vectors are signed with TEST seeds committed in this project; the file says so and carries no
/// production key. A vector run is only evidence if the set is not empty and spans every verdict, which
/// <see cref="The_vectors_span_every_verdict_and_use_the_test_keys"/> guards on the axis that can go blind.
/// </remarks>
public sealed class ConformanceVectorTests
{
    /// <summary>The path of the vectors file under the repository root.</summary>
    private const string RelativePath = "docs/licensing/conformance-vectors.json";

    /// <summary>Every vector in the file passes through the real window and gets the listed verdict.</summary>
    [Fact]
    public async Task Every_vector_gets_the_verdict_the_file_lists()
    {
        using var document = ReadVectors();
        var root = document.RootElement;
        var failures = new List<string>();

        foreach (var vector in root.GetProperty("vectors").EnumerateArray())
        {
            var entitlement = await RunAsync(root, vector);
            var expect = vector.GetProperty("expect");
            var expectedVerdict = expect.GetProperty("verdict").GetString();
            var expectedEnd = expect.GetProperty("validUntil").ValueKind == JsonValueKind.Null
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(expect.GetProperty("validUntil").GetString()!, CultureInfo.InvariantCulture);
            var actualVerdict = char.ToLowerInvariant(entitlement.Verdict.ToString()[0]) + entitlement.Verdict.ToString()[1..];

            if (!string.Equals(expectedVerdict, actualVerdict, StringComparison.Ordinal)
                || expectedEnd != entitlement.ValidUntil)
            {
                failures.Add(
                    $"{vector.GetProperty("name").GetString()}: expected {expectedVerdict}/{expectedEnd}, "
                    + $"got {actualVerdict}/{entitlement.ValidUntil}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The vacuity guard: the set is large enough, spans covered, lapsed and not-covered, and its keys are the
    /// TEST keys of this project (so the file can never smuggle in a production key as "the" key to trust).
    /// </summary>
    [Fact]
    public void The_vectors_span_every_verdict_and_use_the_test_keys()
    {
        using var document = ReadVectors();
        var root = document.RootElement;
        var verdicts = root.GetProperty("vectors").EnumerateArray()
            .Select(vector => { return vector.GetProperty("expect").GetProperty("verdict").GetString(); })
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(root.GetProperty("vectors").GetArrayLength() >= 15);
        Assert.Contains("covered", verdicts);
        Assert.Contains("lapsed", verdicts);
        Assert.Contains("notCovered", verdicts);
        Assert.Equal(
            Convert.ToHexString(TestKeys.TrustedKey.PublicKey).ToLowerInvariant(),
            root.GetProperty("licenceVerificationPublicKeyHex").GetString());
        Assert.Equal(
            Convert.ToHexString(TestKeys.TrustedLeaseKey.PublicKey).ToLowerInvariant(),
            root.GetProperty("leaseVerificationPublicKeyHex").GetString());
    }

    /// <summary>
    /// The control for the run above: a vector whose listed verdict is deliberately wrong IS reported, so the
    /// comparison is capable of failing. Flipping "covered" to "lapsed" on the first vector must not match.
    /// </summary>
    [Fact]
    public async Task A_vector_with_the_wrong_verdict_listed_does_not_match()
    {
        using var document = ReadVectors();
        var root = document.RootElement;
        var first = root.GetProperty("vectors").EnumerateArray().First();

        var entitlement = await RunAsync(root, first);

        Assert.Equal("covered", first.GetProperty("expect").GetProperty("verdict").GetString());
        Assert.NotEqual(EntitlementVerdict.Lapsed, entitlement.Verdict);
    }

    /// <summary>Runs one vector through the real sources and policy.</summary>
    /// <param name="root">The file's root, holding the keys and the host's machine id.</param>
    /// <param name="vector">The vector.</param>
    /// <returns>The window's answer for the vector's module.</returns>
    private static async Task<ModuleEntitlement> RunAsync(JsonElement root, JsonElement vector)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "conformance-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var licencePath = Path.Combine(directory, "licence.json");
            var leasePath = Path.Combine(directory, "lease.json");
            WriteIfPresent(licencePath, vector.GetProperty("licence"));
            WriteIfPresent(leasePath, vector.GetProperty("lease"));

            var clock = new MutableClock(
                DateTimeOffset.Parse(vector.GetProperty("now").GetString()!, CultureInfo.InvariantCulture));
            var identity = new FixedServerIdentitySource(root.GetProperty("hostMachineId").GetString());
            var licenceKey = LicenceVerificationKey.Of(
                Convert.FromHexString(root.GetProperty("licenceVerificationPublicKeyHex").GetString()!));
            var leaseKey = LeaseVerificationKey.Of(
                Convert.FromHexString(root.GetProperty("leaseVerificationPublicKeyHex").GetString()!));

            var source = new LicenceCoverageSource(
                new FileLicenceRawTextSource(
                    new Microsoft.Extensions.Options.OptionsWrapper<LicenceStorageOptions>(
                        new LicenceStorageOptions { LicenceFilePath = licencePath })),
                new LicenceVerifier(new Ed25519LicenceSignatureVerifier(licenceKey), clock, identity),
                new FileLeaseSource(
                    new Microsoft.Extensions.Options.OptionsWrapper<LeaseStorageOptions>(
                        new LeaseStorageOptions { LeaseFilePath = leasePath }),
                    new Ed25519LeaseSignatureVerifier(leaseKey)),
                identity,
                clock);

            var coverage = await source.ReadAsync(CancellationToken.None);

            return ModuleCoveragePolicy.VerdictOf(coverage, vector.GetProperty("module").GetString()!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Writes a file when the vector carries text for it.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="text">The vector's text member: a string, or null for "no file".</param>
    private static void WriteIfPresent(string path, JsonElement text)
    {
        if (text.ValueKind == JsonValueKind.String)
        {
            File.WriteAllText(path, text.GetString());
        }
    }

    /// <summary>Reads the vectors file from the repository, found by walking up from the test's directory.</summary>
    /// <returns>The parsed document; the caller disposes it.</returns>
    private static JsonDocument ReadVectors()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, RelativePath)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, RelativePath)));
    }
}
