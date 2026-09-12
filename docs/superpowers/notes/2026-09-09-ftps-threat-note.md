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

`assert_ftps_pam_stack_requires_group_membership` (docker/polygon/assert-installer-steps.sh) said
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
`docker/polygon/pam-witness.c` is compiled inside each polygon image against that family's own
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
