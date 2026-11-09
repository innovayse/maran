using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Monitoring.Commands.SaveAlertThresholds;

/// <summary>Replaces the thresholds at which the panel alarms about the disk, the processor and the memory (issue #142).</summary>
/// <param name="DiskPercent">The root filesystem alarms when it is fuller than this; a whole number from 50 to 99.</param>
/// <param name="CpuPercent">The processor alarms when it is busier than this; <c>null</c> stops watching it.</param>
/// <param name="MemoryPercent">The memory alarms when more than this is in use; <c>null</c> stops watching it.</param>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="IpAddress"/> is.</param>
public sealed record SaveAlertThresholdsCommand(
    int DiskPercent,
    int? CpuPercent,
    int? MemoryPercent,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
