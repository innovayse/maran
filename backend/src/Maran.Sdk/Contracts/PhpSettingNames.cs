namespace Maran.Sdk.Contracts;

/// <summary>
/// The names of the php.ini settings a customer may change, spelled exactly as php.ini and the agent
/// spell them: the panel's copy of the agent's whitelist.
/// </summary>
/// <remarks>
/// <para>
/// <b>Advice here, authority in the agent.</b> The panel validates against this list so a customer is
/// refused with a sentence before the save; the agent re-derives its own list
/// (<c>maran_ops::php::PhpOverride</c>) and is the one that decides, because the panel is another
/// network peer (rules/security.md item 1). A name added here and not there is refused by the agent
/// and shows the customer an error; a name added there and not here is simply not offered.
/// </para>
/// <para>
/// Settings the pool protects (<c>open_basedir</c>, <c>disable_functions</c>, <c>allow_url_fopen</c>,
/// <c>cgi.fix_pathinfo</c>) are absent on purpose: the pool template writes them with
/// <c>php_admin_*</c>, which no customer setting can countermand.
/// </para>
/// </remarks>
public static class PhpSettingNames
{
    /// <summary>The most memory one PHP request may use.</summary>
    public const string MemoryLimit = "memory_limit";

    /// <summary>The largest single uploaded file.</summary>
    public const string UploadMaxFilesize = "upload_max_filesize";

    /// <summary>The largest request body, every uploaded file in it included.</summary>
    public const string PostMaxSize = "post_max_size";

    /// <summary>How long one PHP request may run, in seconds.</summary>
    public const string MaxExecutionTime = "max_execution_time";

    /// <summary>How many input variables one request may carry.</summary>
    public const string MaxInputVars = "max_input_vars";

    /// <summary>The timezone PHP's date functions use when the code sets none.</summary>
    public const string DateTimezone = "date.timezone";

    /// <summary>Every name, in the order a settings screen lists them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        MemoryLimit,
        UploadMaxFilesize,
        PostMaxSize,
        MaxExecutionTime,
        MaxInputVars,
        DateTimezone,
    ];
}
