# Threat note: the cron creation rpc now carries a plan allowance

Date: 2026-09-13. Surface: `maran.agent.v1.CronService.CreateCronEntry`.
Change: one additive field, `optional uint32 max_entries = 4`, and one additive
`ErrorCode`, `ERROR_CODE_LIMIT_REACHED = 10`. The agent refuses a creation when
the field is present and the account's crontab already holds at least that many
managed entries, decided inside the per-account cron lock it already takes.

## Ruling on the escalation requirement, stated plainly

`rules/security.md` "Sensitive change escalation" enumerates the surfaces that
require a second reviewer and a threat note written first: auth, session and token
handling, the agent's `privs` module, license verification, and the installer's
privileged steps. **This change touches none of them**, so the mandatory second
reviewer and the write-it-first obligation do not attach, and this note does not
claim an OUTSTANDING review it is not owed.

It is written anyway, and the reason is item 12 of the same checklist: this is a
change to the contract of the only root process on the server. What item 12 asks
of a new rpc — closed, typed, idempotent, re-validated, audited, and never
"run this for me" — is worth asking of a widened one, and the answers are below so
a reviewer reads an argument rather than a diff. Being written after the change,
it is a justification and carries that weakness; what keeps it honest is that every
claim below names the file or the test that would contradict it.

## What an attacker could do with this surface

The caller is the panel process, reaching the agent over a unix socket gated by
`peercred` (`agent/crates/agent/src/peercred/`). The question is therefore what a
compromised or buggy panel — or a future module with `AgentCapability` for cron —
gains from this field.

- **It cannot make the agent do more. Only less.** The field's entire effect is an
  additional refusal path. There is no value of it that causes a write, a spawn, a
  path resolution, or any privileged work that did not already happen. The check is
  an integer comparison against the length of a document the operation had already
  read for its own reasons.
- **It cannot be used to exceed a limit.** Omitting it yields exactly the behaviour
  the rpc had before the field existed. A caller that wants no enforcement is in the
  state every caller was in yesterday, so the field opens no bypass — it can only
  tighten.
- **The worst abuse is self-denial, scoped to one account.** `max_entries: 0`
  refuses that account's next cron creation. It touches no other account: the count
  is of that account's crontab and the lock is per account
  (`ops/src/cron/cron_lock.rs`). It installs nothing, writes no file, and leaves the
  live crontab exactly as it was — asserted by
  `a_creation_beyond_the_stated_allowance_is_refused_and_writes_nothing`, which
  checks the installed table and the set of command files are unchanged.
- **Nothing customer-supplied reaches the host through it.** It is a `u32`, not a
  string. It is deliberately NOT wrapped in a validated type, and
  `validated_creation.rs` says why: there is no value of a `u32` this agent must
  refuse, so a `parse` that could never fail would be the defensive call
  `rules/rust.md` tells us to delete rather than label. The three values that DO
  reach a file — account, schedule, command — are unchanged and still validated
  types (checklist items 1 and 4).
- **The new error cannot leak a secret.** `CronError::EntryLimitReached` is a unit
  variant, so there is no field a crontab could be quoted into, and
  `cron_status.rs` maps it with an empty `tool_output` like every other variant.
  That matters in this area specifically: a managed crontab carries the account's own
  environment assignments, and `crontab(1)` quotes back the table it refuses
  (checklist item 8). `no_mapped_failure_carries_a_programs_output` covers the new
  variant, because it iterates `every_variant()` and the variant was added to it.
- **No new surface of the kinds item 10 forbids.** No listening port, no daemon, no
  outbound call, no dependency.
- **Idempotency is preserved, and this was the one place the change could have
  broken something real.** The allowance is checked AFTER the duplicate check, not
  before. An account that has just filled its allowance is exactly the account whose
  creation succeeded, so a retry after a lost response arrives with the crontab
  full; asked in the other order the rpc would answer "your plan is full" about an
  entry that is already installed and running, and the caller would record a failure
  for work that had been done. `rules/architecture.md` requires every agent command
  to report `AlreadyExists` on repetition, and
  `a_retry_of_an_existing_entry_reports_already_exists_rather_than_the_limit` is
  what holds that up.
- **The agent stays stateless.** It remembers no allowance and holds no plan; the
  number is compared and discarded. Enforcement authority stays on the backend,
  which is where `rules/security.md` puts it: the agent compares the number the
  panel sent it.

## What a reviewer should check

1. That the field really is optional on the wire and that absence is not read as
   zero. Zero as an allowance means "allow nothing", so a bare `uint32` would make a
   newer agent refuse every cron entry any older panel on the host asked for. Three
   tests sit on this: `an_unstated_allowance_stays_absent_rather_than_becoming_zero`,
   `a_creation_that_states_no_allowance_is_never_refused_for_a_limit`, and
   `No_allowance_leaves_the_field_absent_rather_than_sending_a_zero_that_means_allow_nothing`.
2. That `ERROR_CODE_ACCOUNT_BUSY` was not widened. Its contract is that the caller
   may reissue the identical request; this refusal is not retryable and a panel that
   read it as busy would loop. `an_allowance_the_account_has_already_used_up_is_its_own_code`
   asserts the two codes differ.
3. That the panel's own pre-check is still there. It is the refusal that touches no
   host at all, and it is where the plan lives.

## What the author could not verify

- **No second reviewer.** This is an agent session; one could not be obtained. The
  change is not in the escalation list, so this is not recorded as a review debt —
  but it is stated rather than left to be assumed.
- **Not exercised against a real `crontab(1)` under contention.** The race is driven
  against the recording host's arrival gate, which parks the first caller between its
  crontab read and its install. That makes the interleaving happen on every run,
  which a real host cannot be made to do; what it does not observe is `crontab(1)`'s
  own spool behaviour, which the existing `#[ignore]`d polygon suite covers for the
  unlocked paths.
- **Nothing here says what happens if a module other than Cron ever drives this
  rpc.** The allowance would then be that module's number, and the reasoning above
  about the panel owning the policy would need re-reading.

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
