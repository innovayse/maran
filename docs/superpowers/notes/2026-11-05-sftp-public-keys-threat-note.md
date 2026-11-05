# Threat note: SFTP public-key login, panel side (issues #135 and #339)

`rules/security.md` requires a threat note, written first, for a change that adds a way to authenticate
to the server. The agent half already exists (`SetSftpAuthorizedKeys`, `EndSftpSessions`, key
validation in `agent-core`). This note covers only what the PANEL adds: a customer can now store keys
for their own SFTP logins, and the panel pushes them to a root process that writes the file sshd
trusts. It is written BEFORE the code: every statement below is a requirement the code must meet, not
a description of code that exists. Where the code lands and differs, the code is wrong or this note is
amended in the same change.

**Second review: OUTSTANDING - must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch `fix/135-sftp-public-keys` MUST NOT merge to `main`
until a human reviewer has read it (rules/security.md "Sensitive change escalation"). The owner is
told at hand-off that this surface carries an outstanding review. Nothing here discharges it.

## What is added

1. Entity `SftpUserKey` (login FK with cascade, label, validated one-line key, fingerprint, algorithm,
   created-at) with a unique index on (login, fingerprint) and a tenant query filter reached through
   the parent login, as `SiteHostname` is.
2. Commands `AddSftpUserKey`, `RemoveSftpUserKey`; query `ListSftpUserKeys` (server-side paging,
   search by label, sort by label or created-at).
3. Routes `api/v1/sftp-users/{id}/keys` (GET list, POST add, DELETE `{keyId}`).
4. `IAgentSftpClient.SetAuthorizedKeysAsync(login, keys)`, its invoker and resilience wrapper.
5. SPA "Keys" dialog per login, locales en/ru/hy, operator documentation, CHANGELOG entry.

Not added: no proto change (so `PROTO_VERSION` and the baseline do not move), no new agent capability
(the module still declares `Sftp`), no port, daemon, or outbound call, no new agent rpc.

The grammar the panel mirrors is the agent's, read from
`agent/crates/agent-core/src/validation/system/ssh_public_key.rs` and `proto/agent/v1/ftp.proto`:
algorithms `ssh-ed25519`, `sk-ssh-ed25519@openssh.com`, `ecdsa-sha2-nistp256/384/521`,
`sk-ecdsa-sha2-nistp256@openssh.com`, `ssh-rsa` with a modulus of 2048 to 16384 bits; at most 8 KiB
per key; at most 32 keys per login; no control character anywhere; no leading option; the comment is
dropped. **The issue plan said RSA of at least 3072 bits; the agent accepts 2048, and the panel mirrors
the agent exactly, because a panel stricter than the agent is a silent second policy and a panel more
lenient is refused by the agent.** If the owner wants 3072, the agent changes first, in its own change.

## 1. Who can do what with this path

The new path lets a caller put an authenticator onto a login that reaches the account's home. Every
case below is a requirement.

- **Cross-account injection (IDOR).** A customer of account A POSTing a key to a login id of account B
  must get 404 and cause no write and no agent call. The login is loaded through the tenant-filtered
  `SftpDbContext`, so another account's row is not found; the answer is `ErrorType.NotFound`, never
  Forbidden, and the audit entry records the probed id (as `DeleteSftpUser` does). The key row is
  looked up by BOTH ids (`keyId` and the login's id): a key id that belongs to another login of the
  SAME account, or to another account, is 404 as well, so the route cannot be used to delete a key
  by guessing its id under a login the caller does own. The `SftpUserKey` filter goes through the
  parent; `TenantScopeTests` must pass without an exemption.
- **Which account the agent is told.** The agent is addressed by `account.Username` read from
  `IAccountDirectory` for the login row's `AccountId`, and by the login's recorded suffix. Nothing
  about account or login identity is taken from the request body; the route id is server-established
  (`[BindNever]` pattern, rules/csharp.md). The agent re-checks that the login belongs to the account
  (`NotFound` otherwise), so a wrong row still cannot reach past the account. That is a second wall,
  not the first.
- **A viewing operator.** An operator viewing the panel as a customer holds a customer token with an
  `act` claim. Adding a key creates a credential that outlives the look, so POST and DELETE carry
  `[ViewAsAccess(ViewAsAccess.Refused)]`; GET carries `Read` and returns label, algorithm, fingerprint
  and created-at only. `ViewAsAccessDeclarationTests` must see all three declarations.
- **A reseller or a sub-user.** The permission the endpoints declare is the one the neighbouring
  `SftpUsersController` actions use (read vs write). A caller who may not create a login may not add a
  key to one. Whether sub-user grants (the 2026-11-03 note) change which permission reads as "write"
  is for the reviewer to confirm against that note; this note does not widen it.
- **The key body.** It is public material but still caller input that ends in a root-written file. It
  is validated at the boundary and again by the agent; it is stored in the row once, as the
  one-line `<algorithm> <base64>` form with options and comment dropped (the form the agent writes).
  It is never written to the log, never echoed by an error text (a validation failure names the
  field, not the value), never put in an audit entry beyond the fingerprint, and never returned by the
  list. The commands travel the local bus only; no durable queue envelope carries them.
- **The label.** Free text the customer chooses, up to 64 characters, no control characters, trimmed.
  It is audited and shown back, so it is validated as the log-injection and layout hazard it is;
  the SPA renders it as text.
- **Duplicates.** The same key (same fingerprint) twice on one login is `Conflict` and writes nothing.
  The unique index is the arbiter: two simultaneous adds of one key yield one success and one
  `Conflict`, never a 500 from a unique-violation exception (the handler maps it). The same key on two
  different logins is allowed; a fingerprint is per login.
- **Limit.** The 33rd key on a login is `Conflict` (a state clash, not a malformed request), checked
  against the constant in `Domain/Policies/` that equals the agent's limit of 32. If the two ever
  disagree the agent refuses the whole set with `INVALID_INPUT` and nothing is written (see 4).
- **A locked or suspended login.** The panel stores the key either way; it knows nothing about locks.
  The agent decides placement: for a login that cannot authenticate (suspended account) it writes the
  key file under the name sshd never reads, and for one that can, under the name sshd reads
  (`set_sftp_authorized_keys.rs`, "Which name the file is written under"). So a key added during a
  suspension stays parked until the account resumes, and the customer cannot use it to log in while
  suspended. The panel adds no lock logic of its own; the requirement on the panel is only that it
  does not treat a locked login as an error and does not skip the agent call. Whether the resume path
  moves the parked file back was NOT read by the author (see "could not verify").
- **Quantity of work per request.** Each add or remove costs one root-side file write plus
  `ssh-keygen -l` and, on remove, one `pkill`. The routes carry the same named `Api` rate-limit
  policy as their neighbours (per authenticated account). The key limit of 32 and the agent's
  never-waiting account lock bound the rest: a flood of requests meets `AccountBusy`, mapped to a typed
  retryable error, not a queue. A customer who loops add/remove can only cut their own login's sessions
  (see 3). No stricter dedicated policy is proposed; the reviewer should say if they want one.

## 2. Double validation

- The panel's `AddSftpUserKeyCommandValidator` implements the grammar above, including decoding the
  base64, checking that the blob names the same algorithm, and measuring an RSA modulus. It exists so
  the customer gets a precise, localized answer (en/ru/hy) and so the fingerprint can be computed
  from the blob.
- The agent revalidates every key (`SshPublicKey::parse`) and refuses the whole request on one bad
  key. The panel's validator is advice that must err in the same direction as the agent, never more
  permissive on purpose: a key the panel accepts and the agent refuses surfaces as an agent
  `INVALID_INPUT` mapped to a typed error, the transaction rolls back, and the discrepancy is logged
  with the agent's text (`AgentErrorTranslator`, the one place it is logged) but not the key. That
  outcome is a defect to fix, and the tests must hold the two grammars together with the same
  fixtures (accepted: one of each algorithm; refused: option prefix, embedded newline, trailing bytes
  after the blob, wrong algorithm name for the blob, RSA 1024, RSA 2048 accepted, oversize, empty).
- The fingerprint is `SHA256:` plus unpadded base64 of the SHA-256 of the decoded blob, computed by
  the panel from the blob it validated, never taken from the request.

## 3. Order of effects and failure semantics

The panel is the source of truth for the key set; the agent converges to whatever full set it is sent.
Every call to the agent sends the COMPLETE set for that login, read from the panel after the change.

**Concurrency first.** Two simultaneous requests on one login each read the set, each add their key
and each send a full set that lacks the other's. The last write wins at the agent while both rows
commit: a listed key that does not work, or after a remove, a removed key that is back. Requirement:
the key-changing handlers serialize per login: they take a row lock on the `SftpUser` row
(`SELECT ... FOR UPDATE` via EF) or an equivalent concurrency token inside the transaction, and read
the set after acquiring it. Without it the "full set" argument below is false.

**Add** (never ends sessions):

1. open the transaction, take the login lock, validate, insert the row, flush (not committed);
2. `SetAuthorizedKeys(account, login, full set including the new key)`;
3. commit.

- Step 2 fails: roll back. The agent's write is atomic and a refused write restores the previous file,
  so agent and panel both hold the old set. The typed agent error goes out.
- Step 3 fails after step 2 succeeded: the agent now honours a key the panel does not list. This is the
  dangerous direction: a live credential that no screen shows and no remove can name. Requirement: on
  a failed commit the handler makes ONE best-effort compensating `SetAuthorizedKeys` with the
  previous set, logs the commit failure, and returns an error. If the compensation also fails the
  hidden key persists until the next successful change to that login's key set (which resends the full
  panel set and overwrites the file) or until the login is deleted. This residual is stated, not
  closed (see 5: no reconcile).
- The alternative order (commit, then agent) was considered. It makes the hidden-credential case
  impossible for ADD and leaves, on agent failure, a listed key that is not live, which is visible and
  retryable. It is worse for REMOVE (a removed row with a live key). The single-transaction order in
  the contract was kept and the add-side compensation added; the reviewer may prefer commit-first for
  add and should say so.

**Remove** (ends sessions):

1. open the transaction, take the login lock, load the key by both ids (404 otherwise), delete the row,
   flush;
2. `SetAuthorizedKeys(account, login, full set without it)`; an empty set removes the file;
3. `EndSessions(account, login)`;
4. commit.

- Step 2 fails: roll back; the key stays listed and, since a refused write restores the previous file,
  stays live. The revoker is told the key was NOT removed, with the typed error. Repeating converges.
- Step 3 fails after step 2 succeeded: the key is already refused for every new login, but sessions
  admitted with it stay open. This MUST surface as an error and MUST NOT pass silently: a revocation
  reported as done over a session still reading the customer's home is the claim without the fact
  (the same reason `DeleteSftpUser` refuses on a failed end-sessions). The handler rolls the ROW back
  (the key stays listed), so a second DELETE is possible and re-sends the same set (idempotent) and
  asks again to end the sessions. If the row were committed gone instead, the retry would be 404 and
  nothing in the panel could end those sessions. The revoker is told, in a dedicated typed error
  (`SftpSessionsNotEnded`, kind server failure, localized): the key is already refused for new
  logins, open sessions may remain, and repeating the removal will end them. The panel then shows a
  key the agent no longer honours; that divergence is in the SAFE direction, it is visible, and the
  retry repairs it. The audit journal records a failure entry naming login, label and fingerprint.
- `AgentNotFound` from EndSessions is not a failure of this kind: the host holds no such login, so
  there is nothing to end (the `DeleteSftpUser` precedent). It then proceeds to commit.
- Step 4 fails after steps 2 and 3 succeeded: the agent lacks the key, the panel still lists it. Safe
  direction. The error goes out; a retry converges. No compensation is needed.
- Ending sessions kills EVERY session of that login, including ones that authenticated by password or
  by another key, and cuts an upload in flight at the byte it had reached; the partial file stays
  (agent contract). The SPA's confirm dialog says so before the call, and the operator documentation
  says so. A customer cannot remove a key without cutting their own login's sessions; that is a cost
  accepted for revocation actually meaning something.
- Account lock: EndSessions deliberately does not take the account lock, so a backup holding it does
  not delay a revocation. `SetAuthorizedKeys` does, and refuses with `AccountBusy` when it is held;
  that is mapped to a retryable error and the transaction rolls back before EndSessions is reached, so
  a held lock leaves the old set live and sessions untouched.

**Replace.** No replace endpoint is planned. If one is added it is a remove plus an add of the set
in one request and follows the remove rules (sessions ended after the set is replaced, row rolled back
if that fails).

**The stale-session window.** Between the agent finishing step 2 and finishing step 3, a session
authenticated with the revoked key remains open. That is milliseconds on the happy path and as long as
the retry takes on a failed one. New logins with the revoked key are refused from the rename in step 2
onward (sshd reads the file at each authentication; `ssh-keygen -l` validated the new file). One
residual race is not closed here: a connection that is mid-authentication at the instant of the rename
could finish with the old key and open a session after the `pkill` has already matched nothing. The
agent does not observe it. The reviewer should decide if a second EndSessions pass after a short
delay is wanted; this note does not add one, because a sleep in a request path is worse than the
window it narrows.

**Transaction and the agent call.** The agent call runs inside an open database transaction, holding a
connection and a row lock for its duration. The agent's own deadlines bound it (the resilience
pipeline retries a failed connect only, then a typed 503). The cost is accepted; the reviewer should
confirm the lock cannot block an unrelated request longer than the pipeline's total timeout.

## 4. Audit

`SftpAuditJournal` (the module's only place that builds an entry) gains `SftpKeyAdded` and
`SftpKeyRemoved`, each recording: the actor as the journal records it, the login name, the label, the
algorithm and the fingerprint. A failure is journalled with the error code, as the delete is. Never
recorded: the key body, the comment of the pasted line, the agent's raw text. The probe of another
account's login is journalled under the supplied identifier. Tests assert the entry and assert that
the body is absent from it.

## 5. Is there a startup or periodic reconcile?

**No, not for the contents of a key set.** Read, not assumed:

- Panel: `grep -rniE "reconcil" backend/src/Maran.Modules/Sftp --include=*.cs` returns one hit, a
  comment in `Commands/CreateSftpUser/CreateSftpUserCommandHandler.cs` about the create path; the
  module's `Jobs/` folder is empty and it holds no startup task. Positive control: the same grep over
  `backend/src/Maran.Modules/Sites` finds `StartupPhpSiteReconciler`, so the command can see one when
  one exists.
- Agent: it runs `reconcile_sftp_key_files` once at start, and `reconcile_orphan_key_files.rs` only
  REMOVES key files whose login no longer exists (issue #338). It writes no key and compares nothing
  with the panel.

Consequence: if the agent's key file and the panel's rows drift apart (a failed compensation, a file
edited by hand as root, a restored host), nothing repairs it until the next successful add or remove
on THAT login, which resends the full panel set, or until the login is deleted. A hidden extra key in
that state is a live credential the customer cannot see. The panel does not close this, and this note
does not claim it does. A reconcile (startup, and an on-demand "reapply" per login) is the proper
fix and is a separate decision for the owner; it is not in this change because it is a new
unattended root-driving job with no principal (and would raise the `IgnoreQueryFilters` count in
rules/README.md). The stated residual stays until then.

## Checklist (rules/security.md items 1-13)

1. **Input:** key, label and ids validated by a FluentValidator on the command; the agent revalidates
   every key; ids come from the route and are server-established.
2. **Paths:** the panel builds no path; the agent derives the key file's name from the validated
   login and writes it through its own safe-write; nothing path-like comes from the request.
3. **Shell:** the panel spawns nothing; the agent uses argv arrays (`ssh-keygen`, `pkill`).
4. **Config-file injection:** the key is the value written to a line-oriented file; the validator and
   the agent reject newlines and control characters before it is written, and the agent drops options
   and comment and prefixes its own `restrict`.
5. **SQL:** EF Core only; no raw SQL; the search term is a literal ILIKE pattern through `LikePattern`.
6. **AuthZ:** permissions declared on every action, tenant filter on the key via its parent, 404 never
   403, an IDOR test for add, remove and list, view-as declared.
7. **Configuration:** no new variable is read.
8. **Secrets:** the key body and the label never appear in logs, errors or audit beyond fingerprint
   and label; no secret is introduced (public keys only).
9. **Crypto:** none written; the fingerprint is SHA-256 of the blob, an identifier, not a security
   control; RSA below 2048 refused by mirroring the agent.
10. **Surface:** no listening port, daemon or outbound call; the new credential path is key login on
    the SFTP daemon that already exists.
11. **Dependencies:** none added (base64 and SHA-256 come from the BCL).
12. **Agent:** no new rpc; the two used are closed, typed, idempotent, revalidated and audited; nothing
    resembling "run this for me".
13. **Module reach:** the module's manifest keeps `Sftp` and declares nothing new.

## What an attacker could do with this surface

- **A customer on their own login:** add a key they own and log in with it. That is the feature. They
  gain nothing they could not already get by setting their own password, and the key cannot name
  options (`command=`, `from=`, `permitopen=`) because options are refused; the agent writes the one
  option it chooses (`restrict`).
- **A customer reaching another account's login:** blocked by the filter (404), by the route lookup
  on both ids, and by the agent's jail membership check.
- **A viewing operator:** cannot add or remove (refused).
- **A customer racing two requests:** serialized by the login lock; without it, a lost update (3).
- **A customer who gets the panel's database to fail between the agent call and the commit:** they
  cannot cause that; if it happens on an add, a hidden key may remain (3, 5).
- **A customer with a stolen session token:** adds their own key and keeps access after the token
  expires. This is the cost of the feature in the same way a password change is; the audit entry and
  the key list are how the account's owner sees it, and removal ends sessions. Mitigation is not new
  in this note.

## What a reviewer must check

1. That the add and remove handlers follow section 3 in order, and that the login lock is really
   taken before the set is read (read the query; try two concurrent requests).
2. The failure of each of the three effects for add and remove, each as a test: agent set refused,
   commit refused, end-sessions refused, `AgentNotFound` on end-sessions, `AccountBusy`; and what is
   left in the row, in the fake agent's file and in the audit table after each.
3. That an end-sessions failure after the keys were replaced reaches the HTTP client as the typed
   error, rolls the row back, and is not swallowed; that the revoker's text says what is and is not
   done.
4. The IDOR tests: another account's login for list, add and remove; a key id from a second login of
   the same account; a nonexistent key id; each 404, none 403, none calls the agent.
5. View-as: POST and DELETE refused with a view-as token, GET allowed.
6. That the validator and the agent's `ssh_public_key.rs` agree on the same fixture list in both
   directions, including RSA 2048 accepted and 1024 refused, and the 32-key and 8 KiB limits.
7. That the key body appears in no log line (run an add with the Serilog request logging at its
   most verbose and search the output with the exact command, plus a positive control that finds a
   planted marker), no error text, no audit entry, and not in the list response.
8. That the label cannot carry a control character into the audit table or the SPA.
9. The migration is expand-only (`maran migrate guard`) and the key table carries its tenant filter
   without an exemption (`TenantScopeTests`).
10. That nothing in the change touches `proto/`, the agent, or `AgentCapability`.
11. Section 5: whether the owner accepts the absence of a reconcile and the stated residual.
12. Whether commit-first is preferred for add over the compensating call (section 3).

## What the author could not verify

1. Nothing in this note has run: the code does not exist yet, and this session cannot run the
   suites or a real host. Every statement is a requirement until a gate pass and a reviewer say
   otherwise.
2. Behaviour on a real sshd of the key file written by the agent for an Ed25519, an ECDSA and an RSA
   key, and of a revoked key being refused and its open session being killed. The agent's own real-host
   test (`agent/crates/agent/tests/sftp_on_a_real_host.rs`) is the evidence and was not run here; whether
   it asserts refusal after revoke and session termination is checked when the code lands, not now.
3. That account resume moves a parked key file back to the name sshd reads (the agent's `logins`
   area); the author read the lock path in `set_sftp_authorized_keys.rs` and not the resume path.
4. That the existing `Api` rate-limit policy is tight enough for a root-side write plus a `pkill`
   per request; no load was measured.
5. The window in section 3 (a connection mid-authentication at the rename) on a real host.
6. That holding a row lock across the agent call never blocks another request beyond the pipeline's
   timeout; the pipeline's total time for `SetAuthorizedKeys` was not measured.
7. That the SPA's confirm text and the operator documentation state the session cut in all three
   languages; translations are written by the author and not reviewed by a native speaker.
8. The sub-user and reseller permission mapping (section 1).
9. Both PostgreSQL majors (13 and 16) for the new migration and the row lock.
