using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>A template the panel ships with, or the one new sites use, is never deleted; any other is.</summary>
public sealed class ZoneTemplateDeletionPolicyTests
{
    /// <summary>The instant templates are created.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a template.</summary>
    /// <param name="builtIn">Whether it ships with the panel.</param>
    /// <returns>The template.</returns>
    private static ZoneTemplate Template(bool builtIn)
    {
        return new ZoneTemplate(Guid.NewGuid(), "t", string.Empty, builtIn, Now);
    }

    /// <summary>A built-in template is refused whatever the default is.</summary>
    [Fact]
    public void Deleting_a_built_in_template_is_refused()
    {
        var template = Template(builtIn: true);

        Assert.Equal(TemplateDeletionVerdict.BuiltIn, ZoneTemplateDeletionPolicy.Check(template, null));
        Assert.Equal(TemplateDeletionVerdict.BuiltIn, ZoneTemplateDeletionPolicy.Check(template, Guid.NewGuid()));
    }

    /// <summary>Inverse control: a template the administrator made is deleted when it is not the default.</summary>
    [Fact]
    public void Deleting_a_custom_template_is_allowed()
    {
        Assert.Equal(TemplateDeletionVerdict.Allowed, ZoneTemplateDeletionPolicy.Check(Template(false), null));
        Assert.Equal(
            TemplateDeletionVerdict.Allowed, ZoneTemplateDeletionPolicy.Check(Template(false), Guid.NewGuid()));
    }

    /// <summary>The default template is refused, so the setting never names a row that is gone.</summary>
    [Fact]
    public void Deleting_the_default_template_is_refused()
    {
        var template = Template(builtIn: false);

        Assert.Equal(TemplateDeletionVerdict.IsDefault, ZoneTemplateDeletionPolicy.Check(template, template.Id));
    }
}
