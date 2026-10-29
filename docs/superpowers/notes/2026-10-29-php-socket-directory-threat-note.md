# Threat note: php-fpm sockets leave the agent's private directory (issue #101)

Written 2026-10-29 for issue #101. It lands on the fix branch BEFORE the first code commit of that
branch (rules/security.md "Sensitive change escalation").

`rules/security.md` requires a threat note, written first, for a change to the installer's
privileged steps and a second reviewer. This change moves the directory the php-fpm sockets live in
out of `/run/maran`, edits the root agent's unit (`ReadWritePaths=`), the installer (a new runtime
directory, the uninstaller) and the agent's path table. The unit is the only sandbox around the
only root process in the product.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by agent sessions,
which cannot obtain one. The change may land on a branch and MUST NOT merge to `main` until a human
reviewer has read this note and the diff and signed off. The owner is told at hand-off that this
surface carries an outstanding review.

**Evidence used, and where it stands.** Everything below marked "read" was read at `dev` through
git objects (no tree touched). Everything marked "measured" comes from a polygon run on Ubuntu 24.04
(`measure91` finding F3: a PHP site answers 502 and the site error log says the socket connect was
refused for permission). Nothing in this note was measured on the RHEL family; see "What the author
cannot verify".

## What is wrong today

- The installer creates `/run/maran` as `root:maran 0750` (`installer/lib/40-user.sh`,
  `install -d -o root -g "$MARAN_GROUP" -m 0750 /run/maran`). The agent unit asks for the same
  (`RuntimeDirectory=maran`, `RuntimeDirectoryMode=0750`, with a comment that "0750 keeps every
  other account out"), and `server.rs` repeats `0o750`. That mode is deliberate: the directory
  holds the root agent's gRPC socket (`agent.sock`, 0660 `root:maran`, peer-credential checked) and
  the agent's scratch directory.
- `AgentPaths::PHP_FPM_SOCKET_DIRECTORY` is `/run/maran/php`, **under** that directory. `write_pool`
  creates it 0755 on every pool write and the pool template makes each socket
  `listen.owner = listen.group = <web server user>`, `listen.mode = 0660`. The socket is therefore
  fine for nginx. Its parent is not: nginx runs as `www-data` (Debian family) or `nginx` (RHEL
  family), which is neither root nor in group `maran`, so path resolution fails at `/run/maran` and
  `connect(2)` returns `EACCES`. Every PHP site answers 502 on an install made by the real
  installer. Read: nothing in `installer/` adds the web user to group `maran`.
- The polygon hides this on purpose: `docker/polygon/images/{ubuntu24,alma9}/suite.Dockerfile`
  create `/run/maran/php` and `chmod 755 /run/maran`, re-apply that after running `40-user.sh`, and
  `asserts/parts/directory-layout.sh` restores it. The gates never observed the mode the installer
  leaves. This is the shape rules/testing.md "A check must be able to observe what it reports on"
  describes, and removing the masks is part of the fix.
- Collateral, read from `systemd.exec(5)` and not measured here: `RuntimeDirectory=maran` is removed
  when the agent unit stops (the unit does not set `RuntimeDirectoryPreserve=`), and that takes
  `/run/maran/php` and every socket in it. A php-fpm master that still holds those sockets open
  keeps listening on unlinked names, so even with the permission fixed an agent restart would break
  every PHP site until its pool is rewritten. The move below removes the socket directory from the
  agent unit's lifetime.

## Who can connect to which pool socket: today and after

The attacker is an ordinary hosting customer whose code runs as their own account uid (PHP in
their pool, a cron entry, an SFTP session). Connecting to a pool socket is the capability that
matters: a FastCGI client that can write to the socket can ask that pool to run any script the pool
user can read, so reaching another tenant's pool is code execution as that tenant, bounded only by
`open_basedir` and the pool user's own rights.

| | today | after |
|---|---|---|
| socket directory | `/run/maran/php`, 0755, parent `/run/maran` is `root:maran 0750` | `/run/maran-php`, `root:root 0755`, nothing in it but sockets |
| pool socket file | `<account>-<version>.sock`, owner = group = web server user, 0660 | unchanged (owner and mode come from the pool template, which does not change) |
| nginx reaches it | no (parent not traversable): 502 | yes: web user owns the socket |
| root, user `maran` | yes (they can traverse) | yes |
| another tenant's uid | no, stopped at the parent directory | **can resolve the path and list the socket names; cannot `connect(2)`**: connecting to a unix socket needs write permission on the socket file, which is 0660 for the web user and web group only |
| the same tenant, to its own pool | no | no (same reason; it never needs to) |
| create, rename or unlink a socket | nobody but root | nobody but root: the directory is `root:root 0755`, so no tenant can unlink a neighbour's socket and bind their own in its place (the reason `SOCKET_DIRECTORY_MODE` is explicit and not umask-derived) |

Cross-tenant reach after the change therefore rests on **one** control, the socket's owner and
mode, where today it rests on that control plus an unintended second one (the unreachable parent)
that also blocks the one legitimate client. This is the honest cost of the change and it is why the
reviewer check on "who is in the web server's group" below is load-bearing. It is not a regression
against the design the code states (`write_pool` already documents the directory as "traversable by
everyone"); it is the design finally taking effect.

What a tenant additionally learns: the names `<account>-<version>.sock` reveal which accounts have
a pool and at which PHP version. Account names are already visible if `/home` is listable (the
diagnosis found it is; a reviewer must re-measure, see below), so no new disclosure is expected.

Chain that would make the table wrong: a tenant uid that is a member of the web server's group, or
that can become the web user. `useradd` is given the account's own group; the sketch of a path from
a tenant into the web group was not found in `ops::accounts`, but "not found by reading" is not
"proved absent" (see below).

## Alternatives considered

**Add the web user to group `maran`.** Rejected, and not close. Group `maran` owns `/etc/maran`
(`panel.env` with the encryption key, the panel's TLS key), `/var/log/maran`, `/var/lib/maran`, all
0750. Membership would hand the internet-facing nginx worker read access to the panel's secrets and
write access to the directory holding the root agent's socket, to save one directory. The
installer already took the opposite decision for the panel's own socket: `/run/maran-api` exists
precisely so the web group gets a directory of its own (`2710 maran:<web group>`, built by
`maran-api.tmpfiles.conf`) and never membership of `maran`.

**`/run/maran` at 0751 (search-only for others).** Smaller diff, weaker. `agent.sock` stays 0660
and peer-credential checked, so the agent is not exposed to a connect, but the directory that holds
the one root-owned control socket stops being unreachable and every local uid can resolve names
inside it (`agent.sock`, the scratch directory). It dissolves the stated invariant in the unit
comment and in `server.rs`, and it touches four places that assert 0750 (`40-user.sh`, the unit,
`server.rs`, `directory-layout.sh`). It spends a defence-in-depth layer on the most sensitive
directory in order to serve a far less sensitive resource. It is acceptable only as a stopgap and
the owner is asked to refuse it (Decision 4).

**Chosen:** a sibling, `/run/maran-php`, `root:root 0755`, holding nothing but sockets.

## The change planned

1. `AgentPaths::PHP_FPM_SOCKET_DIRECTORY` becomes `/run/maran-php`; its doc comment states why it is
   NOT under `/run/maran` (that directory is `root:maran 0750` and the web server must traverse the
   path). `list_php_versions.rs`, `PoolPaths`, `render_vhost.rs` follow the constant.
2. Goldens that embed the path are regenerated (`templates/tests/golden/nginx/php_site.conf`,
   `php_site_ssl.conf`, `php_fpm/pool.conf`) and so are the unit tests naming it
   (`create_site_tests.rs`, `site_body_tests.rs`, `golden_test.rs`, `nginx_link_rule_test.rs`,
   `php_version_tests.rs` twins). The golden diff is the review artefact: it is the byte-exact
   statement of what nginx and php-fpm are now told.
3. `installer/systemd/maran-agent.service`: `/run/maran-php` added to `ReadWritePaths=` (`maran
   structure` requires every absolute `AgentPaths` constant outside `/etc` to be listed) and the
   comment near line 245 that names `/run/maran/php` corrected.
4. The directory is built by `systemd-tmpfiles` from a new snippet beside
   `maran-api.tmpfiles.conf` (`d /run/maran-php 0755 root root -`), installed by
   `70-services.sh` and applied immediately with `systemd-tmpfiles --create`, the pattern the API's
   directory already uses and documents at length. Reasons: it exists before the agent unit starts
   (so `ReadWritePaths=` never names a missing path, which fails the unit's namespace setup), before
   php-fpm starts at boot (a php-fpm that cannot create its socket directory cannot bind the pool,
   and today nothing creates the directory until the first `write_pool` after boot), and it does not
   die with the agent unit. `write_pool`'s own `create_directory(..., 0o755)` stays as a second line.
5. `installer/uninstall.sh` removes `/run/maran-php` and the tmpfiles snippet next to `/run/maran`.
6. The polygon stops lying: the Dockerfile `chmod 755 /run/maran` lines and the re-apply after
   `40-user.sh` are deleted, the directory-layout assert is rewritten to require
   `/run/maran` = `root:maran 750` and `/run/maran-php` = `root:root 755`.
7. Docs that name the old path are corrected in the same change (`README.md` line 198,
   `docker/README.md`, the e2e golden-path comment). A stale sentence here is the defect
   rules/architecture.md names.

## Migration of existing installs

An install made before this change has: pools rendered with `listen = /run/maran/php/...sock`,
nginx vhosts with `fastcgi_pass unix:/run/maran/php/...sock;`, and a php-fpm master bound to those
paths. The agent has no bulk re-render and the installer renders none of them (read; the reviewer
confirms). Those sites already answer 502 on an unmodified real install, so no working site is
broken by the move; but they do not heal by themselves either.

- **Not allowed: a sed in the installer over the rendered files.** The config-write protocol binds
  every writer of a system config, installer shell included (rules/rust.md "Config writes"); an edit
  that skips render, atomic swap, `nginx -t`/`php-fpm -t`, reload and rollback is exactly what that
  paragraph forbids.
- **Proposed:** the panel re-renders each PHP site through the existing agent path (the one a PHP
  version switch uses, with the version unchanged), once, after the upgrade. Order per site:
  (a) the new directory exists (tmpfiles, installer step 70) before the agent is restarted onto the
  new unit; (b) agent restarted; (c) the pool is rewritten, `php-fpm -t`, reload, so the master
  binds the new path; (d) the vhost is rewritten, `nginx -t`, reload. Pool before vhost: the
  reverse order leaves nginx pointing at a socket nobody has bound yet. Either order has a window
  between (c) and (d) in which that one site answers 502; on an affected install that is what it
  answers already. An install where an operator worked around the defect by hand (loosened
  `/run/maran`, added the web user to a group) has working sites and WILL see that window. How the
  panel triggers the re-render is Decision 3.
- The old `/run/maran/php` is left alone: it disappears when the agent unit next stops (above) and
  at reboot. Nothing may recurse into it.
- Rollback: the previous agent and the previous templates name the old path and the old unit does
  not list `/run/maran-php`. A rollback after sites were re-rendered returns them to the 502 state,
  not to a worse one. The new directory is harmless to leave.
- No schema change, so `maran migrate guard` has nothing to say.

## What a second reviewer must check

1. `stat -c '%U:%G %a'` on a REAL install (installer, real systemd, not the polygon's plain process):
   `/run/maran` is `root:maran 750` and `/run/maran-php` is `root:root 755`. Then, AS the web user
   with no supplementary groups (`setpriv --reuid=<web user> --regid=<web group> --clear-groups --`):
   `test -x /run/maran-php` succeeds, `test -S /run/maran-php/<account>-<ver>.sock` succeeds, and the
   same probe against `/run/maran` **fails** (the negative control that proves 0750 stayed).
2. As an unrelated tenant uid (`setpriv --reuid=<tenant> --regid=<tenant> --clear-groups`): the
   socket path resolves, and a `connect` to another account's pool socket is **refused**. Prove the
   probe can fail: run it once as the web user and see it succeed.
3. `id <tenant>` and the web server's group membership list: no account is in the web group. Read the
   `useradd` argv in `ops::accounts` for any `-G`.
4. The end-to-end: a PHP site answers 200 for a script that echoes a constant, not 502. Mutation:
   `chmod 0750 /run/maran-php` turns it back into 502.
5. The polygon's php-pool suites pass with the 0755 masks deleted. They only pass today because of
   the mask.
6. That the new directory cannot be written by a tenant, and cannot be pre-created by one: `/run` is
   root-writable only; check `write_pool` does not follow a pre-existing symlink at that path (a
   `create_directory` on an existing path sets the mode of whatever it resolves to).
7. The unit still starts with `ReadWritePaths=` naming `/run/maran-php`, with the tmpfiles snippet
   present and with it absent (the failure must be loud, not a silent missing directory).
8. `list_php_versions` reports the new socket directory to the panel; nothing in `backend/` or
   `frontend/` hard-codes the old one (`git grep -n "run/maran/php"` over the whole tree is empty
   after the change).
9. Goldens: read the diff, not the pass.

## What the author cannot verify

- **The RHEL family.** The PHP-socket 502 was measured on Ubuntu 24.04 only; PHP and `php-fpm -t` on
  AlmaLinux were not exercised in the measurement run. That Rocky/Alma behave the same is read, not
  seen.
- **SELinux.** The polygon runs with SELinux disabled. On an enforcing RHEL host, nginx (`httpd_t`)
  connecting to a socket created by php-fpm in a new directory depends on the labels that directory
  and socket receive; a new `/run/maran-php` may be labelled differently from the packaged socket
  directory. Unknown until run on an enforcing host.
- The packaged php-fpm unit's own sandbox on each family (Debian's uses `ProtectSystem=full`, which
  leaves `/run` writable by reading; not confirmed per version) and whether it can create a socket
  under a directory it did not make.
- Whether php-fpm re-binds a changed `listen =` on a graceful reload or needs a restart; this decides
  step (c) of the migration.
- That systemd-tmpfiles runs before php-fpm at boot on every supported release (Ubuntu 22.04, 24.04,
  Debian 12, 13, Alma/Rocky 9, 10), and the `RuntimeDirectory` removal-on-stop claim above.
- Whether `/home` is listable on a real install (it decides whether listing socket names discloses
  anything new).
- Absence of a path from a tenant into the web group, beyond reading `ops::accounts`.
- Anything on aarch64.

## Second review: OUTSTANDING

State: no reviewer has read this note or the diff. The branch carrying the change must not merge to
`main`. This is a debt with a name, not an alternative route (rules/security.md).

## Decisions

1. **Directory name and owner: RESOLVED.** `/run/maran-php`, `root:root 0755`, confirmed.
2. **How the directory is created: RESOLVED.** A tmpfiles snippet beside the API's (survives the
   agent unit, exists at boot before php-fpm). Rejected: a second `RuntimeDirectory=` in the agent
   unit (shares the unit's single mode and dies with the unit, keeping the restart defect) and
   `write_pool`'s create alone (leaves the boot-time bind failure).
3. **Migration trigger: OPEN.** The panel re-renders every PHP site once after upgrade
   (recommended), or an operator action, or no migration and sites heal on their next re-render. A
   new agent rpc for a bulk re-render would need a spec change, a proto addition and an
   `AgentCapability` declaration (rules/security.md items 12 and 13); the note recommends reusing
   the existing per-site path.
4. **The 0751 stopgap: RESOLVED, refused**, including as an interim. Adding the web user to group
   `maran` is likewise refused.
5. **RHEL scope: OPEN.** Whether this branch may be proposed for merge before a run on an
   enforcing-SELinux AlmaLinux host exists, or whether that run is a merge precondition.
   Recommended: precondition.
