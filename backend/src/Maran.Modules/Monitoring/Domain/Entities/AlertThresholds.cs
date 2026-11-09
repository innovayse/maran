using System.Globalization;

namespace Maran.Modules.Monitoring.Domain.Entities;

/// <summary>
/// The percentages at which the panel calls the disk, the processor and the memory of the server
/// "in alarm", as an administrator set them (issue #142).
/// </summary>
/// <remarks>
/// <para>
/// One row is the whole table: the key is always <see cref="SingletonId"/>, so two concurrent saves
/// contend on one row. There is no row on a panel nobody has configured; every reader then uses
/// <see cref="CreateDefault"/>, which is why the table needs no seed and a fresh install and an
/// upgrade start in the same state.
/// </para>
/// <para>
/// <b>The disk threshold cannot be switched off; the processor and memory ones can, and start off.</b>
/// A full disk is the failure that stops every customer's writes, so it always has a threshold. The
/// processor and the memory were never watched before this row existed, so an upgrade that began
/// mailing about them would be an alert storm nobody asked for: <c>null</c> is "not watched", and it
/// is the default.
/// </para>
/// <para>
/// <b>The bounds are the entity's own rule.</b> Fifty to ninety-nine inclusive, whole numbers. Below
/// fifty a healthy server alarms all day, which teaches the operator to filter the mail; at a hundred
/// the alarm can only fire after the condition it warns about has already happened. The validator
/// answers the 400; this class refuses to hold anything outside the bounds even when a caller skipped
/// the validator.
/// </para>
/// </remarks>
public sealed class AlertThresholds
{
    /// <summary>The lowest percentage a threshold may be set to.</summary>
    public const int MinimumPercent = 50;

    /// <summary>The highest percentage a threshold may be set to.</summary>
    public const int MaximumPercent = 99;

    /// <summary>The disk threshold of a panel nobody has configured: ninety per cent (spec section 11).</summary>
    public const int DefaultDiskPercent = 90;

    /// <summary>The fixed key of the only row.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000142a");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The root filesystem is in alarm when it is fuller than this percentage.</summary>
    public int DiskPercent { get; private set; }

    /// <summary>The processor is in alarm when it is busier than this percentage; <c>null</c> when it is not watched.</summary>
    public int? CpuPercent { get; private set; }

    /// <summary>The memory is in alarm when more than this percentage is in use; <c>null</c> when it is not watched.</summary>
    public int? MemoryPercent { get; private set; }

    /// <summary>When the thresholds were last saved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates the thresholds, validly: the default disk figure, the other two not watched.</summary>
    /// <param name="updatedAt">When they came into being, from the panel's clock.</param>
    private AlertThresholds(DateTimeOffset updatedAt)
    {
        Id = SingletonId;
        DiskPercent = DefaultDiskPercent;
        CpuPercent = null;
        MemoryPercent = null;
        UpdatedAt = updatedAt;
    }

    /// <summary>EF Core's materialisation constructor; never called by code.</summary>
    private AlertThresholds()
    {
    }

    /// <summary>The thresholds of a panel nobody has configured.</summary>
    /// <param name="updatedAt">The instant to stamp them with, from the panel's clock.</param>
    /// <returns>Disk at <see cref="DefaultDiskPercent"/>, processor and memory not watched.</returns>
    public static AlertThresholds CreateDefault(DateTimeOffset updatedAt)
    {
        return new AlertThresholds(updatedAt);
    }

    /// <summary>Whether a percentage lies within the bounds a threshold may take.</summary>
    /// <param name="percent">The percentage to judge.</param>
    /// <returns><c>true</c> from <see cref="MinimumPercent"/> to <see cref="MaximumPercent"/> inclusive.</returns>
    public static bool IsWithinBounds(int percent)
    {
        return percent is >= MinimumPercent and <= MaximumPercent;
    }

    /// <summary>Replaces all three thresholds with the administrator's choice.</summary>
    /// <param name="diskPercent">The disk threshold; within the bounds.</param>
    /// <param name="cpuPercent">The processor threshold, or <c>null</c> to stop watching it; within the bounds otherwise.</param>
    /// <param name="memoryPercent">The memory threshold, or <c>null</c> to stop watching it; within the bounds otherwise.</param>
    /// <param name="updatedAt">When they were saved, from the panel's clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A percentage is outside the bounds. A caller that reaches this skipped the validator, which is a bug.
    /// </exception>
    public void Replace(int diskPercent, int? cpuPercent, int? memoryPercent, DateTimeOffset updatedAt)
    {
        RequireWithinBounds(diskPercent, nameof(diskPercent));

        if (cpuPercent is not null)
        {
            RequireWithinBounds(cpuPercent.Value, nameof(cpuPercent));
        }

        if (memoryPercent is not null)
        {
            RequireWithinBounds(memoryPercent.Value, nameof(memoryPercent));
        }

        DiskPercent = diskPercent;
        CpuPercent = cpuPercent;
        MemoryPercent = memoryPercent;
        UpdatedAt = updatedAt;
    }

    /// <summary>Refuses a percentage outside the bounds.</summary>
    /// <param name="percent">The percentage to judge.</param>
    /// <param name="parameterName">The parameter the percentage came in as.</param>
    private static void RequireWithinBounds(int percent, string parameterName)
    {
        if (!IsWithinBounds(percent))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                percent,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A threshold lies between {MinimumPercent} and {MaximumPercent} per cent."));
        }
    }
}
