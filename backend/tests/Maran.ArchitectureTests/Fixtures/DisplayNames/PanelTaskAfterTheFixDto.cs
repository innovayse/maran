namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The inverse control for the display-name law: a DTO the law must ACCEPT, shaped the way the
/// tasks screen was fixed.
/// </summary>
/// <remarks>
/// A gate mutated to refuse everything passes every test that only ever hands it broken input
/// (rules/testing.md, "A refusing gate needs an inverse control"). This type is the input it must
/// let through: the machine-stable <c>Kind</c> kept for scripts and support tickets, and
/// <c>KindDisplayName</c> beside it for the column an operator reads.
/// </remarks>
/// <param name="Id">The task's identity.</param>
/// <param name="Kind">The operation, machine-stable.</param>
/// <param name="KindDisplayName">The same operation in the caller's language.</param>
/// <param name="Subject">What it acts on.</param>
public sealed record PanelTaskAfterTheFixDto(Guid Id, string Kind, string KindDisplayName, string Subject);
