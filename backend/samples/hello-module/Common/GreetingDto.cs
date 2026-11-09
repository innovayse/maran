namespace Maran.Samples.Hello.Common;

/// <summary>What <c>GET api/v1/hello</c> answers with.</summary>
/// <param name="Text">The greeting to show.</param>
public sealed record GreetingDto(string Text);
