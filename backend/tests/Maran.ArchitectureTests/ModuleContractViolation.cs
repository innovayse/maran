namespace Maran.ArchitectureTests;

/// <summary>One way one module breaks the module contract.</summary>
/// <param name="Rule">Which rule is broken.</param>
/// <param name="ModuleId">The module the violation is about.</param>
/// <param name="Detail">The offending value or relationship, in words a reader can act on.</param>
public sealed record ModuleContractViolation(ModuleContractRule Rule, string ModuleId, string Detail)
{
    /// <summary>The violation as one line of a failure message.</summary>
    /// <returns>The rule name, the module and the detail.</returns>
    public string Describe()
    {
        return $"{Rule}: module '{ModuleId}' {Detail}";
    }
}
