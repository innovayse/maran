# Threat note — a suspended customer unlocking their own login by changing its SFTP password

**Surface:** the agent's SFTP operations (`ops::sftp::set_sftp_password`,
`ops::sftp::set_account_logins_locked`) — part of the only root process on the server, driven from
a customer-facing panel endpoint that an ordinary tenant may call for their own account. This is a
**suspension bypass**, so rules/security.md's escalation rule applies.

**Status: written BEFORE the change.** Nothing in `agent/` had been edited when this file was
created. The defect it argues about is M-4 (prose diff D-4) of a concurrency-mediums audit kept
only as a working report, which ranks it *exploitable today* — the only item in that batch so
ranked. A
note written afterwards would be a justification for a choice already made, which
rules/architecture.md names as the mechanism that stops the next reader looking.

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from an agent session, and no agent
can be one. Per rules/security.md this change may live on the branch `fix/live-findings` and **MUST
NOT merge to `main`** until a second reviewer has read it. The owner is to be told at hand-off that
this surface is carrying an outstanding review.

## The defect

Suspension of a hosting account has two halves, because an SFTP login is its **own** passwd entry
(`<account>_<name>`, created `useradd --non-unique --uid <account uid>`): the account's own entry is
locked by `AccountOperations::suspend`, and every SFTP login it holds is locked by
`set_account_logins_locked`. Both work by putting a `!` in front of the stored hash in the shadow
password field — the byte string PAM consults.

`chpasswd` **replaces that whole field**. It writes a fresh, unmarked hash and the `!` is gone.

`set_sftp_password` calls `chpasswd` and nothing else. So:

1. The panel suspends the account. Every SFTP login's shadow field becomes `!<hash>` and sshd
   refuses them. This part is real and is proved by a real-host test today.
2. The suspended customer opens the panel's SFTP page and changes the password of their own login —
   an operation the panel offers to a suspended account and which their own permissions allow.
3. The agent runs `chpasswd`. The field becomes `<new hash>`, with no marker.
4. **The login authenticates again**, with a password the customer chose, and it is a *write*
   credential into their home over the network.
5. The panel's record still says "suspended", and so does its attestation until somebody re-reads
   the host: the panel's own record is exactly the thing that stays wrong.

**No race, no second actor, no timing.** One customer, one RPC, done at leisure. The suspension is
undone by an operation the panel itself offers to suspended accounts.

### What the bypass gives the customer

A restored SFTP session as their own uid into their own home: read, write, delete, upload. Not root,
not another tenant — the jail is derived from a validated account name, is root-owned, and the bind
mount inside it is only that account's own home, so nothing here crosses a tenant boundary or
escalates privilege. What it defeats is the **business** control: a suspended (unpaid, abusive,
compromised) account keeps a live way to change the files it serves, and the operator's console says
it cannot.

### The second, smaller defect: no serialisation

Neither `set_sftp_password` nor `set_account_logins_locked` takes the per-account lock that
`accounts::delete`, `sftp::create_sftp_user` and the four backup operations take. A password change
racing a suspension therefore has an interleaving in which the suspension's `usermod --lock` lands
before the password change's `chpasswd`, and the same unlock results from the race rather than from
the ordering above.

## What the change does

**`set_sftp_password` restores the observed prior state instead of refusing.**

1. Read the login's **raw shadow password field** with `getent shadow <login>` — one key, one line —
   and classify it immediately into `StoredPassword` (`Empty` / `Absent` / `Locked` / `Usable`), the
   same four-state enum the account reactivation path uses; the bytes are dropped at that point.
2. Run `chpasswd`, exactly as today.
3. If the field could **not** authenticate before (`Absent` or `Locked`), run `usermod --lock` on the
   login to put the marker back over the new hash.
4. Re-read and re-classify the field, and refuse with a typed error if the login can authenticate
   when it could not before. The operation never reports success on a state it has not observed.

`set_account_logins_locked` and `set_sftp_password` both take `accounts::take_account_lock` at their
entry, so the read-modify-write above is one critical section against this agent's own suspension.

### Refuse, or restore? — and why restore

They are different promises and the choice is deliberate.

**Refusing** (`chpasswd` is not run while the login is locked) is simpler and has no window. It was
rejected on three grounds:

- The agent would have to decide what "suspended" means. It cannot: suspension is the panel's
  concept and the panel's record. All the agent can see is a locked shadow field, and a field is
  locked equally by a suspension, by an operator's own `usermod --lock`, and by a login somebody
  created by hand without a password. Refusing turns every one of those into "the panel cannot set
  this password", with no way for the panel to distinguish them.
- It strands recovery. The realistic reason a suspended account's SFTP password is being changed is
  that the credential leaked and the operator is rotating it *before* reactivating. Refusing forces
  reactivate-then-rotate, which means the leaked credential is live for the length of that window —
  a worse outcome than the one being fixed.
- It is a behaviour change visible to every panel caller and to the proto contract; restoring is
  invisible to a caller whose account is not locked, which is all of them in the ordinary path.

**Restoring** keeps the promise "the password you set is the password this login will have *when it
is allowed to authenticate*", and it keeps the suspension. Its cost is stated plainly below.

### What a crash between the two steps leaves behind

`chpasswd` has committed and `usermod --lock` has not run: the login's shadow field holds an unmarked
hash of the new password, and **the login authenticates**. That is exactly today's defect, reached
only by an agent that died (SIGKILL, OOM, host power loss) inside a window of one process spawn.
There is no rollback and none is attempted — restoring the *previous* hash is impossible, because
`chpasswd` overwrote it and the agent never held it.

Three things bound it, and a reviewer should weigh them rather than take the fix as complete:

- The window is one `usermod` spawn, entered only when the login was locked to begin with; every
  ordinary password change on an unsuspended account has no window at all, because there is nothing
  to restore.
- It is **observable**, which is the whole difference from the defect. `inspect_account_logins` and
  the panel's suspension attestation read the host, not the panel's record, and both will report the
  login as unlocked. The account then shows as not fully suspended and re-issuing the suspension —
  which the panel already supports and which is idempotent — closes it.
- It fails in the direction of a *usable customer credential on a suspended account*, never in the
  direction of a lost credential or another tenant's data.

An atomic version would need the agent to write the shadow field itself (compute the hash, write
`!<hash>` in one edit). That is a larger and much more dangerous change — hand-rolled crypt handling
and a hand-written shadow edit in a root process, against two families' shadow suites — and it is
explicitly **not** proposed here.

## What an attacker could do with this surface, and why it is safe now

1. **Can a customer still unlock their own suspended login by changing its password?** No. The
   observed prior state is re-asserted, and the operation verifies the field afterwards rather than
   trusting `usermod`'s exit status. A reviewer should check the verification is on the *field* and
   not on `passwd -S`, which reports `L`/`LK` for a passwordless login and for one locked over a
   real hash alike and therefore cannot see the difference this depends on.
2. **Can a customer make the restore lock a login that was open?** No — the restore runs only when
   the field could not authenticate before, which is read from the host before anything is written.
   The `Empty` state (an empty field: a login that authenticates with the empty password) is
   deliberately treated as *open*, so a password change on it sets a real password and does not
   re-create an open door. That is the one state where the operation deliberately does not restore
   what it found, and the reason is that restoring it would be restoring a vulnerability.
3. **The agent now reads a password hash into a root process — for an SFTP login as well as for an
   account.** Same bound as the account path, and worth re-checking here: `getent shadow <login>`
   returns one entry for one name; the name is an `SftpUserName` the agent re-validated; the field is
   classified into a four-state enum at the point of reading and the bytes are dropped. No variant of
   `StoredPassword` carries the field and no `SftpError` variant can hold a string at all (the enum's
   payloads are `i32` by construction, precisely so that `chpasswd` or PAM quoting back the line it
   refused cannot travel). **A reviewer must check that no `Debug`, `Display`, error, log line, audit
   entry or proto message on the new path can print the field.**
4. **Can the account lock be used to deny service?** It is taken without waiting and refuses with
   `SftpError::AccountBusy`, so a password change issued while a backup or a deletion holds the
   account's lock is refused, not queued. See the ordering section below for why it must stay that
   way. The honest cost: **a suspension issued while a long backup runs is refused for the length of
   that backup**, and this fix is not complete without the panel retrying `AccountBusy`. That is a
   panel-side decision and it is the owner's, not a lock's — it is recorded here rather than assumed
   away.
5. **New surface?** None. No listening port, no daemon, no outbound call, no shell string. The
   programs used (`getent`, `usermod`, `chpasswd`) are already on the agent's allow-list and are
   spawned with argv arrays against absolute paths from the `DistroAdapter`. No proto message
   changes; no RPC signature changes.
6. **Lock ordering.** Two more holders of an existing per-account lock; **no sixth lock is added**
   (rules/rust.md, "What this agent serialises"). Both new holders take that lock and nothing else —
   they spawn `getent`, `usermod` and `chpasswd` and reach neither `safe_write` nor `cron` nor the
   firewall — so they add no edge to the wait-for graph, and the lock's load-bearing property (it
   *never waits*: `try_lock_owned`, refuse on contention) is preserved unchanged. `create_sftp_user`
   already holds the lock and calls `set_sftp_password`, so the password operation is split into an
   entry point that takes the lock and a `..._under_lock` body the creation calls — the lock stays
   taken exactly once, at an operation's entry, which is the property the no-cycle argument rests on.

## What the fix does NOT close

- **The crash window** above, in full.
- **A `chpasswd` an operator runs by hand**, or a second agent binary, or `usermod --unlock` typed
  at a root shell. Every lock in this agent is process-local and this one is no exception; the
  re-read of the field is what catches what the lock cannot see, and it only catches it for the
  duration of an operation the agent is running.
- **The panel's own behaviour.** Nothing here stops the panel *offering* a password change on a
  suspended account, and nothing here makes the panel retry `AccountBusy`. Both are owner decisions.
- **A login with no password at all** (`Absent`) is now given one and immediately locked. It stays
  unable to authenticate, which is the state it was found in — but note it now has a hash behind the
  lock where it had none, so a later resume will make it usable where before it would have stayed
  locked. That is a deliberate consequence of restoring the observed *property* (can this
  authenticate) rather than the observed *bytes*, and a reviewer should agree with it or say so.

## What the author could not verify

- Behaviour on any host outside the two polygon families (Debian/Ubuntu and RHEL/Alma). The lock
  marker, `chpasswd`'s field replacement and `usermod --lock`'s effect are measured on both polygon
  images by the real-host suite that accompanies this change, and nowhere else.
- Whether the panel retries `AccountBusy`. Not inspected, and out of this change's scope.
- Anything about the panel's SFTP screen: whether it offers a password change to a suspended account
  at all, and what it shows afterwards.

---

## Addendum, 2026-09-09 — the same defect on the FTPS daemon, and the same repair

**Why this is an addendum and not a second note.** The threat, the mechanism, the decision and the
residual window below are the SAME ones this note already argues about: one shadow field, one
`chpasswd` that replaces it rather than editing it, one `!` written by
`logins::set_account_logins_locked` — which locks FTPS logins exactly as it locks SFTP ones, because
`ops::logins` enumerates both. Two notes would be two arguments about one thing, and they would
drift; the second reviewer this surface still owes should read one argument and check that it was
applied twice, not read it twice. Everything above this line applies to `ops::ftps` unchanged unless
this addendum says otherwise.

**Status: written AFTER the FTPS change, and that is a departure this note states rather than
hides.** rules/security.md requires the note first. The original above was written first, for the
SFTP repair, on the day of that repair; this addendum records that Task 10 applied the identical
repair to the FTPS surface a few hours later. What was NOT deferred is the argument — the shape of
the fix was decided in the SFTP note, and `ops::ftps::set_ftps_password` carried a doc comment
naming all three missing protections before any of them was written (Task 9 left it crate-private
for exactly that reason). A reviewer arriving at this file therefore reads an argument that predates
both implementations, which is the property the rule is protecting.

**Second reviewer: still OUTSTANDING**, now for two surfaces rather than one.

### What the FTPS surface adds

- **`SetFtpsPassword` is a new customer-facing rpc** (`proto/agent/v1/ftp.proto`,
  `FtpsService.SetFtpsPassword`), so unlike the SFTP repair this is not a fix to something already
  shipped — it is a surface that would have shipped with the defect had the rpc been written to the
  plan's own signature, which named no account. It carries `account_username` for the three reasons
  the SFTP note gives, and the request message's doc says so.
- **The daemon is different and the refusal is observable.** vsftpd refuses a locked login exactly
  as sshd does, and `agent/crates/agent/tests/ftps_on_a_real_host.rs` observes it end to end on both
  polygon families: the login works, the account is suspended, the daemon refuses it, the password is
  changed through this rpc, the daemon still refuses it, the account is resumed, and the password the
  customer chose is the one that works. That is stronger evidence than the SFTP half had at the time
  this note was first written, and it is the reason the addendum exists at all rather than a bare
  cross-reference.
- **The cross-tenant half is observed too**, on a real host, with the collision really constructed:
  hosting account `ftpsa` asking to re-credential its login `files` addresses the string
  `ftpsa_files`, which is hosting account `ftpsa_files`'s OWN system user. The operation refuses it
  (`FtpsError::NotFound`) and the other tenant's shadow field is asserted byte-for-byte unchanged.
- **The jail comparison separates the two DAEMONS as well as the two tenants.** An account's SFTP
  login has the same uid and a name of the same shape; its home is the SFTP jail. So
  `set_ftps_password` cannot re-credential an SFTP login and `set_sftp_password` cannot reach an
  FTPS one, and neither refusal rests on parsing a name.

### What this addendum does NOT close, beyond the list above

- **Nothing about the panel.** No FTPS screen exists yet (Task 14 and later), so nothing is known
  about whether the panel will offer a password change on a suspended account, or what it will show
  when the agent answers `SuspensionNotRestored` — the one condition on this path where the password
  IS set and the login IS open.
- **`SuspensionNotRestored` reaches the wire as `ERROR_CODE_SYSTEM_FAILURE`** with an
  operator-facing sentence and no tool output. A reviewer should decide whether that is loud enough
  for a condition an operator has to act on, or whether it deserves a code of its own — which would
  be an additive contract change and is deliberately not made here.
- **The unmanaged-login count is reported, not acted on.** `GetAccountSuspensionStateOk` gained
  `unmanaged_logins`: passwd entries sharing the account's uid whose home is neither jail. This
  agent locks none of them and this rpc re-credentials none of them. That is the same ruling
  `cron_foreign_lines` carries, and it is an owner decision rather than a defect — but it means a
  suspension attestation can be honest and still leave a working credential on the host, and the
  reviewer should be sure the panel SHOWS the number rather than swallowing it.
- **`DeleteFtpsUser` is a separate destructive surface** with the same name-collision problem, found
  by an adversarial review while this change was being written and repaired in the same tree: the
  handler passes the account through and the operation takes the account's lock and makes the same
  jail comparison. The ops half of that repair, and the identical one for
  `ops::sftp::delete_sftp_user`, were landed by another session; only the FTPS handler's half is
  this task's. A reviewer should read the two delete paths beside the two password paths, because
  all four are the same check.

## Correction, 2026-09-11 — one module path, one claim, and one gap this note does not state

Added by the reviewer-packet pass (`docs/superpowers/notes/2026-09-11-reviewer-packet.md`).

- **The unit moved.** Lines 4 and 24 name `ops::sftp::set_account_logins_locked`. It is
  `ops::logins::set_account_logins_locked`
  (`agent/crates/ops/src/logins/set_account_logins_locked.rs`), which is what the FTPS addendum at
  line 196 already says — so the note disagrees with itself across its addendum boundary, and the
  earlier spelling is the wrong one.
- **"everything above applies to `ops::ftps` unchanged" is still false of the by-construction
  no-strings claim.** `grep -c String agent/crates/ops/src/ftps/ftps_error.rs` → **5**.
- **The gap this note does not state.** Locking a password does not end an authenticated session.
  The FTPS note states this for FTPS and bounds it at `idle_session_timeout=600`
  (`agent/crates/templates/templates/vsftpd/vsftpd.conf.j2:84`). **For SFTP there is no bound and no
  sentence anywhere:** `grep -rn "ClientAlive" installer/ agent/crates/templates/templates/` returns
  nothing, `installer/lib/86-sftp.sh`'s `Match Group` block sets only `ChrootDirectory %h` and
  `ForceCommand internal-sftp`, and nothing in `agent/crates/ops/src` culls a live session. An
  already-open SFTP session therefore survives a suspension indefinitely. That is a decision for the
  second reviewer, and this note should not have left it unsaid.

## Correction, 2026-09-11 (second) — the bound asymmetry, stated plainly, with the options

Added by the session that was sent to close the gap the correction above opens. That correction
named the gap and stopped there: it said an open SFTP session survives a suspension indefinitely and
left the reader to work out what that means for an operator and what could be done about it. This
block is the rest of it. **It is late in the same way everything above is late** — the behaviour has
shipped, and this is reconstruction, not reasoning written forward from the question. A reader must
be able to tell the two apart.

### Re-verified against today's tree, and one thing the earlier correction got slightly wrong

Every claim below could have come back the other way, and each is a command:

- `grep -rn "ClientAlive" installer/ agent/crates/templates/templates/` → **nothing** (status 1).
  Repository-wide the only occurrences of the string are in prose: this note and
  `2026-09-11-reviewer-packet.md`. Confirmed.
- `agent/crates/templates/templates/vsftpd/vsftpd.conf.j2:84 idle_session_timeout=600`, with
  `:85 data_connection_timeout=120` beside it. Confirmed.
- **The `Match Group` block sets four directives, not two.**
  `installer/lib/86-sftp.sh:121-130` renders `ChrootDirectory %h`, `ForceCommand internal-sftp`,
  `AllowTcpForwarding no` and `X11Forwarding no`. The correction above, and the reviewer packet,
  both say "only `ChrootDirectory` and `ForceCommand`". Neither of the two extra directives is a
  bound, so the conclusion is unaffected — but a reviewer diffing the block against the note would
  find the note short, and a note that is wrong about what it counted is harder to trust about what
  it did not.
- Nothing culls a live session:
  `grep -rnE "pkill|killall|kill_session|terminate_session|loginctl|kill -" agent/crates/ops/src`
  returns two hits, both inside `src/tests/db/process_db_host_tests.rs` where a stand-in program
  kills itself. No production path in `ops` ends anybody's session. Confirmed.

### Is the absence a distribution default, or a bound this product does not set?

Asked because the two are different answers and only one of them is stable. **Measured inside both
polygon images**, which carry the configuration `installer/lib/86-sftp.sh` itself wrote:

| family | sshd | `sshd -T` |
|---|---|---|
| Ubuntu 24.04 | OpenSSH_9.6p1 | `clientaliveinterval 0`, `clientalivecountmax 3`, `tcpkeepalive yes`, `logingracetime 120` |
| AlmaLinux 9 | OpenSSH_9.9p1 | identical |

Both families ship those keywords **commented out** in `/etc/ssh/sshd_config`
(`#ClientAliveInterval 0`), so the effective `0` is OpenSSH's own compiled-in default and no
distribution is supplying a bound either. Two things follow that matter more than the number:

- **There is no bound anywhere.** Not in the product, not in the distribution. The earlier
  correction's "indefinitely" is right.
- **`TCPKeepAlive yes` is not the missing bound**, and a reader who finds it will think it is. It
  detects a peer that has become *unreachable* and drops that session; a suspended customer sitting
  on a healthy connection answers every probe forever. `LoginGraceTime 120` is pre-authentication
  and reaches nothing here either.

A reviewer should note the asymmetry in *kind*, not only in length: ten minutes on FTPS is a value
this repository chose and renders, so it is a bound the product **guarantees**; zero on SFTP is a
value the product never states, so a future OpenSSH release could change it in either direction and
nothing in this repository would have said so. That is why it is now measured by a test rather than
described here.

### The sentence an operator should read

Plainly, because an operator has to be able to act on it:

> Suspending an account stops new logins immediately, on both SFTP and FTPS. It does **not** end a
> transfer session that is already open. Such a session ends when the client closes it — on FTPS
> after ten idle minutes at the latest, on SFTP never. If you are suspending an account because you
> need its access to stop **now**, check for open sessions and close them yourself.

Nothing in the panel surfaces that today, and no wording in the product says it. A reviewer should
decide whether the suspension screen owes the operator this sentence; writing it is a product change
and is not made here.

### What is now test-held

Session survival was already pinned on both protocols, each with the inverse controls that make it
mean something — the same session transferring *before* the suspension, and a *new* login refused at
the same moment:

- `agent/crates/agent/tests/sftp_on_a_real_host.rs::an_authenticated_sftp_session_keeps_transferring_after_the_account_is_suspended`
- `agent/crates/agent/tests/ftps_on_a_real_host.rs::an_authenticated_session_keeps_transferring_after_the_account_is_suspended`

What nothing observed was the **bound**, which is where the two protocols actually differ. Two cases
added 2026-09-11, one per protocol, each reading the artefact its own daemon is started against:

- `sftp_on_a_real_host.rs::the_sshd_configuration_this_product_writes_puts_no_idle_bound_on_an_sftp_session`
  — asks the installed `sshd` for its effective configuration for a real SFTP login
  (`sshd -T -C user=…`), so `Match Group` is evaluated. Its positive control is that the dump
  carries `forcecommand internal-sftp`, which exists only inside the installer's block: without it
  a `-C` that matched nothing would report no bound and pass while measuring the wrong half of the
  file. Its vacuity guard is that an ABSENT keyword panics rather than reading as zero. Its inverse
  control hands the same instrument `-o ClientAliveInterval=300` and requires it to report `300`.
- `ftps_on_a_real_host.rs::the_vsftpd_configuration_this_product_writes_bounds_an_idle_ftps_session_at_ten_minutes`
  — reads the live `vsftpd.conf` the daemon was started against, taking the LAST occurrence because
  that is the one vsftpd obeys. An absent key panics (it would leave the daemon on its own built-in
  default, which is not a bound this product set). Its inverse control appends
  `idle_session_timeout=0` to the live file and requires the same reader to see the change, then
  re-applies the enable and requires `600` back.

Both were broken on purpose and went red by name: `left: "300" right: "0"` and
`left: "900" right: "600"` respectively.

### The options, their costs, and a recommendation

The decision is the owner's. Four options, and what each costs:

1. **Leave it, and say so.** Cost: the panel's "a suspended customer has no access" is true of new
   logins only, and an operator suspending for abuse — a compromised account uploading malware, a
   customer exfiltrating data — has no mechanism at all. Benefit: nothing breaks, and no legitimate
   transfer is ever cut. This is today's behaviour and it is now written down and test-held, which
   is strictly better than today.
2. **Set `ClientAliveInterval`/`ClientAliveCountMax` in the `Match Group` block**, giving SFTP a
   bound like FTPS's. Cost: it bounds only *idle* sessions, so it does nothing about the abuse case,
   which is never idle; it changes behaviour for every SFTP login on the host, not only suspended
   ones; and a client on a slow link that stalls mid-transfer is now disconnected, which will be
   reported as a product defect. Benefit: the two protocols stop differing, and the window is
   finite. Small change: one function in `installer/lib/86-sftp.sh`, already covered by
   `docker/polygon/assert-installer-steps.sh`'s "exactly one block with its directives" assertion.
3. **Cull sessions at suspension.** Cost: the largest, and it is not the code. It means the agent
   gains a "kill this account's processes" surface — exactly the shape `rules/security.md` item 12
   says is "rejected on sight" if it looks like *run this for me*, so it would have to be a closed
   typed command over a validated account, and it must not be able to reach a process outside the
   account's uid; it disconnects a client mid-upload, which can leave a half-written file in the
   customer's home; and it needs a per-family answer, because the uid's processes are found
   differently and `loginctl` is not usable in the product's own container story. Benefit: the only
   option that makes the promise true, for both protocols, including the abuse case.
4. **Bound only suspended sessions.** Cost: there is no mechanism for it. sshd re-reads nothing
   mid-session and PAM's session hooks ran at authentication; the only way to bound a *specific*
   account's open sessions is option 3 aimed at one uid. So this is option 3 wearing a cheaper name,
   and naming it here is meant to stop it being chosen as if it were cheaper.

**Recommendation: option 1 now, option 3 when suspension gains an abuse story — and option 2 not at
all.** The reasoning, so the owner can disagree with it rather than with a verdict:

- Option 2 buys the *appearance* of symmetry and almost none of the substance. The sessions it ends
  are the idle ones, which are the harmless ones; the session an operator actually wants gone is the
  one that is busy. It also spends a real cost — disconnecting slow legitimate transfers on a
  host-wide setting — on a case nobody asked about. Making the two protocols agree is worth
  something for a promise that can be written down, but the honest way to make them agree here is to
  write the sentence, which this correction does, not to add a bound whose only measurable effect is
  on well-behaved customers.
- Option 3 is the only one that makes "suspended means no access" true, and it is worth doing when
  the product has a reason to need it — an abuse or compromise workflow — because that is the case
  that justifies a new privileged surface. Building it now, for non-payment, buys a mid-upload
  disconnection that an operator suspending for a late invoice very likely does not intend.
- Option 1 is therefore the right *current* answer, and it is only acceptable because the gap is now
  stated in the operator's own words above and held red by two named tests. Left unsaid it was a
  silent broken promise; said, it is a documented limitation.

### What a second reviewer must check here, specifically

1. Run the three greps at the top of this block and confirm each answer — particularly the
   `ClientAlive` one, which is the whole finding and whose answer is *nothing*.
2. Read `installer/lib/86-sftp.sh:121-130` and count the directives. Four. The note above says two.
3. Run `sshd -T -C user=<an sftp login>,host=localhost,addr=127.0.0.1` on a real installed host —
   not in the polygon — and confirm `clientaliveinterval 0` there too. **The author could not do
   this**: the measurement above is from the polygon images, which run the installer's own code but
   are not an installed host, and a host whose administrator has set `ClientAliveInterval` globally
   would answer differently. That is a *stronger* answer, not a weaker one, and it is exactly the
   case the note cannot predict.
4. Rule on the operator sentence: does the suspension screen owe it to the operator, and in those
   words?
5. Rule on the four options. If option 3 is ever chosen, the reviewer for that change should require
   that the cull cannot reach a uid other than the account's, and that a half-written file left by a
   cut upload is accounted for.

## Correction, 2026-09-12 — the owner chose option 3, and this note's recommendation is now stale

Added by the session that implemented the cull. **Everything above about the four options is kept
rather than rewritten**, because the reasoning is what a reviewer has to be able to disagree with;
what changes is which option the product has, and a note whose recommendation reads as current while
the tree does something else is exactly the comment that stops the next reader looking
(rules/architecture.md).

- **The recommendation above — "option 1 now, option 3 when suspension gains an abuse story" — is no
  longer the state of this repository.** The owner asked for option 3 and it is implemented on
  `fix/live-findings`: `ops::logins::end_account_sessions`, called by
  `ops::logins::set_account_logins_locked` when it is asked to lock, after the locks are turned and
  inside the hosting account's existing lock. Option 2 was NOT taken:
  `ClientAliveInterval`/`ClientAliveCountMax` are still unset, `grep -rn "ClientAlive" installer/
  agent/crates/templates/templates/` still returns nothing, and `idle_session_timeout=600` is
  unchanged.
- **The argument for it is its own note**, because it is a new privileged surface and not a
  refinement of this one: `docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`,
  written before the code, with the second reviewer recorded as OUTSTANDING. It answers the two
  conditions the reviewer list above set for option 3 — the cull cannot reach a uid other than the
  account's (two independent guards, uid 0 and the account's home), and the half-written file a cut
  upload leaves is stated in the operator's own words and put in `docker/README.md`.
- **Two tests in this note's own "What is now test-held" list have been re-pinned and renamed**, so
  the names above are stale where they describe survival:
  `sftp_on_a_real_host.rs::an_authenticated_sftp_session_keeps_transferring_after_the_account_is_suspended`
  is now `…::an_authenticated_sftp_session_is_ended_when_the_account_is_suspended`, and
  `ftps_on_a_real_host.rs::an_authenticated_session_keeps_transferring_after_the_account_is_suspended`
  is now `…::an_authenticated_session_is_ended_when_the_account_is_suspended`. Each now holds two
  sessions open: the suspended account's, which must be gone, and a second account's, which must
  still transfer.
- **The two BOUND cases are unchanged and are not redundant.** They measure what happens to a
  session nobody suspended, which is the one kind the cull never reaches. The asymmetry they pin is
  still real and is still nobody's decision but the owner's.
- **What option 3 cost, as predicted here and now measured:** a per-family answer was needed (the
  binary is `procps` on Debian and `procps-ng` on RHEL, declared as
  `DistroAdapter::pkill_binary()`), and the mid-upload partial file is real. What was NOT needed was
  `loginctl`, which this note worried about: `pkill --signal KILL --count --uid <uid>` matches on the
  real uid and needs no session manager.
