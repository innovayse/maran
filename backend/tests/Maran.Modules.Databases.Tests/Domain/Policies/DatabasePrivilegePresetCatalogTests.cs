using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Domain.Policies;

namespace Maran.Modules.Databases.Tests.Domain.Policies;

/// <summary>What each quick fill ticks.</summary>
public sealed class DatabasePrivilegePresetCatalogTests
{
    /// <summary>The catalog offers the three presets, each once.</summary>
    [Fact]
    public void The_catalog_offers_the_three_presets_each_once()
    {
        Assert.Equal(
            [DatabasePrivilegePresetKey.ReadOnly, DatabasePrivilegePresetKey.ReadWrite, DatabasePrivilegePresetKey.FullAccess],
            DatabasePrivilegePresetCatalog.All.Select(preset => { return preset.Key; }));
    }

    /// <summary>The full access preset is every privilege the panel can grant.</summary>
    [Fact]
    public void The_full_access_preset_is_every_privilege_the_panel_can_grant()
    {
        var full = DatabasePrivilegePresetCatalog.All.Single(preset => { return preset.Key == DatabasePrivilegePresetKey.FullAccess; });

        Assert.Equal(DatabasePrivilegeSets.All, full.Privileges);
    }

    /// <summary>The read only preset cannot change data or structure.</summary>
    [Fact]
    public void The_read_only_preset_cannot_change_data_or_structure()
    {
        var readOnly = DatabasePrivilegePresetCatalog.All.Single(preset => { return preset.Key == DatabasePrivilegePresetKey.ReadOnly; });

        Assert.Equal([DatabasePrivilege.Select, DatabasePrivilege.ShowView], readOnly.Privileges);
    }

    /// <summary>The read and write preset changes rows and nothing about the structure.</summary>
    [Fact]
    public void The_read_and_write_preset_changes_rows_and_nothing_about_the_structure()
    {
        var readWrite = DatabasePrivilegePresetCatalog.All.Single(preset => { return preset.Key == DatabasePrivilegePresetKey.ReadWrite; });

        Assert.Contains(DatabasePrivilege.Insert, readWrite.Privileges);
        Assert.DoesNotContain(DatabasePrivilege.Create, readWrite.Privileges);
        Assert.DoesNotContain(DatabasePrivilege.Drop, readWrite.Privileges);
        Assert.DoesNotContain(DatabasePrivilege.Alter, readWrite.Privileges);
    }

    /// <summary>No preset repeats a privilege.</summary>
    [Fact]
    public void No_preset_repeats_a_privilege()
    {
        Assert.All(DatabasePrivilegePresetCatalog.All, preset =>
        {
            Assert.Equal(preset.Privileges.Count, preset.Privileges.Distinct().Count());
        });
    }
}
