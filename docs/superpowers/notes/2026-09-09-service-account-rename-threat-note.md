# Threat note — renaming the panel's system account from `panel` to `maran`

Date: 2026-09-09. Branch: `fix/live-findings`.
Surface: **the installer's privileged steps** and **the identity every file the product owns is
owned by**, on hosts that are already installed.
Written BEFORE the change, as `rules/security.md` "Sensitive change escalation" requires.

## Second reviewer: OUTSTANDING

**This change has had no second reviewer, and no agent session can be one.** The requirement is
recorded here as OUTSTANDING, which means:

- The change **may land on this branch; it MUST NOT merge to `main`** while this line stands.
- The owner is told at hand-off that `installer/` is carrying an outstanding privileged review.
- A note recording the debt is not the discharge of it.

**What a reviewer must check** (in this order, because each later item assumes the earlier one):

1. That `maran` is an acceptable public name for a system account and group on a customer's
   server, and that the product is willing to own that name in `/etc/passwd` forever. A rename
   is cheap once; it is not cheap twice, because the second one migrates hosts that carry data.
2. That the adoption guard in `installer/lib/40-user.sh` refuses the account it must refuse. The
   guard is a GECOS marker, which is **root-writable**: it proves "an installer of this product
   created this account", not "no attacker chose this name". See "Residual risk" below.
3. That the migration in `installer/lib/15-identity.sh` is correct **under interruption**, which
   is the property most likely to be wrong: each of its four renames is separately guarded, and a
   run interrupted between any two of them must converge on a re-run. The reviewer should kill the
   installer between each pair on a throwaway host and re-run it.
4. That the deliberate outage window (below) is acceptable, and that no shorter design was
   rejected for the wrong reason.
5. That the PostgreSQL role rename is safe on a host with a live connection pool, and that
   `pg_hba.conf` and `/etc/maran/panel.env` cannot be left disagreeing about the role name.

**What the author could not verify:**

- Anything on a **real upgraded server**. Every measurement here was taken in polygon containers
  built from `docker/polygon/*.Dockerfile` (one per family). There is no long-lived installed host
  in this environment to upgrade, so the migration was exercised against a synthetic legacy state
  planted by the assertion (a `panel` user and group, the layout owned by them, a `panel`
  PostgreSQL role and a `panel.env` naming it) rather than against a host that has been running
  the panel for months.
- The behaviour of `usermod`/`groupmod` on every supported distribution's exact shadow-utils
  build. It was measured on the two polygon families (Ubuntu 24.04, AlmaLinux 9) only.
- Whether any operator has scripts of their own that name `panel` (a sudoers rule, a monitoring
  check, a backup script). Nothing in this repository can see those, and the migration cannot fix
  them. This belongs in the release notes.

## 1. The defect this change closes

`installer/lib/40-user.sh` created the account the API runs as with the bare name `panel`, and
`create_panel_user` was idempotent by `id -u` alone:

```sh
if id -u "$MARAN_USER" >/dev/null 2>&1; then
  echo "User '${MARAN_USER}' already exists."
else
  useradd --system ... "$MARAN_USER"
fi
```

**On a host that already has an account called `panel` — another control panel, a local service, a
site's deploy user, a company policy that names one — the installer prints "User 'panel' already
exists" and then proceeds to:**

- `install -d -o panel -g panel -m 0750 /var/lib/maran` — hand that account the panel's state
  directory, which holds the DataProtection keys;
- `install -d -o root -g panel -m 0750 /etc/maran` and `chown root:panel /etc/maran/panel.env`
  (mode 0640) — hand that account read access to the configuration file holding the database
  connection and the token-signing key;
- `chown root:panel` the panel's TLS private key (`installer/lib/80-nginx.sh`);
- `Group=panel` on `maran-agent.service`, so the agent's unix socket — the door to the ONLY root
  process on the server — is group-readable by that account, and `--allow-uid <its uid>` is
  written into the agent's own command line, so `SO_PEERCRED` **admits it by uid**;
- `User=panel` on `maran-api.service`, so the API *runs as* that principal.

That is not a naming inconvenience. It is the installer granting an unrelated local principal the
panel's identity, its secrets, and an admitted uid on the root daemon's socket. Anything that
account could already do — a cron entry, a PHP-FPM pool, a login — becomes a way to drive the
agent. The probability is not negligible: `panel` is precisely the name a *competing* control
panel or an in-house tool is most likely to have taken, and those are the hosts an operator
installs this product onto.

A secondary consequence: `panel` is the only name this product puts on a host that does not say
whose it is. Every other one is namespaced — groups `maran-sftp`, `maran-ftps`, directories
`/var/lib/maran`, `/var/lib/maran-sftp`, `/var/lib/maran-ftps`, `/var/log/maran`, `/var/backups/maran`,
unit `maran-agent.service`. An operator reading `ls -l /etc/maran` months later learns nothing from
`panel`.

## 2. The name

Chosen: **user `maran`, group `maran`**, created together by `useradd --user-group` as today.

Rejected: `maran-panel`, `maran-api`, `maranapi`, keeping `panel`.

The argument, on the three surfaces an operator actually reads:

- **`ps`.** `ps aux` prints the USER column at a fixed width and truncates a longer name with a
  `+` — `maran-panel` shows as `maran-p+`, which is worse than uninformative because it is
  ambiguous with any other `maran-*` principal (`maran-sftp`, `maran-ftps`) the operator may be
  hunting. `maran` is five characters and never truncates anywhere.
- **`ls -l` and the unit files.** `maran` sits at the head of the family the product already
  established: `maran` owns the product's own state, `maran-sftp` and `maran-ftps` are the
  capability groups it hands to customer logins. That reads as one scheme with one owner; a
  `maran-panel` beside `maran-sftp` reads as three peers, one of which happens to be privileged.
- **PostgreSQL.** This is the decisive one. `installer/lib/30-postgresql.sh` names the database
  role after the OS user *on purpose*, because unix-socket connections use **peer authentication**
  (the role name must equal the OS user name) and that avoids a `pg_ident.conf` mapping to
  maintain. A hyphen is not a bare SQL identifier: `CREATE ROLE maran-panel` is a syntax error,
  and every statement in the installer and the uninstaller (`CREATE ROLE`, `CREATE DATABASE …
  OWNER`, `ALTER ROLE … RENAME TO`, `DROP ROLE`) would have to grow quoting, in a script that
  interpolates the name unquoted today. A name that forces quoting into SQL that is assembled by
  string interpolation is a name that will eventually be interpolated without the quotes.

**User and group share the name.** They already do (`--user-group`), and the group is not a private
group in name only — it is the product's read grant: `/etc/maran` `root:maran 0750`, `panel.env`
`root:maran 0640`, the TLS key `root:maran`, `/var/log/maran` `root:maran 0750`, `/run/maran`
`root:maran 0750`, and the agent socket `root:maran 0660`. Splitting the names (user `maran-api`,
group `maran`) would buy nothing and would put two names where the reviewer now has to check that
the right one is on each line.

**Not renamed, deliberately, and why:** `/etc/maran/panel.env`, `/var/log/maran/panel` and
`Firewall__PanelPort`. Those name *the panel* — the web application — and not the account. They are
read by `maran-api.service`'s `ReadWritePaths=`, by `agent/crates/agent-core/src/agent_paths.rs`,
by the agent's real-host suites and by the polygon; renaming them is a separate change with its own
risk and no security content. Leaving them is not an oversight: the word "panel" is correct there.

## 3. Adoption must stop being accidental — what replaces `id -u`

`create_service_user` (renamed from `create_panel_user`) now distinguishes three states:

1. **No account and no group of that name** → create them, stamping a marker in the GECOS field:
   `useradd --system --no-create-home --shell /usr/sbin/nologin --user-group --comment "Maran panel service account"`.
2. **An account exists whose GECOS is exactly that marker** → adopt it silently. This is the
   re-run case, and it is how **idempotence survives**: the second run of the installer over its
   own first run sees its own marker and proceeds exactly as before. Idempotence is now bought by
   *recognition* rather than by *name collision*, which is the difference between this and the
   defect.
3. **An account (or a group) of that name exists that this installer did not create** → the step
   **refuses and the install stops**, naming the uid, the shell, the home directory and the GECOS it
   found, so the operator can see whose account it is. The escape hatch is deliberate and explicit:
   re-running with `MARAN_ADOPT_EXISTING_USER=1` in the environment adopts it, after printing the
   full identity being adopted. There is no way to reach adoption by accident.

The group is checked separately from the user, because a group with no user of the same name makes
`useradd --user-group` fail late and confusingly; and because a pre-existing *group* named `maran`
is the same grant problem in miniature — everyone in it would gain read on `panel.env` and the
agent socket.

**Why a GECOS marker and not a uid range, a shell check or a "does it own /var/lib/maran" test.**
A uid range says only that somebody's `useradd --system` made it. A nologin shell is what a
hardened service account looks like, so it is the *most* likely shape for a foreign account of this
name. "Owns `/var/lib/maran`" is circular on a fresh install (nothing owns it yet) and is exactly
the state the defect produces on a re-run. The marker is the only property that is set by *us*, at
creation, and read back later.

## 4. The migration, and what it does not have to do

Enumerated by reading the installer, not from memory. What the `panel` account owns or is named by
on an installed host:

| Path / object | Ownership or reference | Written by |
|---|---|---|
| `/etc/maran` | `root:panel 0750` | `40-user.sh`, `60-config.sh` |
| `/etc/maran/panel.env` | `root:panel 0640` | `60-config.sh` |
| `/etc/maran/tls`, `panel.crt`, `panel.key` | `root:panel 0750` / `root:panel` | `80-nginx.sh` |
| `/var/lib/maran` (DataProtection keys) | `panel:panel 0750` | `40-user.sh` |
| `/var/log/maran` | `root:panel 0750` | `install.sh`, `40-user.sh` |
| `/var/log/maran/panel` | `panel:panel 0750` | `40-user.sh` |
| `/run/maran` (and the agent socket in it) | `root:panel 0750`, socket `root:panel 0660` | `40-user.sh`, `maran-agent.service` `Group=` |
| `/run/maran-api` (the API's listening socket dir) | `2710 panel:<web group>` | `maran-api.tmpfiles.conf` |
| `maran-api.service` | `User=panel`, `Group=panel` | `70-services.sh` |
| `maran-agent.service` | `Group=panel`, `--allow-uid <panel uid>` | `70-services.sh` |
| PostgreSQL role `panel`, owner of database `maran` | peer auth | `30-postgresql.sh` |
| `/etc/postgresql/*/main/pg_hba.conf` (or `/var/lib/pgsql/data/`) | `local maran panel peer` | `30-postgresql.sh` |
| `/etc/maran/panel.env` line `Database__Username=panel` | the API's connection string | `60-config.sh` |

**The migration is a rename, never a re-creation, and that is the whole design.** `usermod -l` and
`groupmod -n` preserve the uid and the gid, so **every file in the table above keeps its owner
without a single `chown`**: only the name printed for that uid changes. A migration that created
`maran` and chowned the tree would have to enumerate that table correctly and would break anything
it missed; this one cannot miss a path it does not know about, including paths a future step adds
and paths an operator created by hand.

`installer/lib/15-identity.sh` (`step_identity`), run immediately after preflight and before every
step that uses the name, in this order:

1. **Decide whether this host is a legacy Maran install at all**, by observation rather than by the
   name alone: the account `panel` exists **and** `/var/lib/maran` is owned by exactly that uid and
   gid. A foreign `panel` on a host with no Maran install is not touched — it is left for step 40's
   refusal to report, which is the correct place for it.
2. **Stop `maran-api.service`** if the unit exists. This starts the outage window (§5) and is not
   optional: the unit on disk still says `User=panel`, so the moment the account is renamed the
   running service is a process whose configured user no longer exists, and its PostgreSQL
   connection string still names the old role.
3. **`groupmod -n maran panel`** — only if group `panel` exists and group `maran` does not.
4. **`usermod -l maran -c "Maran panel service account" panel`** — only if user `panel` exists and
   user `maran` does not. The `-c` stamps the adoption marker, so step 40 later adopts it as its
   own rather than refusing it.
5. **`ALTER ROLE panel RENAME TO maran`** — only if role `panel` exists, role `maran` does not, and
   the role owns database `maran`. (`ALTER ROLE … RENAME` clears an md5-stored password; this role
   has none, it authenticates by peer.)
6. **Rewrite `Database__Username=panel` to `maran` in `/etc/maran/panel.env`** — only if that exact
   line is present. Written to a temporary file inside `/etc/maran`, chowned `root:maran 0640`, and
   `mv`d over the original, so a reader never sees a partial file and the file never leaves the
   directory it is protected by.

`pg_hba.conf` needs no step of its own: `30-postgresql.sh` rewrites it from `MARAN_DB_ROLE`, which
is now `maran`, on this same run — after step 15 has already renamed the role.

**Interruption.** There is no "migration done" flag; each of the five actions is guarded by the
state it changes, so each is individually idempotent and the set converges from any prefix:

| Interrupted after | State on disk | What a re-run does |
|---|---|---|
| 2 (stop) | account still `panel`, API down | performs 3–6 |
| 3 (groupmod) | user `panel` whose primary group is `maran`; gid unchanged so no file changed owner | 3 is skipped (no group `panel`), 4–6 run |
| 4 (usermod) | OS user `maran`, PG role `panel`, env says `panel` | 4 is skipped, 5–6 run |
| 5 (role rename) | OS and role agree, env still says `panel` | 6 runs |
| 6 (env rewrite) | fully migrated | everything is skipped; step 40 adopts by marker |

In every one of those states the panel is **down** and the files are **intact** — nothing in the
migration can lose data, because nothing in it deletes, chowns or recursively touches anything. The
recovery from any of them is the same: run `installer/install.sh` again. The step prints that
sentence before it stops the service, so an operator who interrupts the run reads the instruction
in their own scrollback.

## 5. The outage window — it exists, it is deliberate, and here is its length

**Yes, there is a window.** It opens when step 15 stops `maran-api.service` and closes when step 70
restarts it. Between them run: dependencies (20), PostgreSQL (30), the user and layout (40),
**artifact download and verification (50)**, configuration (60). On an upgrade the window is
dominated by step 50 — the release archive download and signature check — so it is **as long as the
download takes**: seconds on a fast link, minutes on a slow one. **UNMEASURED HERE:** no host in
this environment runs an installed panel, so the wall-clock window has not been observed on a real
upgrade; what was measured is that the migration step's own work is four `usermod`/`groupmod`/SQL
statements and a file rewrite, so it contributes nothing to the length. The window is the rest of
the install.

Why this and not something shorter:

- **The window cannot be avoided by ordering alone.** The running API holds `Database__Username=panel`
  in memory from its own start-up; the instant the OS account is renamed its *new* PostgreSQL
  connections fail peer authentication, and `pg_hba.conf` stops naming its role a moment later.
  Whatever the order, a running old API stops being able to reach its database at the rename.
- **A stopped unit is a better failure than a running one.** The alternative (leave it running) is
  a panel that is `active (running)` while every request 500s, which is the state an operator is
  least able to diagnose. `inactive (dead)` with a printed reason is honest.
- **It is NOT required by `usermod`, and an earlier draft of this note said it was.** Measured on
  both polygon families with a live process owned by the account
  (`setpriv --reuid panel --regid panel sleep 300 &` then `usermod -l maran panel`): **exit 0**, uid
  and gid unchanged, every file following the rename. The correction is recorded rather than
  quietly removed, because the wrong version was a reason invented for a choice already made — the
  shape `rules/architecture.md` names as the thing that stops the next reader looking. The stop is
  kept on the argument above, which stands on its own; it is not load-bearing for the rename.
- **Rejected: patch `User=`/`Group=` in the installed unit files inside step 15, restart the API
  there, and let step 70 re-render them properly.** That would shrink the window to seconds. It was
  rejected because it puts a second authority for the identity into a `sed` over a generated file:
  a half-applied edit leaves a unit naming an account that *exists*, which starts and then fails in
  a way nothing looks at, and it is exactly the "two copies of one value" shape this repository
  keeps paying for. If the window turns out to matter on real hosts, the right fix is to move the
  artifact download before the migration, not to duplicate the identity.

## 6. Residual risk

1. **The GECOS marker is root-writable.** An attacker who can already write `/etc/passwd` is root
   and does not need this. But a *lower* bar exists in theory: an operator's automation that
   creates a `maran` account and copies our comment string would be adopted. The marker proves
   provenance against accident, not against a determined local root. Stated so the reviewer does
   not read it as an authentication.
2. **`MARAN_ADOPT_EXISTING_USER=1` is a real bypass.** It is one environment variable away from the
   old behaviour. It exists because refusing with no way forward turns a rename into an
   un-installable product on a host where the operator genuinely wants us to use their account. It
   prints what it is adopting first.
3. **A host with BOTH a legacy `panel` (ours) and a foreign `maran`** migrates into the collision:
   step 15 refuses to rename onto an existing name (guard 4), and step 40 then refuses the foreign
   `maran`. The install stops with both facts printed. This is the correct answer and it is a dead
   end for the operator until they rename their own account — say so in the release notes.
4. **Operator-side references to `panel`** — sudoers, monitoring, log pipelines, backup scripts —
   are invisible to us and are not migrated. Release notes.
5. **The window in §5 is a denial of service the operator opts into**, and an interrupted install
   extends it indefinitely. It is not an escalation: nothing gains privilege while the API is down.
6. **What this change does NOT alter:** no mode, no ownership relationship, no group membership, no
   `ReadWritePaths=`, no socket permission, no `SO_PEERCRED` policy. The `--allow-uid` value on the
   agent is the same uid before and after, because the rename preserves it. The security boundary
   is exactly the one argued in
   `docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md` and
   `docs/superpowers/notes/2026-09-03-panel-socket-threat-note.md`; only the name printed for the
   principal changes.

## 7. What proves it

`docker/polygon/assert-installer-steps.sh`, at image build time on both families:

- the directory-layout table now resolves `maran`'s uid and gid (it resolved `panel`'s), with its
  eleven-path vacuity guard unchanged;
- `assert_the_service_account_name_has_one_authority` — `40-user.sh` and `15-identity.sh` must
  agree on the name, so the migration can never rename onto a name step 40 does not create;
- `assert_a_foreign_account_at_the_service_name_is_refused` — plants an account named `maran`
  with a foreign GECOS, runs step 40's own `create_service_user`, and requires it to REFUSE;
  then stamps the marker and requires the same function to ACCEPT (the inverse control, without
  which a guard mutated to refuse everything would pass);
- `assert_the_legacy_panel_account_is_migrated_in_place` — plants the legacy state (user and group
  `panel`, `/var/lib/maran` owned by them, a file inside it), runs step 15's migration, and
  requires the uid and gid to be **unchanged** while the names changed, which is the property the
  whole design rests on.

Each of these was shown to go red by breaking the property it asserts, in a throwaway container of
each family, with the failure quoted in a working report kept only as a scratch file (not
committed). Both images were then rebuilt with
`docker build --no-cache` — a cached `COPY` layer has scored an older file here before — and every
assertion in the script passed on both families, including the FTPS name-agreement check this
change did not touch.

**Also measured, and it contradicted this note's own draft:** `usermod -l` on an account with a
live process returns 0 on both families, so the service stop in step 15 is a deliberate choice and
not a requirement (§5).

**NOT measured:** the agent's polygon suite lane. Three other sessions were editing
`agent/crates/**` while this change was made, so a `cargo test` over that tree measures their
work in flight rather than this one; this change adds no Rust and alters no path, mode or group
relationship the suites read.

---

## Correction, 2026-09-09 — one assertion is named wrongly in §7

Added by a verification pass over every threat note on `fix/live-findings`, which found this
note VERIFIED except that one cited assertion does not exist under the name given. §7 cites
`assert_the_legacy_panel_account_is_migrated_in_place`. No function of that name exists. The
assertion is real and does what §7 describes — uid and gid unchanged while the names change — and
its name is **`assert_the_legacy_service_account_is_migrated_in_place`**
(`docker/polygon/assert-installer-steps.sh`, defined at the `assert_the_legacy_service_account_…`
block and called from the run list). A reviewer grepping the name written above would have found
nothing and concluded the proof was missing.
