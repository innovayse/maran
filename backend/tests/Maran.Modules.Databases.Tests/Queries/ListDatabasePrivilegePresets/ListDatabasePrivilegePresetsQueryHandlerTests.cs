using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Queries.ListDatabasePrivilegePresets;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Queries.ListDatabasePrivilegePresets;

/// <summary>The catalogue the privileges editor renders, none of which the SPA holds itself.</summary>
public sealed class ListDatabasePrivilegePresetsQueryHandlerTests
{
    /// <summary>The catalogue lists the eighteen privileges and the three presets.</summary>
    [Fact]
    public async Task The_catalogue_lists_the_eighteen_privileges_and_the_three_presets()
    {
        var result = await HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(18, result.Value.Privileges.Count);
        Assert.Equal(3, result.Value.Presets.Count);
    }

    /// <summary>Every name is a real translation and not a resource key.</summary>
    [Fact]
    public async Task Every_name_is_a_real_translation_and_not_a_resource_key()
    {
        var result = await HandleAsync();

        Assert.All(result.Value.Privileges, privilege =>
        {
            Assert.False(string.IsNullOrWhiteSpace(privilege.Name));
            Assert.DoesNotContain("DatabasePrivilege", privilege.Name, StringComparison.Ordinal);
        });
        Assert.All(result.Value.Presets, preset =>
        {
            Assert.DoesNotContain("DatabasePrivilegePreset", preset.Name, StringComparison.Ordinal);
        });
    }

    /// <summary>Names follow the request culture.</summary>
    [Fact]
    public async Task Names_follow_the_request_culture()
    {
        using (new CultureScope("ru"))
        {
            var result = await HandleAsync();

            Assert.Equal("Только чтение", result.Value.Presets.Single(preset => { return preset.Key == DatabasePrivilegePresetKey.ReadOnly; }).Name);
        }
    }

    /// <summary>Privileges that could widen an account beyond its own databases cannot appear.</summary>
    [Fact]
    public async Task Privileges_that_could_widen_an_account_beyond_its_own_databases_cannot_appear()
    {
        var result = await HandleAsync();

        var keys = result.Value.Privileges.Select(privilege => { return privilege.Key.ToString(); }).ToList();
        Assert.DoesNotContain(keys, key => { return key is "GrantOption" or "File" or "Process" or "Super" or "Reload"; });
    }

    /// <summary>Runs the handler.</summary>
    private static async Task<Result<DatabasePrivilegeCatalogueDto>> HandleAsync()
    {
        return await new ListDatabasePrivilegePresetsQueryHandler(DatabasesTestContext.PrivilegeNames())
            .HandleAsync(new ListDatabasePrivilegePresetsQuery(), CancellationToken.None);
    }
}
