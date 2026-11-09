namespace Maran.Sdk.Contracts;

/// <summary>
/// What an account's plan allows a customer to set for PHP: four maxima, the plan's side of the
/// customer's php.ini settings (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// A stored setting above its maximum is not deleted; the pool carries the setting clamped to the
/// maximum, and the customer sees that their wish exceeds the plan. A plan edit therefore never loses
/// what a customer chose: raising the bound again gives the wish back.
/// </para>
/// <para>
/// <c>date.timezone</c> has no maximum. <see cref="UploadMaxMb"/> bounds BOTH <c>upload_max_filesize</c>
/// and <c>post_max_size</c>, because a body smaller than the file it carries silently loses the file.
/// </para>
/// </remarks>
/// <param name="MemoryLimitMaxMb">The largest <c>memory_limit</c>, in mebibytes.</param>
/// <param name="UploadMaxMb">The largest <c>upload_max_filesize</c> and <c>post_max_size</c>, in mebibytes.</param>
/// <param name="MaxExecutionSeconds">The longest <c>max_execution_time</c>, in seconds.</param>
/// <param name="MaxInputVars">The largest <c>max_input_vars</c>.</param>
public sealed record PhpLimitsSnapshot(int MemoryLimitMaxMb, int UploadMaxMb, int MaxExecutionSeconds, int MaxInputVars)
{
    /// <summary>
    /// The conservative set a plan carries until its operator states one: what a plan that predates
    /// the bounds reads as, and what a snapshot nobody filled in reads as.
    /// </summary>
    public static readonly PhpLimitsSnapshot Default = new(256, 64, 120, 3000);
}
