namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// One controller action the panel deliberately serves to a caller who is not signed in, with the
/// reason that is safe.
/// </summary>
/// <param name="Action">The action as <c>ControllerType.MethodName</c>, e.g. <c>AuthController.LoginAsync</c>.</param>
/// <param name="Reason">Why nobody needs to be authenticated to call it; never blank.</param>
public sealed record PinnedAnonymousAction(string Action, string Reason);
