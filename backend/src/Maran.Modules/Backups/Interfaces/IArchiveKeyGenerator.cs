namespace Maran.Modules.Backups.Interfaces;

/// <summary>The seam through which the panel draws the key a backup destination's archives are sealed with.</summary>
public interface IArchiveKeyGenerator
{
    /// <summary>Draws a new key.</summary>
    /// <returns>64 lowercase hexadecimal characters.</returns>
    string Mint();
}
