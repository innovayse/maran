using Maran.Modules.Marketplace.Commands.InstallModule;
using Maran.Modules.Marketplace.Commands.RemoveModule;
using Maran.Modules.Marketplace.Commands.RestartForModule;
using Maran.Modules.Marketplace.Commands.RollbackModule;
using Maran.Modules.Marketplace.Commands.UpdateModule;
using Maran.Modules.Marketplace.Queries.GetModuleDetails;
using Maran.Modules.Marketplace.Queries.ListCatalogue;

namespace Maran.Modules.Marketplace.Tests.Validators;

/// <summary>
/// The shape rules of the module's inputs: a module id and a version are plain before they reach a URL, a
/// lock or an audit entry. Each refusing input is paired with a nearby input that is accepted.
/// </summary>
public sealed class OperationValidatorsTests
{
    /// <summary>The error codes a validation failure carries, in order.</summary>
    /// <param name="result">The validation result.</param>
    /// <returns>The messages, which are the codes.</returns>
    private static string[] CodesOf(FluentValidation.Results.ValidationResult result)
    {
        return result.Errors.Select(error => { return error.ErrorMessage; }).ToArray();
    }

    /// <summary>Module ids that are not one to sixty-four lower-case letters or digits starting with a letter are refused by every operation.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="valid">Whether it is acceptable.</param>
    [Theory]
    [InlineData("mail", true)]
    [InlineData("mail2", true)]
    [InlineData("", false)]
    [InlineData("2mail", false)]
    [InlineData("Mail", false)]
    [InlineData("ma-il", false)]
    [InlineData("ma il", false)]
    [InlineData("../etc", false)]
    public void A_module_id_must_be_plain(string id, bool valid)
    {
        var results = new[]
        {
            new InstallModuleCommandValidator().Validate(new InstallModuleCommand("1.0.0", true, "x", id)),
            new UpdateModuleCommandValidator().Validate(new UpdateModuleCommand("1.0.0", true, "x", id)),
            new RemoveModuleCommandValidator().Validate(new RemoveModuleCommand("x", id)),
            new RollbackModuleCommandValidator().Validate(new RollbackModuleCommand("x", id)),
            new RestartForModuleCommandValidator().Validate(new RestartForModuleCommand(id)),
            new GetModuleDetailsQueryValidator().Validate(new GetModuleDetailsQuery(id)),
        };

        Assert.All(results, result =>
        {
            Assert.Equal(valid, result.IsValid);
            Assert.True(valid || CodesOf(result).Contains("ModuleIdInvalid"));
        });
    }

    /// <summary>A version must be a strict semantic version; a loose spelling is refused, and a precise one is accepted.</summary>
    /// <param name="version">The version.</param>
    /// <param name="valid">Whether it is acceptable.</param>
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3-beta.1", true)]
    [InlineData("1.2", false)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    [InlineData("1.2.3 ", false)]
    public void A_version_must_be_a_semantic_version(string version, bool valid)
    {
        var install = new InstallModuleCommandValidator().Validate(new InstallModuleCommand(version, true, "x", "mail"));
        var update = new UpdateModuleCommandValidator().Validate(new UpdateModuleCommand(version, true, "x", "mail"));

        Assert.Equal(valid, install.IsValid);
        Assert.Equal(valid, update.IsValid);
        Assert.True(valid || CodesOf(install).Contains("ModuleVersionInvalid"));
    }

    /// <summary>The catalogue query refuses a channel, search term or cursor that is not plain, and accepts ordinary ones and absent ones.</summary>
    [Fact]
    public void The_catalogue_query_inputs_must_be_plain()
    {
        var validator = new ListCatalogueQueryValidator();

        Assert.True(validator.Validate(new ListCatalogueQuery()).IsValid);
        Assert.True(validator.Validate(new ListCatalogueQuery("stable", "mail server", "cursor-1")).IsValid);
        Assert.Equal("MarketplaceChannelInvalid", Assert.Single(CodesOf(validator.Validate(new ListCatalogueQuery("Stable/../x")))));
        Assert.Equal("MarketplaceSearchInvalid", Assert.Single(CodesOf(validator.Validate(new ListCatalogueQuery(Search: "a\nb")))));
        Assert.Equal("MarketplaceSearchInvalid", Assert.Single(CodesOf(validator.Validate(new ListCatalogueQuery(Search: new string('a', 101))))));
        Assert.Equal("MarketplaceCursorInvalid", Assert.Single(CodesOf(validator.Validate(new ListCatalogueQuery(Cursor: new string('a', 257))))));
    }
}
