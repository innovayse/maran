namespace Maran.SharedKernel.Interfaces;

/// <summary>Injectable time source; DateTime.Now is forbidden (rules/csharp.md).</summary>
public interface IClock
{
    /// <summary>Current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
