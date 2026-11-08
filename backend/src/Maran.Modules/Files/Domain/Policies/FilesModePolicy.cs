namespace Maran.Modules.Files.Domain.Policies;

/// <summary>
/// What the panel accepts as a permission mode, and how it writes one back.
/// </summary>
/// <remarks>
/// A mode travels as octal DIGITS (<c>"644"</c>), never as a number, so a client cannot confuse decimal
/// 644 with octal 644. Only the nine permission bits are accepted: setuid, setgid and sticky are
/// refused here and again by the agent.
/// </remarks>
public static class FilesModePolicy
{
    /// <summary>The highest mode accepted: <c>0777</c>.</summary>
    public const uint MaximumMode = 0b1_1111_1111;

    /// <summary>The mode a file the panel creates is given: <c>0644</c>, owner read and write, everyone else read.</summary>
    public const uint DefaultFileMode = 0b110_100_100;

    /// <summary>The read bit of all three classes: <c>0444</c>.</summary>
    private const uint AllReadBits = 0b100_100_100;

    /// <summary>Reads octal digits into permission bits.</summary>
    /// <param name="candidate">One to three octal digits, as typed.</param>
    /// <param name="mode">The permission bits when the candidate is acceptable; otherwise zero.</param>
    /// <returns>True when the candidate is one to three octal digits and nothing else.</returns>
    public static bool TryParse(string? candidate, out uint mode)
    {
        mode = 0;
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 3)
        {
            return false;
        }

        uint value = 0;
        foreach (var digit in candidate)
        {
            if (digit is < '0' or > '7')
            {
                return false;
            }

            value = (value << 3) | (uint)(digit - '0');
        }

        mode = value;
        return true;
    }

    /// <summary>Writes the permission bits of a mode as three octal digits.</summary>
    /// <param name="mode">The mode the agent reported; bits above the nine permission bits are dropped.</param>
    /// <returns>Three octal digits, for example <c>"644"</c>.</returns>
    public static string Format(uint mode)
    {
        return Convert.ToString(mode & MaximumMode, 8).PadLeft(3, '0');
    }

    /// <summary>
    /// The mode folders get when a recursive change names none: the file mode with execute added wherever
    /// read is set, because a folder that can be read but not entered is unusable.
    /// </summary>
    /// <param name="fileMode">The mode the files of the change get.</param>
    /// <returns>The folders' mode: 755 for 644, 700 for 600; a class that cannot read gains no execute.</returns>
    public static uint DirectoryModeFor(uint fileMode)
    {
        return (fileMode & MaximumMode) | ((fileMode & AllReadBits) >> 2);
    }

    /// <summary>Reads the folders' mode of a recursive change, which may not be zero.</summary>
    /// <param name="candidate">One to three octal digits, as sent.</param>
    /// <param name="mode">The permission bits when the candidate is acceptable; otherwise zero.</param>
    /// <returns>
    /// True when the candidate is acceptable to <see cref="TryParse"/> and is not zero, because the agent reads
    /// a folder mode of zero as "the same as the file mode" and a request must not say one thing and get another.
    /// </returns>
    public static bool TryParseFolderMode(string? candidate, out uint mode)
    {
        return TryParse(candidate, out mode) && mode != 0;
    }
}
