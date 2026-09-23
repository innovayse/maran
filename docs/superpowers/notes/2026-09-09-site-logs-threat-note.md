# Threat note — a site's nginx logs move out of the customer's home

Date: 2026-09-09
Surface: the agent's privileged file surface (`ops::sites`), the nginx vhosts the agent renders,
and the installer's privileged steps.
Finding this discharges, from the privileged-surface audit: **F-1 (CRITICAL)** — nginx, running
as root, opens each site's log files inside a directory the customer owns, and a symlink planted
there is followed — proved on a polygon image, not reasoned about.

## Second reviewer: OUTSTANDING

`rules/security.md` ("Sensitive change escalation") requires a second human reviewer for a change
to the agent's privileged surface and to the installer's privileged steps. This change touches
both. **No second reviewer has read it.** The author is an agent session, which cannot dispatch
one, and an agent cannot be one: the whole point of the rule is that the author of a privileged
change is the person least able to see what they assumed.

Therefore, in the exact shape `rules/security.md` permits and names as a debt:

- This note is written **before** the change, and lands as a file.
- The change **may live on `fix/live-findings`. It MUST NOT merge to `main`.**
- The owner is told at hand-off that this surface carries an outstanding review.

### What a reviewer must check

1. That **no component** of the new log path is owned by, writable by, or traversable by a hosting
   account — including after the account has been recreated with a different uid, and including
   `/var/log/maran` itself, which this change does not create and does not re-argue.
2. That the **read** path (`ops::sites::follow_log`) now checks for **root** and not for the
   account, in **both** places it checks (the directory and the file), and that no third check was
   left pointing at the account's uid.
3. That the **migration** cannot itself be turned into an escalation: it runs as root, it reads
   files in `/etc/maran/nginx/sites` (root-owned), and it must not follow anything a customer
   planted. Check specifically that the only two things it does under `/home` are `lstat` and
   `readlink` on the old log path — neither of which follows a symbolic link or opens a file — and
   that it never opens, writes, renames, chowns or deletes anything there.
4. That **suspended** sites are covered — they render a different template through the same
   `SitePaths`, so a reviewer should confirm the derivation is genuinely shared and not duplicated.
5. That **logrotate**, newly introduced by this change, cannot be turned into the same defect. It
   runs as root over `/var/log/maran/sites/*/*.log`.
6. The two things the author could not verify, listed under "What this does not cover" below.

### What the author could not verify

- **SELinux on AlmaLinux.** `/var/log/maran` inherits `var_log_t`. The new subtree inherits it too,
  and nginx's `nginx_t` domain is permitted to write `var_log_t`. This was reasoned from the policy,
  **not** exercised against a host in enforcing mode — the polygon images do not run SELinux
  enforcing. A reviewer with an enforcing AlmaLinux host should confirm nginx can create and append
  to `/var/log/maran/sites/<account>/<domain>.access.log`.
- **AppArmor on Ubuntu.** Ubuntu ships no enforcing profile for nginx by default, so this was not
  exercised either. An operator who has enabled one must add the new path to it.
- **Whether an operator has already been serving from `~/logs` with a tool of their own** — a
  bespoke log shipper, a customer-facing stats generator. This change stops writing there. Nothing
  in this repository does that, but nothing in this repository can see an operator's own cron job.

## 1. The defect

### Where it was

`agent/crates/ops/src/sites/model/site_paths.rs` derived a site's two logs as

```
/home/<account>/logs/<domain>.access.log
/home/<account>/logs/<domain>.error.log
```

`create_site.rs` handed that directory to `SiteHost::create_directories_as_account`, which
(`process_site_host.rs`) forks and drops to the account's uid before calling `create_dir_all`. So
the directory was created **by the customer, as the customer, inside the customer's home**, and the
customer owned it and everything in it. `site_body.conf.j2` and `suspended_site.conf.j2` then
rendered `access_log`/`error_log` pointing there.

### What an unprivileged customer could do with it

nginx's **master** process runs as root — it must, to bind :80/:443 and to re-exec — and it is the
master, not a worker, that opens every `access_log`/`error_log` target. It opens them
`O_WRONLY|O_APPEND|O_CREAT` and **without `O_NOFOLLOW`**.

```
rm  ~/logs/example.com.access.log
ln -s /etc/ld.so.preload ~/logs/example.com.access.log
```

Then wait for, or cause, a reload. `CreateSite`, `EnableSite`, `DisableSite`, `DeleteSite`,
`InstallCertificate` and `UpdateSitePhpVersion` all end in one (`safe_write`'s `Reload`), and **one
nginx serves every tenant**, so any other customer's site action is enough. Root follows the link,
**creates the target if it is absent**, and appends to it from then on.

The appended bytes are the customer's to choose, because an access-log line embeds the request:

```
GET /home/<account>/evil.so HTTP/1.1
```

puts an attacker-chosen absolute path into `/etc/ld.so.preload`, which the dynamic loader honours in
**every process started on the host afterwards, root ones included**.

### The proof (quoted from the audit; run today, on `maran-polygon-ubuntu24`)

An unprivileged `probeuser` planted `~/logs/probe.access.log -> /etc/ld.so.preload`. A vhost was
written naming exactly what `site_body.conf.j2` renders. nginx was started as root. One request:

```
$ ls -l /etc/ld.so.preload
-rw-r--r-- 1 root root 107 ...
$ cat /etc/ld.so.preload
127.0.0.1 - - [.../...] "GET /home/probeuser/evil.so HTTP/1.1" 404 162 "-" "curl/8.5.0"
ERROR: ld.so: object '/home/probeuser/evil.so' from /etc/ld.so.preload cannot be
       preloaded (cannot open shared object file): ignored.
```

The file was **created by root**. The loader's own error line is the proof it is being read on every
subsequent `exec`. A customer who puts a real shared object at `~/evil.so` has code execution as
root in the next process any user on the box starts.

Variants needing no content control: point the error log at a file whose *existence* or *growth*
breaks the host, or at another tenant's path to make root create it.

### Why nothing already present stopped it

- `resolve_in_home` is applied to the **document root** only. The log paths were named by
  `SitePaths` and handed to a template; the agent never opened them, so no containment check ever
  saw them.
- `fs.protected_symlinks` does not engage. It covers symlinks in **sticky, world-writable**
  directories. `~/logs` is neither.
- Ownership checks could not fire: the account owning that directory was the design.
- The **read** path was already careful — `follow_log` pins the directory descriptor, uses
  `O_NOFOLLOW`, and checks uid and `nlink`. The **write** path is nginx's, and nothing guarded it.

### Why `chown root ~/logs` is not a fix, and was rejected

The account owns `~`. It can `mv logs logs.bak; mkdir logs` and own the new one. Ownership of a leaf
means nothing when somebody else owns its parent — the same lesson `/var/lib/maran-scratch`,
`/var/lib/maran-sftp` and `/var/log/maran` were each moved or re-owned for
(`docs/superpowers/notes/2026-09-05-backups-threat-note.md` §1;
`docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md`, section
`/var/log/maran`). **Containment has to come from the path being outside the home entirely.**

## 2. The fix

### 2.1 Where the logs live now

```
/var/log/maran/sites/<account>/<domain>.access.log
/var/log/maran/sites/<account>/<domain>.error.log
```

`AgentPaths::SITE_LOG_ROOT` and `AgentPaths::account_site_log_dir(account)`.

**This is deliberately inside `/var/log/maran` and not a fourth root-only sibling under `/var/lib`.**
The three earlier instances of this class were fixed by relocating to a sibling
(`/var/lib/maran-scratch`, `/var/lib/maran-sftp`, and the release staging directory), because in
each of those the offending ancestor was owned by the *panel* uid and nothing forced the child to
stay under it. `/var/log/maran` is different, and was already split for exactly this class of reason
one release ago: root owns the tree and appends the install log and the panel vhost's nginx logs
directly inside it, and the panel gets one subdirectory of its own,
`/var/log/maran/panel`, `panel:panel 0750`. Adding a **second root-owned subdirectory** for
customer-site logs is the same scheme continued, not a new one invented. Operators, `grep -r
/var/log/maran`, and the uninstaller all keep working with no new place to know about.

Owner and mode of **every** component:

| Path | Owner | Mode | Created by |
|---|---|---|---|
| `/` | `root:root` | `0755` | the distribution |
| `/var` | `root:root` | `0755` | the distribution |
| `/var/log` | `root:root` | `0755` | the distribution |
| `/var/log/maran` | `root:panel` | `0750` | `install.sh harden_log_directory` + `installer/lib/40-user.sh` (asserted by `assert_root_only_directory_with_mode … 750`) |
| `/var/log/maran/sites` | `root:root` | `0750` | `installer/lib/40-user.sh` (asserted by `assert_root_only_directory_with_mode … 750`) |
| `/var/log/maran/sites/<account>` | `root:root` | `0750` | **the agent, as root**, never a forked child |
| `<domain>.{access,error}.log` | `root:root` | `0644` | the nginx master, as root |

**Why no component is customer-reachable.** A hosting account is a member of its own group and of
nothing else — `useradd --user-group`, no supplementary groups (`fork_as_account` calls `setgroups`
with the account's gid alone). It is not in `panel`. So:

- `/var`, `/var/log` are `0755` root-owned: world-traversable but not world-writable, so no
  unprivileged uid can create, rename or unlink an entry in either.
- `/var/log/maran` is `0750 root:panel`: a hosting account matches neither owner nor group, so its
  permission bits are `other` = `---`. **A customer cannot even `stat` a path below this point**,
  let alone plant one.
- `/var/log/maran/sites` is `0750 root:root`: group is `root`, so even the *panel* uid — the
  product's own largest attack surface — matches only `other` = `---`. The panel process cannot
  enter this directory at all.
- `/var/log/maran/sites/<account>` is `0750 root:root`, created by the root daemon with an explicit
  mode (`DirBuilder::mode(0o750)`), not by `create_dir_all` under an inherited umask.

The account's log directory is created **as root** and no longer through
`create_directories_as_account`. That reverses this area's usual rule — *customer paths are touched
under the account's uid* — and the reversal is the point: this is no longer a customer path. The
rule exists so that root never follows a link a customer planted; here root creates a directory in a
tree no customer can write to, so there is no link to follow. The document root is unchanged and is
still created as the account.

### 2.2 How the customer still reads their own logs

**Through the panel, as before: `TailSiteLog`.** That is already how the product exposes site logs,
and it is the only mechanism the SPA has ever used.

`ops::sites::follow_log` was written against a hostile file — the whole module's opening comment is
an argument about a file the *account* controls. Every one of its protections survives the move
intact and now guards **root's** files instead:

- `open_directory` opens with `O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC` and then `fstat`s the descriptor.
  Its uid assertion changes from *the account's uid* to **`0`**.
- `open_log` reaches the file with `openat` **through that pinned descriptor**, with
  `O_RDONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC`, and refuses anything that is not a regular file, is
  not owned by the expected uid, or has `nlink != 1`. Its uid assertion likewise changes to **`0`**.
- The directory is pinned once and every later poll goes through the descriptor, so no rename of an
  intermediate component can redirect a running tail.
- The byte budgets (`HISTORY_BUDGET`, `FOLLOW_CEILING`) and the idle ceiling are unchanged.

`tail_site_log` no longer calls `resolve_in_account_home` for the log directory: the path is no
longer inside a home, so a home-containment check would be answering a question nobody is asking.
The containment that replaces it is the ancestor chain in the table above plus the two descriptor-
anchored checks. **These checks are now defence in depth rather than the primary containment**, and
that is worth saying plainly: a symlink at `/var/log/maran/sites/<account>/<domain>.access.log`
requires write access to a `0750 root:root` directory, which means root already. They are kept
because a directory that is out of reach today is out of reach only for as long as somebody keeps it
that way, and because they are what makes the regression test able to observe a refusal.

**What the customer loses, deliberately.** Until now an account could also read the raw files
directly over SFTP, because the account owned them and the home is bind-mounted into the SFTP jail.
After this change it cannot. That capability *was* the vulnerability: file ownership by the customer
is precisely what made root's write follow the customer's link. The panel tail replaces it. This is
an owner-visible product decision and is recorded as one in
`docs/superpowers/notes/2026-09-08-open-owner-decisions.md`. An operator who wants raw log files in
customer homes again must get them there by a copy the customer cannot influence — never by pointing
nginx at a path the customer owns.

### 2.3 Migration of existing installs

The danger on an existing host is **not** the `~/logs` directory. It is the **vhosts already on
disk**, in `/etc/maran/nginx/sites`, which still name a path under `/home`. Until those are changed,
the next reload re-opens the customer's directory and the escalation stands. Upgrading the binary
alone fixes nothing.

So the migration runs in the **installer**, as a new step (`installer/lib/81-site-logs.sh`,
`step_site_logs`), immediately after step 80 has established the nginx tree and before the finish
step. Not agent startup: the agent has no reconciliation pass today, adding one is a new privileged
loop that runs on every restart, and the installer is where every other one-time host-layout
migration in this product already lives (`harden_log_directory` is the direct precedent).

What the step does, in order:

1. Creates `/var/log/maran/sites` `root:root 0750` and asserts it with the existing
   `assert_root_only_directory_with_mode` gate. Refuses to continue if the path is a symlink or has
   the wrong owner or mode — `/var/log` is `root:root 0755`, so nothing unprivileged can have put
   anything there, and a refusal costs nothing next to reaching through somebody else's link.
2. Reads each `*.conf` in `/etc/maran/nginx/sites` — **`root:root 0755`, created by the installer,
   written only by the agent; no unprivileged uid can create or replace a file there** — and finds
   `access_log`/`error_log` directives naming a path under `/home/`. The account name is taken from
   that path and the domain from the directive's own file name. **Both come out of a root-owned
   file that root wrote**, and the account name is nevertheless held to the same grammar
   `AccountName` enforces before it becomes a directory name — not as defence against the customer,
   who cannot write that file, but so the step cannot invent a directory out of a vhost an operator
   hand-edited.

   The step's only contact with `/home` is `lstat` and `readlink` on the old log path, for the
   report in the next paragraph. Neither follows a symbolic link; neither opens a file. It opens,
   writes, renames, chowns and deletes nothing under `/home`, at any point.
3. Creates `/var/log/maran/sites/<account>` `root:root 0750` for each account it found.
4. Rewrites the two directives in place through the same render → stage → fsync → rename → validate
   → restore protocol step 80 already uses for the panel vhost, then runs `nginx -t` over the whole
   served tree and reloads. A refusal restores every file it touched and aborts the install, because
   a half-migrated tree is one where some vhosts still point into homes.

**What happens to an existing `~/logs`, and why that is safe.**

**Nothing. It is not deleted, not moved, not chowned, and not touched at all.**

The argument, stated because "leave it" is the kind of decision that looks like an omission:

- A customer's log files are the customer's data and the operator's record. Deleting them is
  destructive, is not reversible, and buys nothing — see below.
- A **symlink** the customer planted there is, after this change, just a symlink in the customer's
  own home, pointing wherever they like. It is followed only by processes running as **them**, with
  **their** privileges, which is a thing they could already do with `ln -s` anywhere else in their
  home. It was dangerous for exactly one reason: **root opened it.** Once no root process names any
  path under `~/logs`, the link is inert. Removing it would destroy evidence of an attempted
  escalation and would not close anything.
- Taking ownership of `~/logs` as root, or moving it aside, would be **worse than useless**: the
  account owns `~`, so it re-creates the name in one command, and the installer would then have
  spent a destructive write to produce a directory the customer controls again. That is the exact
  failure mode this whole change exists to reject.

What the step **does** do about it is **report**. For every vhost it migrated whose old
`access_log`/`error_log` name is currently a **symbolic link**, it emits a `SECURITY:` line naming
the account, the link and its target — the same shape and the same reason as
`harden_log_directory`'s warning:

```
SECURITY: /home/<account>/logs/<domain>.access.log is a SYMBOLIC LINK to <target>.
  The root nginx master opened that name on this server before this upgrade, so root
  writes may already have been redirected into that target. Nothing root opens carries
  this name any more. Inspect <target> before trusting this host.
```

It is **left in place, deliberately**, so the operator can see where it pointed. That is a judgement
call and the reviewer should challenge it if they disagree: the alternative is moving it aside as
`harden_log_directory` does, which there was necessary because root kept opening that name and here
is not.

**Blind spot, stated in the step's own output, as `rules/testing.md` requires.** The step can only
migrate vhosts that are **in** `/etc/maran/nginx/sites`. A vhost an operator hand-wrote into
`/etc/nginx/conf.d` naming a path under a home is not the agent's file and is not touched; the step
prints `UNOBSERVED HERE: vhosts outside /etc/maran/nginx/sites are not read or migrated` so the
sentence is in the install log rather than only in this note.

### 2.4 Log rotation

**There was none.** `grep -rn logrotate` across the repository before this change finds only prose:
a comment in `follow_log.rs` explaining why a short read is treated as a rotation, and three
mentions in notes and plans. **No logrotate policy, no installer step, nothing.** Site logs grew
without bound; the only thing that ever limited them was the account's disk quota, which no longer
applies now that they are outside the home. So "rotation follows the files" here means **rotation
is created**, at the new location, as part of this change — and that is a change in behaviour a
reviewer should see named rather than buried:

`installer/logrotate/maran-sites`, installed to `/etc/logrotate.d/maran-sites` as `root:root 0644`
by the same step:

```
/var/log/maran/sites/*/*.log {
    daily
    rotate 14
    missingok
    notifempty
    compress
    delaycompress
    create 0640 root root
    su root root
    sharedscripts
    postrotate
        [ -f /run/nginx.pid ] && kill -USR1 "$(cat /run/nginx.pid)"
    endscript
}
```

Security-relevant choices in it, each deliberate:

- **`su root root`.** logrotate refuses to rotate a directory it does not consider safe unless told
  whose privileges to use. Naming root explicitly means the rotation never runs under a uid that
  could be a customer's, whatever the directory's group becomes later.
- **`create 0640 root root`.** The rotated-in replacement is root's, with the same containment as
  the file it replaces. Not `0644`: nothing needs to read these but root and the agent.
- **`kill -USR1`, not `copytruncate`.** `USR1` makes the nginx master **reopen** its log files,
  which is the supported idiom and keeps `nlink == 1` true for the live file — the invariant
  `follow_log::open_log` asserts. `copytruncate` races the writer and loses lines. `follow_log`
  handles a reopen already: it sees `end < offset` and answers `Window::Restart`.
- **No `su` to a customer, no wildcard reaching under `/home`.** The glob is anchored at the
  root-owned tree, and every directory it can expand into is `0750 root:root`. logrotate running as
  root over a customer-owned directory would have been this very defect wearing a different hat.

### 2.5 A latent defect the move exposed — how the panel enumerates an account's sites

Not part of the fix as planned, found by running the suite, and recorded here because it is a
**security-relevant wrong answer** and a reviewer must know the marker changed.

`ops::sites::inspect_account_sites` answers "which vhosts does this host serve for this account,
and is each one the suspended stub?" — the question the panel uses to decide whether an account
really has been taken off the air. It decided membership by looking for the literal
`/home/<account>/` in each vhost's text.

A vhost's `root` directive does **not** reliably carry that literal. `resolved_site_paths` renders
the **canonical** document root — what `resolve_in_home` reports — so on a host where `/home` is a
symlink, or the homes are bind-mounted, the rendered text is the canonical path and not
`/home/<account>/`. Both layouts are ordinary. What was actually supplying that literal was the two
**log** paths, which were *named* rather than resolved.

So moving the logs out of the home made this enumeration return **nothing** on precisely those
hosts — and an empty list is the answer that reads as *every site is suspended*. An account whose
sites were all still serving would have been reported as serving none.

The marker is now `AgentPaths::account_site_log_dir(account)` with a trailing separator. It is
better on its own merits and not merely available: it is outside every home, so no home layout can
change its text; it is named and never resolved, because it is root-owned all the way up and there
is nothing to canonicalize away; and it appears in every vhost this agent renders, serving and
suspended alike. One account's marker is not a prefix of another's because of the trailing
separator, which the suite still proves (`acme` must not claim `acmecorp`).

**The defect was latent in the old marker**, not created by this change; the change is what made it
observable. A reviewer should check the new marker against a host with bind-mounted homes.

### 2.6 Suspended sites

`disable_site` renders `suspended_site.conf.j2`, which names `access_log`/`error_log` from the same
`SitePaths` the serving templates use. Because the derivation is **shared** — there is one
`SitePaths::for_site`, and `resolved_site_paths` is the one function every operation in the area
calls — a suspended site follows the move with no template change and no second decision to keep in
sync. That is the property to check, and the regression test checks it by suspending a site and
asserting the rendered vhost names the new root and nothing under `/home`.

The log directory is ensured to exist by `resolved_site_paths`, which every vhost-rendering
operation calls (`create_site`, `enable_site`, `disable_site`, `update_site_php_version`,
`install_certificate`, `remove_certificate`). This matters beyond tidiness: `nginx -t` **opens** the
error-log target, so a vhost naming a directory that does not exist fails validation and the site
operation fails with it. Putting the `mkdir` in the one place every operation already goes through
is what stops that being six chances to forget.

## 3. The proof that the fix works — the attack is the test

Four tests appended to `agent/crates/agent/tests/sites_on_a_real_host.rs`, `#[ignore]`d and
polygon-only like every other `*_on_a_real_host.rs` suite, run under real root on
`maran-polygon-ubuntu24` and `maran-polygon-alma9`.

They go into the EXISTING sites suite rather than into a new file, deliberately. `maran polygon
verify` **discovers** its suite list from `agent/crates/*/tests` and fails by name on a
`*_on_a_real_host.rs` that no `--test` argument names, so a new file is also a change to the
polygon lane's wiring and to `scripts/test-baseline.txt`'s suite list. These tests belong to the
sites area and the sites suite already has the account, nginx and vhost fixtures they need; a new
target would have bought a second copy of those and a lane change, for nothing.

The test **is the escalation**, not a restatement of the fix:

`a_customers_symlink_where_the_logs_used_to_live_no_longer_reaches_root`:

1. Create a real account through `AccountOperations::create`.
2. Create a real site through `create_site`, against the real nginx, and assert the rendered vhost
   names the new location and nothing inside the home — before the attack, so a failure there reads
   as "the paths did not move" rather than being inferred from a witness file.
3. **As that account, unprivileged** — through `fork_as_account`, the agent's own privilege drop,
   so nothing after the fork runs as root — re-create `~/logs` and plant
   `~/logs/<domain>.access.log -> /etc/maran-site-log-escalation-witness`.

   The witness is deliberately **not** `/etc/ld.so.preload`, which is what the original probe used
   and what a real attacker would use. A run that fails — which is the run this test exists to
   produce against unfixed code — would otherwise leave a live `ld.so.preload` in the polygon
   image naming a path inside a deleted account's home, and every process started in that
   container afterwards would carry the loader's complaint. Which file root is tricked into
   creating is irrelevant to the proof and very relevant to the blast radius of a red test. It is
   under `/etc` because that must be a directory root can write and the account cannot, so the file
   appearing can only be root's doing.
4. Trigger the reload path the way a customer can: create a second site — an ordinary site
   operation, on the one nginx that serves every tenant — then reload.
5. Assert the witness path **was not created**.

And, because a test that passes because the reload never happened proves nothing, the same test
carries a **positive control** in the same run:

- assert the RUNNING master is serving this vhost, by fetching the page over a real socket and
  requiring the account's own body back. `nginx -T` would not do: it starts a NEW nginx process to
  dump the configuration and says nothing about what the master that is already running has
  loaded. A 200 carrying the page can only come from the live master, which is the only thing that
  proves the reload was a reload and not a no-op;
- assert a request to the site produced a log line **at the new location**,
  `/var/log/maran/sites/<account>/<domain>.access.log`, so "root wrote nothing anywhere" is excluded
  as an explanation for the witness's absence.

Three further tests in the same suite:

- `every_directory_the_site_log_tree_is_made_of_belongs_to_root` — **ownership and mode of every
  directory the fix creates**: `/var/log/maran/sites` and `/var/log/maran/sites/<account>` are each
  a real directory, `root:root`, `0750`. Checked with `symlink_metadata` and not `metadata`,
  because a symbolic link to a root-owned `0750` directory satisfies every check made on the
  followed target while being exactly what this change exists to refuse. It also asserts the
  account owns neither, and — the other direction — that the **document root is still the
  account's and still inside the home**, so the fix is shown to have moved the logs and nothing
  else.
- `a_symlink_planted_at_the_new_log_location_is_refused_by_the_tail` — the link has to be planted
  as **root**, because the tree is `root:root 0750` and no hosting account can reach the name at
  all. That is the honest statement of what the tail's `O_NOFOLLOW` is now worth: defence in depth.
  Defence in depth that no test can observe is decoration, so the test manufactures the only state
  that exercises it and asserts `TailSiteLog` answers `LogUnreadable`, delivers zero lines, and
  creates nothing at the target.
- `a_suspended_sites_logs_move_with_the_serving_ones` — see §2.5.

**The test is proved able to fail.** One line of `SitePaths::for_site` is reverted —
`AgentPaths::account_site_log_dir(account)` back to `home.join("logs")` — in an **rsync snapshot
outside the repository**, and the suite is re-run in the polygon against that snapshot. It
reproduces the escalation, by name, with the witness file created by root. The named failure and
both runs' collected totals were quoted in a working report kept only as a scratch file, not
committed.

## 4. What this fix does NOT cover

Stated because a threat note that lists only what it closes is the kind that ages into a false
assurance.

1. **Every other root write near a customer.** This closes the `access_log`/`error_log` instance.
   The audit swept for siblings and found none — php-fpm's session and upload directories are
   created as the account and its pool names no log file, the cron `.log`/`.exit` files are written
   by the account's own shell, and the crontab staging file is in root's `0700` scratch — but that
   sweep is a snapshot. **A new root process that opens a path under `/home` reopens this defect**,
   and no mechanical gate in this repository would catch it today.
2. **Disk accounting.** Site logs used to count against the account's quota. They no longer do:
   they are on the operator's `/var`. A customer generating traffic now fills `/var/log`, and
   logrotate's 14-day/compressed window is the only bound. An operator on a small VPS should size
   that window down. **This is a new denial-of-service surface, traded for the escalation, and the
   trade is deliberate** — a full `/var/log` is an operational problem, a root-writable symlink is
   a root compromise.
3. **Log content is still attacker-chosen.** A request line is whatever the internet sent. It is
   now written only into a file whose path no attacker chose, but anything that later *parses* these
   logs must still treat every byte as hostile. `follow_log` already decodes lossily and never
   interprets.
4. **Account deletion does not remove `/var/log/maran/sites/<account>`.** Left deliberately: the
   logs are the operator's record of what that account served, and the directory is `root:root
   0750`, so an account recreated under the same name — with a different uid — inherits a directory
   no uid but root has ever been able to write. It is litter, not a hole. An operator who wants it
   gone deletes it, and `uninstall.sh` already removes `/var/log/maran` wholesale on request.
5. **The vhosts of an operator who hand-writes their own** (see the blind spot in §2.3).
6. **`/var/log/maran` itself is not re-argued here.** Its `root:panel 0750` ownership and the
   reasoning behind it belong to
   `docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md`, section
   `/var/log/maran`. This change depends on that being correct and does not re-verify it.
7. **Nothing was verified under SELinux enforcing or an AppArmor profile** — see "What the author
   could not verify".

---

## Correction, 2026-09-09 — the service group is `maran`, not `panel`

Added by a verification pass over every threat note on `fix/live-findings`, which found this
note MOSTLY VERIFIED with one factual error. The ownership table
in §2.1 and the prose under it name the panel's system user and group `panel`
(`/var/log/maran` as `root:panel`, `/var/log/maran/panel` as `panel:panel`). That account and
group were renamed on this branch, in the same working tree: `installer/install.sh` sets
`MARAN_USER=maran` and `MARAN_GROUP=maran`, and `installer/lib/40-user.sh` installs
`/var/log/maran` as `root:$MARAN_GROUP 0750`. See
`docs/superpowers/notes/2026-09-09-service-account-rename-threat-note.md`.

**The argument is unaffected** — a hosting account is a member of its own group and of nothing
else, so it matches neither owner nor group whatever that group is called, and every mode in the
table is unchanged. Only the names are wrong, and they are wrong on the page a reviewer checks
against a real host first.

**Addendum, 2026-09-10 — the same names, two places further on.** The correction above names §2.1's
ownership table and the prose under it; the spelling also survives in residual risk 6
(`/var/log/maran`'s "`root:panel 0750` ownership") and in the parenthesis closing this note. Every
occurrence is listed by `grep -n "panel:panel\|root:panel" docs/superpowers/notes/2026-09-09-site-logs-threat-note.md`,
and the true values by `grep -n "install -d.*var/log/maran$" installer/lib/40-user.sh` —
`/var/log/maran` is `root:maran 0750` and `/var/log/maran/panel` is `maran:maran 0750`. The
directory NAME `/var/log/maran/panel` is not a stale spelling: it is the panel's own log
subdirectory and the installer still calls it that.

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
