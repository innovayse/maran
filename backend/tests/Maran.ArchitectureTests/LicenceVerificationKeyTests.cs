using System.Text.RegularExpressions;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Services;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Maran.ArchitectureTests;

/// <summary>
/// Makes it a build failure for the licence verifier to trust a key whose private half is readable in
/// this repository (issue #470).
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> The verifier embedded a public key whose private half was committed in the
/// licensing test project, so anyone who could read the repository could mint a licence that verified.
/// The fix is structural: the production verifier holds no key in source at all. The release embeds one
/// (<c>installer/keys/licence-verification.pub</c>) or the build trusts nothing, and a test injects its
/// own key through the verifier's constructor. This census holds the other half of that promise: whatever
/// key a release does embed must not be the public half of any 32-byte secret written down as a 64
/// hexadecimal literal under <c>backend/</c>.
/// </para>
/// <para>
/// UNOBSERVED HERE: a private key kept outside <c>backend/</c> (another directory, another repository, a
/// binary file) or written in a form other than a 64-character hexadecimal literal. The scan cannot see
/// those; the key ceremony (generate offline, keep the private half with the issuing service only) is what
/// covers them, and the threat note says so.
/// </para>
/// </remarks>
public sealed class LicenceVerificationKeyTests
{
    /// <summary>A 32-byte secret written as 64 hexadecimal characters inside a string literal.</summary>
    private static readonly Regex SeedLiteral = new("\"([0-9A-Fa-f]{64})\"", RegexOptions.Compiled);

    /// <summary>The production key, if the release embedded one, is not derived from any committed secret.</summary>
    [Fact]
    public void The_production_licence_key_is_not_the_public_half_of_a_committed_private_key()
    {
        var offenders = Offenders(EmbeddedLicenceVerificationKeySource.Read(), CommittedSeeds());

        Assert.True(
            offenders.Count == 0,
            "The embedded licence verification key is the public half of a private key committed in "
            + $"{string.Join(", ", offenders)}. Anyone can mint a licence that verifies; generate a fresh key pair offline.");
    }

    /// <summary>
    /// The production LEASE key is not the public half of a private key committed in the repository either
    /// (issue #8, decision D3: a separate key, on the same terms as the licence key). With no embedded lease
    /// key the answer is "no key" and nothing is offended.
    /// </summary>
    [Fact]
    public void The_production_lease_key_is_not_the_public_half_of_a_committed_private_key()
    {
        var lease = EmbeddedLeaseVerificationKeySource.Read();
        var offenders = lease.IsConfigured ? OffendersOf(lease.PublicKey, CommittedSeeds()) : [];

        Assert.True(
            offenders.Count == 0,
            "The embedded lease verification key is the public half of a private key committed in "
            + $"{string.Join(", ", offenders)}. Anyone can mint a lease that verifies; generate a fresh key pair offline.");
    }

    /// <summary>
    /// The lease key and the licence key are different keys (decision D3), so a leaked lease key cannot mint
    /// licences and the reverse. Two builds' worth of embedded keys must not be the same bytes.
    /// </summary>
    [Fact]
    public void The_production_lease_key_is_never_the_production_licence_key()
    {
        var lease = EmbeddedLeaseVerificationKeySource.Read();
        var licence = EmbeddedLicenceVerificationKeySource.Read();

        Assert.False(lease.IsConfigured && licence.IsConfigured && lease.PublicKey.SequenceEqual(licence.PublicKey));
    }

    /// <summary>
    /// The inverse control of the test above: the check does refuse equal keys. A planted licence key reused
    /// as a lease key is the violation; two different generated keys are accepted.
    /// </summary>
    [Fact]
    public void Equal_lease_and_licence_keys_are_the_violation_and_different_ones_are_not()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var first = ((Ed25519PublicKeyParameters)generator.GenerateKeyPair().Public).GetEncoded();
        var second = ((Ed25519PublicKeyParameters)generator.GenerateKeyPair().Public).GetEncoded();

        Assert.True(LeaseVerificationKey.Of(first).PublicKey.SequenceEqual(LicenceVerificationKey.Of(first).PublicKey));
        Assert.False(LeaseVerificationKey.Of(first).PublicKey.SequenceEqual(LicenceVerificationKey.Of(second).PublicKey));
    }

    /// <summary>The scan finds the committed test secrets, so an empty result above means something.</summary>
    /// <remarks>The positive control: a scan that matched nothing would let the test above pass over nothing.</remarks>
    [Fact]
    public void The_scan_finds_the_private_keys_the_test_projects_commit()
    {
        var seeds = CommittedSeeds();

        Assert.Contains(
            seeds.Keys,
            file => { return file.Contains("Maran.Modules.Licensing.Tests", StringComparison.Ordinal); });
    }

    /// <summary>A key derived from a committed private key is named as an offender.</summary>
    /// <remarks>The planted break: the exact situation issue #470 reported, with a committed secret's own public half.</remarks>
    [Fact]
    public void A_key_derived_from_a_committed_private_key_is_refused()
    {
        var seeds = CommittedSeeds();
        var (file, seed) = seeds.SelectMany(entry =>
        {
            return entry.Value.Select(value =>
            {
                return (entry.Key, value);
            });
        }).First();
        var planted = LicenceVerificationKey.Of(PublicHalf(seed));

        var offenders = Offenders(planted, seeds);

        Assert.Contains(file, offenders);
    }

    /// <summary>A freshly generated key, and the no-key state, are accepted.</summary>
    /// <remarks>The accepting control: a check mutated to refuse everything would pass every test that only plants a bad key.</remarks>
    [Fact]
    public void A_fresh_key_and_the_no_key_state_are_accepted()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var fresh = LicenceVerificationKey.Of(((Ed25519PublicKeyParameters)generator.GenerateKeyPair().Public).GetEncoded());
        var seeds = CommittedSeeds();

        Assert.Empty(Offenders(fresh, seeds));
        Assert.Empty(Offenders(LicenceVerificationKey.None, seeds));
    }

    /// <summary>Which committed files hold a seed whose public half is the given key.</summary>
    /// <param name="trusted">The raw 32-byte public key a build would trust.</param>
    /// <param name="seeds">The seeds found per file.</param>
    /// <returns>The relative paths of the offending files, ordered.</returns>
    private static List<string> OffendersOf(
        byte[] trusted,
        IReadOnlyDictionary<string, IReadOnlyList<string>> seeds)
    {
        return seeds
            .Where(entry => { return entry.Value.Any(seed => { return PublicHalf(seed).SequenceEqual(trusted); }); })
            .Select(entry => { return entry.Key; })
            .OrderBy(file => { return file; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Names the files holding a secret whose public half is the given key.</summary>
    /// <param name="key">The key a verifier would trust.</param>
    /// <param name="seeds">Each file under <c>backend/</c> and the 64-hexadecimal literals it contains.</param>
    /// <returns>The offending files; empty when the key is none or matches no committed secret.</returns>
    private static List<string> Offenders(
        LicenceVerificationKey key,
        IReadOnlyDictionary<string, IReadOnlyList<string>> seeds)
    {
        if (!key.IsConfigured)
        {
            return [];
        }

        return OffendersOf(key.PublicKey, seeds);
    }

    /// <summary>Derives the Ed25519 public key a 32-byte secret belongs to.</summary>
    /// <param name="seedHex">The secret, 64 hexadecimal characters.</param>
    /// <returns>The raw 32-byte public key.</returns>
    private static byte[] PublicHalf(string seedHex)
    {
        return new Ed25519PrivateKeyParameters(Convert.FromHexString(seedHex), 0).GeneratePublicKey().GetEncoded();
    }

    /// <summary>Reads every 64-hexadecimal string literal in the backend's C# sources.</summary>
    /// <returns>Repository-relative file path to the literals that file contains; files with none are omitted.</returns>
    private static Dictionary<string, IReadOnlyList<string>> CommittedSeeds()
    {
        var root = SpaLocaleKeys.RepositoryRoot();
        var backend = Path.Combine(root, "backend");

        return Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(path => { return !IsBuildOutput(path); })
            .Select(path =>
            {
                IReadOnlyList<string> literals = SeedLiteral.Matches(File.ReadAllText(path))
                    .Select(match => { return match.Groups[1].Value; })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return (Path: Path.GetRelativePath(root, path), Literals: literals);
            })
            .Where(entry => { return entry.Literals.Count > 0; })
            .ToDictionary(entry => { return entry.Path; }, entry => { return entry.Literals; });
    }

    /// <summary>Whether a path is under a build output folder.</summary>
    /// <param name="path">The file path.</param>
    /// <returns><c>true</c> for <c>bin</c> and <c>obj</c> content.</returns>
    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
    }
}
