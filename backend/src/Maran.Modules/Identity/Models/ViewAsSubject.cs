using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// What a view-as session looks at: one account or one reseller, by kind, id and name. One id with a
/// kind beside it, so the id never means an account on one path and a reseller on another.
/// </summary>
/// <param name="Kind">Whether <paramref name="Id"/> names an account or a reseller.</param>
/// <param name="Id">The account's or the reseller's identity.</param>
/// <param name="Name">The account's or the reseller's name, as the banner and the audit subject say it.</param>
public sealed record ViewAsSubject(ViewAsKind Kind, Guid Id, string Name);
