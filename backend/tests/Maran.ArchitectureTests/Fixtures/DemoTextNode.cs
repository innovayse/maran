namespace Maran.ArchitectureTests.Fixtures;

/// <summary>A type that contains itself, as no command of the panel does: planted for the free-text depth census.</summary>
/// <param name="Child">The next node, which gives the type no bounded depth.</param>
/// <param name="Text">A text at this level.</param>
public sealed record DemoTextNode(DemoTextNode? Child, string Text);
