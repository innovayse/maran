namespace Maran.Host.Demo.Common;

/// <summary>One published sign-in. Public by design: the demo's login page prints it.</summary>
/// <param name="Role">Either <c>administrator</c> or <c>customer</c>.</param>
/// <param name="Username">The login name.</param>
/// <param name="Password">The password, shared by every visitor.</param>
public sealed record DemoIdentityDto(string Role, string Username, string Password);
