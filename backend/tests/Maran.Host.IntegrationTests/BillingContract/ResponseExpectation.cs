using System.Text.Json.Serialization;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>One property of an answer that the billing provider reads: where it is, what type it has, and what it equals.</summary>
/// <param name="Name">The property's dotted path in the body (<c>disk.usedBytes</c>), or <c>$</c> for the body itself.</param>
/// <param name="Type">
/// One of <c>string</c>, <c>guid</c>, <c>instant</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>object</c>
/// or <c>array</c>.
/// </param>
/// <param name="EqualsLiteral">The text the property must equal (placeholders expanded), or <c>null</c> for any value.</param>
/// <param name="EqualsVariable">The name of an input or captured value the property must equal, or <c>null</c>.</param>
public sealed record ResponseExpectation(
    string Name,
    string Type,
    [property: JsonPropertyName("equals")] string? EqualsLiteral = null,
    string? EqualsVariable = null);
