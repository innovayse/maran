namespace Maran.Host.Demo.Common;

/// <summary>
/// The fact a call refused by the content filter publishes beside the problem's code: which member held the
/// text. The text itself is never sent back.
/// </summary>
/// <param name="Field">The name of the command member that held the refused text, for example <c>Name</c>.</param>
public sealed record DemoContentRefusalDto(string Field);
