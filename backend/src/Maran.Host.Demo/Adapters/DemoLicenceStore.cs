using Maran.Modules.Licensing.Interfaces;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// Where the demo keeps its installed licence: in memory, so the demo process reads and writes no licence
/// file (threat note T4). It replaces both the module's file-backed source and its file-backed writer.
/// </summary>
public sealed class DemoLicenceStore : ILicenceRawTextSource, ILicenceWriter
{
    /// <summary>Guards <see cref="_text"/>.</summary>
    private readonly object _gate = new();

    /// <summary>The installed licence text, or <c>null</c> when none is installed.</summary>
    private string? _text;

    /// <summary>Reads the installed licence text.</summary>
    /// <returns>The text, or <c>null</c> when nothing is installed.</returns>
    public string? ReadRawLicenceText()
    {
        lock (_gate)
        {
            return _text;
        }
    }

    /// <summary>Keeps the licence text in memory, replacing any previous one.</summary>
    /// <param name="rawLicenceText">The text that already verified.</param>
    /// <param name="cancellationToken">Cancellation token; nothing here waits.</param>
    /// <returns>A completed task.</returns>
    public Task InstallAsync(string rawLicenceText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rawLicenceText);

        lock (_gate)
        {
            _text = rawLicenceText;
        }

        return Task.CompletedTask;
    }
}
