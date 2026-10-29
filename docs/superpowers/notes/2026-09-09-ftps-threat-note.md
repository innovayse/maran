# Threat note — FTPS (vsftpd) on the panel host: a new port, a new PAM stack, a new privileged installer step

Date: 2026-09-09
Surface: three at once, each of which `rules/security.md` escalates on its own —

1. **A new listening port** (`rules/security.md` §10): TCP 21 for the FTPS control channel and
   TCP 30000–30099 for the passive data range.
2. **A new PAM stack**: `/etc/pam.d/maran-ftps`, a file that decides who may authenticate to a
   daemon running as root on the panel host.
3. **A new privileged installer step**: `installer/lib/89-ftps.sh`, which installs a package,
   creates a system group, writes a PAM service, creates the chroot base and installs a
   systemd unit — all as root, on a machine reachable from the internet.

Scope of this note: **Task 2** of `docs/superpowers/plans/2026-09-08-maran-ftps.md` — the installer
step, the PAM service and the unit. It is written **before** any of those three files existed, which
is the shape `rules/security.md` requires: a note written afterwards is a justification validating a
choice already made, and validates nothing.

## Second reviewer: OUTSTANDING

`rules/security.md` ("Sensitive change escalation") requires a second human reviewer plus an explicit
threat note for a change to the installer's privileged steps, and §10 requires that a new listening
port not arrive by surprise. **No second reviewer has read this change. The requirement is
OUTSTANDING.** The author is an agent session: it cannot dispatch a reviewer, and it cannot be one —
the rule exists because the author of a privileged change is the person least able to see what they
assumed.

Therefore, in the exact shape `rules/security.md` permits and names as a debt:

- This note is written **first**, before a privileged line, and lands as a file rather than as prose
  in a report only one session reads.
- The change **may live on `fix/live-findings`. It MUST NOT merge to `main`** until a human has read
  this note. `main` is protected and the second reviewer is a merge gate (`rules/git.md`).
- The owner is told at hand-off that this surface carries an outstanding review.

A note recording the debt is the minimum, never the discharge of it.

### What a reviewer must check

1. **The PAM stack refuses a non-member.** `/etc/pam.d/maran-ftps` is the entire authorization
   boundary for this daemon. Confirm that `auth required pam_succeed_if.so user ingroup maran-ftps`
   is the **first** `auth` line and that the same test is repeated in the `account` phase, so that a
   login which is not in `maran-ftps` — `root`, `panel`, every hosting account, every other system
   account, whether or not it holds a password — cannot authenticate here. Confirm the file names
   `pam_unix.so` directly and never `common-auth` / `password-auth`: those aggregates pull in the
   host's whole authentication policy (`pam_faillock`, `pam_sss`, a directory service an operator
   configured) and would make this daemon's answer depend on configuration nobody wrote for it.
   Confirm the stock `/etc/pam.d/vsftpd` is **not** edited by the step.
2. **The mask-before-install ordering.** On the Debian family the `vsftpd` package's own postinst
   enables and starts `vsftpd.service` with the distribution's configuration — port 21 open,
   `local_enable=YES`, no TLS required. Confirm `mask_packaged_vsftpd` is called **before**
   `install_vsftpd_package` in `step_ftps`, and that a reordering is caught by a check rather than
   by a reader. The window an install-then-disable would open is short but real, and it is on a
   machine that is by definition internet-reachable, because the operator just reached it.
3. **The jail's ancestor chain.** `/var/lib/maran-ftps` is `root:root 0711` (it was `0700` when
   this note was first written; see the amendment "The jail base is 0711, not 0700" below, which a
   reviewer should read together with this item), a **sibling** of
   `/var/lib/maran` and never a child of it. `/var/lib/maran` is `panel:panel 0750`, and an
   unprivileged uid that owns an ancestor of a chroot can rename a level aside and leave an entry of
   its own at the name every customer's jail hangs under. This is not hypothetical here: it is the
   same defect that made **every SFTP login on every real install fail**, and the reason the SFTP
   jail base moved to `/var/lib/maran-sftp`. Confirm no component of the chroot path is owned by, or
   writable by, any uid but root. Confirm also the step's refusal path: a symlink standing at
   `/var/lib/maran-ftps` must be **replaced**, not followed.
4. **The passive range's exposure.** 30000–30099 is 100 ports that will be reachable once an
   operator enables FTPS and the firewall step opens them. Confirm the range is not opened by this
   installer step (it is not — this step opens nothing), that enabling is an explicit administrator
   action, and that the range's size is bounded by `max_clients` rather than being an arbitrary
   allocation.
5. **The unit does not start and is not enabled.** Confirm `install_ftps_unit` leaves
   `maran-ftps.service` installed, disabled and stopped, and that nothing in the step starts it.
   The promise this whole task makes is: **the daemon ends up installed and not listening.**

### What the author could not verify

- **A second reviewer.** An agent session cannot dispatch one. This is the debt above, restated
  where a reader looking for gaps will find it.
- **A real internet-facing install.** Nothing here was run on a host with a public address. The
  claim "no socket is ever bound during the install" was proved against the artifacts a container
  can answer for (the mask symlink, and the package postinst's behaviour under it), not against a
  booted machine with a routable interface.
- **Whether *vsftpd* refuses a non-member** — as distinct from whether PAM does, which was
  measured. A live `pam_authenticate` against this stack was run with `pamtester` in throwaway
  `ubuntu:24.04` and `almalinux:9` containers on 2026-09-09: a member of `maran-ftps` was
  `successfully authenticated`; a system login **holding the correct password** and not in the group
  got `Authentication failure`; `root` got `Authentication failure`. What that does **not** prove is
  the composite — that vsftpd is pointed at this service name, converses with it as expected, and
  refuses the session. No daemon was started, no port was bound, and the environment where this
  step's own assertions run boots no systemd, so the step asserts the stack on the **file** and says
  so. The composite belongs to Task 3 Step 5 and Task 10's `ftps_on_a_real_host.rs`.
- **SELinux in enforcing mode on AlmaLinux.** vsftpd on RHEL is governed by `ftpd_t` and by booleans
  (`ftpd_full_access`, `ftpd_use_passive_mode`) that this step does not set. The polygon images do
  not run SELinux enforcing, so nothing here was exercised against it. A reviewer with an enforcing
  AlmaLinux host must confirm the daemon can read `/etc/maran/vsftpd/vsftpd.conf` and traverse
  `/var/lib/maran-ftps/<account>`.
- **What an operator already had on port 21.** A host that was already running an FTP daemon of the
  operator's own is a host where masking `vsftpd.service` changes their service. The step masks only
  `vsftpd.service` and touches no other unit, but nothing in this repository can see an operator's
  own daemon.

## What an attacker could do with this surface, and why it is safe now

### The clear-text window that would have existed

`apt-get install vsftpd` on Ubuntu 24.04 / Debian 13 leaves
`/etc/systemd/system/multi-user.target.wants/vsftpd.service` in place and the daemon running, with
the distribution's shipped configuration: `listen_ipv6=YES`, `local_enable=YES`,
`anonymous_enable=NO`, and **no TLS requirement**. That is a port 21 accepting local system logins
over an unencrypted control channel — passwords in the clear — on a machine the operator has just
exposed. An installer that installed the package and then disabled the unit would have opened that
port for the length of the install, every time, on every Debian-family host.

`systemctl mask vsftpd.service` replaces the unit with a symlink to `/dev/null` in
`/etc/systemd/system`, which shadows `/lib/systemd/system/vsftpd.service`; systemd refuses to start
a masked unit, so the postinst's own `systemctl start` cannot bind anything and the failure is
harmless.

**One measured correction a reviewer must not skip.** ~~The mask does not stop the package from
*enabling* the unit — `deb-systemd-helper` keeps its own bookkeeping and does not consult the
link.~~ **That paragraph was wrong and is struck rather than deleted, so a reviewer who read the
earlier version can see what changed.** It rested on a single measurement; two later ones
disagreed; the four cells of *mask × image* were run a third time on 2026-09-09 and the first
reading did not survive. What is actually true, in throwaway `docker run --rm` containers:

| image | vsftpd | mask before `apt-get install` | `multi-user.target.wants/vsftpd.service` | `vsftpd.service.dsh-also` |
|---|---|---|---|---|
| `ubuntu:24.04` | 3.0.5-0ubuntu3.1 | yes | absent | absent |
| `ubuntu:24.04` | 3.0.5-0ubuntu3.1 | no | present | present |
| `debian:trixie` | 3.0.5-0.2 | yes | absent | absent |
| `debian:trixie` | 3.0.5-0.2 | no | present | present |

The mechanism, re-driven by hand with the postinst's own calls: `deb-systemd-helper enable`
delegates to systemd where `systemctl` exists, and systemd refuses a masked unit —
`Failed to preset unit, unit /etc/systemd/system/vsftpd.service is masked.` — after which the
helper exits before writing either the link or its state file, and the postinst swallows that with
`|| true`. The postinst's `deb-systemd-helper unmask` does not remove our mask either: it unmasks
only masks it recorded itself, and returns early — "Not unmasking … because the state file … does
not exist". The link is still `/dev/null` after every masked install.

**The conclusion a reviewer must draw is unchanged, and this is why the correction does not
weaken anything.** The safety property still rests entirely on the **mask**, and the `.wants`
entry is still not the control — because its absence depends on which branch the helper took.
Where `systemctl` is not present the helper creates the links itself. `almalinux:9` creates no
wants entry either way.

What was **not** observed, and is marked so rather than assumed: whether the postinst would have
started the daemon. A container boots no systemd and Docker's own `policy-rc.d` denies the start
outright (`policy-rc.d denied execution of start`), so the start was refused for a reason unrelated
to this step. The mask is left in place
permanently: this panel never runs the packaged unit, it runs `maran-ftps.service` with its own
configuration path. **The ordering is the control**, which is why a reviewer is asked to check it
and why a check asserts on the symlink rather than on a port that nothing could have bound anyway in
the environment where the check runs.

### The credential surface a system login adds

An FTPS login is a row in `/etc/passwd` with the owning account's uid (`useradd --non-unique`). Two
consequences, closed rather than waved at:

- It could in principle be presented to another PAM-consuming service on the host. The shell is
  `nologin`; the login joins no group but `maran-ftps`; and `/etc/pam.d/maran-ftps` is a service of
  our own, so nothing that already exists on the host starts accepting these credentials because
  this step ran.
- Membership of `maran-ftps` is the whole authorization. That is a stronger statement than a
  deny-list: it is a property of what the stack **contains**, not of what somebody remembered to add
  to `/etc/ftpusers`. `root` is refused because it is not in the group, not because a list names it.

### Suspension locks passwords, not sessions

`SetAccountLoginsLocked` locks the password of every login an account holds. An FTPS session that
has **already authenticated** survives the lock until it disconnects; vsftpd re-authenticates
nothing mid-session. The bound is `idle_session_timeout=600` in the rendered configuration — ten
minutes of inactivity — and a session that keeps transferring is not idle and is not cut. Stated
here in one sentence so a reviewer weighs it rather than discovers it. Closing it properly would
mean killing the daemon's children for that uid on suspension, which is a behaviour this plan does
not implement and does not pretend to.

### systemd hardening directives: weighed, deliberately not prescribed

> **Superseded for the unit by "Amendment, 2026-10-26" below.** This section is kept as the
> 2026-09-09 record. Its present tense ("are **not** in the unit", "the rest", the
> `RuntimeDirectory=` as the one containment property) describes `dev` before issue #92, not the
> unit on this branch, which now carries the directives that amendment lists. The four rejections
> named here (`ProtectHome=`, `NoNewPrivileges=`, `ProtectSystem=strict`, `PrivateTmp=`) stand, and
> two of their reasons are doubted there.

`ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp=yes`, `NoNewPrivileges=yes` and the rest were
considered for `maran-ftps.service` and are **not** in the unit. The reason is that most of them
contradict what vsftpd does by design: it chroots into `/var/lib/maran-ftps/<account>` whose `home`
is a **bind mount of the customer's real home** (so `ProtectHome` breaks the product's only
function), it forks children that change uid to the account (so `NoNewPrivileges` breaks `setuid`
and `pam_unix`'s helper), and it writes to `/var/log/maran/ftps.log` and needs its `RuntimeDirectory`
(so a blanket `ProtectSystem=strict` needs an exception list that grows with every path). A unit
that *looks* hardened while contradicting the daemon is worse than one that states the weighing:
the first stops the next reader looking, the second tells them exactly which directives were
rejected and why, so a reviewer can disagree with a named decision instead of guessing at an
omission. What the unit **does** carry is `RuntimeDirectory=maran-ftps/empty`, which is a real
containment property and not a decoration: vsftpd's `secure_chroot_dir` must exist, be empty and not
be writable by the unprivileged user the daemon drops to, and `RuntimeDirectory` gives all three and
takes the directory away again on stop — so no stale directory can outlive the daemon and be
replaced by something else.

## Amendment, 2026-09-09: the jail base is 0711, not 0700

This note is EXTENDED rather than rewritten, and the second-reviewer requirement above stays
**OUTSTANDING** — this amendment adds to what a reviewer must check, it discharges nothing.

**What changed.** `installer/lib/89-ftps.sh` now creates `/var/lib/maran-ftps` as
`root:root 0711`. It created it `0700`, and at `0700` **every FTPS login on every real install
fails**. Measured on one host, one login, only this mode changed between the two runs:

    /var/lib/maran-ftps 0700 -> 500 OOPS: cannot change directory:/var/lib/maran-ftps/alice
    /var/lib/maran-ftps 0711 -> 230 Login successful. / 226 Directory send OK.

The cause is a difference between the two daemons this panel confines, and it is the reason the
two jail bases are siblings with different modes rather than one shared rule. `sshd` walks
`ChrootDirectory` as **root**, before it drops privileges — which is why
`installer/lib/40-user.sh` gives `/var/lib/maran-sftp` `0700` and is right to. `vsftpd` `chdir()`s
into the login's passwd home **after** dropping to the account's uid, so every component of the
jail path must carry the execute bit for that uid. Under the forced TLS this product ships the
customer's symptom is worse than the message: vsftpd writes that `500` in the clear on a channel
the client is reading as TLS, so the connection simply breaks with nothing naming the cause.

**What 0711 grants, and to whom.** `0711` is execute-without-read: the directory is
**traversable and not listable**. Concretely, measured as uid `nobody` on `ubuntu:24.04` and
`almalinux:9` alike:

- **any local uid can traverse it** — `stat /var/lib/maran-ftps/<account>` succeeds;
- **no local uid can read it** — `ls -A /var/lib/maran-ftps` is refused with `Permission denied`,
  so the account names, which are the entry names, are not enumerable from here. At `0755` both
  succeed, which is why the mode is not simply widened.

**So the security of the arrangement rests on the per-account jail and not on the base — and it
already did.** The base's `0700` was never load-bearing: it made the daemon fail rather than
making anything safe, and a mode assertion agreeing with it is what stopped anyone noticing.
What holds beneath it:

- `/var/lib/maran-ftps/<account>` — `root:root 0755`, the chroot root. World-readable **on
  purpose**: the login must traverse and list it, and must not be able to write it, because
  vsftpd refuses to run at all with a writable chroot root (`500 OOPS: vsftpd: refusing to run
  with writable root inside chroot()`). It is root-owned and contains exactly one entry.
- `/var/lib/maran-ftps/<account>/home` — the account's real home, bind-mounted. Its mode is the
  home's own, `<account>:<web server group> 0750`, and **that** mode is the containment: no other
  uid may enter it.

The net grant is therefore that a local uid who already knows an account name reaches a second
path to that account's home — a home whose own `0750` refuses it, exactly as the first path does.
`/home` is `root:root 0755` on both supported families, so that uid could already walk to
`/home/<account>` and be refused there. **This change does not widen what any local uid can read.**
It makes the daemon able to reach the jail at all.

**What a reviewer must check, in addition to the five items above:**

6. **That `0711` and not `0755` is what the installer produces**, and that the jail directory
   beneath it is root-owned and not writable by the login. The base's mode is now a
   *functional* requirement as well as a security one, which is a combination worth distrusting:
   confirm the two halves are checked separately, because a single "the mode is 0711" assertion
   would have passed throughout the life of the defect this amendment fixes — `0700` was the
   intended value, and the assertion agreed with it.
7. **On an SELinux-enforcing AlmaLinux host**, that `ftpd_t` can traverse the base at `0711`.
   The polygon runs SELinux permissive; this was not exercised. (Already listed under "What the
   author could not verify"; repeated here because the mode changed underneath it.)

**What the author could not verify about this change:** a real internet-facing host, and a real
FTPS login on the RHEL family. The login above was measured on the Debian family; the traversal
half — the syscall the login's `chdir` makes, performed by an unprivileged uid — was measured on
both. The step now carries its own gate for this
(`assert_jail_base_is_traversable_and_not_listable`), which asks an unprivileged uid to traverse
the base and requires reading it to be refused, so a wrong mode fails the install by name on the
machine being installed rather than silently producing a panel whose FTPS never works.

## Amendment, 2026-09-09: a rendered vsftpd.conf is rewritten, never appended to

Recorded here because it is a property of a config file this feature's security depends on.
Measured on `ubuntu:24.04` (3.0.5-0ubuntu3.1) and `almalinux:9` (3.0.5-8.el9), with a control for
each value so neither answer can be a coincidence: when a key appears twice in one vsftpd
configuration, **the last occurrence wins**. An appended `force_local_logins_ssl=NO` after a
rendered `=YES` turns forced TLS **off**; an appended `listen_port` moves the listener.

An append is therefore not an inert duplicate that the next render tidies away — it silently
replaces the rendered decision, including the decision that a customer's password may not cross
the wire in the clear. `installer/lib/89-ftps.sh` appends to nothing (there is no `>>` in it: it
uses `install -D`, `install -d`, a staged render plus `mv -f`, and `ln -sfn`) and it does not
write `/etc/maran/vsftpd/vsftpd.conf` at all — that file is the agent's, written whole through
`ops::safe_write`. The one legitimate override is `-o<key>=<value>` on `maran-ftps.service`'s
`ExecStart`, applied after the file is read; measured to win over the file, and under last-wins
that is the same rule rather than an exception to it.

## What this step deliberately does not do

- It does not enable or start `maran-ftps.service`. FTPS is off until an operator turns it on, and
  that is a server-level decision with the firewall consequences shown before it happens.
- It does not open any port. The firewall step is not modified by this task.
- It does not write `/etc/maran/vsftpd/vsftpd.conf`. The configuration is rendered by the agent
  through `ops::safe_write` (Task 6/8); the installer creates only the directory it lands in.
- It does not create a per-account jail or a bind mount. Those are account-lifetime resources
  belonging to the agent, which derives every path from a validated `AccountName`.
- It does not remove `/var/lib/maran-ftps` on uninstall. Those jails hold live bind mounts of
  customers' homes, and an `rm -rf` across one deletes the home it points at. The uninstaller says
  what is left and how to finish by hand.

## Amendment, 2026-09-09: the gate over the authorization boundary now asks PAM, not the file

**OUTSTANDING** — like every amendment here, this adds to what a reviewer must check and discharges
nothing. It is recorded because it changes the gate over an authentication surface.

### What was wrong with the gate

`assert_ftps_pam_stack_requires_group_membership` (docker/polygon/asserts/assert-installer-steps.sh) said
"requires" and observed presence: it grepped `/etc/pam.d/maran-ftps` for the two
`required pam_succeed_if.so user ingroup maran-ftps` lines and for the absence of the host's
aggregate stack. A PAM stack is not its lines, it is their control flow, and the two are separable —
measured, on both families, twice: prepend `auth sufficient pam_permit.so` and
`account sufficient pam_permit.so` to the shipped stack, leave both `required` lines byte-identical,
and the assertion stays green while printing that the stack "requires maran-ftps membership in both
phases". What it was green over: an account in **no group**, offering the **wrong password**, gets
`pam_authenticate = 0 Success`. A total authentication bypass under a green gate.

### What the gate proves now

A live `pam_authenticate` **is** feasible where the assertion runs, and it now runs there.
`docker/polygon/asserts/pam-witness.c` is compiled inside each polygon image against that family's own
libpam and drives the real `pam_start` / `pam_authenticate` / `pam_acct_mgmt` against the stack
step 89 has just installed, at the path libpam reads it from. Five transactions, and the assertion
fails unless all five happen:

| login | password | must be |
|---|---|---|
| in `maran-ftps` | correct | ACCEPTED |
| **not** in `maran-ftps` | **correct** | REFUSED |
| not in `maran-ftps` | wrong | REFUSED |
| in `maran-ftps` | wrong | REFUSED |
| not in `maran-ftps`, through a stack with `sufficient pam_permit.so` prepended | wrong | ACCEPTED |

The second row is what the group gate exists for: the outsider holds a valid shadow entry, and
membership is the whole difference. The third is what catches the bypass. The fifth is the inverse
control — a witness that refuses everything would pass the three refusals above and prove nothing,
so the reviewer's own mutant is planted under a service name of its own and must be let through.
Measured red, by name, against that mutant on Ubuntu 24.04 and AlmaLinux 9:
*"PAM service maran-ftps answered ACCEPTED authenticate=0 Success acct_mgmt=0 Success for
ftpswitnessout, and it must be REFUSED."*

The cost is two packages (`libpam0g-dev`, `pam-devel`), one C file, and two throwaway accounts with
a throwaway password that exist for five PAM transactions inside an image build and are deleted
before the layer closes — the assertion fails if either outlives it. No daemon is started and no
port is bound.

**One correction to that last clause, measured rather than reasoned, because the clause was not
true when it was written.** "The assertion fails if either outlives it" was enforced by asking
`getent passwd`, and `getent passwd` answered correctly the whole time while the alma9 image
shipped two 0-byte files at `/var/spool/mail/ftpswitnessin` and `/var/spool/mail/ftpswitnessout`,
owned by the bare uids 1000 and 1001 — the numbers those accounts had held. The RHEL family's
`useradd` creates a mail spool even under `-M`; the Debian family's does not; and `userdel -f`
removes it on neither. So the witness accounts did outlive their assertion, as files rather than
as logins, and a later suite's account landing on a recycled uid would have found a file it owned
at a path it never created. The spools are now deleted by name and **the leftover guard was
widened to the axis that had gone blind** — it asks about the spool as well as the passwd
database, because a deletion check that only asks the half that was already right is a check that
agrees with the defect.

A reviewer should note the asymmetry rather than assume it away: broken deliberately, that guard
goes red on AlmaLinux 9 by name — *"the throwaway account ftpswitnessin is gone from /etc/passwd
but its mail spool is still in this image, owned by a uid that is now free to be handed to a later
suite's account"* — and stays **green on Ubuntu 24.04**, where the Debian `useradd` creates no
spool and there is therefore nothing for it to catch. That is an honest `UNOBSERVED HERE` on the
Debian family, not a second confirmation.

### What the gate still cannot see

- **Everything the daemon does around those two calls.** vsftpd's own `pam_service_name` reaching
  this file, the TLS handshake, the chroot into the jail, the data channel. No daemon boots in an
  image build. That composite is still Task 10's `ftps_on_a_real_host.rs`, and this amendment does
  not move it.
- **PAM under SELinux enforcing**, for the same reason as the rest of this note: the polygon images
  do not run it.
- **The session phase.** `pam_open_session` is not driven; only `auth` and `account` are.

### Where this gate may run, which turned out not to be everywhere

Found while proving the assertion goes red, and recorded because the next person to move it will
otherwise lose a day to it. **On the RHEL family, inside a `--privileged` container, `pam_unix.so`
answers `9 Authentication service cannot retrieve authentication info` for every login — including
one whose shadow entry was just written and can be read back with `getent shadow`.** The same
image, the same stack, the same accounts, run *without* `--privileged`, answers `0 Success`.
Measured on `maran-polygon-alma9` with a stack of two lines (`auth required pam_unix.so`,
`account required pam_unix.so`) and no Maran code involved at all, so it is a property of the
container and not of anything this repository writes.

This does not reach the shipped gate: a `docker build` layer is never privileged, and that is the
lane the assertion runs in on both families. It is written down because of the direction it fails
in, which is the part worth checking rather than trusting. Were the assertion ever moved into one
of the polygon's `--privileged` lanes, it would go **red on its first case** — the member with the
correct password, refused — and not silently green: the witness would be unable to authenticate
*anybody*, which is precisely the failure the fifth case (the planted `pam_permit` stack, which
must be ACCEPTED) exists to catch. A reviewer should confirm that ordering still holds if the
assertion moves, because a witness that refuses everything passes every test that only ever hands
it something it must refuse.

### The second half: nothing compared the two halves' names, and the installer said otherwise

`installer/lib/89-ftps.sh` stated that "the polygon images assert them equal" of the group name.
Nothing anywhere compared the installer's `MARAN_FTPS_GROUP`, `MARAN_FTPS_JAIL_ROOT` and
`MARAN_FTPS_PAM_SERVICE` with the agent's `ftps_group()`, `AgentPaths::FTPS_JAIL_ROOT` and rendered
`pam_service_name` — three cross-language constants, each asserted only against itself. A one-
character drift on either side means the installer creates group A, the agent adds logins to group
B and the PAM stack tests group A: **every FTPS login on every install refused, with every gate
green.** For a reviewer that is the same class of finding as a wrong permission, because the
sentence claiming a check is what stops the next reader adding one.

`assert_the_installer_and_the_agent_spell_the_ftps_names_the_same` now makes the comparison, in the
same image build, on both families, and the installer's comment says where it lives instead of
claiming a gate. What a reviewer should know about its reach: it compares the agent's **source
text**, not the compiled agent, so a name a future adapter computes rather than declares is
invisible to it — it refuses such a template expression by name rather than pretending to compare
it — and it is a polygon-build check, so it does not run in the backend lane the way
`maran structure` does.

---

## Correction, 2026-09-10 — the service account is `maran`, not `panel`

Item 3 of the reviewer's list ("The jail's ancestor chain") states `/var/lib/maran` as
`panel:panel 0750`, and item 1 lists `panel` among the system logins the PAM stack must refuse.
That user and group were renamed on this branch: `installer/install.sh` sets `MARAN_USER=maran`
and `MARAN_GROUP=maran` (`grep -n "^MARAN_USER=\|^MARAN_GROUP=" installer/install.sh`), and
`installer/lib/40-user.sh:106` installs `/var/lib/maran` as `$MARAN_USER:$MARAN_GROUP 0750`. So on
an installed host the directory is **`maran:maran 0750`**, and the login a reviewer must confirm
the PAM stack refuses is `maran`. See
`docs/superpowers/notes/2026-09-09-service-account-rename-threat-note.md`.

**The argument is unaffected and the mode is unchanged.** What item 3 turns on is that
`/var/lib/maran-ftps` is a sibling of that directory and that no component of the chroot path is
owned by an unprivileged uid; renaming the account that owns the sibling changes neither. Only the
names were wrong — and they were wrong on the page a reviewer checks against a real host first,
which is why this is recorded here rather than quietly edited out of the text above.

Regenerate the true values: `grep -n "install -d.*var/lib/maran$" installer/lib/40-user.sh`.

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

---

## Amendment, 2026-10-26: hardening `maran-ftps.service` one directive at a time (issue #92)

Written **before** any line of `installer/systemd/maran-ftps.service` changed, which is the order
`rules/security.md` requires: written afterwards, this section would justify a unit that already
existed. Branch `fix/92-installer-ftps-unit-hardening`. The measured outcomes go into the table at
the end of this section as they are taken; every row starts as NOT YET MEASURED.

**This amendment supersedes "systemd hardening directives: weighed, deliberately not prescribed"
above** for every directive it plans. That section stays as written, because it is the record of
what was decided on 2026-09-09, but from this branch on it describes the rejections only, not the
unit. Where this amendment doubts one of its reasons, it says so below.

**The order of work on this branch, and why it does not weaken the one-at-a-time measurement.**
The work runs in two phases. In the code phase, every planned directive is written into
`installer/systemd/maran-ftps.service` at once, with the issue's starting capability set, and
nothing is built, run or measured. In the verification phase, each row of "Measured outcomes"
below is measured. Each row is measured against a unit **generated inside the throwaway
container**, from the branch's unit with the directives after that row's position deleted, so the
attribution "this directive broke it" survives exactly as if the directives had been added one by
one. `installer/systemd/` is never edited to take a measurement. A directive or capability that
breaks a row, or that a single-member removal shows to be unneeded, is then removed from the
shipped unit in a follow-up code change, and the `final` row is measured again on **that** file,
with its `sha256sum` recorded beside the row. Until then the shipped unit is a plan carrying the
expectations in "Which directive is expected to hold, and why" below. It is not a measured result.

This section was drafted, interrupted, and then checked and corrected against vsftpd's source and
the polygon images, still before any unit line changed. Four claims of the first draft did not
survive, and the corrections are marked where they land rather than smoothed over: that `SITE
CHMOD` lets a tenant set a setuid bit (vsftpd masks it); that `RestrictNamespaces=` or a missing
`CAP_SYS_ADMIN` would break a login (vsftpd falls back silently); that `NoNewPrivileges=` is a
positive control (it may well break nothing); and that `/etc/shadow` in the images forces root
through `CAP_DAC_*` (it does not).

It was then checked a second time on 2026-10-26, by a later session of the same stage and still
before any unit line changed (`sha256sum installer/systemd/maran-ftps.service` still printed
`185892d6…`, the value under "The surface today"). Every file and line this section cites in the
repository was re-read and holds. Three things were missing, and they were added where they land
rather than appended here: that "`NoNewPrivileges=` absent from the unit" is not the same claim as
"no_new_privs off on the daemon" (under "What stays off"); that the agent's own two validation
layers cannot see most of what these directives could break (interface item 8); and that the
residual root parent keeps only what the measurement leaves it, not the whole starting set (under
"Why this makes the surface safer"). Reviewer items 29 and 30 follow from the first two.

### Second reviewer for this amendment: OUTSTANDING

The 2026-09-23 verdict above covers this note **as it stood on that date**. It does not extend to
this amendment or to the unit diff it describes. The change hardens the most exposed root process
the product installs, through a file the installer's privileged step writes, so it is a sensitive
change under `rules/security.md` in its own right. **No second reviewer has read it. The
requirement is OUTSTANDING.** The author is an agent session: it cannot dispatch a reviewer and
cannot be one. The branch **MUST NOT merge to `main`** until a human second reviewer has read this
section and the unit diff and recorded a verdict here. The owner is told at hand-off that this
surface carries an outstanding review.

### The surface on `dev`, before this branch, measured

```
systemd-analyze security --offline=yes installer/systemd/maran-ftps.service
→ Overall exposure level for maran-ftps.service: 9.4 UNSAFE :-{
```

Re-measured 2026-10-26 on systemd 255 (255.4-1ubuntu8.18) against the file whose
`sha256sum` is `185892d605b7cc9ef18887a63d358510be22e4172630df7e7dfdcd229220218a`; 74 lines of
that output are marked `✗` (`systemd-analyze security --offline=yes
installer/systemd/maran-ftps.service 2>&1 | /usr/bin/grep -c '✗'`). The same command scores
`maran-agent.service` 4.4. The issue's measurement holds unchanged.

On `dev` (the file with the sha256 above), the `[Service]` section restricts nothing but
`RuntimeDirectory=`; everything below in this section describes that unit, not the one on this
branch. Who talks to this daemon, and
what the unit stands between them and the host today:

- **An unauthenticated client on the internet** reaches the pre-login process, which vsftpd runs as
  `nopriv_user=nobody` chrooted into `/run/maran-ftps/empty`, and through it the privileged parent
  that runs PAM. Every barrier here is vsftpd's own privilege separation and the PAM stack. The unit
  contributes none.
- **Which processes are root at all.** vsftpd 3.0.5, read upstream, keeps exactly two kinds of
  root process, and neither is a logged-in session. The first is the standalone listener, which is
  the unit's `MainPID`. The second is each connection's privileged parent, which is root **only
  until the login completes**. That parent runs PAM. When the login succeeds it forks the session
  child, which drops to the account through `vsf_secutil_change_credentials`
  (`common_do_login`, `twoprocess.c:382-484`, the drop at `:458`). The parent then calls
  `vsf_priv_parent_postlogin` (`:482`), which drops it to `nopriv_user` (`nobody`), chrooted into
  `secure_chroot_dir`, with **no** capability (`minimize_privilege`, `postprivparent.c:83-122`, the
  drop at `:115`). The two
  capabilities it could have kept are conditional, and neither condition holds here:
  `CAP_CHOWN` only under `chown_uploads` (default off, `tunables.c:164`, not set by the
  template), and `CAP_NET_BIND_SERVICE` only under `connect_from_port_20`, which the template sets
  to `NO`. It keeps root only under `session_support` (`postprivparent.c:89`), which defaults
  off (`tunables.c:199`) and which the template does not set. So the root processes an attacker
  can aim at are the listener and the pre-login parents of connections that have not yet
  authenticated. These are the processes `CapabilityBoundingSet=` mainly constrains. Every other
  process of the unit is unprivileged, and what the unit adds for those is the seccomp-backed and
  mount-backed directives. This is a source fact and not a measurement; the session observation
  below checks it on the shipped binaries.
- **Whoever gains code execution in the listener or a pre-login privileged parent**, through a
  defect in vsftpd's privileged-socket protocol, in PAM or a PAM module, or in whatever TLS work
  that parent does (not determined here), holds root with the **entire** capability set
  (`CAP_SYS_MODULE`, `CAP_SYS_RAWIO`, `CAP_SYS_PTRACE`, `CAP_SYS_BOOT`, `CAP_MKNOD`, `CAP_NET_ADMIN`, `CAP_NET_RAW`, `CAP_SYS_TIME` and
  the rest), every system call under every ABI, every address family including `AF_PACKET`, a
  writable `/proc/sys`, the kernel log, the cgroup tree, the clock, the hostname and a `/dev` with
  the host's block devices. That is not only "root on this host" but root with every kernel-level
  persistence path: load a module, write the disk under the filesystem, ptrace the agent.
- **An authenticated tenant** runs in a session child under their account's uid, chrooted into
  `/var/lib/maran-ftps/<account>`. One thing reaches past that, and one that issue #92 says does
  turns out not to:
  - **`SITE CHMOD` is enabled, and its mode is masked by vsftpd itself.** The rendered
    configuration does not set `chmod_enable`, so vsftpd's default (on) applies
    (`/usr/bin/grep -n -E '^chmod_enable' agent/crates/templates/templates/vsftpd/vsftpd.conf.j2`
    prints nothing; the same command for `^chroot_local_user` prints line 54, so the search can
    hit). **Issue #92 assumes that a tenant can set a setuid bit through it, and vsftpd's source
    says otherwise.** In upstream vsftpd 3.0.5 (the version both families ship; tarball sha256
    `26b602ae454b0ba6d99ef44a09b6b9e0dfa7f67228106736df1f278c70bc91d3`), `handle_site_chmod`
    (`postlogin.c:1561`, "our chmod() implementation only allows 0 - 0777") calls
    `vsf_sysutil_chmod`, which does `mode = mode & 0777` before `chmod(2)` (`sysutil.c:1496-1501`,
    "Safety: mask \"mode\" to just access permissions, e.g. no suid setting!"). `vsf_sysutil_fchmod`
    masks the same way (`sysutil.c:1486-1493`). Uploads open with `file_open_mode` (default `0666`,
    `tunables.c:255`) and `MKD` creates with `0777` (`postlogin.c:1189`). So upstream gives a tenant no path to a setuid
    or setgid bit, and FTPS is **not** a second door around the policy `FileMode` enforces for the
    panel (`agent/crates/agent-core/src/validation/fs/file_mode.rs`, `NOT_PERMISSION_BITS`). Two
    things are not read and must be measured instead: the distributions' own patches over that
    source, and the shipped binaries. Row 0 of the table below does that by `stat`-ing the file
    after `SITE CHMOD 4755`, **not** by reading the reply code, because a masked chmod answers
    `200` exactly as an unmasked one does.
  - **The kernel.** A tenant who finds a vsftpd bug in the post-login child has an unprivileged
    process with every system call, every ABI and every socket family to aim a kernel bug at.

### The change planned (unit `ftps-unit`: `installer/systemd/maran-ftps.service` and this note)

The issue's directive list, written into the unit together in the code phase, then **measured one
at a time on top of the survivors**, in the order below. Each row is measured against a unit
generated in the container with every later row deleted (see "The order of work" above), so that a
breakage is attributed to the directive that caused it rather than to whichever arrived with it.
The shipped unit ends as exactly the measured combination, and that combination is measured again
as a whole at the end (the `final` row).

| order | directive | why vsftpd should not need what it removes | a breakage would look like |
|---|---|---|---|
| 1 | `ProtectKernelTunables=yes` | the daemon writes no `/proc/sys` or `/sys` | start failure, or nothing |
| 2 | `ProtectKernelModules=yes` | the daemon loads no module | start failure |
| 3 | `ProtectKernelLogs=yes` | PAM logs through `/dev/log`, not `/dev/kmsg` | missing PAM log lines |
| 4 | `ProtectControlGroups=yes` | the daemon writes no cgroup | start failure |
| 5 | `ProtectClock=yes` | the daemon sets no clock | start failure |
| 6 | `ProtectHostname=yes` | the daemon sets no hostname | start failure |
| 7 | `SystemCallArchitectures=native` | vsftpd is a native binary | `SIGSYS` in a session |
| 8 | `LockPersonality=yes` | the daemon changes no execution domain | `SIGSYS` in a session |
| 9 | `RestrictRealtime=yes` | the daemon uses no realtime scheduling | start failure |
| 10 | `MemoryDenyWriteExecute=yes` | vsftpd generates no code (note a) | `SIGSYS` at TLS or at PAM |
| 11 | `RestrictSUIDSGID=yes` | no setuid or setgid file of its own (note b) | refused upload or `mkdir` |
| 12 | `RestrictAddressFamilies=` (note c) | only IP, unix and netlink sockets (note c) | refused login, slow greeting |
| 13 | `PrivateDevices=yes` | only API pseudo-devices (note d) | PAM cannot log; TLS cannot seed |
| 14 | `CapabilityBoundingSet=` enumerated | see below | refused login, `chroot` or namespace |

- (a) vsftpd is C; OpenSSL and the PAM modules are expected not to map memory writable and
  executable at once, which is what the row measures.
- (b) vsftpd masks every mode it sets to `0777` (above), so no FTP command can be what this
  directive refuses; the row is observed by a probe (below), not by the session.
- (c) The value is `AF_INET AF_INET6 AF_UNIX AF_NETLINK`: listening and data connections, a
  reverse lookup of the client where the build does one, PAM's syslog socket, and libpam's audit
  socket. **Both families, not only RHEL** (an earlier draft of this section said RHEL only).
  `objdump -p /lib/x86_64-linux-gnu/libpam.so.0 | grep NEEDED` on the workstation, whose
  `libpam0g` is `1.5.3-5ubuntu5.7` (the same version as `maran-polygon-ubuntu24:v3`), prints
  `NEEDED libaudit.so.1`. The EL9 `pam` package is expected to link it as well; that was read, not
  measured. A refused audit socket would show as a refused login, and a refused lookup as a slow
  or missing greeting.
- (d) The private `/dev` that `systemd.exec(5)` describes holds API pseudo-devices such as
  `/dev/null`, `/dev/zero` and `/dev/urandom`, and no physical device. The directive also drops
  `CAP_MKNOD` and `CAP_SYS_RAWIO` from the bounding set. `/dev/log` is a socket that lives
  under `/run/systemd/journal`, not a device node. Whether it stays reachable in the private `/dev`
  is **measured, not assumed** (reviewer item 14). A PAM that cannot reach it still authenticates,
  so the session alone cannot tell.

**The capability set.** Measured from the issue's starting set: `CAP_NET_BIND_SERVICE` (port 21),
`CAP_SYS_CHROOT` (the jail), `CAP_SETUID CAP_SETGID` (the session's change to the account),
`CAP_CHOWN`, `CAP_DAC_OVERRIDE CAP_DAC_READ_SEARCH` (the privileged parent reads `/etc/shadow`
through `pam_unix` as root), `CAP_FOWNER`, `CAP_AUDIT_WRITE` (libpam's audit records, on both
families: note c),
`CAP_SYS_ADMIN` (vsftpd's defaults `isolate=YES` and `isolate_network=YES`, `tunables.c:232-233`,
which the rendered configuration does not override, clone new PID, IPC and network namespaces per
session, and that requires it). `CAP_KILL` is not in the set: vsftpd's children end through
`PR_SET_PDEATHSIG` (`sysdeputil.c:1247,1258`), which needs no capability, and the suspension cull
signals from the agent's own unit, which holds `CAP_KILL` itself.

After the starting set passes, **each member is removed singly**. "The session stays green" is
the verdict for most members and **the wrong verdict for three of them**, because removing them
does not fail a login, it silently weakens it:

- **`CAP_SYS_ADMIN` cannot be judged by the session at all.** vsftpd's namespace forks are
  `_failok`: `vsf_sysutil_fork_isolate_failok` and `vsf_sysutil_fork_newnet`
  (`sysdeputil.c:1290-1330`) try `clone(CLONE_NEWPID|CLONE_NEWIPC)` and `clone(CLONE_NEWNET)`,
  and on `EPERM` or `EINVAL` fall back to a plain `fork()` with no message, for the rest of the
  daemon's life. Without `CAP_SYS_ADMIN`, every login still succeeds and no session is isolated.
  So this member's row observes the isolation itself: during a live session, the session
  processes' `/proc/<pid>/ns/pid`, `ns/ipc` and `ns/net` must differ from the main process's.
  Its positive control is `RestrictNamespaces=yes`, applied once: the session must stay green
  **and** the namespace observation must flip, which proves the observation sees what the session
  cannot.
- **`CAP_DAC_OVERRIDE` and `CAP_DAC_READ_SEARCH` cannot be judged on the polygon images as they
  stand.** Root reads a file it owns that carries the owner-read bit with no capability at all,
  and both images carry one: `ls -l /etc/shadow` in `maran-polygon-ubuntu24:v3` printed
  `-rw-r----- root shadow`, and in `maran-polygon-alma9:v3` printed `-r-------- root root`
  (measured 2026-10-26). A stock RHEL-family host ships `/etc/shadow` at `0000`, per the `setup`
  package's file list (read, not measured here), where root reads it only through those
  capabilities. A removal that stays green on the image would therefore break every login on a
  real AlmaLinux host. So these two rows run with `/etc/shadow` set to `0000` inside the
  throwaway container on the RHEL family, and the before-row is repeated in that state.

- **`CAP_DAC_OVERRIDE` and `CAP_DAC_READ_SEARCH` mask each other** (an earlier draft of this
  section missed it). Either one alone lets root read a `0000` `/etc/shadow`. So each single
  removal stays green while the other remains, and under the rule below **both** would be dropped,
  which breaks every login on a stock RHEL-family host. They are therefore measured as a pair, as
  `rules/testing.md` requires for two checks that mask each other: each removed alone, then both
  removed together. If only the joint removal is RED, exactly one is kept, and it is
  `CAP_DAC_READ_SEARCH`, because it grants reading and searching only, while `CAP_DAC_OVERRIDE`
  grants writing as well. The only need named here is a read of the shadow file.

Any other member whose removal leaves the whole session green on both families is dropped, unless
a code path the session does not drive is named as needing it, in which case the path is written
here.

### Which directive is expected to hold, and why

Written now so that the measurement can contradict it. Every line is an expectation, not a
result.

- **Rows 1–6, expected to hold on both families.** `ProtectKernelTunables`, `ProtectKernelModules`,
  `ProtectKernelLogs`, `ProtectControlGroups`, `ProtectClock` and `ProtectHostname`. None of the
  processes in the unit writes a sysctl, loads a module, reads `/dev/kmsg`, writes a cgroup,
  sets the clock or sets the hostname. vsftpd logs to its own file (`vsftpd_log_file`) and PAM
  logs through `syslog(3)`, which writes to `/dev/log` and never to the kernel log. Reading
  `/proc/sys` (OpenSSL reads the FIPS switch there on the RHEL family) still works under a
  read-only mount.
- **Rows 7–9, expected to hold.** `SystemCallArchitectures=native`, `LockPersonality` and
  `RestrictRealtime`. vsftpd is a native binary that calls `personality(2)` and realtime
  scheduling nowhere. Run in the unpacked upstream 3.0.5 tree,
  `/usr/bin/grep -n -E 'personality|sched_setscheduler|SCHED_FIFO|SCHED_RR' *.c *.h` printed
  nothing. Its positive control was the same tool and flags with `'prctl|PR_SET_PDEATHSIG'`, which
  printed `ptracesandbox.c:586` among others. That is source, not the shipped binaries, and
  libraries are not covered. The same tree's only `fchown(2)` caller is `privops.c:250`, on the
  anonymous-upload path, and `anonymous_enable=NO`. That is the basis for expecting `CAP_CHOWN`
  to be removable.
- **Row 10, `MemoryDenyWriteExecute`, expected to hold.** vsftpd, libpam, `pam_unix`,
  `pam_succeed_if` (the whole stack, `installer/lib/89-ftps.sh:500-504`) and OpenSSL generate no
  code at run time. This is the same argument `maran-agent.service` measured for itself before
  setting it. It is the row most likely to fail through a library rather than through vsftpd, so a
  `SIGSYS` or `SIGSEGV` at the TLS handshake is the thing to look for.
- **Row 11, `RestrictSUIDSGID`, expected to hold.** No process in the unit passes a set-id bit in
  a mode argument, because vsftpd masks every mode to `0777` (above). A setgid bit that a new
  directory inherits from a setgid parent is applied by the kernel. It is not a syscall argument,
  so the filter neither sees nor refuses it.
- **Row 12, `RestrictAddressFamilies`, expected to hold**, given note c. Name lookups through NSS
  modules use `AF_UNIX`, and a module that used anything else would show as a refused login.
- **Row 13, `PrivateDevices`, expected to hold for the session.** It is the least certain row on
  the **logging** axis, because of `/dev/log` (note d). That is why its evidence is the journal
  line, not the login.
- **Row 14, the capability set.** The issue's starting set is expected to hold, because it is a
  superset of what the source shows the two root processes using. Within it, the expectations for
  each member are:
  - **Expected needed:** `CAP_NET_BIND_SERVICE` (the listener binds 21), `CAP_SYS_CHROOT`,
    `CAP_SETUID` and `CAP_SETGID` (every drop above, `initgroups` included), `CAP_SYS_ADMIN`
    (isolation only, judged by the namespace observation), and one of the two DAC capabilities
    (the pair above).
  - **Expected removable:** `CAP_CHOWN`, because no root process chowns anything with
    `chown_uploads` off, and `CAP_FOWNER`, because no code path that needs it was found.
  - **Uncertain:** `CAP_AUDIT_WRITE`. It is expected to be needed wherever the kernel accepts
    audit messages, if libpam treats an audit write refused to uid 0 as an error. That was not
    read here. A container in its own network namespace may refuse the audit socket outright, so
    the row is UNOBSERVED rather than "removable" unless the journal shows an audit record on
    row 0.

  The code phase writes the **whole starting set**. A member leaves the shipped unit only on a
  measurement, never on these expectations. One that turned out unneeded on the Ubuntu image
  would otherwise be dropped from a unit that also runs on a stock RHEL-family host.

**What stays off, and why, so that nothing is left as "the rest":**

- `ProtectHome=`: the 2026-09-09 reason is that the jail's `home` is a bind mount of the
  customer's real home. **Kept off by the owner's scope for this change; the reason is not
  re-measured here, and on mechanism it is doubtful.** `ProtectHome=yes` over-mounts `/home`,
  `/root` and `/run/user`. The jail's `home` is a separate mount at
  `/var/lib/maran-ftps/<account>/home`, made by a `.mount` unit
  (`agent/crates/ops/src/ftps/ensure_account_jail.rs`), and a login's passwd home is the jail, not
  `/home/<account>` (`agent/crates/ops/src/ftps/create_ftps_user.rs`). Nothing in that path
  crosses `/home`, so the directive may well leave every session working. A reviewer should know
  that the rejection stands on the owner's decision, not on the sentence given for it.
- `NoNewPrivileges=`: the 2026-09-09 reason is that sessions change uid to the account and that
  it breaks `setuid` and `pam_unix`'s helper. **Kept off by the owner's scope; the reason is not
  re-measured here, and on mechanism it is doubtful too, so it is NOT used as this harness's
  positive control** (an earlier draft of this section used it as one). `no_new_privs` refuses
  privilege gained through `execve` of a setuid or file-capability binary. It does not refuse a
  privileged process calling `setuid(2)`, and vsftpd executes nothing. Two facts in this tree
  point the same way: `installer/systemd/maran-agent.service` runs with `NoNewPrivileges=true` and
  still forks and changes uid to an account for every customer file operation, and vsftpd's own
  seccomp sandbox sets `PR_SET_NO_NEW_PRIVS` on its post-login processes itself
  (`seccompsandbox.c:674`). Where SELinux is disabled, as in the polygon containers, `pam_unix`
  running as root reads the shadow file directly and does not run its helper. **With SELinux
  enabled, the default on the RHEL family, Linux-PAM's `get_account_info()` runs `unix_chkpwd` even
  for root (recalled from upstream source, not re-read here), and `no_new_privs` can then refuse the
  SELinux domain transition on that `execve`.** So on a stock RHEL-family host the 2026-09-09
  reason may be right, and no harness row here can observe it (see "What the author cannot
  verify"). A control that might stay green is no control.

  **"Absent from the unit" is not the same claim as "off on the daemon", and the measurement must
  read the second.** Older editions of `systemd.exec(5)` said that the seccomp-backed directives
  (rows 7–13 among them) imply `NoNewPrivileges=yes` for a service that runs without
  `CAP_SYS_ADMIN`. The manual on this workstation (systemd `255.4-1ubuntu8.18`) no longer says so:
  `zcat /usr/share/man/man5/systemd.exec.5.gz | /usr/bin/grep -n -E 'NoNewPrivileges'` prints lines
  808, 966 and 5183. The first is `DynamicUser=` implying it, the second the directive's own entry
  and the third an exit status. None of them ties it to a seccomp directive. The same command with
  `'is implied'` prints fifteen lines, so the search could hit. What the author recalls from
  systemd's source, **without having read it here**, is that the test is whether the service
  manager's child holds `CAP_SYS_ADMIN` in its **effective** set at the moment it installs the
  filters. A root unit with no `User=` holds it then, whatever `CapabilityBoundingSet=` says,
  because the bounding set only limits what the later `execve` grants. If that is right,
  `NoNewPrivs:` stays `0` on every row, including the single removal of `CAP_SYS_ADMIN`. The
  applied-proof already records the `NoNewPrivs:` line of `/proc/<MainPID>/status`, so this is
  decided by measurement. A row that shows `1` means the documented rejection is not in force,
  whatever the unit's text says. It is then written here as a finding, beside whether the session
  stayed green under it (reviewer item 29).
- `ProtectSystem=strict`: an exception list that grows with every path (the log, the runtime
  directory, the jail). Unchanged from 2026-09-09.
- `PrivateTmp=`: **the 2026-09-09 text named it among the rejections and gave it no reason, and
  this amendment does not invent one after the fact.** What is true: it is not known to break
  anything, and it buys little, because no session and no pre-login process ever sees the host's
  `/tmp`. Both are chrooted, and only the privileged parent could reach it. It stays off in this
  change because the owner's task keeps the documented rejections. It is recorded as **unmeasured,
  kept off by scope**, not as measured-and-rejected, and a reviewer may ask for it to be measured.
- `RestrictNamespaces=`: on, it refuses the per-session namespaces `isolate` and `isolate_network`
  create. **The issue says it must stay off because those namespaces would break. They would not
  break: vsftpd falls back to a plain `fork()` on `EPERM` (the `_failok` forks above), so every
  login would still succeed, with no session isolated, and nothing would say so.** That is a worse
  failure than a broken login and the reason it stays off. It is also why it is used once as the
  positive control of the namespace observation. An allow-list form (`RestrictNamespaces=pid ipc
  net`, denying `user`, `mnt`, `uts`, `cgroup`) was **not tried**; the task keeps it off entirely.
- **Not weighed in this change, named so the omission is visible:** `SystemCallFilter=` (not in the
  issue's list, and the largest single block of `✗` lines above), `ProtectSystem=full` (only
  `strict` was ever rejected; `full` makes `/usr`, `/boot` and `/etc` read-only, which vsftpd
  never writes, and it is **the strongest remaining candidate** against the residual named below),
  `ProtectProc=` / `ProcSubset=`, `PrivateUsers=`, `UMask=`, `RemoveIPC=`, `KeyringMode=`.
  `PrivateNetwork=` and `IPAddressDeny=` are impossible for a daemon whose purpose is to accept
  internet connections, and `User=` / `DynamicUser=` are impossible for one that must `chroot`
  and `setuid`.

### The interface between parts, and what the unit must keep for its readers outside the branch

**This branch has one part (`ftps-unit`), so no part depends on an API from another part.** No
Rust, C#, proto or installer-script line changes. This file, the unit and checks 28-30 in `scripts/lib/check-structure.sh` are the whole diff (the
installer polygon's read-back of the manager's answer, I-1, and the checks' own tests, S-4, remain
open), and
the part can be written alone.

The unit is nonetheless **read by code outside this branch**, and those readers are its public
contract. Each item below is something the hardened unit must keep exactly as it is, because one
of them changing silently breaks a reader that this branch does not touch:

1. **The name `maran-ftps.service`.** It is `AgentPaths::FTPS_UNIT`
   (`agent/crates/agent-core/src/agent_paths.rs:202`), the target of the agent's `systemctl` verbs
   in `ops::ftps::model::FtpsUnit` (`restart`, `is-active`, `disable --now`, `stop`),
   `MARAN_FTPS_UNIT_NAME` in `installer/lib/89-ftps.sh`, and `installer/uninstall.sh:720-722`.
2. **The single `ExecStart=` line, byte-identical:**
   `ExecStart=/usr/sbin/vsftpd /etc/maran/vsftpd/vsftpd.conf -obackground=NO`.
   `docker/polygon/asserts/parts/ftps.sh:739-746` takes the **last** `^ExecStart=` line, splits it
   on whitespace and requires one word to equal `AgentPaths::VSFTPD_CONFIG_PATH`.
   `agent/crates/agent/tests/fixtures/polygon_vsftpd.rs` runs that same command directly. So no
   `ExecStartPre=` or `ExecStartPost=` is added either: the fixture would not run it, and the
   polygon would then measure a daemon started differently from the shipped one.
3. **`Type=simple` with `-obackground=NO`**, for the reason the unit already states.
4. **`RuntimeDirectory=maran-ftps/empty` and `RuntimeDirectoryMode=0755`.** They must match
   `secure_chroot_dir=/run/maran-ftps/empty`
   (`agent/crates/templates/templates/vsftpd/vsftpd.conf.j2:56`) and the fixture that emulates
   them (`polygon_vsftpd.rs`, the doc comments at its lines 62-64 and 266-269).
5. **`[Install] WantedBy=multi-user.target`**, which the agent's enable path relies on.
6. **A static file with no placeholders.** `install_ftps_unit` copies the file **verbatim**
   (`install -D -o root -g root -m 0644`, `installer/lib/89-ftps.sh:537-544`), and the polygon
   asserts `f:/etc/systemd/system/maran-ftps.service:0:0:644` (`ftps.sh:188`). There is no
   substitution step like the one `maran-agent.service` gets for `__MARAN_QUOTA_DEVICE_BINDS__`.
   So a directive that needs a host-specific value cannot be used in this change.
7. **Nothing the daemon reads or writes becomes hidden or read-only.** The daemon reads the
   configuration under `/etc/maran/vsftpd/`, the certificate and key the agent renders,
   `/etc/pam.d/maran-ftps`, `/etc/passwd`, `/etc/group` and `/etc/shadow`, and the jails under
   `/var/lib/maran-ftps`. It writes `/var/log/maran/ftps.log` and works in
   `/run/maran-ftps/empty`. `ProtectSystem=` and `ProtectHome=` stay off and `ReadOnlyPaths=` is
   not used, so no planned directive changes the visibility or writability of any of them. The one
   new property is that the unit gets its **own mount namespace**, because rows 1–4 and 13 work by
   mounts. Jail mounts the agent makes after start must therefore propagate into it, which the
   mount-propagation rows below measure.
8. **The agent's two validation layers do not run this unit's sandbox, and they stop at the
   greeting.** `enable_ftps` (`agent/crates/ops/src/ftps/enable_ftps.rs`, its doc comment "The
   two validation layers") validates a configuration first through `validate_candidate_config`,
   which starts vsftpd itself on a loopback port as a child of the agent. That child runs under
   `maran-agent.service`'s sandbox and never under this unit's, so no directive planned here can
   change its answer. The second layer restarts `maran-ftps.service`, asks `is-active`, and reads
   the `220` greeting on the control port. The greeting is sent before `AUTH TLS`, PAM, `chroot`,
   the session's uid change, the per-session namespaces and the data channel. So a directive that
   lets the daemon start and greet but breaks any of those passes both layers. The panel would
   report FTPS enabled while every login failed, and nothing would roll back. **The agent is not
   changed by this branch and none of this is a defect in it.** It is the reason no row may be
   decided on "the unit is active" or on the greeting, and why every row drives a real login,
   upload and `SITE CHMOD` over TLS (reviewer item 30).

The polygon images fingerprint `installer/**`, so any edit to this file makes every existing image
NOT CURRENT (reviewer item 16).

### Why this makes the surface safer, and what it does not make safe

Stated narrowly, because an overclaim here is the defect `rules/architecture.md` names:

- **Not a tenant door this change closes:** setting a setuid or setgid bit through `SITE CHMOD`.
  vsftpd already masks it (above), subject to row 0 confirming that on the shipped binaries.
  What `RestrictSUIDSGID` adds is a refusal **below** vsftpd, for every process of the unit: a
  compromised session child cannot create a setuid-to-the-account file, and a compromised root
  parent cannot create a setuid-root one. That closes one persistence path out of the many the
  last bullet below leaves open, and it is no more than that.
- **Shrunk for the tenant's session child and the pre-login process:** the kernel surface a bug in
  either can reach. The seccomp-backed directives (7–12) and the mount-backed ones (1–4, 13) apply
  to every process of the unit, not only the root parent.
- **Removed from a compromised root parent:** module loading, raw device and disk access, ptrace of
  other processes (the agent included), the clock, sysctls, the hostname, packet sockets, and
  every capability outside the enumerated set.
- **NOT closed, and this amendment does not pretend otherwise:** code execution in the privileged
  parent remains root-equivalent on the filesystem. Under the starting set the code phase writes,
  it keeps `CAP_DAC_OVERRIDE`, `CAP_CHOWN`, `CAP_FOWNER`, `CAP_SETUID` and `CAP_SYS_ADMIN`. After
  the measurement it is expected to keep `CAP_SETUID`, `CAP_SYS_ADMIN` and one of the two DAC
  capabilities ("Which directive is expected to hold"). Even `CAP_DAC_READ_SEARCH` alone reads
  every file on the host, `/etc/shadow` and the agent's keys included. Whatever survives the
  measurement, it runs as uid 0 and `ProtectSystem=` is off, so it can write `/etc` through
  ordinary ownership with no capability at all, and a cron entry or a unit file is persistence it
  still has. The
  `ProtectSystem=full` and `SystemCallFilter=` lines above are where that residual would be
  attacked next. `CAP_SYS_ADMIN` is held solely for `isolate` and `isolate_network`, and it is the
  broadest capability left: it alone re-grants `mount(2)`, so a root parent holding it can undo
  the mount-backed directives (1–4, 13) inside its own namespace. Trading it for the isolation is
  a configuration change owned by the agent's template and out of this unit's scope; the trade is
  named here so a reviewer weighs it rather than meets it.

### What "measured" means here: the check must be able to see the directive

- **The existing lanes cannot observe a single directive.** `ftps_on_a_real_host.rs` and the
  polygon images start vsftpd by running the unit's `ExecStart=` line directly under a
  `systemctl` shim (`agent/crates/agent/tests/fixtures/polygon_vsftpd.rs`, "UNOBSERVED HERE: the
  service manager"). They would stay green whatever the unit's `[Service]` section said, so no
  result from them is evidence for or against any row below.
- **So every row is measured under a real systemd as PID 1**, in throwaway containers named
  `maran-wf-92-installer-ftps-unit-hardening-*`, one per family. What the two polygon images carry,
  read 2026-10-26: `maran-polygon-ubuntu24:v3` has systemd `255.4-1ubuntu8.17`, vsftpd
  `3.0.5-0ubuntu3.1`, libpam0g `1.5.3-5ubuntu5.7`; `maran-polygon-alma9:v3` has
  `systemd-252-67.el9_8.2.alma.1`, `vsftpd-3.0.5-8.el9`, `pam-1.5.1-28.el9_8.1`,
  `openssl-libs-3.5.5-2.el9_8` (`dpkg-query -W` and `rpm -q` inside each). Those images are three
  weeks old and predate this branch; whichever image a row is measured in is named beside the row.
- **The container shape must first pass row 0 on each family, or no later row means anything.**
  The 2026-09-09 amendment above measured that on the RHEL family, inside a `--privileged`
  container, `pam_unix.so` answers `9 Authentication service cannot retrieve authentication info`
  for every login, with no Maran code involved. A systemd-as-PID-1 container is usually run
  privileged, so the AlmaLinux lane may be RED before any directive is added, for a reason that
  has nothing to do with the unit. If it is, the harness finds a shape in which row 0 is green
  (narrower grants than `--privileged`), names it here, and keeps it for every row. A directive
  that cannot be applied in that shape (a namespace systemd cannot create there) is UNOBSERVED,
  not rejected.
- **Applied-proof before session-proof.** A directive systemd ignored passes every session test.
  Unknown keys on an older systemd are warned and dropped, and a feature unavailable in a container
  may be skipped. So each row first observes the directive on the running main process:
  `/proc/<MainPID>/status` (`CapBnd`, `Seccomp`, `NoNewPrivs`), `/proc/<MainPID>/mountinfo`
  (read-only `/proc/sys`, private `/dev`), and `systemctl show -p <Directive>`. A row with no
  applied-proof is recorded as UNOBSERVED, never as survived.
- **The session**, per row and per family: a plaintext `USER` refused with `530` (forced TLS), then
  over TLS: login, upload, `LIST`, `SITE CHMOD 644` (must be ACCEPTED and the file must then be
  `0644`, the inverse control), `SITE CHMOD 4755` followed by a `stat` of the file on the host
  (row 0 records the mode that actually lands; vsftpd's source predicts `0755`, and if the shipped
  binary lands `4755` instead, that is a finding and is written here before anything else),
  logout. During the live session, the `Uid:` line of `/proc/<pid>/status` for every process in
  the unit's cgroup is recorded. The only uid-0 processes must be the `MainPID` and the pre-login
  parents of connections that have not yet logged in, which is what the upstream source predicts
  (see "Which processes are root at all"). A logged-in session with a root process is a finding,
  and the capability analysis above is then wrong. Then the mount namespace the directives
  introduce: **a login created after the daemon
  started** can log in (a jail bind mount made after start must propagate into the unit's
  namespace), **an account removed while the daemon runs** leaves no jail mount in
  `/proc/<MainPID>/mountinfo`, and the same removal **while a session of that account is still
  open** is measured once on the final unit, because a mount kept busy in the daemon's namespace
  is exactly the case in which propagation of the unmount can fail.
- **`RestrictSUIDSGID` is observed by a probe, not by the session**, since no FTP command passes a
  setuid bit to the kernel. The harness adds, in a drop-in that exists only inside the throwaway
  container and never in `installer/systemd/`, an `ExecStartPre=-` (the `-` so that its expected
  failure does not stop the unit) that tries to set `u+s` on a scratch file under the unit's own
  sandbox and logs the result: it must succeed on row 0 (the inverse control) and fail with
  `EPERM` from row 11 on. The journal line is the evidence, not the unit's state.
- **The isolation observation**, per row: the session processes' `ns/pid`, `ns/ipc` and `ns/net`
  differ from the main process's (see "The capability set"). A row that keeps the session green and
  loses this is RED.
- **The harness's own positive controls**, one per axis that can go blind, each applied once and
  each required to turn its axis RED before any row on that axis is believed:
  - the session axis: `CapabilityBoundingSet=` with every member of the starting set **except**
    `CAP_SYS_CHROOT`. With `chroot_local_user=YES` the session's `chroot(2)` (`sysutil.c:2500`) must
    then fail, so the login must fail;
  - the isolation axis: `RestrictNamespaces=yes`, under which the session must stay green and the
    namespace observation must flip;
  - the probe axis: row 0 against row 11, above.
  `NoNewPrivileges=yes`, which an earlier draft of this section used here, is not a control, for
  the reason given under "What stays off".
- **Before and after**, `systemd-analyze security --offline=yes` on the shipped file, recorded here.

### What a second reviewer must check (continuing the numbering above)

8. That every directive in the final unit has a row below showing applied-proof and a green
   session **on both families**, and that every directive that broke has a row naming the failure
   observed, with the directive absent from the unit.
9. That the measurements came from a systemd-launched daemon with applied-proof, not from a lane
   that runs `ExecStart=` directly. Ask to see the `CapBnd` and `Seccomp` lines.
10. That the running main process's `CapBnd` holds none of `CAP_SYS_MODULE`, `CAP_SYS_RAWIO`,
    `CAP_SYS_PTRACE`, `CAP_SYS_BOOT`, `CAP_MKNOD`, `CAP_NET_ADMIN`, `CAP_NET_RAW`, `CAP_SYS_TIME`,
    `CAP_BPF`, and that each capability retained carries its reason in the unit's comment.
11. That `ProtectHome=`, `NoNewPrivileges=`, `ProtectSystem=strict`, `PrivateTmp=` and
    `RestrictNamespaces=` are absent from the unit.
12. That the mount-propagation rows are present and green: a login created after the daemon
    started works, and a removed account's jail mount does not linger in the daemon's namespace.
    A jail that lingers there is a deleted customer's home still mounted inside a root daemon.
13. That on **both** families (libpam links libaudit on Ubuntu too, note c) PAM's audit path still
    works under `RestrictAddressFamilies` and the capability set: the journal carries no audit or
    `pam_unix` error for a successful login.
14. That PAM still **logs** under `PrivateDevices` and `ProtectKernelLogs`. A PAM that cannot reach
    `/dev/log` still authenticates, so "the login works" does not prove "the login is logged".
    The check is the journal line for the login, not the session.
15. That the unit's comment block no longer says "weighed, and deliberately not prescribed" over a
    unit that now prescribes, because a comment describing what the unit does not do is a defect
    (`rules/architecture.md`). The comment must say what each directive is for and point here.
16. That the `ExecStart=` line is byte-identical (the polygon assertion in
    `docker/polygon/asserts/parts/ftps.sh` parses it), and that the polygon images were rebuilt
    before any polygon count is believed: `installer/**` is part of the image fingerprint, so this
    edit makes every existing image NOT CURRENT.
17. That every directive used exists in the oldest systemd of a supported system (Ubuntu 22.04
    ships 249; Debian 12 and the EL9 family ship 252), checked against systemd's own
    documentation, because an unknown key is dropped with a warning and the unit then silently
    carries less than it says.
18. That the suspension cull (`ops::logins::end_account_sessions`) still ends a live FTPS session
    under the hardened unit.
19. That `CAP_SYS_ADMIN`'s row was decided on the namespace observation and not on the session,
    and that the final unit's session processes still sit in their own PID, IPC and network
    namespaces. vsftpd loses that isolation without a word when the capability is missing.
20. That the `CAP_DAC_OVERRIDE` and `CAP_DAC_READ_SEARCH` rows on the RHEL family ran with
    `/etc/shadow` at the stock `0000`, not at the image's `0400`.
21. That the `RestrictSUIDSGID` probe and every other harness drop-in are absent from
    `installer/systemd/maran-ftps.service`.
22. That the doubts this section records about the 2026-09-09 reasons for `ProtectHome=` and
    `NoNewPrivileges=` were weighed. The two stay off on the owner's decision; a reviewer may ask
    for them to be measured, and the note should not go on repeating those reasons as fact.
23. That vsftpd's `SITE CHMOD` masking holds on both shipped binaries (row 0). If it does not, the
    tenant door issue #92 describes is real, and `RestrictSUIDSGID` is the only thing closing it.
24. That `CAP_DAC_OVERRIDE` and `CAP_DAC_READ_SEARCH` were measured as a pair (row 14-dac), not
    only singly. Each masks the other, so two green single removals prove nothing about removing
    both, and dropping both breaks every login on a stock RHEL-family host.
25. That the file the `final` row measured is the file on the branch head: its recorded sha256
    equals `sha256sum installer/systemd/maran-ftps.service` at merge time. Any edit after that row,
    even to a comment, re-opens it.
26. That the unit still keeps every item in "The interface between parts" above: the name, the
    single byte-identical `ExecStart=` line with no `ExecStartPre=` or `ExecStartPost=`,
    `Type=simple`, the runtime directory and its mode, `WantedBy=`, and no `__TOKEN__`
    placeholder.
27. That the uid-0 census (note g) was taken during a **logged-in** session on both families, and
    that no logged-in session process was root. The whole argument for which capabilities the
    bounding set needs rests on that fact.
28. That no member of the starting capability set left the shipped unit on an expectation alone.
    Each removal must have a measured row behind it on **both** families.
29. That the `NoNewPrivs:` line of `/proc/<MainPID>/status` was recorded on every row, the single
    removal of `CAP_SYS_ADMIN` included, and that it reads `0` on the `final` row on both
    families. `NoNewPrivileges=` being absent from the unit (item 11) does not prove that
    `no_new_privs` is off, if systemd implies it (see "What stays off").
30. That no row was decided on `systemctl is-active` or on the `220` greeting alone. Those are
    the only questions the agent's enable path asks after a restart (interface item 8), and a
    directive that breaks TLS, PAM, the chroot or the data channel answers both correctly.
31. That the unit's comments mention the configuration path only as exactly
    `/etc/maran/vsftpd/vsftpd.conf` or `/etc/maran/vsftpd`. The polygon census
    (`docker/polygon/asserts/parts/ftps.sh:756-771`) fails the image build on any other
    `/etc/maran/vsftpd...` spelling in the unit, comments included.

### What the author cannot verify

- **Whether systemd implies `no_new_privs` for the seccomp-backed directives on a root unit.**
  The mechanism under "What stays off" is recalled from systemd's source and not read in this
  session. The systemd 255 manual here does not state it either way. Row applied-proof decides it.

- **A second reviewer.** This is the debt above, restated where a reader looking for gaps finds it.
- **A real host.** The systemd in these measurements runs as PID 1 in a container on
  the workstation's kernel (`7.0.0-38-generic`), not on Ubuntu 24.04's or AlmaLinux 9's own kernel.
  The seccomp filters and namespaces these directives create are kernel features, so a row that
  passes here is a strong indication for a booted host and not a measurement of one.
- **The other six supported systems** (Ubuntu 22.04, Debian 12, Debian 13, AlmaLinux 10, Rocky 9,
  Rocky 10) and **aarch64**. `SystemCallArchitectures=native` in particular is only exercised on
  x86_64 here.
- **SELinux enforcing on the RHEL family.** Unchanged from 2026-09-09: the containers do not run it.
  Two consequences are new. `NoNewPrivileges=` may be a real constraint there (the helper
  exec under "What stays off"), so its rejection is not shown to be unnecessary on that family.
  And the shadow read may be made by `unix_chkpwd`, a child of the pre-login root parent that
  inherits the same bounding set, so the `CAP_DAC_*` reasoning holds but the reader is the helper.
- **Commands the session does not drive.** Rename, delete, `MKD`, resumed transfers, `MLSD`, and
  graphical clients' sequences are not in the session above. A directive that breaks only one of
  them would pass every row.
- **Error paths.** A capability dropped because the green session never needed it may be needed on
  a path the session never takes (a failed chroot, a full disk, a TLS renegotiation).
- **vsftpd's own `seccomp_sandbox`.** Upstream turns it on by default (`tunables.c:236`), and the
  rendered configuration does not set it. Whether either family's patches change that default, and
  how its filter stacks with systemd's, is not determined here. A green row shows the two coexist
  on the session driven, nothing more.
- **The distributions' patches over vsftpd.** Every source fact in this section (`SITE CHMOD`
  masking, the `_failok` namespace forks, the defaults) was read in upstream 3.0.5, not in Ubuntu's
  or the EL9 family's patched source. Row 0 and the isolation observation measure the two that
  matter on the shipped binaries. The rest are expectations.
- **The RHEL family's audit path inside a container.** Whether the container's kernel audit
  socket accepts PAM's messages at all is a property of the container, not of the unit. If the
  journal shows no audit record on row 0, the `CAP_AUDIT_WRITE` and `AF_NETLINK` rows observed
  nothing and are recorded as UNOBSERVED.
- **`/etc/shadow` on a stock RHEL-family host.** Its `0000` mode comes from the `setup` package's
  file list and was read, not measured. The rows reproduce that mode in the container.
- **That no logged-in session keeps a root process.** This was read in upstream source
  (`twoprocess.c`, `postprivparent.c`), not in the distributions' patched source. The uid-0 census
  (note g) measures it on the two images and nowhere else.
- **How libpam treats a refused audit write as uid 0.** Whether it is an error or is ignored
  decides whether `CAP_AUDIT_WRITE` is needed. It was not read here, which is why that member is
  marked uncertain and stays in the unit until measured.
- **`systemd.exec(5)` side effects.** What each directive implies beyond its name was taken from
  the manual, not from systemd's source: that `PrivateDevices` drops `CAP_MKNOD` and
  `CAP_SYS_RAWIO`, and which nodes the private `/dev` holds. The applied-proof (`CapBnd`,
  `mountinfo`) is what confirms it per row.
- **The code-phase unit itself.** It is written before any measurement exists. Until the `final`
  row is green on both families, the hardened unit on this branch is a plan, and installing it on
  a real host is untested.

### Independent agent review of this amendment (code phase), and the decision on each finding

Five agent reviewers read the code-phase unit and this section, each through one lens: correctness,
security, rules, compatibility and design. **This is not the second review.** The agents are the same
kind of reader as the author, they measured nothing, and the human second review above stays
**OUTSTANDING**. Every lens approved with no blocking and no major finding. The findings below are
all minor. Each decision is recorded here and the fix is applied by the fixers of this stage, so
this section states the decision and not the diff. None of them closes issue #92.

**The security lens's checklist, as it answered.** Items 3 (no shell strings: `ExecStart` is an argv
list), 8 (nothing logged or exposed) and 10 (no new port, daemon or outbound call: the unit only
narrows the existing daemon) came out yes. Items 1, 2, 4, 5, 6, 7, 9, 11, 12 and 13 are not applicable
to a static unit file and a note: no input path, path handling, caller-supplied value, SQL, endpoint,
environment variable, dependency, rpc or module reach changed. The escalation item came out yes: the
note exists as a file, was written for this change, states the review OUTSTANDING, and its planned
rows 1-14 match the unit. The lens's caveat stands: this is a plan, not a measurement, and the
measured outcomes below are all NOT YET MEASURED.

| lens | finding | decision |
|---|---|---|
| correctness, minor | Four lines of this note are over 120 columns (one predates the branch); whether `.editorconfig` exempts Markdown was not checked. | Accepted. The fixers check the rule and wrap the lines it covers. |
| correctness, minor | All 14 directive groups are untested expectations; the measured outcomes and exposure score are unmeasured. | Accepted as the state of the branch, not a defect of the code phase. Phase B measures every row, deletes any failing directive from the unit, and fills the table; the `final` row's sha256 must equal the unit at the branch head. #92 stays open until then. |
| correctness, minor | The row 11 comment calls `RestrictSUIDSGID` "the kernel agreeing with" vsftpd's mode masking, which is an expectation until the probe confirms it. | Accepted. The unit's wording is softened to an expectation, and returns to a statement only if the row 11 probe confirms it. |
| security, minor | The unit says each directive "is measured" and carries no marker that none has been yet. | Accepted. The unit says the wording is the procedure and points at this note's measured outcomes table. After phase B it states which rows were measured. No merge before the table is filled. |
| security, minor | `CapabilityBoundingSet` keeps `CAP_SYS_ADMIN`, `CAP_CHOWN`, `CAP_FOWNER` and both DAC capabilities. `CAP_SYS_ADMIN` re-grants `mount(2)` and can undo the mount-backed directives for a root parent. | Accepted, with no change to the code phase: the trade-off is already stated above and the starting set is the issue's. Phase B carries out the single-member removals. Each capability measured removable is deleted from the unit and from its comment list. |
| security, minor | The `NoNewPrivileges=` claim about SELinux and `unix_chkpwd` is recalled from memory. | Accepted. The claim stays marked unverified in this note. The human second reviewer is asked to verify it against the PAM source. |
| rules, minor | Directives are unproven on either family. | Same decision as the second correctness finding. |
| rules, minor | The second review is OUTSTANDING, so the branch must not merge to `main`. | Accepted. It is a hand-off item, not a defect. The owner is told at hand-off. |
| rules, minor | The unit's sentence about its own mount namespace does not name the directives that cause it. | Accepted together with the design lens's finding on the same sentence: the unit names the mount-based directives or moves the sentence beside the first of them, worded as an expectation. |
| compatibility, minor | On an upgrade where FTPS is already running, `installer/lib/89-ftps.sh` places the unit and reloads systemd but never restarts the service, so the old unrestricted sandbox stays until the next restart, and nothing says so. | Accepted. The new directives take effect only at the next start or restart of `maran-ftps.service`. The installer deliberately does not restart a service it did not enable. This is stated in the interface section of this amendment. Rollback is restoring the previous unit file. |
| compatibility, minor | The unit's comment names Ubuntu 24.04 and AlmaLinux 9, but eight systems and two architectures are supported. | Accepted. The unit's comment says the other supported systems, aarch64 and SELinux enforcing are unmeasured and points here. Every directive used exists in the oldest supported systemd (249), so none is silently ignored. The gap stays named in "What the author cannot verify" and the second reviewer must accept it knowingly. |
| compatibility, minor | Several rows are compatibility-sensitive and unmeasured: the DAC capabilities with `/etc/shadow` at `0000`, SELinux enforcing, and `SystemCallArchitectures=native` and `MemoryDenyWriteExecute` on aarch64. | Accepted. Phase B states for each row whether it was observed on x86_64 in a container only. The second-review requirement and the no-merge condition remain. |
| design, minor | The own-mount-namespace expectation is a general claim above the directives, owned by no directive. | Accepted. Moved beside the first mount-namespace directive and worded as an expectation, so the unit stays true if that row is later deleted. |
| design, minor | This amendment is long and repeats reasoning the unit's comments also carry, so the two can drift. | Deferred. Trimming before measurement would delete the stated expectations the measurements are judged against. When the outcomes are filled in, duplicated expectation prose is trimmed and the unit's comments become pointers here. |

**What none of this review establishes.** No agent ran a build, a container or a real FTPS session.
The review checked that the unit and this note agree and that the rules are met. It did not check
that any directive holds.

### Measured outcomes (phase B, 2026-10-27)

Measured on the unit whose sha256 is `5bfa8cb8893e16253b6c1ab8f50188ef4f21d1a8164c78f64ce1fe9ffd84bef9`
(the file on the branch worktree at the time of writing; the `dev` unit is `185892d6…`). If the unit
changes again, every number here must be retaken. A "session" below is a real FTPS login with TLS
forced, an upload of 200000 random bytes, a passive-mode listing, `SITE CHMOD`, a download and a
logout, against a vsftpd run by the real unit under systemd as PID 1 in a privileged container
(Ubuntu 24.04, systemd 255, and AlmaLinux 9.8, systemd 252), on x86_64, on the workstation's kernel.
Every cell below is **x86_64, container only**.

The session client is `curl` pinned to TLS 1.2. The reason is a fault found while measuring and
recorded under "Findings" below: a TLS 1.3 transfer is not a valid signal.

| row | directive | Ubuntu 24.04 | AlmaLinux 9 | kept? |
|---|---|---|---|---|
| 0 | none (the `dev` unit): before-control | session green; `CapBnd 000001ffffffffff`, `Seccomp 0`, every namespace equal to PID 1 | session green; same | n/a |
| 0s | row 0 with `/etc/shadow` at `0000` | n/a | **NOT MEASURED** (see finding 5: the workstation's AppArmor profile denies the setuid helper `dac_override`, so the EL runs used `0400`) | n/a |
| +a | starting set minus `CAP_SYS_CHROOT` | RED as expected: login fails, upload not stored | RED as expected | no, by design |
| +b | `RestrictNamespaces=yes` | session green; session ipc and net namespaces stop being isolated from MainPID | session green; isolation UNOBSERVED (finding 3) | no, by design |
| 1 | `ProtectKernelTunables` | applied; probe: `/proc/sys` read-only, control writable | same | held |
| 2 | `ProtectKernelModules` | applied (read-back); probe VACUOUS (no `modprobe` in the image) | same | held, behaviour UNOBSERVED |
| 3 | `ProtectKernelLogs` | applied (read-back); no probe; `CAP_SYSLOG` already outside the bounding set | same | held, behaviour UNOBSERVED |
| 4 | `ProtectControlGroups` | applied; probe: `mkdir` in `/sys/fs/cgroup` refused | same | held |
| 5 | `ProtectClock` | applied; probe: `date -s` refused, control succeeds | same | held |
| 6 | `ProtectHostname` | applied; unit uts namespace differs from PID 1; probe: change refused, control changed it | same | held |
| 7 | `SystemCallArchitectures=native` | read-back and green session only; no non-native binary to try | same | held, behaviour UNOBSERVED |
| 8 | `LockPersonality` | applied; probe: `setarch -R` refused, control succeeds | same | held |
| 9 | `RestrictRealtime` | applied; probe: `chrt -f 1` refused, control succeeds | same | held |
| 10 | `MemoryDenyWriteExecute` | read-back and green session (TLS handshake included); no W+X `mmap` probe | same | held, behaviour UNOBSERVED |
| 11 | `RestrictSUIDSGID` | applied; probe: `chmod u+s` refused, control succeeds. The FTPS session cannot see it: `SITE CHMOD 4755` lands as `0755` with and without it (vsftpd masks the mode) | same | held; the session is blind to it |
| 12 | `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX AF_NETLINK` | read-back; transient `AF_UNIX`-only unit refuses TCP connect. `AF_PACKET` not probed | same | held |
| 13 | `PrivateDevices` | applied; private `/dev` has 18 entries and 0 block or loop devices (against 242-243 and 36 without); `/dev/log` journal line still reaches the journal (a bad-password PAM line appears) | same | held |
| 14 | `CapabilityBoundingSet`, the starting set of ten | `CapBnd 00000000202404cf` = chown, dac_override, dac_read_search, fowner, setgid, setuid, net_bind_service, sys_chroot, sys_admin, audit_write | same value | held, no member shown removable |
| 14-dac | `CAP_DAC_OVERRIDE`, `CAP_DAC_READ_SEARCH`: each alone, then both | session green, but `/etc/shadow` was `root:shadow 0640` so the pair is masked | session green, but shadow was `0400` so the pair is masked | **kept**; green here does not show them removable |
| final | the shipped unit, end to end | session green (login 230, upload identical, `LIST` 229 and 226, `SITE CHMOD 644` gives 644, `4755` gives 755, download identical, plaintext `USER` refused with 530) | same | `5bfa8cb8…` |

"Applied" means read from `/proc/<MainPID>` and `systemctl show`, not from the unit text. "Held" means
a positive control (a transient unit with the one property, and a control without it) behaved as the
directive says. A row marked UNOBSERVED has applied-proof only and is **not** shown to protect
anything.

- (g) Row 0 behaved as the source predicted: session green, `SITE CHMOD 4755` landed as `0755`, and
  the `u+s` probe succeeded on the unrestricted unit. The uid-0 census of the unit's cgroup during a
  held session was taken by the measurer, not tabulated here.
- (h) Row +b: the session stayed green and, on Ubuntu 24.04 only, the session namespaces stopped
  being isolated. That is the isolation axis's positive control and it is why `RestrictNamespaces`
  stays off and `CAP_SYS_ADMIN` stays in the set.

`systemd-analyze security`, before and after, all taken by `--offline=yes` or on the live unit:

| where | `dev` unit | branch unit |
|---|---|---|
| workstation, systemd 255, offline | 9.4 UNSAFE, 74 crosses | 5.9 MEDIUM, 41 crosses |
| Ubuntu 24.04 container, 255, offline | 9.4 UNSAFE | 5.9 MEDIUM |
| AlmaLinux 9 container, 252, offline | 9.6 UNSAFE | 6.1 MEDIUM |
| live unit, Ubuntu 24.04 | 9.6 UNSAFE, 75 crosses | 5.9 MEDIUM, 41 crosses |
| live unit, AlmaLinux 9 | 9.6 UNSAFE, 74 crosses | 5.9 MEDIUM, 41 crosses |

The score is an exposure heuristic that counts directives. It is not a measure that the daemon is
safer in practice; the rows above are. `systemd-analyze verify` and `systemctl show` on Ubuntu 22.04
(systemd 249), Ubuntu 24.04 and AlmaLinux 9 reported no unknown key, no unknown lvalue and no parse
failure for the branch unit, and `daemon-reload` was clean on all three. Ubuntu 22.04 got only that
parse and read-back, no session.

### Verification record, 2026-10-27

**Gates.** `maran check` reported 8/8 OK. `maran structure` printed STRUCTURE-OK on the worktree, and
again on an `rsync` copy of the unmutated tree. The branch adds three checks to
`scripts/lib/check-structure.sh` (numbers 28, 29, 30): the five documented rejections stay out of the
unit; there is exactly one positive, non-empty `CapabilityBoundingSet=` that names none of nine
dangerous capabilities; and `ExecStart=` is exactly the polygon's line with no pre or post hook and no
`__TOKEN__`. They read the unit text only (comments stripped, continuations joined), so they cannot
see runtime. The branch diff against `dev` outside the unit, this note, `CHANGELOG.md` and that script
touches no Rust, C#, proto, SPA or migration file, so `maran test`, `format`, `agent` and `polygon`
were **not run** and no row of `scripts/test-baseline.txt` was compared, accepted or moved. The
`ftps_on_a_real_host` baseline row (15 ignored) is unchanged by construction, not by a run. The
polygon images are NOT CURRENT after the unit edit (`installer/**` is in their fingerprint) and were
not rebuilt, so the polygon's build-time census over the unit was checked by reading only.

**Mutation proof for checks 28-30.** Fifteen mutations, each applied to its own `rsync` copy and run
through the full `maran structure`, each confirmed to differ from the base by `cmp`: `NoNewPrivileges`,
`ProtectHome`, `PrivateTmp`, `RestrictNamespaces` and `ProtectSystem=strict` added (check 28); the
capability lines deleted, a second `CapabilityBoundingSet=CAP_SYS_PTRACE`, a negated
`~CAP_SYS_MODULE`, and `CAP_SYS_PTRACE` appended to the continuation line (check 29); an
`ExecStartPre=`, `-obackground=NO` changed to `YES`, and an `__MARAN_X__` token (check 30); and three
breakages of the checks' own matchers, which the checks' positive controls caught. All fifteen were
killed by the named check. One parallel run also showed a check-16 violation in a pre-existing check;
the solo re-run gave exactly the one named violation, so that was a load flake and not a result. The
worktree was not touched by any mutation.

**Findings the measurements produced.**

1. The branch unit works under a TLS-forced FTPS session on Ubuntu 24.04 and AlmaLinux 9.8, and the
   exposure score fell by about 3.5 points (table above).
2. **Mount propagation has a precondition.** The jail is a bind mount created after the daemon
   starts. With the unit's own mount namespace, that mount reaches the daemon only if the host's root
   mount is shared. In the test containers `/` was private and the mount did not appear in the unit
   (host 1, unit 0), the login landed in an empty root-owned home and the upload failed. After
   `mount --make-rshared /` the mount propagated both ways (1, 1, then 0, 0 on unmount). Every systemd
   host sets `/` shared, so this is a harness condition and not a defect, but it is also the one fact
   the unit's mount-based directives rest on. The unit's wording "expected to propagate" is now
   measured, with that precondition, and the unit comment should be updated by its owner.
3. **The namespace isolation is observable on Ubuntu 24.04 only.** There the session processes get
   their own ipc and net namespaces; with `CAP_SYS_ADMIN` removed or `RestrictNamespaces=yes` they
   do not, and the login and upload stay green, which confirms the silent fallback. On AlmaLinux 9.8
   the session processes share MainPID's ipc and net namespaces even under the **unchanged** `dev`
   unit, so this note must not claim vsftpd isolation on the RHEL family. The `CAP_SYS_ADMIN` row is
   UNOBSERVED there.
4. **A TLS 1.3 transfer is not a valid signal.** vsftpd 3.0.5 with a TLS 1.3 client truncated about a
   quarter of 200000-byte transfers at 114688 bytes (curl rc 18) on both the `dev` unit and the
   branch unit and on both families (ubuntu24 `dev` 14 of 20 intact, branch 19 of 20; alma9 15 of 20
   both). TLS 1.2 gave 40 of 40 intact on all four combinations. It is independent of the hardening,
   and it deserves its own issue, which this stage did not open.
5. **Two harness traps unrelated to the unit.** On the RHEL family with `/etc/shadow` at `0000`,
   `unix_chkpwd` was denied `dac_override` by the workstation's AppArmor profile even with the
   container's profile unconfined (the journal shows an AppArmor DENIED line), so the EL runs used
   `0400`. And the mount propagation precondition in finding 2.
6. **Which of the 14 "expected to hold" rows are now observed.** Rows 1, 4, 5, 6, 8, 9, 11, 12
   and 13 held on a positive control. Rows 2, 3, 7 and 10 have applied-proof only. Row 14 is applied;
   its members are not shown removable.
7. **Item 29 answered.** `NoNewPrivs` reads `0` in `/proc/<MainPID>/status` for the shipped unit, with
   `Seccomp: 2`; with `NoNewPrivileges=yes` added as a control it reads `1`. So the seccomp-backed
   directives did not turn on `no_new_privs` here. The likely reason is that `CAP_SYS_ADMIN` stays in
   the bounding set, which lets systemd install a filter without it. That reason is an inference from
   the observation and was not tested by removing the capability and reading the value again.
8. **Capability removals.** Single removals of `CAP_DAC_OVERRIDE`, `CAP_DAC_READ_SEARCH`, both,
   `CAP_AUDIT_WRITE`, `CAP_CHOWN` and `CAP_FOWNER` left the session green on both families. That does
   not show them unneeded: the shadow file was readable without the pair, no audit record reached the
   container's journal (journald audit collection was off), and the session never exercised a chown
   or an owner change. **No member was removed from the unit**, and none is claimed removable.
   `CAP_SYS_CHROOT` is shown required (row +a). `AF_NETLINK`, `AF_UNIX` and `MemoryDenyWriteExecute`
   and `PrivateDevices` removals were also green, so those are kept on precaution, not on measurement.

**Incident, disclosed.** A control probe for `ProtectKernelTunables` (the run without the property)
wrote `1` to `/proc/sys/vm/swappiness` from a privileged container, which is the workstation's own
setting. It was restored to `60`, the value before, and verified. Container hostnames changed by the
`ProtectHostname` control were container-local, and every measurement container has been removed.

### What remains unverified after phase B

- **The second review is OUTSTANDING.** Nothing above is a review. The agent reviewers, the gates and
  the measurements were all made by agent sessions and do not discharge `rules/security.md`. A human
  second reviewer must read this amendment and the unit diff and record a verdict here. The branch
  **MUST NOT merge to `main`** before that, and the owner is told that this root, internet-facing
  surface carries an outstanding review.
- **The installer and the panel were not driven.** The hosts were built by hand from the shipped
  unit, the agent's golden `vsftpd.conf` (both key spellings), the installer's PAM stack, a
  self-signed certificate and a `mount`-made jail. The installer needs the network for beta artifacts.
  Neither the agent's mount unit nor the panel's enable and `ftp-users` endpoints, nor the agent
  restarting the unit through real systemd, was exercised. The install-polygon read-back guard (the
  proposed I-1, which would read systemd's own answer after install) and the S-4 guard (every measured
  directive still present) are **not written**; the polygon suite runs `ExecStart` directly with no
  service manager and is blind to the `[Service]` section.
- **The DAC pair on a stock RHEL host.** Row 0s and the pair removal with `/etc/shadow` at `0000`
  could not be observed on this workstation. Keep both capabilities.
- **SELinux enforcing, aarch64, and the six other supported systems.** Not measured. The container on
  the workstation's kernel is not a booted host.
- **Behaviour of rows 2, 3, 7 and 10,** and of `AF_PACKET`, and of `CAP_AUDIT_WRITE`, `CAP_CHOWN` and
  `CAP_FOWNER` on any path the session did not take.
- **Sessions not driven:** rename, delete, `MKD`, resumed transfers, `MLSD`, graphical clients,
  concurrent logins, the suspension cull, and a held session during unmount. Only an unmount with no
  session was measured.
- **The edit to the shipped unit.** This phase changed no unit line. The unit's own wording
  ("is measured", "expected to propagate", the RestrictSUIDSGID comment) predates these numbers and
  should be reconciled with the table above by the unit's owner, with the sha256 retaken if the file
  changes. `CHANGELOG.md` is modified in this worktree by another part and was not read here.
- **Issue #92 stays open** until the human review is recorded and the `final` row's sha256 is
  confirmed against the branch head.

## Amendment, 2026-10-29: enable waits for the control port, and a rollback removes what it wrote (issue #102)

What changed in `agent/crates/ops/src/ftps/`, and what an attacker could do with it:

- **Enable and TLS reload wait for the daemon to answer.** `systemctl restart` returns once the unit
  forks; vsftpd binds port 21 tens of milliseconds later, so the single probe that followed it failed
  and the enable rolled back on a healthy host. `wait_for_control_port.rs` now probes for the `220`
  greeting every 100 ms up to 10 s, on the host's injected clock. The bound is what keeps this safe:
  a daemon that accepts but never greets costs at most 10 s plus one 2 s probe, well inside the
  panel's 30 s operation timeout, and the probe still only connects to 127.0.0.1:21.
- **"Already converged" is no longer trusted on the config alone.** The early return now requires the
  daemon to be running AND answering; otherwise enable re-validates, rewrites (which restarts the unit)
  and waits. Before, a retry after a failed enable answered `enabled: true, running: false` as success,
  so an operator saw FTPS on while nothing listened.
- **A rollback with no previous config removes the config it wrote.** This reverses the earlier
  choice, recorded in `enable_ftps.rs`, to leave the rendered `vsftpd.conf` on disk after a failed
  first enable: that leftover file is exactly what made the next enable take the converged branch.
  The removal goes through the existing `FtpsHost::remove_file` seam on the agent-owned config path
  only; no caller-supplied path reaches it.

Not changed: the unit, its sandbox, PAM, the jail base, TLS settings. No new privilege, no new path
written. Not measured here: the real-systemd install lane (the polygon's `systemctl` is a shim), and
a crash-looping daemon still costs the full 10 s before `NotListening`.
