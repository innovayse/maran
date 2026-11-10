using Maran.Modules.ServerMigration.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>A grant ledger that answers what a test tells it and counts what it was asked.</summary>
public sealed class ScriptedGrantLedger : IExportGrantLedger
{
    /// <summary>Whether a manifest read is allowed.</summary>
    public bool ManifestAllowed { get; set; } = true;

    /// <summary>Whether the artifact read is allowed.</summary>
    public bool ArtifactAllowed { get; set; } = true;

    /// <summary>How many manifest reads were asked for.</summary>
    public int ManifestAsked { get; private set; }

    /// <summary>How many artifact reads were asked for.</summary>
    public int ArtifactAsked { get; private set; }

    /// <inheritdoc />
    public Task<bool> CountManifestReadAsync(Guid grantId, CancellationToken cancellationToken)
    {
        ManifestAsked++;

        return Task.FromResult(ManifestAllowed);
    }

    /// <inheritdoc />
    public Task<bool> TakeArtifactReadAsync(Guid grantId, CancellationToken cancellationToken)
    {
        ArtifactAsked++;

        return Task.FromResult(ArtifactAllowed);
    }
}
