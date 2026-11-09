namespace Maran.Host.Demo.World.Enums;

/// <summary>The lifecycle state the demo world leaves a hosting account in.</summary>
public enum DemoAccountState
{
    /// <summary>The account is active, the ordinary state.</summary>
    Active = 0,

    /// <summary>The account is suspended after all its resources were created, so its screens show the state.</summary>
    Suspended = 1,
}
