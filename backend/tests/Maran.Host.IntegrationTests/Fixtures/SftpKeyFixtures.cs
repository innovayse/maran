namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Real OpenSSH public keys, generated once with <c>ssh-keygen</c>, and the fingerprints
/// <c>ssh-keygen -l</c> printed for them, so a test holds the panel's own fingerprint computation
/// against the tool that defines it rather than against itself.
/// </summary>
/// <remarks>
/// Only the public halves are kept; the private halves were discarded and are not recoverable. The
/// lines carry their comment (<c>first@laptop</c> and so on) on purpose: the panel must drop it, and
/// a test asserts that it does.
/// </remarks>
public static class SftpKeyFixtures
{
    /// <summary>An Ed25519 key line, comment included.</summary>
    public const string Ed25519 =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIUJRnDAZEkPKAlf7FOOY4fWWstilA67ufNEywcO3LFG first@laptop";

    /// <summary>The <c>ssh-keygen -l</c> fingerprint of <see cref="Ed25519"/>.</summary>
    public const string Ed25519Fingerprint = "SHA256:aC7tYbUy6IN3AgUI2TdNgyyEQXA2c7fnctExMecqzDU";

    /// <summary>The key blob of <see cref="Ed25519"/> as the panel stores it: no comment.</summary>
    public const string Ed25519Stored =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIUJRnDAZEkPKAlf7FOOY4fWWstilA67ufNEywcO3LFG";

    /// <summary>An ECDSA P-256 key line, comment included.</summary>
    public const string Ecdsa =
        "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBP+T+zOA3Lrbay+iMdS3SP576714NWFZSvvCjDKsCiqAJhRaKnYnv9xC1+yky8/zdR3HpKAE033d3sD4hNSvhm8= second@laptop";

    /// <summary>The key blob of <see cref="Ecdsa"/> as the panel stores it: no comment.</summary>
    public const string EcdsaStored =
        "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBP+T+zOA3Lrbay+iMdS3SP576714NWFZSvvCjDKsCiqAJhRaKnYnv9xC1+yky8/zdR3HpKAE033d3sD4hNSvhm8=";

    /// <summary>The <c>ssh-keygen -l</c> fingerprint of <see cref="Ecdsa"/>.</summary>
    public const string EcdsaFingerprint = "SHA256:Lf7Ee6+vviah2RBFycyZgwdi/lKeAeSvYWi2PC6ItH8";

    /// <summary>An RSA 2048-bit key line, comment included; 2048 is the smallest modulus the agent accepts.</summary>
    public const string Rsa =
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQDI82cdzF5NeelYzdhavtiiKDvwej0HUIjXEYXpP7O9a7wLtIrwWFPCP7I+SdFLLX7bPD0VV1sxRBPqygcgL8incwq0DV/vyMKmvFkJzLlDFSizzxtmZpt9Xk1ZlUlw1TDPHgXgFM0gTl+OEKTXLpy+MjALFy0X3zufM8lOhZVq6BPfIYjr73GSz7M0YqhyvSECHiSZ1LLC4YcWMbXPJ0p/J+Xq05R2aM1/nysMMv+O9//KlE5l7v5GTFtXrHqwTdydi06Bb2sLwxQkpbMZkSZtdV5CcodepC5UW21e2LlNGAlsChTxi7ced6lKA50EQ8xJ97pmm5RESmaaX6KF46Hd third@laptop";

    /// <summary>The <c>ssh-keygen -l</c> fingerprint of <see cref="Rsa"/>.</summary>
    public const string RsaFingerprint = "SHA256:knX2t9bbQ1LeH5HX5e9gTCjM3Wmubfuy+O8HBimeros";

    /// <summary>
    /// A second Ed25519 key line, for the tests that need a key different from <see cref="Ed25519"/>.
    /// </summary>
    public const string OtherEd25519 =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEokM0DXej70Zdrn2jxIb4s4FyXxhoYPW5EZuCNNxgCl other@laptop";

    /// <summary>An RSA 1024-bit key line: well formed, and below the 2048-bit floor the agent enforces.</summary>
    public const string Rsa1024 =
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAAAgQDQXXoQ3mSH0oZ3BJ/d9Mp6IM+GCgO4slMPqaSTH53hftUJJbMDKRpWrSsgHCy6zIJ2byhOcY1mJYX7lX646QovZzl7bVryjrHSZkn7mEfVkaLhCVkLSb639n7mX+Suz9C4Cmcm+Z1nzYHmo1bSaRmkHmJi7qo2RRnUI28n/yI2iQ== weak@laptop";
}
