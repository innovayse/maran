namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>
/// The shape of a customer's php.ini setting: what its value is a count of, which decides how it is
/// written, bounded and shown (issue #138).
/// </summary>
public enum PhpSettingKind
{
    /// <summary>A size in mebibytes: <c>memory_limit</c>, <c>upload_max_filesize</c>, <c>post_max_size</c>.</summary>
    Size,

    /// <summary>A duration in seconds: <c>max_execution_time</c>.</summary>
    Seconds,

    /// <summary>A plain count: <c>max_input_vars</c>.</summary>
    Count,

    /// <summary>An IANA timezone name: <c>date.timezone</c>.</summary>
    Timezone,
}
