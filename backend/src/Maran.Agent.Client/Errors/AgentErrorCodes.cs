namespace Maran.Agent.Client.Errors;

/// <summary>
/// The codes the agent client gives an agent refusal that a module has to recognise by name.
/// </summary>
/// <remarks>
/// Each value is the key of an entry in this project's <c>ErrorMessages</c> table, which is
/// internal and so cannot be named from a module. Spelling a code once here, where the table is,
/// replaces the private copy every module used to keep; a test reads each constant back from the
/// table, so a rename over there fails the build of the tests rather than switching a branch off
/// silently. A code appears here when a module needs it, not as a mirror of the whole table.
/// </remarks>
public static class AgentErrorCodes
{
    /// <summary>
    /// The thing being read, changed or removed is not on the host. A module that removes something
    /// treats it as already done; one that reads it answers a missing resource of its own.
    /// </summary>
    public const string NotFound = "AgentNotFound";
}
