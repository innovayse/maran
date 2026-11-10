namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Why an import was refused, for the cases the panel tells the operator apart.</summary>
public enum BackupImportRefusal
{
    /// <summary>The failure is not a refusal of the package: this host failed, or the reason is one this build does not know.</summary>
    Unspecified = 0,

    /// <summary>The signature does not verify over the manifest, or the key is not an ECDSA P-384 one.</summary>
    SignatureInvalid = 1,

    /// <summary>The public key's fingerprint is not the one the operator confirmed.</summary>
    FingerprintMismatch = 2,

    /// <summary>The manifest is not one the agent understands.</summary>
    ManifestMalformed = 3,

    /// <summary>A system user of the manifest's account name already exists on this host.</summary>
    AccountExists = 4,

    /// <summary>A backup with the manifest's id is already published with another digest.</summary>
    BackupIdTaken = 5,

    /// <summary>The declared size is over the ceiling, or the host has no room for it.</summary>
    TooLarge = 6,

    /// <summary>The stream carried a different number of bytes than the signed manifest declares.</summary>
    LengthMismatch = 7,

    /// <summary>The stream's SHA-256 differs from the signed one.</summary>
    DigestMismatch = 8,

    /// <summary>The artifact is not one this host will publish: a member outside the layout, a disagreeing embedded manifest, a missing dump or a sealed artifact.</summary>
    ArtifactRefused = 9,
}
