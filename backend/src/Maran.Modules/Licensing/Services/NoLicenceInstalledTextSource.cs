using Maran.Modules.Licensing.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// The only <see cref="ILicenceRawTextSource"/> this slice ships: there is no persistence yet
/// (out of scope for this pass — see the interface's own remarks), so the honest answer is always
/// "nothing installed".
/// </summary>
public sealed class NoLicenceInstalledTextSource : ILicenceRawTextSource
{
    /// <inheritdoc />
    /// <returns>Always <c>null</c>: this build has no location a licence file or row could live in.</returns>
    public string? ReadRawLicenceText()
    {
        return null;
    }
}
