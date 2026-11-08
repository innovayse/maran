namespace Maran.ArchitectureTests;

/// <summary>
/// One line of the customer self-service matrix: an operation a customer can perform, the module that
/// serves it, and the integration test class that proves another customer's resource is answered not found.
/// </summary>
/// <param name="Key">The operation, <c>METHOD route</c>.</param>
/// <param name="Module">The module that serves it.</param>
/// <param name="IsolationClass">
/// The integration test class that holds the tenant-isolation case, or <c>null</c> when the operation
/// reaches no resource of another account and <paramref name="Reason"/> says why.
/// </param>
/// <param name="Reason">Why there is no isolation class; empty when there is one.</param>
public sealed record CustomerOperationEntry(string Key, string Module, string? IsolationClass, string Reason);
