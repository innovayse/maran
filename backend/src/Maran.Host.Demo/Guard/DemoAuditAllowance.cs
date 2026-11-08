using Maran.Host.Demo.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Bounds what the guard may write to the audit journal in one reset window, so a script cannot fill
/// the database with refusals (threat note T6): a capped number of refusals, and the first exhaustion once.
/// </summary>
public sealed class DemoAuditAllowance
{
    /// <summary>Guards the counters.</summary>
    private readonly object _gate = new();

    /// <summary>Refusal rows still allowed.</summary>
    private int _refusalsLeft;

    /// <summary>Whether the exhaustion row was already taken.</summary>
    private bool _exhaustionTaken;

    /// <summary>Creates the allowance.</summary>
    /// <param name="options">The settings.</param>
    public DemoAuditAllowance(IOptions<DemoOptions> options)
    {
        _refusalsLeft = options.Value.AuditedRefusalsPerWindow;
    }

    /// <summary>Takes one refusal row.</summary>
    /// <returns><c>true</c> when the refusal may be journaled.</returns>
    public bool TryTakeRefusal()
    {
        lock (_gate)
        {
            if (_refusalsLeft <= 0)
            {
                return false;
            }

            _refusalsLeft--;
            return true;
        }
    }

    /// <summary>Takes the one row that records the global budget running out.</summary>
    /// <returns><c>true</c> only for the first caller.</returns>
    public bool TryTakeExhaustion()
    {
        lock (_gate)
        {
            if (_exhaustionTaken)
            {
                return false;
            }

            _exhaustionTaken = true;
            return true;
        }
    }
}
