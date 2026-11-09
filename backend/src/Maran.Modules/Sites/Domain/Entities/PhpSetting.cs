namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>
/// One php.ini setting a customer chose for their whole account (issue #138): a name from the
/// whitelist and the value they asked for, as php.ini spells it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per account, not per site.</b> A php-fpm pool belongs to an account at one PHP version, so two
/// sites of an account on one version share their pool and cannot carry different <c>php_value</c>
/// lines. Settings that applied to one site would have to be invented away at the second.
/// </para>
/// <para>
/// <b>The row is the customer's wish, not what the pool carries.</b> A plan edit can lower a bound
/// below a stored value; the row is kept and the pool is rendered with the value clamped to the plan
/// (<see cref="Policies.PhpSettingsPlanClamp"/>), so raising the bound again gives the wish back and no
/// plan edit ever deletes a customer's choice. The value is stored in php.ini's spelling (<c>256M</c>),
/// the spelling the agent is sent; the wire spelling a screen uses is
/// <see cref="Policies.PhpSettingSpelling"/>'s.
/// </para>
/// <para>
/// Tenant-scoped through its own <see cref="AccountId"/>, so another customer's settings answer 404.
/// </para>
/// </remarks>
public sealed class PhpSetting
{
    /// <summary>The longest setting name or value the table holds.</summary>
    public const int TextMaxLength = 64;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account whose pools carry the setting.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The setting's name, one of <c>PhpSettingNames</c>. Unique per account.</summary>
    public string Name { get; private set; }

    /// <summary>The value the customer asked for, in php.ini's spelling (<c>256M</c>, <c>120</c>, <c>Europe/Berlin</c>).</summary>
    public string Value { get; private set; }

    /// <summary>Creates a setting for an account.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account whose pools carry the setting.</param>
    /// <param name="name">The setting's name; the caller has checked it against the whitelist.</param>
    /// <param name="value">The value in php.ini's spelling; the caller has checked it.</param>
    public PhpSetting(Guid id, Guid accountId, string name, string value)
    {
        Id = id;
        AccountId = accountId;
        Name = name;
        Value = value;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private PhpSetting()
    {
        Name = string.Empty;
        Value = string.Empty;
    }

    /// <summary>Replaces the value the customer asked for.</summary>
    /// <param name="value">The new value in php.ini's spelling; the caller has checked it.</param>
    public void ChangeValue(string value)
    {
        Value = value;
    }
}
