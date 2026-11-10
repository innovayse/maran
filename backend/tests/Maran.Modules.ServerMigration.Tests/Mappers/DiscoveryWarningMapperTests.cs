using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Tests.TestSupport;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>A stored warning goes on the wire with its sentence in the caller's language.</summary>
public sealed class DiscoveryWarningMapperTests
{
    /// <summary>The warning keeps its kind and subject, and the sentence is the kind's.</summary>
    [Fact]
    public void The_sentence_is_the_kinds()
    {
        var warning = new DiscoveryWarning(Guid.NewGuid(), Guid.NewGuid(), DiscoveryWarningKind.NameRefused, "bad name");

        var dto = DiscoveryWarningMapper.From(warning, new EchoDisplayNames());

        Assert.Equal(DiscoveryWarningKind.NameRefused, dto.Kind);
        Assert.Equal("bad name", dto.Subject);
        Assert.Equal("DiscoveryWarningNameRefused", dto.Message);
    }

    /// <summary>Every kind asks for its own sentence.</summary>
    [Fact]
    public void Every_kind_asks_for_its_own_sentence()
    {
        var keys = Enum.GetValues<DiscoveryWarningKind>().Select(DiscoveryWarningMapper.KeyOf).ToList();

        Assert.Equal(Enum.GetValues<DiscoveryWarningKind>().Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A kind outside the closed set is refused rather than worded as another.</summary>
    [Fact]
    public void A_kind_outside_the_closed_set_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return DiscoveryWarningMapper.KeyOf((DiscoveryWarningKind)99);
        });
    }
}
