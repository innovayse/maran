namespace Maran.Modules.Accounts.Common;

/// <summary>
/// The wire shape of what a plan allows a customer to set for PHP (issue #138): the same four
/// maxima in a request that states them and in the answer that reports them.
/// </summary>
/// <param name="MemoryLimitMaxMb">The largest <c>memory_limit</c>, in mebibytes.</param>
/// <param name="UploadMaxMb">The largest <c>upload_max_filesize</c> and <c>post_max_size</c>, in mebibytes.</param>
/// <param name="MaxExecutionSeconds">The longest <c>max_execution_time</c>, in seconds.</param>
/// <param name="MaxInputVars">The largest <c>max_input_vars</c>.</param>
public sealed record PhpLimitsDto(int MemoryLimitMaxMb, int UploadMaxMb, int MaxExecutionSeconds, int MaxInputVars);
