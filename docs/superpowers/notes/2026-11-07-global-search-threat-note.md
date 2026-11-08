# Global search (issue #198, slice S1) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change adds
the first endpoint that reads across several modules' tenant tables in one call. It is a tenant boundary
that is crossed by composition, so it is treated as a sensitive surface even though it adds no write and
no agent rpc. The change **may land on a branch and MUST NOT merge to `main`** until a second reviewer
has read this note and checked every item under "What a reviewer must check". Written BEFORE the code, so
it can contradict a choice instead of validating one.

## What is being added, in one paragraph

A signed-in person types two or more characters into the jump-to palette. The SPA sends them to
`POST /api/v1/search` (body `{ "term": "..." }`). The Host hands the term to every search provider the
composed modules registered (`ISearchProvider`, an Sdk interface), each provider answers with a short list
of hits (`kind`, `id`, `title`, `subtitle`), and the Host returns them grouped by kind. Slice S1 ships two
providers: Accounts (name, primary domain, owner e-mail) and Sites (every hostname a site claims). The
SPA maps `kind` + `id` to one of its own routes; the server sends no URL. Nothing is written, no agent
call is made, no table, setting or environment variable is added, and nothing is stored about the search.

## What an attacker could do with it, and why each is closed

### 1. Cross-tenant disclosure (the headline risk)

One request touches several modules' tables. If any provider read a table without the tenant filter, the
endpoint would hand any signed-in person another customer's account name, owner e-mail or domain.

- **Scoping is the module's query filter, not the provider's care.** A provider starts from its module's
  own `DbContext` set (`Accounts`, `SiteHostnames`), whose global query filter is the tenant boundary
  (rules/security.md item 6). `IgnoreQueryFilters` is a banned symbol in `backend/src`; the two providers
  contain none, and the count in rules/README.md is unchanged. A provider is given no account id, no role
  and no principal as an argument: the contract (`SearchQuery`) carries the term and the limit only, so
  there is no scope value for a provider to mishandle.
- **The Accounts provider answers only for an administrator or a reseller.** The accounts screens are
  administrator and reseller screens (`AccountsController` policy); a customer's context would show the
  customer their own account row, which is not something the palette should offer as a link to a page the
  customer cannot open. That is a relevance rule, not the scope rule: the scope is still the filter, so an
  error in the role check can show a person LESS than the filter allows, never more. A reseller sees only
  the accounts it owns (the Accounts filter), an administrator all of them.
- **The Sites provider applies the same feature gate the Sites controller applies.** A customer whose plan
  does not include the Sites feature, or a team member the owner has not granted it to, gets no hits —
  otherwise the palette would disclose names the Sites screens refuse to show them (`SitesController` is
  `[RequiresAccountFeature(AccountFeature.Sites)]`). A reseller or an API key gets none (the controller
  refuses them, and the Sites filter shows them nothing). The gate reads the Sdk windows
  `IAccountFeatureDirectory` and `IAccountMemberGrants`, the same two the filter uses.
- **API keys get nothing.** The endpoint's policy is JWT-only (`AnyAuthenticated` carries no API-key
  scheme), and every tenant filter shows `ApiKeyCaller` nothing.
- **Another tenant's object is indistinguishable from none.** A term that matches only another tenant's
  row yields an empty group and the same response shape as a term that matches nothing; there is no 403,
  no count, and no "N hidden" figure.

### 2. Existence oracle

"Is there an account called X" is what an enumeration attack asks. Closed for customers by (1): they see
only their own sites, and the Accounts group is empty for them. An administrator may legitimately learn it.
The endpoint has its own rate limit per signed-in user (`search` policy), the term has a minimum length of
two characters and the shared `SearchTerm` maximum of 100, so a script cannot walk the namespace one
character at a time at the general API rate.

### 3. The query text must not end up in access logs

The first draft of the plan used `GET ?q=`. The panel's own nginx site writes its access log in the
`maran_no_query` format (`installer/nginx/maran.conf`), which records the path and not the query string,
so a GET would not have reached that log. But a query string also lands in browser history, in the
`Referer` a page sends, and in the access log of any other proxy an operator puts in front of the panel,
and a search box is where people paste e-mail addresses, domains and, by accident, secrets. **The endpoint
is POST with the term in the body**, so the text never appears in a URL at all, whatever sits in front of
the panel. The application side: the Host's request logging records the path, never the body; the fan-out
logs the provider's kind and the outcome, never the term (a test pins it with a planted term). The SPA
keeps the term out of every URL and out of the console, and does not persist it (no "recent searches" in
this slice, so there is no stored copy to leak between identities).

POST has two consequences, both decided here:

- The CSRF header the cookie-bearing state changes require applies; the SPA client already sends it.
- A view-as token reaches POST endpoints only when the action declares `SafeMutation`
  (`ViewAsBoundaryHandler`). This endpoint is read-only and stores nothing, so it declares
  `ViewAsAccess.SafeMutation`: an operator viewing the panel as a customer gets that customer's results
  and nothing the customer could not search. It is a minimal endpoint, outside the controller census of
  `ViewAsAccessDeclarationTests`, so the reason is written here and in the endpoint's doc comment.

### 4. Side channels in the response

- A hit's `subtitle` is built only from fields the caller's role already reads on the object's own page:
  for an account the primary domain and the owner e-mail (administrator and reseller read both on the
  account page), for a site the matched hostname. No hash, token, key, path, status text or stack text.
- A provider that throws or exceeds its time budget yields `state: "unavailable"` for its group and **no
  exception text**. The exception is logged server-side with the provider's kind.
- The response carries no timing-dependent field and no total across tenants.

### 5. Resource exhaustion

- Each provider is bounded: `HitLimit` hits (eight), at most `CandidateLimit` candidate rows read (one
  hundred), one scan of a few columns, the term escaped with `LikePattern` so `%` and `_` cannot widen it
  into "match everything".
- Each provider runs under a hard time budget (`SearchFanOut.ProviderTimeout`); the whole request is
  bounded by the request's own cancellation. A provider that ignores cancellation is abandoned (its task
  is not awaited past the budget), so one hung module cannot stall the answer.
- The fan-out runs providers **one after another, not in parallel**, and this is a correctness choice, not
  an omission: every provider in one request shares the request scope, and two of them can end up using the
  same scoped `DbContext` (the Sites provider asks the Accounts module's feature window while the Accounts
  provider is its own caller). A `DbContext` is not safe for concurrent use. The cost is latency measured
  in the sum of the providers, bounded by the per-provider budget.
- Rate limit: a fixed window per signed-in user, its own policy so search keystrokes cannot exhaust the
  general API budget (and the general budget cannot be spent by search).

### 6. SQL

EF Core, parameterised only (`EF.Functions.ILike` with `LikePattern`-escaped patterns). No raw SQL, no
string-built query, no new extension and no migration. Consequence: the in-memory provider the module unit
tests use cannot evaluate `ILIKE`, so the tenant-scoping tests that go through the match run in the
integration suite; unit tests cover the role and feature gates that return before any query.

### 7. Disabled or unlicensed modules

The fan-out asks `ModuleStateResolver` for the module that owns each provider (found by assembly in
`ModuleRegistry`) and does not call a provider whose module is not `Enabled`. A provider that belongs to no
composed module is not called either (fail closed). The SPA hides what the licence does not include, but
that is advice; this is the check.

### 8. Static palette items (pages, actions, settings)

They are SPA chrome, built from the navigation the SPA already holds, searched in the browser, and never
sent to the server. Choosing one only navigates; the router guards and the endpoint behind the page are
the authority, exactly as for a menu click.

## What a reviewer must check

1. Neither provider contains `IgnoreQueryFilters`, and both start from the module's filtered set.
   `grep -rn "IgnoreQueryFilters" backend/src/Maran.Modules/Accounts/Services/AccountsSearchProvider.cs backend/src/Maran.Modules/Sites/Services/SitesSearchProvider.cs`
   prints nothing (run it with `/usr/bin/grep`; positive control: the same grep over
   `Maran.Modules/Accounts/Services/AccountDirectory.cs` prints the documented bypasses).
2. The IDOR tests exist with their accept-side controls: customer A cannot find customer B's site by its
   exact hostname, and CAN find their own; a reseller finds only its own accounts; a customer finds no
   account; a customer without the Sites feature (plan or grant) finds no site.
3. The fan-out test with a provider that never completes returns the other groups and marks the hung one
   `unavailable`, and a control with all providers completing returns all `ready`.
4. The term appears in no log: the fan-out test plants a term and searches the captured log output (with a
   positive control that the log capture does see the provider's kind).
5. The endpoint declares the `search` rate-limit policy, a JWT-only authorization policy, and
   `ViewAsAccess.SafeMutation`; the minimum term length is enforced on the server (400), not only by the SPA.
6. `ModuleStateResolver` is consulted: a provider whose module is locked is not called, and one that belongs
   to no module is not called.

## What the author could not verify

- That the query-filter scoping holds for every future provider: it holds for these two because each uses
  its module's filtered set. A provider written later can still bypass its filter; the only mechanical
  guard is the `IgnoreQueryFilters` ban and this review.
- Performance of the candidate query at large table sizes: no trigram index is added in S1 (the tables the
  two providers read are small relative to a single server's account count, and the lists already search
  these columns with `ILIKE`). Measured nothing at scale; the candidate cap bounds the work, not the scan.
- Whether a proxy in front of the panel logs request bodies. The shipped nginx site does not (it logs the
  request line without the query string, and never a body); a different front proxy is outside this
  repository.
- Exact-match ordering is applied inside the candidate window only: with more than one hundred substring
  matches ordered by name, an exact match is moved first by the query's ordering, but a prefix match may
  fall outside the window. That is a relevance limit, not a disclosure.
