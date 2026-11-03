namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// One controller action a view-as token may use to change something, with the reason that change is
/// safe to make from a token that stands for somebody else.
/// </summary>
/// <param name="Action">The action as <c>ControllerType.MethodName</c>, e.g. <c>PinnedPagesController.PinAsync</c>.</param>
/// <param name="Reason">Why the change creates no credential, access path or work that outlives the look; never blank.</param>
public sealed record PinnedSafeMutation(string Action, string Reason);
