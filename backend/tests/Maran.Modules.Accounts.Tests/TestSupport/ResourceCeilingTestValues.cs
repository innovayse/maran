using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>The wire ceilings the plan tests state, named so a command reads as what it asks for.</summary>
public static class ResourceCeilingTestValues
{
    /// <summary>"No ceiling", as an administrator sends it.</summary>
    public static ResourceCeilingDto Unlimited { get; } = new(ResourceLimitKind.Unlimited, 0);

    /// <summary>A ceiling at <paramref name="maximum"/>.</summary>
    /// <param name="maximum">The figure.</param>
    /// <returns>The limited wire ceiling.</returns>
    public static ResourceCeilingDto Limited(int maximum)
    {
        return new ResourceCeilingDto(ResourceLimitKind.Limited, maximum);
    }
}
