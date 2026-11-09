using Maran.Modules.Files.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Validators;

/// <summary>
/// Refuses a <see cref="FilesOptions"/> that would silently disable a protection or contradict itself.
/// </summary>
public sealed class FilesOptionsValidator : IValidateOptions<FilesOptions>
{
    /// <summary>The agent's own upload ceiling, 256 MiB: a larger setting could only ever be refused by it.</summary>
    public const long AgentUploadCeilingBytes = 256L * 1024 * 1024;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FilesOptions options)
    {
        var failures = new List<string>();

        if (options.MaxUploadBytes < 1 || options.MaxUploadBytes > AgentUploadCeilingBytes)
        {
            failures.Add($"{Setting(nameof(FilesOptions.MaxUploadBytes))} must be between 1 and {AgentUploadCeilingBytes} (the agent's own ceiling).");
        }

        if (options.MaxEditBytes < 1)
        {
            failures.Add($"{Setting(nameof(FilesOptions.MaxEditBytes))} must be at least 1.");
        }

        if (options.MaxDownloadBytes < 1)
        {
            failures.Add($"{Setting(nameof(FilesOptions.MaxDownloadBytes))} must be at least 1.");
        }

        if (options.MaxEditBytes > options.MaxDownloadBytes)
        {
            failures.Add($"{Setting(nameof(FilesOptions.MaxEditBytes))} must not exceed {nameof(FilesOptions.MaxDownloadBytes)}: the editor reads a file the same way a download does.");
        }

        RequireRange(failures, nameof(FilesOptions.MaxBatchItems), options.MaxBatchItems, 1, 10_000);
        RequireRange(failures, nameof(FilesOptions.TrashRetentionDays), options.TrashRetentionDays, 1, 3650);
        RequireRange(failures, nameof(FilesOptions.ExtractMaxEntries), options.ExtractMaxEntries, 1, long.MaxValue);
        RequireRange(failures, nameof(FilesOptions.ExtractMaxBytes), options.ExtractMaxBytes, 1, long.MaxValue);
        RequireRange(failures, nameof(FilesOptions.ExtractMaxRatio), options.ExtractMaxRatio, 1, 100_000);
        RequireRange(
            failures, nameof(FilesOptions.MaxConcurrentTransfersPerAccount), options.MaxConcurrentTransfersPerAccount, 1, 1_000);
        RequireRange(
            failures, nameof(FilesOptions.MaxConcurrentTransfersGlobal), options.MaxConcurrentTransfersGlobal, 1, 10_000);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>The configuration key of a setting, for a failure message.</summary>
    /// <param name="member">The option's property name.</param>
    /// <returns>The key as an operator writes it.</returns>
    private static string Setting(string member)
    {
        return $"{FilesOptions.SectionName}:{member}";
    }

    /// <summary>Adds a failure when a value is outside its inclusive range.</summary>
    /// <param name="failures">Where failures are collected.</param>
    /// <param name="member">The option's property name.</param>
    /// <param name="value">The configured value.</param>
    /// <param name="minimum">The lowest accepted value.</param>
    /// <param name="maximum">The highest accepted value.</param>
    private static void RequireRange(List<string> failures, string member, long value, long minimum, long maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{Setting(member)} must be between {minimum} and {maximum}.");
        }
    }
}
