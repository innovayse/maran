# Threat note — Plan 5, cron, firewall and the installer's firewall steps

Required by `rules/security.md` ("Sensitive change escalation"). Covers the parts of
`docs/superpowers/plans/2026-09-02-maran-cron-firewall-monitoring.md` that fall under
that rule: the installer's privileged firewall steps, the agent's root-side cron read
path, the brute-force ban path together with the forwarded-headers configuration it
depends on, and the cron environment denylist. **This change needs a second reviewer.**

The question each section answers is the one the rule asks: **what could an attacker do
with this surface, and why is it safe now.**

## Summary

**Covered here, because the code exists and was read:**

1. The installer's privileged firewall steps — `installer/lib/87-firewall.sh`,
   `installer/uninstall.sh`.
2. The cron read path — `agent/crates/ops/src/cron/entry_files.rs`,
   `open_cron_directory.rs`, `process_cron_host.rs` (ruling R4).
3. The brute-force ban path — `backend/src/Maran.Modules/Firewall/`, and
   `backend/src/Maran.Host/Extensions/ForwardedHeadersExtensions.cs`, which decides
   whose address a ban lands on (ruling R8).
4. Cron environment handling — the `MAILTO`/`SHELL` refusals (ruling R13).
5. The password-reset token flow and the SMTP sending path (rulings R11 and R12) —
   `backend/src/Maran.Modules/Identity/Commands/{RequestPasswordReset,ResetPassword}/`,
   `Identity/Domain/{PasswordResetToken,SecurityPolicy}.cs`, and
   `backend/src/Maran.Modules/Monitoring/IntegrationEvents/Handlers/SendMailRequestedHandler.cs`.
   An earlier revision of this note deferred that section on the ground that Monitoring
   held no `.cs` files. It now holds all of them, and section 5 below is the analysis.

**Is anything unsafe right now?** Nothing that a reader of this note can act on as
"exploitable today", but three things are stated plainly rather than filed as clean:

- **The brute-force detector does not exist.** `BruteForceDetected` is declared in
  `backend/src/Maran.Sdk/Contracts/BruteForceDetected.cs` and handled in
  `Firewall/IntegrationEvents/Handlers/BruteForceDetectedHandler.cs`, but **nothing in
  the repository publishes it** (grep over `backend/src` and `backend/tests`: the only
  producers are the handler's own tests). The whole automatic-ban half of R8 is
  therefore dormant. Everything §3 says about it is a statement about the machinery
  that is in place for when the Identity side lands, not about a live feature.
- **Loopback is a trusted proxy, and on a hosting box loopback is not only nginx.**
  §3 is the section to read.
- **Two findings from the round-3 installer re-review are still open** (F5 and F6 of
  `.superpowers/sdd/2026-09-02-maran-cron-firewall-monitoring/task-16-rereview-3.md`);
  its CRITICAL and all three MAJORs are closed in the working tree. §1 names each.

**One guard the design claimed and the code did not have** was found during this pass,
in the forwarded-headers configuration, and it was already being fixed while this note
was being written. §3 records it, because a defence that has to be re-derived once has
to be pinned.

## The attacker model

The same one the `privs` and databases/SFTP notes use, with one addition that matters
here.

- A **hosting customer**, who fully controls the contents of their own home directory
  and the panel inputs their role permits, but controls neither the panel's code nor the
  agent's arguments. Plan 5 gives this person something new: the ability to run
  arbitrary commands on the host, on a schedule, as their own uid. Every process on the
  machine is therefore potentially theirs.
- A **remote attacker** with no account, hammering the panel's login form from one
  address or from many.
- An **operator** running the installer or the uninstaller. Not hostile — but their
  ordinary actions are inputs, and a step that leaves a host with no firewall after a
  normal uninstall is a denial of service reachable without any attacker at all. That is
  the surface §1 is graded on.

---

## 1. The installer's privileged firewall steps

`installer/lib/87-firewall.sh` (669 lines) seeds two nftables files into `/etc/maran/`,
wires an `include` block into the family's nftables target, enables and starts the
service, and disables firewalld. `installer/uninstall.sh` (501 lines) takes all of it
back. Both run as root, and both edit a file the host boots from.

**This area is being actively fixed by another agent while this note is written.** What
follows describes the design and names which defects the round-3 re-review left open;
line numbers are deliberately absent because they will move.

### The one failure this whole step is ordered around

`nft -f` on an `include` whose target file does not exist is not a warning. It is
`Error: File not found`, exit 1, and **the entire load aborts** — so the operator's own
tables in the same file do not load either. A host left in that state boots with
`nftables.service` FAILED and **no firewall at all**, not a partial one. That was
measured against real `nft` during the review, not assumed.

Every ordering decision in both scripts follows from it.

**Install order.** Both rendered files are written **before** any include mentions
either; the bans file is included first, because file order is load order and the rules
table's chain assumes the bans table already exists.

**Uninstall order.** The include lines come out **first**. Every later step can then be
interrupted safely: the next boot loads the distribution's own configuration and simply
does not have our tables. Deleting the files first and being interrupted produces
exactly the FAILED-unit state above.

### What defends this now

**The ruleset text is rendered by the agent, never written in shell.** `maran-agent
render-firewall-bans` and `render-firewall-ruleset --ssh-port … --panel-port …` print
the same templates the agent applies at runtime, so the seed and every later mutation
come from one source. A copy of that text in the installer would be a second source, and
the first divergence between them is a firewall that changes shape the moment an
administrator touches a rule.

**The SSH port is detected, never guessed.** `Firewall__SshPorts` and
`Firewall__PanelPort` are read from the `panel.env` that step 60 wrote, and a missing
value **aborts the step**. The seeded policy is `policy drop`; a guessed SSH port is a
server the operator can no longer reach and cannot fix remotely. `ssh_port_flags` is its
own unit with its own test because the list must be usable **whole** — it is read
through a command substitution rather than a process substitution precisely so that a
partial list cannot silently seed a ruleset for the ports that happened to come first.

**Every write is staged and renamed inside the destination's own directory.** `>` on the
live path would truncate the existing file before the agent produced a byte, so a re-run
against a broken agent would leave an empty ruleset file the include still names — a
firewall that has quietly become an open host. `/tmp` is refused for the same staging
because a rename is only atomic within one filesystem.

**The candidate include target is checked with `nft -c -f` before it becomes the live
file,** and the refusal happens *before* the diagnostic message is chosen: the candidate
is `rm -f`'d in the first line of the failing arm, and both message branches share one
`exit 1`. No string an operator's file contains can wave a broken file through — which
an earlier substring-classifying version could.

**The marker block is removed by a state machine, not `sed '/BEGIN/,/END/d'`.** A `sed`
range whose end marker is missing deletes from BEGIN to end of file; that is not
hypothetical, it destroyed an operator's own `table inet mine` during round 1 of this
plan's review. An unterminated block, a stray END and a nested BEGIN are all errors that
refuse rather than licences to delete the rest of the file. Markers are matched by
**prefix**, on both sides, so a host installed under last month's marker wording is still
found rather than growing a second block per release.

**What `nft --check` does NOT catch, said plainly.** `nft --check -f <file>` parses and
type-checks a ruleset and loads nothing. It is a SYNTAX and TYPE check: **a ruleset that
locks every operator out of the host passes it.** `policy drop` with no rule for the SSH
port, a rule for the wrong port, a whitelist entry for a range nobody arrives from — all
of them are well-formed nftables and all of them are accepted. Nothing automated can
close this, because "which packets must still be able to reach this machine" is not a
property of the file. What stands in its place is R1's two-table split (a ban table that
exempts `lo` and a ruleset table that does the same, so an accept in either ends that
chain), R2's unconditional SSH and panel accepts derived from two host facts the
installer detected rather than guessed, and the fact that both ports travel on every
mutation so a re-render can never drop them. It is a threat note item, not a test.

**Every mutation is serialised, and the race it closes was measured rather than
reasoned about.** `ops::firewall::firewall_lock` holds one process-wide mutex, and a
root daemon is one process per host. Two of this area's operations are check-then-act
against kernel state: `ensure_bans_table` applies the bans file only when the table is
absent, and **re-applying that file over a live table ERASES its elements** — verified on
nftables v1.0.9 — so two concurrent first-bans would both see an absent table and the
second apply would silently drop the first's ban, leaving the panel holding a row for a
ban the kernel is not enforcing. `allow_port` and `deny_port` are read-modify-write over
the whole ruleset file, where the later rename discards the earlier rule. There is no
tenant to scope a finer lock to: the unit of serialisation is the host's firewall.

**The service is started and then looked at.** `systemctl enable --now` reports success
for a unit whose `ExecStart` failed on a bad include, so the step asks the kernel
directly whether `table inet maran` is loaded — and asks a second time after firewalld
has been disabled, because a firewalld shutdown rewrites the ruleset.

**firewalld is disabled LAST.** This was round 3's second MAJOR (F2) and it is closed:
`step_firewall` now runs `seed_firewall_files`, `wire_firewall_includes`,
`start_firewall_service`, then `disable_firewalld`, then re-checks the kernel. Disabling
first stopped the working firewall of a RHEL host before a single ruleset byte existed,
while every line below could still abort — leaving the host wide open with the log's
last word a present-tense promise that Maran was now managing the firewall. The two
firewalls now overlap for three lines, which costs nothing: firewalld keeps its own
tables, ours are `inet maran` and `inet maran_bans`, and the kernel enforces the union.

**`disable_firewalld` gives three answers, not two** (round 3's F3, closed). The
`list-unit-files` query's status *and* its stderr are captured as evidence. "systemctl
answered and the answer was no" proceeds quietly; "the query broke" is loud, says what
broke, and attempts the disable anyway. Afterwards it **looks**: `is-enabled` and
`is-active` are read as words, not statuses, and a firewalld still enabled or still
active aborts the install with instructions. The earlier `2>/dev/null || true` form
reported a broken query as "No firewalld unit on this host" and swallowed a refused
disable entirely — an install that finished saying "Firewall active" while firewalld was
still in charge and would erase the panel's table at its next reload.

**The uninstaller asks the host, at the moment it acts, whether anything still includes
the rendered files** (round 3's F1 CRITICAL and F4 MAJOR, both closed).
`maran_firewall_includers` greps every plausible include target on the host for
`include "…/etc/maran/…"` and is called **twice**: once by
`remove_firewall_rendered_files` and once by `remove_maran_config_directory`, which is
now the only place `/etc/maran` is deleted. The defect it replaces is worth recording
because it is the exact denial-of-service this section is about: `remove_firewall`
computed a `local left_wired`, printed "Keeping /etc/maran/firewall*.nft" on the
strength of it, and `remove_config_and_state` ran `rm -rf /etc/maran` four functions
later knowing nothing about it — the script stated the invariant in prose and broke it
in the same run, **at exit status 0**. The fix is not a flag passed between them, which
would leave the next function free to make the same mistake; it is that both deletions
consult the predicate themselves, so there is nothing to keep in sync. The question is
also asked about **include lines rather than about our markers**, which is what closes
F4: an operator who followed the installer's own advice ("remove both markers and
everything between them") and left the two include lines behind was previously not seen
at all, and both files were deleted silently at rc 0.

When something does still include them, the two rendered files stay, **everything else
in `/etc/maran` goes** — `panel.env` above all, which holds the encryption key and must
never be left on a host the panel has been removed from — and the operator is shown the
exact lines to remove.

**The service is only disabled if this installer enabled it.** `record_firewall_service_enablement`
writes a marker before the enable, at the one moment the question can be answered; its
absence means nftables was enabled before Maran arrived, and disabling it on uninstall
would take away a firewall the operator already had.

### What is left open

**F5 — three installer assertions cannot fail for the reason they name.** In
`docker/polygon/assert-installer-steps.sh`, `assert_firewall_renders_through_the_agent`
greps the **raw** step file for `render-firewall-bans`, `render-firewall-ruleset` and
`--ssh-port`. All three are satisfied by `87-firewall.sh`'s own doc comments: deleting
both `render_firewall_file` invocations leaves the checks green. The same function
already knows the fix and applies it to its *negative* checks only, which build a
comment-stripped `$code`. It is graded MINOR because `assert_firewall_seeding_composes`
asserts the real argv behaviourally and would catch the deletion — but that makes these
three checks redundant *and* misleading, which is worse than either alone. **Still open;
verified by reading the current file.**

**F6 — nothing anywhere runs the uninstaller.** `grep -rl uninstall.sh` over the tree
returns `installer/uninstall.sh` itself and a comment in `88-cron.sh`. Neither polygon
image runs it, and there is no other harness. `remove_firewall` carries a **hand-copied
duplicate** of `87-firewall.sh`'s marker state machine and of its `nft_check` residue
logic, and the installer's copy is mutation-proven while the uninstaller's copy is
proven by nothing: applying the `sed '/BEGIN/,/END/d'` mutation to `uninstall.sh` passes
`maran structure`, `bash -n` and both image builds, and reintroduces the defect that
already destroyed an operator's own table once. `rules/testing.md`'s Definition of Done
is not met for the uninstaller's half of the marker handling. **Still open.**

**`disable_firewalld` is exercised by nothing.** Neither polygon image installs firewalld
(`grep -i firewalld` over both Dockerfiles: no hits), and `docker/polygon/systemctl-stand-in.sh`
has **no `list-unit-files` arm at all**, so its catch-all would send a polygon run down
the "no firewalld unit" branch regardless. The function's three-answer logic, its
post-disable check and its abort are argued in prose and asserted by nothing. This is
the largest untested privileged path in the step.

**Neither script has ever run on a host in the state its guards are for.** The refusals
above are reasoned and, for the installer's half, mutation-tested against the polygon.
The uninstaller's refusal paths — a damaged marker pair, an `nft`-rejected candidate, a
host that includes the files without our markers — have been reproduced by a reviewer by
hand and by no automated harness.

---

## 2. The cron read path (ruling R4)

### What this surface is

Every write and every removal under a customer's home in `ops::cron` runs inside
`fork_as_account`, the workspace's one privilege drop: `ProcessCronHost::write_command_file`
creates `~/.maran/cron` at `0700`, writes `<id>.cmd` at `0600` and sets both modes
explicitly rather than trusting a umask the agent does not control;
`remove_entry_files` removes the three files. There is no `chown` anywhere in the file
and no branch that creates a file as root "and then fixes it up".

The three **reads** are different, and this is the ruling that needs explaining.
`fork_as_account` returns `Result<(), PrivError>` — an exit status and nothing else —
and it closes every inherited descriptor above standard error before the child's work
begins. No pipe, socket or handoff file can be passed in, so a dropped child cannot hand
bytes back with the primitives `agent-core::privs` provides today. A read that must
**return** a customer's file contents therefore cannot be written inside one.

The area's own documentation is careful not to overstate this, and it is right to be: a
channel **is** constructible, because `close_range` closes descriptors and not memory
mappings — a `MAP_SHARED | MAP_ANONYMOUS` region made before the fork survives the sweep
and stays writable after `setuid`. What does not exist is the primitive, and adding one
would touch the single module in this workspace where `unsafe` is permitted. There is a
real argument that a shared mapping from an unprivileged child into the root parent's
address space is a worse surface than the read below.

So the agent opens `<id>.cmd`, `<id>.log` and `<id>.exit` **as root**, under
`/home/<account>/.maran/cron/`, a `0700` directory the account owns and can rewrite
between any two syscalls the agent makes.

### Every guard, the attack it defeats, and whether it is actually there

Each was checked against the code, not against the doc comment. **All of them are
present.**

**`O_NOFOLLOW` on every directory of the descent** — `open_cron_directory.rs`,
`DIRECTORY_FLAGS`. Defeats a symlink at `.maran` or at `cron`. `O_NOFOLLOW` refuses only
the **trailing** component of a path, so a single `open` of `/home/<a>/.maran/cron` would
follow a symlink planted at `.maran` — measured, not assumed. The walk is therefore one
component at a time, which is what makes the flag cover every level instead of the last.

**`O_DIRECTORY` on every level** — same constant. Defeats a plain file, a device or a
FIFO substituted for a directory level.

**`O_CLOEXEC` on every open** — both flag sets. Keeps the descriptor out of anything the
agent spawns.

**Each level reached from the level above with `openat`** —
`open_in_directory(&directory, component, …)`. Defeats a rename or `rmdir` of a level
**after** it was opened: a descriptor names an inode, and nothing renames an inode.

**`uid` verified on every directory of the descent** — `verify_directory`. Defeats a
level that is not the account's, and it is the only claim that survives the account
being able to rename things inside its own home.

**`O_NOFOLLOW` on the file itself** — `entry_files.rs`, `ENTRY_FILE_FLAGS`. Defeats a
symlink at `<id>.log` pointing at `/etc/shadow`.

**`O_NONBLOCK` on the file** — same constant. Defeats a FIFO left at the entry's name.
Opening one with no writer blocks **in the kernel forever**, and a FIFO is not a symlink,
so `O_NOFOLLOW` says nothing about it; `O_NONBLOCK` is what makes the open return.

**`metadata.is_file()`** — `read_entry_file`. Refuses to read the FIFO the flag above
merely made openable, and a device node, and a directory: three checks from one.

**`metadata.nlink() != 1` refuses** — `read_entry_file`. Defeats a **hardlink** to
somebody else's file. It is not a symlink and it really is inside the home, so every path
check ever written passes it; only the inode gives it away.

**`metadata.uid() != uid` refuses** — `read_entry_file`. Defeats a file inside a
directory that *is* the account's, linked to something they should not reach.

**The file is opened through the directory descriptor** —
`open_in_directory(&directory, name, ENTRY_FILE_FLAGS)`. Defeats a directory swapped
between the directory open and the file open.

**`name` must be a single component** — `directory_entry_name`, called inside
`open_in_directory`. `openat` resolves a relative path, so `../../etc/shadow` would walk
straight out of the pinned directory. Refuses empty, `/`, `.`, `..` and interior NUL.

**The id cannot be a path** — `CronEntryId::parse`. `Path::join` with an absolute string
**replaces** the path joined to, so an id of `/etc/cron.d/evil` would move the write out
of the home entirely and `../../..` would climb out. Exactly 36 characters of lowercase
hex with hyphens at 8, 13, 18 and 23 removes the alphabet those attacks are written in —
which is why the path helpers carry no traversal check: they cannot be reached by a value
that needs one.

**The byte budget is enforced DURING the read** — `read_tail`, via `file.take(ceiling)`,
with `saturated` computed from `buffer.len()`. The account owns the file and can grow it
between the `fstat` and the read, so a budget checked *before* the read is a budget an
attacker chooses the moment to exceed. The `fstat` length decides only where to seek; the
ceiling itself is `take`'s. A command file that saturated is **refused**, not truncated —
a shortened command shown in a listing is a lie, and one compared against a new entry
would report a duplicate that is not one.

**The account's uid is resolved at the moment of use** — `EntryFiles::read` calls
`AccountIds::resolve` per read. A cached uid for an account that was deleted and
recreated would authorise a read of whoever now holds that uid.

Fifteen tests in `agent/crates/ops/src/tests/cron/entry_files_tests.rs` pin these, and
none of them needs root: `read_entry_file` takes the **home** and the **uid** as
parameters precisely so a test can own a `TempDir` and run as its own uid, which is the
same relationship a customer has with their home. A symlink at an intermediate
component, a symlink at the cron directory, a symlink at the file, a hardlink, a FIFO
and a level owned by somebody else each have their own named test. The FIFO test is
worth knowing about: if `O_NONBLOCK` were dropped it would never return, so it fails as
a timeout rather than as an assertion — the honest shape for that particular bug.

### What is left open

**The file-ownership refusal has no test that reaches it.** `metadata.uid() != uid` is
present and correct, but every test that supplies a stranger uid is refused earlier, by
`verify_directory` at the home level, and creating a file owned by another uid inside a
directory the test owns needs root. So the guard that catches "a file linked to
somebody else's inode, inside a directory that really is the account's" is exercised
only through its `nlink` sibling. Not a hole — a defence whose next reader is free to
move it, which is precisely the failure mode this area's own doc comment warns about
one paragraph earlier.

**What comes back is the account's own report, and nothing above may treat it as
evidence.** `<id>.log` and `<id>.exit` live inside the account's home and the account
can write both, including the exit file's mtime. A customer who wants to can claim any
output and any status at any time. `get_cron_entry_output`'s doc says so; this note
repeats it because the panel renders it in a UI an operator reads. It is informational,
not authoritative, and no escalation follows from it.

**The crontab is read as root, and its size is now bounded.** `crontab -l` runs as root
by design — `crontab(1)` is the correct writer of the spool — and the account's table is
a customer-controlled input read into the root daemon and copied several times through
`parse` and `render`. It has an argued ceiling like the other three reads. The
"no crontab for" marker is matched **only in standard error**, because standard output
carries bytes the customer writes: an account with `# no crontab for alice` in its table
could otherwise make a failed `crontab -l` read as "no crontab", after which the next
install would write an empty document back and erase every entry, foreign lines
included.

---

## 3. The brute-force ban path (ruling R8)

### What this surface is

A ban is an element in `banned_v4`/`banned_v6`, sets in `table inet maran_bans` whose
input chain hooks at priority -5. The panel decides who goes in. Two callers exist:
`BanAddressCommandHandler`, reached from an `AdminOnly` endpoint, and
`BruteForceDetectedHandler`, which is meant to be reached from the Identity module's
login-failure path.

**The second caller has no producer.** Nothing in `backend/` publishes
`BruteForceDetected` outside the handler's own tests. The automatic ban is machinery
waiting for a detector, and every claim below about "an attacker aiming a ban" is about
what will be true when Task 13 lands, stated now because that is when it can still be
designed against.

### Everything rests on one value: the address Kestrel reports

`ForwardedHeadersExtensions` is what decides it. The committed configuration
(`HEAD`, `a4cc865`) is: `XForwardedFor | XForwardedProto`, `ForwardLimit = 1`,
`KnownProxies`/`KnownNetworks` cleared, then `IPAddress.Loopback` and
`IPAddress.IPv6Loopback` re-added.

**The re-add is load-bearing and was missing for part of this session.** ASP.NET Core's
`ForwardedHeadersMiddleware` computes `checkKnownIps` as "either list is non-empty"; with
**both** lists empty the trust check is not performed at all and the header is honoured
from **every** peer. `Clear()` without a re-add therefore does not mean "trust nobody",
it means "trust everybody" — the exact opposite of what the surrounding comment claims.
A working-tree read during this pass found the two `Add` lines absent, and later found
`IPAddress.Loopback` replaced by a literal `10.10.10.10`; both are consistent with the
concurrent agent mutation-testing `ForwardedClientAddressTests`, whose own doc comment
says "clearing the known-proxy list without re-filling it turns one of them red". The
committed code is correct. **The reason it is being recorded anyway** is that this is a
one-line, silent, direction-reversing defect in the value every per-address protection
in the panel rests on, and it is guarded by two tests
(`Maran.Host.Tests/Middleware/ForwardedHeadersTests.cs`,
`Maran.Host.IntegrationTests/ForwardedClientAddressTests.cs`) whose survival a reviewer
should confirm on the final diff rather than on this sentence.

### Aiming a ban at somebody else

**Over the internet, this is closed.** nginx terminates TLS on 8443 and sets
`X-Forwarded-For $proxy_add_x_forwarded_for`, which **appends** the peer it actually
saw. `ForwardLimit = 1` means the panel takes exactly one hop, so a client that stuffs
its own chain gets `<their claim>, <their real address>` and the panel reads the real
one. A caller whose peer address is not loopback has its header ignored entirely.
`ForwardedClientAddressTests` drives the real `Program` pipeline over HTTP and reads the
address back out of the **audit journal**, so it exercises the actual registration and
the actual placement (`app.UseForwardedHeaders()` first in the pipeline) rather than a
configuration the test re-stated.

**A source address the panel bans is a source address that really spoke to it.**
Everything on this path arrives over TCP, and HTTP authentication happens after the
three-way handshake has completed: a caller who forges a source address in the IP header
never receives the SYN-ACK, never sends the ACK, and so never gets to send a request at
all. So blind off-path spoofing cannot make the panel count a failed login against a
third party's address, and it cannot make the panel ban one. That is the reason the
forgery risk in this section is a LOCAL one about a header, and not a network one about
a packet — and it is why the whole of §3 turns on `X-Forwarded-For` rather than on
`RemoteIpAddress`.

**Locally, it is not, and this is the finding of this section.** Kestrel binds
`http://127.0.0.1:5080` and loopback is a trusted proxy, so **any process on the host
that can open a TCP connection to loopback can set `X-Forwarded-For` to any value it
likes.** On a shared hosting server that set includes the customers: Plan 5 is the plan
that gives them scheduled command execution as their own uid, and one `curl` in a cron
entry is enough. Once the detector lands, a customer could

- fail logins while claiming a competitor's address, or an arbitrary third party's, and
  have the panel ban it and journal it under that name;
- claim a fresh address per request and never accumulate failures against their own;
- forge the "from where" of every audit entry they generate.

Nothing here is a privilege escalation — the attacker already has an account on the box
— but it turns the panel into an instrument for banning people who have done nothing,
and it makes the journal's address column untrustworthy for any request that could have
originated locally.

Two things bound it, and neither is a fix. Aiming a ban at **`127.0.0.1` specifically**
does not lock the panel out: both tables exempt loopback before consulting the ban sets
(`iif "lo" accept` at priority -5 in `maran_bans`, and again at priority 0 in `maran`),
and an accept verdict in one chain ends only that chain, so the packet is accepted twice
over. That is a deliberate, tested property of the templates (`bans_table.nft`,
`ruleset.nft` goldens) and it is what keeps the nginx→Kestrel hop alive whatever the ban
set holds. And a ban aimed at a *real* address still has to get past the whitelist. But
neither stops the general case, and the honest statement is that **the panel's trust
boundary is "loopback", while the machine's actual boundary is "not a customer's
process"**. The obvious closures — a shared secret header nginx sets, or a unix socket
between nginx and Kestrel — are a spec change and are named here, not proposed here.

### Locking everybody out

**The whitelist is the only reason the panel cannot ban its own operator.**
`BruteForceDetectedHandler` checks it before anything else happens, and a skipped ban is
journalled as `BanSkippedWhitelisted` — its own action rather than a failure, because
nothing went wrong and the absence of an expected ban is exactly what the entry
explains. An administrator mistyping a password from the office is, at the detector,
indistinguishable from an attack; the difference is a row somebody put there
beforehand.

**If the installer's seeding does not run, the whitelist starts empty.**
`detect_seed_whitelist_cidr` reads `SSH_CLIENT`'s first field and writes
`Firewall__SeedWhitelistCidr=<ip>/32` (`/128` for v6) into `panel.env`; `WhitelistSeeder`
imports it as the first row, **once**, and only into an empty whitelist — so a row an
administrator deliberately removed does not come back, which is the promise `panel.env`
makes in as many words. Two ordinary situations produce no seed: a console or otherwise
local install, which genuinely has no client address, and **`sudo`, whose `env_reset`
default drops `SSH_CLIENT` on the way to root even though the operator did arrive over
SSH**. The installer mitigates the second by walking up the process tree for an ancestor
whose `/proc/<pid>/environ` still carries it, treating the record as raw bytes so a
crafted `FOO=x\nSSH_CLIENT=…` entry cannot be read back as a genuine one.

When there is still no seed, the consequence is stated twice — in `panel.env` itself and
as a WARNING in the closing installer output — and it is this: **once the detector
lands, the sole administrator of a day-one server is one typo away from banning
themselves off the machine, with no remote way back in.** A malformed seed is logged and
skipped rather than stored, because a row that matches no packet tells its reader they
are exempt while they are not.

**The manual ban endpoint does not consult the whitelist.**
`BanAddressCommandHandler` normalises, calls the agent, then writes the row; there is no
whitelist check on that path. That is defensible — the endpoint is `AdminOnly` and an
administrator banning an address has said what they mean — but it means the whitelist
protects against the *automatic* path only, and an administrator can still ban their own
office by hand. Worth an explicit decision rather than an implicit one.

### Evading a ban

**A ban is one exact address.** `BanAddress` refuses a CIDR (that is `SourceCidr`, a
separate type, so a `/0` cannot reach the ban path and ban the internet on one bad
request), refuses a non-canonical spelling by writing the parsed address back out and
comparing, and refuses the v4-mapped form `::ffff:a.b.c.d` outright — because the
firewall keeps one set per family and a mapped address in the IPv6 set matches no packet
an IPv4 client ever sends. `IpAddressNormalizer` on the panel side is what makes that
refusal survivable: it maps the address Kestrel reports back to plain IPv4 before
anything downstream sees it. Without it every ban on a dual-stack host is rejected by
the agent and the feature is inert **while every panel-side unit test stays green**,
because nothing on that side can tell the two spellings apart. It is also what keeps the
escalation ladder able to count: two spellings are two rows, and the second offence
would read as a first.

An attacker with a supply of addresses still walks around all of it. The ladder
(15m → 1h → 24h by prior episodes in 24h, `BanTtlPolicy`) escalates per address, so
rotating resets it to the first rung every time. That is the same accepted trade the
auth note records for the account lockout, and it is why the account-level counter in
`User` exists beside the address-level one.

**A ban does not survive a restart, and re-applying it is a background service that can
give up.** Both families' nftables units flush the ruleset on stop and reload, and the
agent keeps no ban state; `StartupBanReconciler` re-applies the **remaining** time of
every episode still in force (re-applying the full duration would assemble a permanent
ban out of temporary ones on a machine that restarts often, with nothing in the journal
saying so). It retries a failing pass five times, thirty seconds apart, and then
**stops** — logging that every banned address is reaching the host until the panel is
restarted or the bans are re-applied by hand. An agent that is down for more than about
two and a half minutes after a reboot therefore leaves the host unbanned indefinitely,
by design, because a service that retried forever would hide a broken host behind an
error line every thirty seconds. Whether "stop and shout" or "keep trying" is right here
is a decision worth a second opinion.

### Smaller things that hold

- Every Firewall controller is `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]`
  at the class level: bans, rules and the whitelist alike.
- `BanAddressCommandValidator` deliberately does **not** check the address's form. It is
  parsed once, by `IpAddressNormalizer`, which is the thing that has to succeed for the
  ban to mean anything; a second format rule here would keep passing every address after
  a mutation removed the normalisation, so nothing would go red. It does check the
  duration, because the handler cannot: zero minutes is a well-formed request for
  something the contract spells "permanent".
- `CidrRange` refuses a scope id **before** parsing, because `IPNetwork.TryParse` accepts
  `fe80::1%eth0/128` and **silently drops** the scope — a row that would be stored and
  shown as one range and matched as another.
- The agent stores no ban reason. An earlier design carried the operator's reason down
  and put it in an `nft` comment, which was an injection primitive, because `nft` parses
  its arguments in its own grammar. The reason is panel metadata and there is no
  parameter for it.
- `ban_address` issues **one** `nft add element` and deliberately no delete first. `add`
  on an address already in the set replaces the element and refreshes its timeout, which
  is what an escalating policy needs; the delete an earlier version performed opened a
  window during which the address was **not banned**, and the module lock serialises the
  agent's callers but does not stop packets.

---

## 4. Cron environment handling (ruling R13)

`EnvVarName::parse` accepts `^[A-Z_][A-Z0-9_]{0,63}$` and then refuses two names outright,
from a `RESERVED_NAMES` list rather than two comparisons so a third name is one edit.

**`MAILTO` is refused because it is an outbound relay.** cron mails an entry's output to
whatever `MAILTO` names, through the host's mail transfer agent. A customer who could set
it would have a send primitive they aim by writing to standard output — arbitrary
content, arbitrary recipient, from the server's own IP and reputation. The agent already
captures output to `<id>.log`, so a second copy through the MTA buys the customer
nothing they do not already have and buys the operator a spam surface and a blocklisting.

**`SHELL` is refused because it changes the interpreter under every entry.** cron runs
each line under `$SHELL`, so one assignment re-points **every** managed entry — including
entries created before the assignment, and including entries the customer may not have
been thinking about. The interpreter every line runs under must be the agent's decision.

**Refusing the names is not enough on its own, so the agent writes both itself.**
`CrontabDocument::render` emits `MAILTO=""` and `SHELL=<the platform's sh>` immediately
below its banner and **below every foreign line**, on purpose: whatever a hand-edited
preamble or a package-seeded crontab set, ours re-sets it for the region beneath. The
interpreter cron is told about and the interpreter each line names come from the one
`sh_binary` parameter, so they can never be two different programs. `get_cron_environment`
never reports either name back, on the same principle from the other side: they are not
the account's to see or to change.

**The foreign region is preserved byte for byte and in position.** A cron environment
assignment applies to the lines **below** it, so moving a foreign `PATH=` would change
which foreign entries it governs; the parser is infallible by construction, because a
crontab is not this agent's file and a parser that could refuse would turn one
hand-edited line into an account whose entries the panel can no longer list, enable or
delete.

**The value half carries the config-file-injection refusals** (`rules/security.md`
item 4). `EnvVarValue` refuses control characters — a newline turns one assignment into
several, which is how a customer would reach the very `MAILTO` the name denylist just
refused — and refuses `%`, because cron rewrites the first unescaped `%` on a line into a
newline. That rewrite is why the customer's command was moved out of the crontab into a
file of its own; the value could not be moved, an assignment *being* the line, so it pays
for staying there with one more refused character. Surrounding whitespace and wrapping
quotes are refused too, because cron strips both: the panel would store and display a
value the host does not apply. The length ceiling is **derived**, not chosen — cron reads
an environment line into a 1000-byte buffer and silently keeps what fits, so the value's
ceiling is that buffer minus the maximum name and the `=`.

Nothing on the installed cron line is customer-supplied at all: the schedule renders from
five validated fields, the id is a uuid the agent minted, the three paths are built by
`AgentPaths`, and the command lives in the `.cmd` file the line names.

---

## 5. The password-reset token flow and SMTP (R11, R12)

**Written now that the code exists.** An earlier revision of this section said the
analysis could not be done because `backend/src/Maran.Modules/Monitoring/` held no `.cs`
files. That is no longer true: `SendMailRequested`, `SendMailRequestedHandler`,
`SmtpMailer`, `SmtpSettings` and `SmtpSettingsCache` all exist, and so do Identity's
`PasswordResetToken`, `RequestPasswordResetCommandHandler`, `ResetPasswordCommandHandler`
and `SecurityPolicy`. The two most security-sensitive additions in this plan are no longer
behind a document claiming the analysis was impossible.

### What an attacker can reach

Two anonymous, unauthenticated endpoints: `POST /api/v1/auth/forgot-password` takes an
address and causes the panel to send mail; `POST /api/v1/auth/reset-password` takes a
token and sets a password without knowing the old one. Between them they are the one path
by which somebody who knows no credential can end up holding an account.

### The token

Thirty-two bytes from the system CSPRNG, base64url-encoded. Stored as a **base64 SHA-256
digest** and never in plaintext, so a database dump, a replica or a backup taken during
the token's hour yields digests — recognising a token you already hold, and nothing more.
Argon2id would be wrong here and is not used: the secret has no guessable structure to
slow an attacker against, and the cost would land on an endpoint anyone may call.

Valid for **one hour**, and **single use**: `PasswordResetToken.IsUsable` checks both in
one method so no caller can check one and forget the other. Spending stamps `UsedAt`
rather than deleting the row, which is what lets a replay be refused *and* journalled
rather than met with "no such token". Requesting a reset retires the account's other
outstanding tokens, and completing one retires the rest, so two mails never leave two live
keys to an account whose owner has just said they lost control of it.

A completed reset **revokes every session** (`SessionRevocationReason.PasswordChanged`)
and clears any lockout. Without the revocation the reset would restore the owner's access
without removing an intruder's — a stolen refresh cookie outlives a password change unless
something ends it.

### The at-rest property, and why it is now proven rather than assumed

R11 requires the token-bearing envelope never to be persisted. The panel calls
`PersistMessagesWithPostgresql`, so "this particular message is not persisted" was a claim
about a Wolverine default — the kind of thing a library upgrade changes silently. It is
now asserted: `PasswordResetEndpointTests.The_token_bearing_envelope_is_never_written_to_
the_message_store` requests a reset, waits until the mailer has actually been handed the
message, then searches **every** table in the `wolverine` schema for the token — both in the
row's text rendering and **byte-wise in every `bytea` column** — and requires zero rows.

The byte-wise half is not a detail. Until a second review caught it, the search was
`to_jsonb(row)::text like '%token%'` alone, and every envelope column that could hold a
token (`body`, in the incoming, outgoing, dead-letter and control-queue tables) is `bytea`,
which `to_jsonb` renders as hex: the test could not have failed for the reason it existed.
It was written up here as proof while proving nothing.

It now carries **two guards on two different axes**. `Assert.NotEmpty(tables)` proves the
search looked somewhere. A **positive control** proves it could find something: one envelope
is deliberately handed to the durable store carrying a needle of its own, and the same probe
is required to find it. (The control writes through Wolverine's own message-store API rather
than publishing, because a publish cannot be made durable from outside the host's messaging
configuration — a scheduled publish was tried first and is not persisted, which is the
property this test defends.) Removing the byte-wise branch turns the control red, which was
verified by doing it.

`SendMailRequestedHandler` catches every exception, including cancellation, precisely so a
throw cannot hand the same envelope to the dead-letter store — which is the at-rest
persistence this arrangement exists to prevent.

### The enumeration channel

The response is identical for an address that belongs to an account and one that does not:
one `return` statement, HTTP 200, the same body. Both are asserted over real HTTP
(`A_known_and_an_unknown_address_get_the_same_status_and_the_same_body`, comparing bodies
with only the per-request correlation id normalised away).

The send is **published to a local, non-durable queue and never awaited**. This is the
constraint an earlier draft got wrong in a way that was worse than the problem: an inline
send makes a known address cost a full SMTP round trip — seconds when slow, a timeout when
broken — while an unknown one returns at once, which is an oracle readable with a
stopwatch from anywhere. `Both_a_known_and_an_unknown_address_answer_in_milliseconds_
against_a_five_second_mailer` pins it: the mailer takes five seconds and both requests
answer inside one, and the mailer is separately observed to have been entered *afterwards*
so that a message routed nowhere cannot pass the timing assertion while meaning the
opposite.

**What remains distinguishable, stated rather than filed as clean.** Fine-grained timing
is *shaped*, not equalised. The token is generated and hashed before the lookup's outcome
is branched on, so both paths pay for the CSPRNG draw and the digest; what is not
symmetric is the database work. Measured on the test host, post-warm-up: a **known**
address answers in roughly **80–135 ms**, an **unknown** one in roughly **6–16 ms**. That
gap is real and is large enough to read over a network. Its cause is the extra work only
the known path does — retiring outstanding tokens, inserting the new one, its own
`SaveChanges` round trip, and the publish. It is not closed by this design and should not
be described as closed.

What bounds it is the rate limiter, not the timing: `password-reset` is its own bucket,
**three requests per fifteen minutes keyed by the caller's normalised address**, so a
sweep costs one address per five minutes per source IP. That is the control an operator is
relying on, and it is the one to check first if this endpoint is ever reported as an
enumeration vector. The same limiter is what stops the endpoint being a mail bomb with the
operator's own return address on it.

**What that limiter does not bound, stated so nobody reads it as more than it is.** The
partition key is the *caller's* address (`ClientAddress.Of(context.Connection.RemoteIpAddress)`),
not the address being asked about, so a sweep of *N* accounts costs *N/3* source addresses
rather than *N/3* windows — a commodity proxy pool or a single IPv6 /64 is not bounded by it
at all. And §3 of this same note records that the client address is **forgeable from inside
the machine**: loopback is a trusted proxy and hosting customers have local process
execution, so the population this panel hosts is exactly the population that can defeat the
key. The limiter bounds an unsophisticated attacker from outside; it is not a bound on a
distributed or on-machine one, and the timing residual above should be read against that.

The refusal side carries no oracle: a token that never existed, one that has expired, one
already spent, and one whose user has since been deleted all return the same
`PasswordResetTokenInvalid`, asserted over HTTP with bodies compared
(`A_spent_token_an_expired_one_and_a_token_nobody_issued_are_refused_identically`).

### The link, and host-header injection

The reset link is built from **configuration** (`PasswordReset:PanelUrl`), never from the
request's `Host` header. Building it from the header is how a correct token implementation
becomes an account takeover: an attacker requests a reset for somebody else's account with
`Host: evil.example`, and the panel composes a mail, in its own name, carrying a live token
pointed at the attacker's server. With no `PanelUrl` configured the mail carries the token
and the instruction to paste it — worse to use and completely safe. The recipient address
is read off the **user row**, never echoed from the request, so a future normalising lookup
cannot turn this into a way to have the panel mail a token to an address of the caller's
choosing.

### The message body

`SendMailRequested.Body` can carry a live token, so nothing that handles it logs it,
journals it, or attaches it to an outward error. The handler's log line names the recipient
and nothing else; the audit entry names the recipient and nothing else. `AuditEntry` has no
free-form payload field, so there is nowhere for a token to travel into a journal that is
never deleted.

### R12 — the security policy

The policy is a one-row singleton in **Identity** (`SecurityPolicy`, fixed primary key,
`ValueGeneratedNever`), cached in `SecurityPolicyCache` and invalidated by the save handler
**after** the commit. Two earlier revisions of this note claimed that race closed before it
was, and both were wrong in the same direction, so the mechanism is stated here exactly.

The failure being prevented: a load that began before the save returns after it, and if it
publishes what it read, the pre-save row — since this cache has **no expiry by design** —
stays the panel's policy **until the process restarts**. Forced two-factor switched on by an
administrator silently never takes effect, while the security screen (which reads the row,
not the cache) shows it as on. A security control failing open, silently, permanently.

Ordering the invalidation after the commit does not prevent it; that was the first claim.
Neither does a **generation counter** compared just before the publish; that was the second.
The compare and the publish are two operations, and an invalidation landing between them
republishes the stale row — the same permanent failure through a two-instruction window
instead of a database round trip. A narrower window is not a closed one.

What closes it is **mutual exclusion**: the load holds `SemaphoreSlim _gate` from before it
queries until after it publishes, and `Invalidate()` takes that same gate. There is no
interleaving in which an invalidation is observed by a load that is between reading a row and
publishing it, so there is no window left to narrow. The generation counter is gone rather
than kept as belt-and-braces — a check that cannot fire is decoration that the next reader
reasons about as protection. The price is that `Invalidate()`, called on the save request's
thread, blocks for at most one policy query; policy saves happen a handful of times in a
server's life and the caller waiting is the administrator who asked for the change. The gate
is not reentrant, so nothing on the load path may invalidate; nothing does.

**No expiry, reconsidered rather than inherited.** An explicit-only invalidation turns any
missed invalidation into a permanent one, which is an argument for a short lifetime as a
backstop. It is declined here for two reasons: a time-based cache makes a saved policy take
effect at an unpredictable moment, and "forced 2FA is on, but not for everyone yet" is not a
state an operator can reason about; and the panel is **one process per server**, so an
invalidation that reaches this object has reached every reader there is — there is no second
process for an expiry to catch up. The residual is stated plainly: if the API is ever run as
more than one process, this cache is wrong in a way an expiry would bound but not fix, and it
must be revisited then rather than given a lifetime.

The snapshot field is read on a lock-free fast path and so is accessed through `Volatile`,
because plain double-checked locking on a non-volatile reference is unsound on arm64, which
is in this product's OS matrix.
`SecurityPolicyCacheTests.A_save_that_commits_while_a_load_is_in_flight_waits_for_that_load_and_then_wins`
pins it, and pins it at the instant that matters: the invalidation is fired from inside the
load — after the row is read, before it is published — on its own thread, and the test
requires it **not** to complete there. An invalidation that skips the gate completes in that
window, so both broken shapes (the bare assignment and the generation counter) turn the test
red by name.

Reads are administrator-only. The validator bounds every field so that a single careless
setting cannot switch a protection off: minimum password length is clamped to 8–128, the
lockout threshold to 3–100 attempts,
and the lockout itself to 1–1440 minutes — the last two because *anyone* can trigger a
lockout by naming a username, so an unbounded lockout would be a permanent denial of
service against a named administrator for the cost of ten wrong passwords.

SMTP settings stay in **Monitoring** and are not duplicated here. The SMTP password is
encrypted at rest and no query returns it.

### Forced two-factor steering

An administrator whose panel forces a second factor and who holds none is issued a token
carrying `tfa_setup`, and `TwoFactorEnrolmentCompleteHandler` refuses every endpoint not
marked `[AllowDuringTwoFactorEnrolment]`. The requirement is attached to *both* panel
policies and to `DefaultPolicy` as well as `FallbackPolicy`, because a bare `[Authorize]`
evaluates the default policy — and several auth endpoints carry exactly that. The
restriction is global and the exemption is per endpoint, so an endpoint added tomorrow is
refused by default and the failure mode of forgetting the marker is somebody locked out of
a screen, which is loud, rather than a screen quietly left open.

It answers **403** — the one deliberate exception to this plan's 404-not-403 rule — because
the caller is authenticated and being steered, not probed; a 404 would tell a legitimate
administrator that the panel they just signed into has no screens. It is verified by
walking the host's real route table and requiring every governed endpoint to refuse, not by
naming three examples, and the walk asserts it found something so a filter that matched
nothing cannot pass.

The decision is read off the token rather than the database, so the cost is bounded and
stated: a policy change takes effect for an existing session when its access token is next
re-issued, at most one access-token lifetime (fifteen minutes) away, because refresh
re-evaluates the policy for the same user. Neither direction leaves a caller with more
access than the policy in force before the change allowed.

### Defects found and fixed while writing this

`SharedKernel/Utilities/Mail/EmailAddressRule.cs` compared `candidate` with `candidate` —
an always-true expression that disabled the rule's round-trip check and let a display-name
form (`Ops Team <ops@example.com>`) through every field in the panel that takes an address.
Fixed to compare the parser's `Address` with the input. Two already-written tests named it
and were red before the fix.

---

## Residual risk, in one place

### Update — 2026-09-04 (Definition-of-Done pass, Task 17)

Two findings from the closing pass that this note could not have carried when it was
written, and two corrections to bullets below that the tree has since overtaken.

**New — the scope-id composition defect: a peer that was counted but could not be
banned.** `ClientAddress.Of` preserved an IPv6 **scope id** (`fe80::1%3`), while the
agent's `BanAddress` holds a Rust `IpAddr`, in which a scoped address fails to parse
outright. The panel therefore counted failed logins against a subject it had no way to
express as a ban: an address could accumulate offences forever and the ban that followed
them could never be applied. The composition was measured, not argued — an ASP.NET Core
probe reproducing `AddPanelForwardedHeaders` exactly showed that `IPEndPoint.TryParse`
**accepts and preserves a numeric scope and silently drops a named one**, and that a
correctly-installed host cannot have one injected, because `installer/nginx/maran.conf`
uses `$proxy_add_x_forwarded_for` (which appends nginx's own `$remote_addr` on the right)
and `ForwardLimit = 1` consumes from the right. Fixed by stripping the scope at **both**
boundaries — `SharedKernel/Utilities/Network/ScopelessAddress.cs`, applied in
`ClientAddress.Of` and in `IpAddressNormalizer` — because `ClientAddress.Of` turned out
to be the partition key for all three rate limiters and the audit journal, not only for
the detector; fixing the Firewall module alone would have left two counters each needing
the full threshold, **doubling an attacker's budget**. The composition test was verified
RED before the change with the defect's exact signature (`Assert.Single() Failure: The
collection was empty`) and names none of the three components deliberately.

*The residual is forensic, and it is a real cost.* Two different link-local machines —
`fe80::1%2` and `fe80::1%3` — now derive to the same `fe80::1`. They share a rate-limit
counter, share an escalation ladder rung, and are journalled under one name, so the audit
trail can no longer tell them apart. That was accepted deliberately: counting finer than
the response can name yields one of two defects, and the panel had the worse one. A
second consequence worth an owner decision rather than a fix here: `::1%3` now derives to
`::1`, so a ban aimed at the reverse proxy is *expressible* (it was already expressible as
plain `::1`), which raises whether loopback should be intrinsically whitelisted rather
than only by a seeded row.

**New — `ListenSocketGuard` warns and keeps serving on a pure-TCP panel.** The guard
narrows the panel's unix socket to its owner and the web server's group and **stops the
panel** for every failure it can meet on that path. The one thing it will not stop for is
a panel that is listening on TCP at all: that is development's normal state, and — until
an operator re-runs the installer — an existing server's, so it is logged at warning level
and the panel serves. The consequence is the one §3 describes: on a TCP panel, loopback is
a trusted proxy, any local process connects from `127.0.0.1` and chooses the address the
panel records, rate-limits and bans on. Accepted, because refusing to boot would strand
every already-installed panel on an upgrade, and because the operator-facing warning names
the exact remedy. But it means the mitigation for §3 is **an installer step plus a log
line an operator has to read**, not an invariant the process enforces — and the scope-id
analysis above rests on nginx's rendering of `X-Forwarded-For`, which the panel neither
controls nor tests.

**Correction — the brute-force detector IS built.** The first bullet below ("the ban
feature's automatic half is dormant") was true when this note was written and is not true
now: `Identity/Services/BruteForceDetector.cs` publishes `BruteForceDetected`,
`Firewall/IntegrationEvents/Handlers/BruteForceDetectedHandler.cs` consumes it behind the
whitelist check, and `StartupBanReconciler` re-applies the remainder after a restart.

**Correction — the loopback trust boundary has its own note.** The second bullet's "this
is the item to fix before the detector ships" was overtaken by
`2026-09-03-panel-socket-threat-note.md` and `2026-09-03-loopback-trust-boundary.md`: the
shipped panel listens on a unix socket in a `2710` directory, with `ListenSocketGuard` as
the second lock. The residual is the warn-only TCP path recorded immediately above.


- The brute-force detector is not built, so the ban feature's automatic half is dormant
  and untested end to end against a real login flow.
- Loopback is a trusted proxy and hosting customers have local process execution, so the
  client address the panel records — and will ban and journal by — is forgeable from
  inside the machine. This is the item to fix before the detector ships.
- The whitelist protects the automatic ban path only; a manual ban is unfiltered.
- A day-one install that saw no `SSH_CLIENT` has an empty whitelist, and the only thing
  standing between its administrator and a self-inflicted lockout is their reading two
  warnings.
- `StartupBanReconciler` gives up after five attempts; a slow agent after a reboot means
  every ban is silently absent until somebody restarts the panel.
- The installer's `disable_firewalld` and the whole of `uninstall.sh` are exercised by no
  automated harness (F6, and the coverage note above); three positive polygon assertions
  are satisfied by doc comments (F5).
- The cron file-ownership refusal is present but unexercised by any test.
- `.cmd`, `.log`, `.exit` and their mtimes are account-writable, so the cron UI reports
  what the account's own runs left behind. Informational, never authoritative.
- The crontab parser has unit tests over hand-written cases but **has not been fuzzed**;
  the plan's Definition of Done listed fuzzing as an open item and it was never done. The
  parser reads a file the account can write, so it is the one component here whose input is
  fully attacker-shaped.
- **The additive law of `rules/proto.md` was, until this pass, checked by nothing**:
  `scripts/lib/proto-lint.sh` compiled the contract and discarded the descriptor set, so a
  renumbered or removed field — the change that silently breaks a released client, because
  the panel and the agent upgrade independently — passed CI. Now closed: the lint compares
  the compiled contract against `proto/agent/v1/contract-baseline.txt` and refuses breaking
  deltas by name. Residual: the baseline is a committed file, so a reviewer still has to
  notice a commit that rewrites it with `maran proto --accept`; the check itself never
  writes it, and the file is text so such a rewrite reads as deleted inventory lines.
- **A firewall that refuses every write after the host's SSH port moves, and an operator
  who is not told why.** This is operational risk rather than adversarial risk — nobody
  attacks anybody with it — and the note has no section for that kind of finding, so it
  is recorded here rather than under §1's "What is left open", which is scoped to the
  installer's own steps. Every rule mutation begins by reading
  `/etc/maran/firewall-ruleset.nft` and parsing it against the ports the request carries
  (`read_ruleset` in `list_rules.rs`, called first by `allow_port` and by `deny_port`).
  The ports on the wire come from `panel.env` — `Firewall__SshPorts` and
  `Firewall__PanelPort`, bound into `FirewallOptions` and sent on every call — while the
  file was rendered once, by the installer, from those same values at install time. The
  moment the two halves stop agreeing, `managed_rules` returns
  `FirewallError::PortsDisagree` and **`allow_port`, `deny_port` and `list_rules` all
  refuse**; both writers read before they write, so nothing is written and no rpc
  re-seeds or removes the file. Rule management is wedged until an operator intervenes on
  the host. **The ban path is unaffected** and that is worth stating: `ban_address`,
  `unban_address` and `list_bans` never read this file — they work on `table inet
  maran_bans` — so brute-force banning keeps running throughout.
- **What the operator actually sees is the sharp end of it.** The agent's own message
  carries the whole recovery — re-render with
  `maran-agent render-firewall-ruleset --ssh-port <port> --panel-port <port>` (one
  `--ssh-port` per port sshd listens on) and write the result to the agent's ruleset path,
  which is exactly what `seed_firewall_files` does at install — but that sentence never
  leaves the server. `AgentErrorTranslator.ToError` **logs** the agent's text and returns
  `Error.Of(code)` alone, and `ValidationFailed` renders as `AgentValidationFailed`: *"Your
  server rejected these details as invalid. Please correct them and try again."* An
  administrator with a wedged firewall is therefore shown a validation error about details
  they did not supply, and the only sentence that explains the state is in the panel's log.
  Recording the recovery here does not close that: **there is no in-panel way out**, and
  the way out on the host is manual — bring `Firewall__SshPorts` in `panel.env` back into
  agreement with the host's sshd, re-render the ruleset with the agent as above, and
  restart the panel so the new options bind. Nothing about the wedge is corrupting: the
  refusal comes from the read, so the live ruleset and the kernel are untouched.
- The password-reset endpoint's fine-grained timing is shaped, not equalised: a known
  address answers in roughly 80–135 ms against roughly 6–16 ms for an unknown one, a gap
  readable over a network. What bounds enumeration is the `password-reset` rate limiter
  (three requests per fifteen minutes per source address), not the timing — **and that
  limiter is keyed on the caller's address, so it bounds a distributed sweep not at all,
  and the address it keys on is the forgeable-from-inside-the-machine one two bullets
  above.** Accepted for v1 as a named follow-up rather than closed: a fixed sleep would be
  a self-inflicted denial of service on an anonymous endpoint, and moving the known-address
  branch onto the background queue is a redesign. Section 5.
- **`forgot-password` matches the address case-sensitively.** The column is plain
  `varchar(254)` with no `citext` and nothing lowercases on write, so an administrator
  stored as `Admin@Example.com` who types `admin@example.com` gets the same 200 as a
  success and no mail — on the one screen that exists for people already locked out. Left
  open deliberately: normalising is a decision about the unique index and the setup flow as
  well as this lookup, and a case-insensitive comparison here alone would disagree with the
  index that guarantees the lookup finds at most one row.
- `PasswordReset:PanelUrl` has no entry in the installer's generated env yet, so a
  production panel that has not set it sends reset mail carrying a token to paste rather
  than a link. Safe, and worse to use.
- Identity's brute-force threshold and window are still `BruteForceOptions` from
  configuration rather than fields on the `SecurityPolicy` row, so the security-policy
  screen does not show them. One decision, one source, but two places an operator has to
  look.
