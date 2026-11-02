namespace Maran.Modules.Accounts.Common;

/// <summary>The owning module counted (<c>state</c> is <c>counted</c>).</summary>
/// <param name="Used">How many of the kind the account owns; zero is a real count.</param>
public sealed record CountedResourceCountDto(int Used) : ResourceCountDto;
