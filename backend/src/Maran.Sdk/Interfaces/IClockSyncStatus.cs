namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers one question: did the latest reading find the host's clock not synchronised with a time
/// source (issue #150)? Implemented by the Monitoring module, which takes that reading every
/// sampling round; read by Identity, so a rejected two-factor code can say the clock may be why.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never asks the agent.</b> It answers from the latest round's stored reading, so a flood of
/// rejected sign-ins cannot become a flood of programs started as root. The price is that the answer
/// can lag by one sampling interval, or longer while the agent is unreachable — acceptable for a
/// hint attached to a code that was rejected either way.
/// </para>
/// <para>
/// <b>A reading that was never taken answers <c>false</c>.</b> "Not known" must not put a clock
/// warning in front of a person whose code was simply wrong.
/// </para>
/// </remarks>
public interface IClockSyncStatus
{
    /// <summary>Reads whether the latest reading found the host's clock not synchronised.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>
    /// <c>true</c> only when the latest successful reading found the clock not synchronised;
    /// <c>false</c> when it was synchronised or when no reading has been taken.
    /// </returns>
    Task<bool> IsClockUnsynchronizedAsync(CancellationToken cancellationToken);
}
