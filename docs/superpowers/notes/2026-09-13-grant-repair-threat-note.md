# Threat note — the operation that rewrites existing `GRANT` rows

Required by `rules/security.md` ("Sensitive change escalation"). Covers the new
`RepairDatabaseGrants` rpc, `agent/crates/ops/src/db/repair_grants.rs`, and the
move of the escape into `agent/crates/ops/src/db/grant_pattern.rs`. **This change
needs a second reviewer, and that reviewer is OUTSTANDING.**

It is the FIFTH outstanding privileged review on this branch. Adding a fifth
honestly is the rule holding rather than bending: the alternative is a privileged
surface with no written argument at all. What it is not is a discharge of the
other four.

## This note is LATE, in the same way its predecessor was

`rules/security.md` says the note is written **first, before the change**. This one
was not: the operation was designed and written in the same session, and this file
followed. Saying so first is not ceremony — a note written afterwards is, in
`rules/architecture.md`'s words, a justification validating a choice already made.
So, explicitly:

- **Reconstruction.** Nothing. No part of this design predates this note's own
  lane.
- **Reasoning, new here.** The predicate, the ordering argument, the placement
  argument, and what the operation can and cannot tell an operator about
  exploitation. Each was written forward from the question and each is measured
  against a real MariaDB rather than argued; the log and the quoted failures were kept only in a
  working report, not committed.
- **Still not established by anybody.** Whether the defect was ever USED on any
  real host. This note says why that cannot be settled from here, and does not
  imply the repair settles it.

The defect itself, and its live reproduction, are
`docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md`. That note's
§"Could a shipped installation already have been exploited?" names the gap this
change closes: *"the fix does not repair existing grants… there is no migration
for it today."*

## What this surface is, and why it is privileged

The agent connects to the local MySQL/MariaDB as `root` over the unix socket,
authenticated by its uid, holding no credential. Every other operation in
`ops::db` acts on a name a request supplied. **This one acts on rows nobody asked
about**: it reads the whole of `mysql.db` and rewrites the grants it recognises as
its own. Done wrong it revokes a customer's access to their own data — one
operation, every customer on the host, which is worse than the defect it repairs.

The attacker model has two halves:

1. **The customer**, unchanged from the previous note: a tenant who can influence
   their own account name and who is shown their own database password. They
   cannot reach this rpc; they benefit from it, or are harmed by it if it is
   wrong.
2. **The operator's own history.** The rows on a real host were not all written by
   this panel. A DBA's hand-made grant, a monitoring user, a reporting tool, a
   grant from another host — each is a row this operation must recognise as not
   its business. The realistic harm here is not an attacker; it is this code
   deciding a stranger's row is ours.

## The predicate, and why it cannot mistake a correct row for a broken one

A row is repaired only when ALL of these hold, and refused when any does not:

1. `Host` is `localhost` — the only host `create_database` grants from;
2. `Db` holds at least one `_` and NO backslash;
3. `Db` and `User` both decode as names this agent could have created — at the
   LAST separator, against the WHOLE account name — and both decode to the SAME
   account;
4. `SHOW GRANTS` renders the row as exactly the `ALL PRIVILEGES` grant
   `create_database` issues.

Condition 2 is what makes the already-correct case **structural rather than
careful**: a repaired row holds `\_` for every separator, so it holds a backslash,
so it can never be a candidate. The escaped form comes from `grant_pattern_for`,
which is now a file of its own precisely so that the writer and the recogniser
cannot be two different answers to "what does a correct grant look like". A unit
test drives `create_database` and then the repair, and asserts the fresh grant is
reported as already correct — so a drift between them fails by name instead of
making every new database look broken.

Condition 3 is the lesson of this branch's earlier defects — a cross-tenant
destructive delete, and a classifier that mistook a stranger's row for the
account's own. A prefix scan is the wrong predicate: `alice_` is a prefix of
`alice_bob_shop`, which is account `alice_bob`'s. The decode is performed by the
name types themselves, beside the constructors that built them.

Condition 4 exists because the repair re-issues `GRANT ALL PRIVILEGES`. Against a
narrower grant an operator made by hand on a panel-shaped name, a blind re-grant
would **escalate** it — a worse outcome than the wildcard. Measured: a
`GRANT SELECT` row on a panel-shaped name is refused, and the mutation that
removes the check is caught by name.

## What it does with a row it cannot classify

It refuses it: the row is untouched, counted, reported with its raw `Host`, `Db`
and `User` columns and one of four reasons, and a `tracing::warn!` line names it in
the agent's own journal. Four reasons rather than one because an operator acts on
them differently. Nothing unrecognised is ever guessed at, and nothing is silently
skipped: every row lands in exactly one of "already correct", "repaired",
"would repair" or "refused", and those add up to the number examined, so the
report reads as a census of the grant table rather than a list of what happened to
be interesting.

`report_only` exists for the same reason. An operation that can take a customer's
access away must be inspectable before it acts, and a predicate an operator cannot
check is a predicate they have to trust.

## Order, and what a crash between the two statements leaves

The escaped pattern and the unescaped one are **different `Db` values**, so they
are two different rows and the server holds both in between. The grant is issued
first:

- **grant then revoke** — a crash in between leaves the correct row PLUS the old
  wide one. The customer keeps access; the exposure is not yet closed; the next run
  sees the unescaped row and finishes. **The midpoint is re-validated by
  re-running**, which is exactly what the config-swap window this branch
  documented cannot claim.
- revoke then grant would leave a customer's application with NO access to its own
  database, permanently, until somebody noticed. An outage per customer.

Measured on a real server: `REVOKE … ON \`a_b\`.*` matches the `Db` column
literally and removes the pattern row while leaving the escaped row just written.

## Where it runs, and what the rejected placement cannot see

It is an **explicit rpc the panel calls**, not a startup reconciler. The agent
already runs two reconcilers, so the pattern exists; it was rejected here:

- The agent **MUST stay stateless** (`rules/architecture.md`), so nothing may
  record "already repaired" anywhere but the server itself. A boot pass would
  therefore re-derive the answer on every start — and a pass that revokes and
  re-issues live database access on every boot is a standing risk for no gain
  after the first one.
- A startup pass **cannot be inspected first**: there is no `report_only` for a
  boot, and no operator reading a report before it acts.
- A startup pass **has no audience for a refusal**. Four reasons reported into a
  log nobody is reading at boot is not "an operator sees it".
- A startup pass runs before the panel is listening, so **nothing records that it
  happened**; an rpc is audited like every other (`rules/security.md` item 12).

What the rpc cannot see, stated rather than assumed: **only the server**. It has
no access to the panel's rows, so it cannot tell a database the panel has a record
of from one the panel has forgotten, and it cannot tell whether a refused row
belongs to a customer the panel knows. That is the same limit `list_databases`
carries, and for the same reason — the server has no notion of a tenant. It also
cannot see a host it is not running on: nothing here enumerates a fleet.

The rpc ships inert. No C# module drives it yet, which `rules/architecture.md`
explicitly sanctions ("new operations ship compiled into the open agent and stay
inert until a C# module drives them") — and a module that wants it must declare
the capability.

## Does the repair prove anything about whether the defect was USED?

**No, and it must not be read as doing so.** A repaired grant says nothing about
what was read while it was wrong. The host is not "clean of consequences" after
this operation; it is only no longer exposed going forward.

One signal is available, and the operation reports it: for every row it repairs it
lists the **other databases on this server that the old pattern also matched**.
That is evidence of EXPOSURE and its limits are exact:

- a non-empty list means that credential could have read and written those
  databases for as long as the row stood;
- an empty list does **not** clear the row. A matching database may have been
  created and dropped in between, and a pattern is matched at connection time, so
  the reach was never limited to what exists at the moment of repair.

Whether the reach was exercised is answered only by `mysql.general_log` or the
audit plugin, and **neither is on by default**, so on a default install the
question is likely to be unanswerable — to the server, those queries were
authorised, and it keeps no record of an authorised read. An operator who needs
the question answered has the pattern list from a `report_only` pass, their own
application logs, and nothing else.

## What a second reviewer must check

1. **The four predicate conditions, one at a time.** Each is the only thing
   standing between this operation and somebody else's grant. The mutation log (kept only as a
   working report, not committed) names the failure each break produced; a
   reviewer should re-run it rather than believe it.
2. **That `SHOW GRANTS` renders `ALL PRIVILEGES` as those two words on every
   shipped server.** Measured on MariaDB 10.11.14 (Ubuntu 24.04) only. If a family
   rendered the complete set as a list instead, condition 4 would refuse every row
   and the repair would be a loud no-op — the safe direction, but a no-op.
3. **The AlmaLinux 9 family.** Not built, not run. Unmeasured here.
4. **That the batch-output unescaping is right.** The client prints a stored `\_`
   as `\\_`; a reader that forgets it sees every repaired row as escaped twice. The
   mutation that removes it is caught by name, in both lanes, which is the only
   reason this is stated as fact.
5. **Whether the report's host-wide names are acceptable to expose.** The refused
   rows carry other tenants' database and user names to the panel. `list_databases`
   already returns names per account; this is the first host-wide listing of them.
   An operator needs them to act on a refusal, and the alternative — a count with
   no names — is a refusal nobody can follow up. A reviewer should agree or say so.
6. **Whether `AccountName` should permit `_` at all.** Still the upstream question,
   still untouched. The separator being legal inside the account half is what makes
   a collision constructible.

## What the author could not verify

- **Any real deployment.** No shipped installation was examined; nothing here says
  what is on a customer's host.
- **AlmaLinux 9**, above.
- **`scripts/test-baseline.txt`.** The new `#[ignore]`d real-host case raises that
  suite's declared count from 7 to 8; the file is outside this lane's writable
  scope and the row is reported as prose here rather than in a committed file.
  `maran polygon verify` refuses that suite until it is raised.
- **That an operator will read the refusals.** The operation reports them and logs
  them; whether the panel's UI shows them is a backend and SPA question nobody has
  written yet.

---

# Addendum, same day — the panel half, and why this is not a sixth note

The rpc above no longer ships inert. `Maran.Modules.Databases` now drives it through
`GET /api/v1/database-grants` (report only) and `POST /api/v1/database-grants/repair`
(the rewrite), and the Databases manifest already declared `AgentCapability.Db`, so no
new capability was added and none is needed.

**This is an addendum and NOT a sixth outstanding note, and that is a judgement rather
than a convenience.** `rules/security.md`'s escalation list names auth, session and
token handling, the agent's privs module, licence verification and the installer's
privileged steps; a new endpoint is none of those categories. What it does do is decide
one question this note explicitly left open — item 5 of "What a second reviewer must
check", *whether the report's host-wide names are acceptable to expose* — and the honest
place for that answer is beside the question, in the note the reviewer will already be
reading. Inventing a sixth file would cheapen the five real ones; hiding the answer in a
report only one session reads is what `rules/security.md` forbids. **The outstanding
second reviewer is still ONE debt, the fifth, and it now covers both halves.**

## What changed in the attacker model

The first note said the customer "cannot reach this rpc". That is no longer true of the
SURFACE in front of it: a signed-in customer can now address the endpoint. They are
refused, and the refusal is the whole of the new argument.

- **`AuthorizationPolicies.AdminOnly` on the controller class, and it IS the
  authorisation.** Nothing here is tenant-scoped, because the subject is not the panel's
  rows — it is the database server's grant table, which has no notion of a tenant. There
  is therefore no global query filter behind this endpoint and no 404-instead-of-403 to
  fall back on. Weakening the policy would not narrow the answer; it would publish every
  account's grants to every customer.
- **403, deliberately not 404.** The tenant answer exists so an identifier cannot be used
  as an oracle. There is no identifier here and nothing to probe: one server has one
  grant table and it plainly exists. Answering 404 would be a lie about a server-wide
  table, and it would tell a customer nothing useful either way.
- **Who sees another tenant's identifiers.** An administrator, and only an administrator.
  A refused row carries the server's raw `Host`, `Db` and `User` columns, and a row is
  refused precisely because this panel did not write it — so the names can be another
  customer's database and user, or the operator's own reporting credential. On this
  product the administrator already holds every account on the server, so the disclosure
  adds no reach they did not have; what it adds is a host-wide LISTING of names in one
  place, which `list_databases` never produced. The alternative — a count with no names —
  is a refusal nobody can act on, which is the argument the first note made and this half
  accepts. A customer sees none of it: `DatabaseGrantsAuthorizationTests` asserts that
  none of the stubbed stranger's names, and no field of the census, reaches the body of a
  customer's 403.
- **The rate limiter is the `api` policy**, as on every other module endpoint. This
  surface is not a credential oracle — it returns no password and no token — so it needs
  no narrower bucket than the panel's ordinary one.

## The dry run is the only way in, by construction

`POST .../repair` carries one member, `expectedRepairCount`. The handler runs the agent's
**report-only pass itself**, compares that figure against what the server reports NOW,
and refuses with `DatabaseGrantRepairPlanChanged` (409) on any mismatch. So:

- a caller who read no report has no figure to send — proto3's default `0` does not match
  a host with rows to repair, and is refused;
- a report that went stale between reading and acting — a database created, a grant added,
  another administrator having already run the repair — is refused rather than acted on;
- the SPA offers the control only over a held INSPECTION, and never over the result of a
  repair.

**What a single unconditional button would have cost**, stated so the choice is
reviewable: one click rewriting live database access for every customer on the host, by an
operator who had seen neither the rows that would change nor the rows the panel would
refuse — and, afterwards, no way to tell what was changed from what was left. That is the
failure this branch has spent its life avoiding, and it is worse than the defect being
repaired.

**The cost of the choice this lane made instead**, also stated: the grant table is read
twice, so the check is not atomic with the rewrite. A row can change between the two
passes. The design does not pretend to close that — the agent re-validates every row
against its four conditions before touching it, and this comparison is about what the
OPERATOR was shown, not about locking the server.

## What the screen may not claim, and what holds it to that

Four buckets and four refusal reasons reach the operator as sentences the backend
localizes (`Resources/DisplayNames*.resx`, en/ru/hy), never as the agent's enum names;
each refusal carries a second sentence saying what to do about a row the panel has now
promised never to touch. `rules/vue.md` forbids machine text on screen, and the module's
own resx suite asserts that no message in any of the three locales names a filesystem
path, with a planted path at every guarded root as its positive control.

The screen carries no success banner, no tick and no "your server is secure". It states,
in every language, that a repair closes the reach **going forward**, that it does not
establish whether anyone used it, and that an empty exposure list on a repaired row does
not clear that row — because a matching database may have been created and dropped in
between and a pattern is matched at connection time. A Playwright case asserts those
sentences are present AND that the page contains no "your server is secure", "no longer
at risk" or "nothing was exposed".

## What a second reviewer must check, in addition to the six above

7. **That `AdminOnly` is the right boundary for a host-wide listing of other tenants'
   identifiers**, which is item 5 answered rather than item 5 closed. A reviewer should
   agree or say so.
8. **That the confirmation gate is worth the double read**, and that refusing on a moved
   COUNT (rather than on the rows themselves) is the right granularity. A row-by-row match
   would refuse on changes that do not affect what will be rewritten, and would put
   another tenant's names into a request body.
9. **The Armenian wording.** Three terms were chosen by judgement and are flagged for the
   owner here (a working note, not committed elsewhere): the word used for a SQL `GRANT` row,
   for a wildcard pattern, and for escaping. They are consistent with the tree's existing
   `սերվերի գործակալ` and `վահանակ`, but nothing in this repository can tell whether a
   fluent sentence says the right thing.

## What the author of this half could not verify

- **Any real deployment**, unchanged from above.
- **Whether an operator reads the refusals.** The first note listed this as unverified
  because no screen existed. A screen now exists and is asserted end to end against
  stubbed responses; whether a human acts on it is still not something this repository can
  observe.
- **The panel's own audit entry, now WRITTEN — and what a reviewer should check about
  it instead.** The debt this note recorded is closed: `AuditActions.DatabaseGrantsRepaired`
  exists, with an `AuditActionDatabaseGrantsRepaired` entry in each of the Identity module's
  three `Resources/DisplayNames` files (`AuditActionDisplayNameTests` enforces the triple),
  and `RepairDatabaseGrantsCommandHandler` records through `DatabaseAuditJournal` on the
  repair and on the stale-report refusal alike. Two suites assert the entries exactly, not
  for containment.

  What remains for a reviewer is the SUBJECT, because the journal is never deleted
  (`rules/security.md`) and a refused row's `Db` and `User` columns belong to another tenant.
  The subject is therefore a count in both outcomes — `examined=9;narrowed=2;refused=1`,
  or `confirmed=1;hostReports=2` — and never a name from the grant table. It is deliberately
  `key=value;key=value` rather than a sentence: the first version wrote english prose, and a
  live browser check found it in the audit screen's subject column on a russian page, where
  every other subject is an identifier. It must not be localized either — the journal outlives
  the locale whoever wrote the row happened to be using. The exact-string
  assertions exist for that reason: a containment assertion would pass on an entry that named
  the counts and a stranger's database beside them. A reviewer should confirm no later change
  puts a `Db` or `User` value into that string, and that the refusal entry keeps
  `Succeeded = false`, since an entry claiming success would say the host was rewritten when
  nothing was touched.

  Also recorded as a fact rather than a defect: the agent's own log names the rows, the
  panel's entry names the operator and the counts. Neither is sufficient alone, and the two
  are correlated by time, not by a shared identifier.

---

## Second review: RECORDED 2026-09-23

Every sentence above that calls the second reviewer OUTSTANDING described the state until this
date. It is kept rather than edited away, because what a note claimed while the debt stood is part
of what a reader is judging.

**Reviewer:** Edgar Poghosyan (edgar2031), the repository owner — the second human the rule asks
for, and legitimately so: he wrote none of these notes. Every one was written by an agent session,
which is the conflict the requirement exists to break.

**Verdict:** accepted, with no condition attached to this note.

**Typed by the agent at the reviewer's instruction**, because the reviewer does not write English.
Recorded here so a later reader can tell whose judgement this is and whose keyboard it came
through — those are not the same person.

The verdict for all twenty-eight notes is tabulated in
`docs/superpowers/notes/2026-09-22-second-review-packet.md`.
