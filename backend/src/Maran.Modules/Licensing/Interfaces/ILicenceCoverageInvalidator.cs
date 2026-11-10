namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Tells the licence window its remembered answer is stale. The install command calls it after the new
/// licence is on disk, so the very next request sees the new verdict; a failed install does not call
/// it and the previous verdict stands.
/// </summary>
public interface ILicenceCoverageInvalidator
{
    /// <summary>Forgets the remembered coverage; the next question reads the licence again.</summary>
    void Invalidate();
}
