namespace Maran.Host.Demo.Seeding.Exceptions;

/// <summary>
/// A seed step could not do what the world asks. Raised, not returned: a half-seeded demonstration
/// must stop the command that is building it, and the message names the step and the failure code,
/// never a secret.
/// </summary>
public sealed class DemoSeedException : Exception
{
    /// <summary>The step that failed.</summary>
    public string Step { get; }

    /// <summary>The panel's failure code, or a short description when the failure had none.</summary>
    public string Code { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="step">The step that failed.</param>
    /// <param name="code">The failure code or a short description.</param>
    public DemoSeedException(string step, string code)
        : base($"Seed step '{step}' failed: {code}.")
    {
        Step = step;
        Code = code;
    }
}
