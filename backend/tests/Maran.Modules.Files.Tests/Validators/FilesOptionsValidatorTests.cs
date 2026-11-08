using Maran.Modules.Files.Options;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Tests.Validators;

/// <summary>Holds <see cref="FilesOptionsValidator"/> to refusing a value that would disable a protection.</summary>
public sealed class FilesOptionsValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly FilesOptionsValidator _validator = new();

    /// <summary>The shipped defaults are accepted, so the refusals below are not a validator that refuses everything.</summary>
    [Fact]
    public void The_default_options_are_valid()
    {
        Assert.True(_validator.Validate(null, new FilesOptions()).Succeeded);
    }

    /// <summary>An upload ceiling above the agent's own is refused.</summary>
    [Fact]
    public void An_upload_limit_above_the_agents_ceiling_is_refused()
    {
        var options = new FilesOptions { MaxUploadBytes = FilesOptionsValidator.AgentUploadCeilingBytes + 1 };

        Assert.True(_validator.Validate(null, options).Failed);
    }

    /// <summary>An edit limit above the download limit contradicts itself.</summary>
    [Fact]
    public void An_edit_limit_above_the_download_limit_is_refused()
    {
        var options = new FilesOptions { MaxEditBytes = 10, MaxDownloadBytes = 5 };

        Assert.True(_validator.Validate(null, options).Failed);
    }

    /// <summary>A zero bound would silently disable the protection it names.</summary>
    /// <param name="member">Which option is set to zero.</param>
    [Theory]
    [InlineData(nameof(FilesOptions.MaxBatchItems))]
    [InlineData(nameof(FilesOptions.TrashRetentionDays))]
    [InlineData(nameof(FilesOptions.ExtractMaxRatio))]
    [InlineData(nameof(FilesOptions.MaxConcurrentTransfersPerAccount))]
    [InlineData(nameof(FilesOptions.MaxConcurrentTransfersGlobal))]
    public void A_zero_bound_is_refused(string member)
    {
        var options = new FilesOptions();
        typeof(FilesOptions).GetProperty(member)!.SetValue(options, 0);

        Assert.True(_validator.Validate(null, options).Failed);
    }
}
