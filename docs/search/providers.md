# Global search: writing a search provider

The jump-to palette asks the panel `POST /api/v1/search` with `{ "term": "..." }`. The Host asks every
registered `ISearchProvider` in turn and returns one group per provider. A module that owns something
worth finding implements the interface and registers it; nothing else changes.

## The contract (`Maran.Sdk`)

- `ISearchProvider.Kind` is a value of the closed `SearchKind` enum. Adding a searchable kind adds a value
  there, the provider, and the SPA's mapping from kind to screen (`utils/searchHitTarget.ts`) in one change.
- `SearchAsync(SearchQuery, CancellationToken)` returns at most `SearchQuery.HitLimit` hits, best first.
  `SearchHit` carries `Kind`, `Id`, `Title` and `Subtitle` and nothing else. The server sends no URL.
- Register it in the module: `services.AddScoped<ISearchProvider, <Name>SearchProvider>()`, and put the
  class in `Services/`.

## Rules every provider obeys

1. **Scope comes from the module's filtered `DbContext`.** Start from the module's own set; never call
   `IgnoreQueryFilters`. The provider receives no account id, role or principal.
2. **Relevance gates are allowed, scope gates are not.** Answer nothing for a role that has no screen for
   the kind, or for a feature the caller's plan or grants do not include (read `IAccountFeatureDirectory`
   and `IAccountMemberGrants`, as the module's controller filter does). Such a gate can only show less.
3. **Allow-list the fields.** A title and subtitle may carry only fields the caller's role already reads on
   the object's own page. Never a hash, token, key, path or status text.
4. **Escape the term** with `LikePattern.Contains` and pass `LikePattern.EscapeCharacter` to
   `EF.Functions.ILike`. Cap the candidate read at `SearchQuery.CandidateLimit`, rank with `SearchRank`.
5. **Honour the cancellation token.** The Host gives each provider a time budget (`SearchFanOut.ProviderTimeout`)
   and abandons one that overruns; its group is reported `unavailable` and the other groups still answer.
6. **Never log the term.**

Providers run one after another, not in parallel, because they share the request scope and a scoped
`DbContext` is not safe for concurrent use.

## Licensing

The Host does not call a provider whose module is not enabled (`SearchModuleGate`); a provider that belongs
to no composed module is not called. A module is enabled by the same rule the module catalogue and the
request-time gate use: an included module always is, and a paid module only while the licence window
(`ILicenceEntitlements`) reports it covered, so a lapsed or unlicensed paid module is never searched and a
licensed one is. The window is asked once per search, for the paid modules only, and not at all when every
composed module is included.

## Tests a provider owes

Unit tests for the role and feature gates; integration tests for the match and the tenant scoping through
it (customer A cannot find customer B's object by its exact name, and can find their own), because the
in-memory provider cannot evaluate `ILIKE`.
