using System.Text.Json.Serialization;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What an account's usage row says about how many of one kind it owns: a count, or the statement
/// that it could not be taken. Exactly one of two shapes, told apart by <c>state</c>, so the number
/// exists only where something was measured and a screen cannot print "0" for "the agent did not
/// answer".
/// </summary>
/// <remarks>
/// The closed set is stated by the <c>JsonDerivedType</c> attributes: <see cref="CountedResourceCountDto"/> and
/// <see cref="UnavailableResourceCountDto"/> are the whole set.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(CountedResourceCountDto), "counted")]
[JsonDerivedType(typeof(UnavailableResourceCountDto), "unavailable")]
public abstract record ResourceCountDto;
