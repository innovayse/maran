# Threat note: App install / uninstall (issue #517)

`rules/security.md` requires a threat note, written before the change, for anything that widens what
the panel makes the root agent do. The app store lets an administrator install and remove system
packages from the panel: a web server (nginx marker, Apache), PHP versions and Node.js runtimes. This
note covers the host side (installer units, polygon) and states what the agent and backend must hold
to. It generalises `2026-10-26-php-package-install-unit-threat-note.md` (one PHP package through
`maran-php-install@`) and `2026-11-06-php-install-unit-hardening-threat-note.md` (the unit's sandbox);
read those for the reasoning that is not repeated here. Design contract: `docs/apps/README.md`.

## App install / uninstall

### What changes

- `maran-php-install@.service` is replaced by `maran-app-install@.service`. A sibling
  `maran-app-remove@.service` is new. Both are root oneshot templates, installed by
  `installer/lib/70-services.sh` (`render_package_unit`), never enabled, with no `[Install]` section.
  Only the agent starts them: `systemctl start maran-app-install@<package>.service` and
  `maran-app-remove@<package>.service`.
- The install unit's argv is unchanged in kind: `apt-get install -y --no-remove -- %i`,
  `dnf install -y -- %i`, `zypper --non-interactive install --no-recommends -- %i`. The removal unit
  runs `apt-get remove -y -- %i`, `dnf remove -y -- %i`, `zypper --non-interactive remove -- %i`.
  No shell, no program taken from a variable, `%i` and never `%I`, `--` before the instance.
- Both units carry the same sandbox directives (the 2026-11-06 list), so the two root package routes
  cannot drift apart. Ceilings: `installer/systemd/unit-security-ceilings.txt`, both 4.4.
- The installer no longer installs or assumes a site runtime. `nginx` stays installed: it is the
  panel's own front on 8443 (`docs/apps/README.md`, "Scope of the panel's own front").
- The unattended OS update hold list gains `apache2*`, `httpd*` and `nodejs*` beside `php*`, because
  those are now apps whose restart or version change is an event an administrator schedules.

### Who may do it

Only an administrator. The backend endpoints (`POST /api/v1/apps/{id}/install`, `/uninstall`,
`/service/{action}`) are admin-only; the agent's `apps.proto` rpcs are reachable only over the
root-owned socket from the panel's service account (`2026-09-03-panel-socket-threat-note.md`). A
reseller, a customer, a sub-user and an API key without the admin role cannot reach them. The SPA
hides the page from other roles as advice; the enforcement is the backend and the agent
(`rules/security.md`, "Security logic on the backend").

### What is executed, and what the panel can choose

The panel sends an `app_id`. The agent parses it into `ValidatedAppId` against the closed catalogue
(`nginx`, `apache`, `php-<7.4..8.4>`, `nodejs-<18|20|22|24>`) and maps it to ONE package name through
the distro adapter. That package name is the unit instance. Nothing the panel sends reaches a command
line except through that mapping, so the panel can choose among the catalogue's packages and nothing
else. The unit itself does not re-check the instance (it cannot: it runs the package manager). A
compromised agent could start an instance naming any package, exactly as before for PHP; the units
are therefore not a boundary against a compromised agent, and the section "What this does not stop"
says so. Dependencies a package drags in are chosen by the package manager from the configured
sources, which is the next heading.

### Package source trust

A package install runs the package's maintainer scripts as root. Trust is therefore exactly the trust
in the configured package sources:

- `nginx`, `apache` and the distro-version Node.js come from the distribution's own repositories
  (`apache2` on Debian and SUSE, `httpd` on RHEL; `nodejs` as the distribution names it).
- PHP versions come from the sources the host already configures for them (Sury on the Debian family,
  Remi on the RHEL family, the distribution's single minor on SUSE). The installer does not add
  those sources and the units do not. An operator who has not configured a PHP source is offered only
  what the distribution has: `ListInstallableApps` reports what the sources offer (issue #466) and
  never a static list.
- Node.js is NodeSource-free: only what the configured sources offer. The panel never fetches a
  script or a repository key, and never runs `curl | sh`.
- A package manager verifies signatures with the keys the host trusts. The units pass no
  `--allow-unauthenticated`, `--nogpgcheck` or `--force-*` option and the polygon asserts refuse a
  render that does (`docker/polygon/asserts/parts/app-package-units.sh`, planted controls).

### Removal

Removing a package is destructive in a way an install is not, so the removal route has its own
boundaries:

- **Refused while in use.** The backend refuses `UninstallApp` with `apps.inUse` listing the sites
  that use the app (a web server: any site; `php-x`: sites on PHP x; `nodejs-x`: sites on Node x).
  The agent refuses independently with `AgentAppInUse` when it can see a pool, vhost or per-site unit
  that uses the app, and with `AgentAppNotInstalled` when the app is absent. Two layers, because the
  backend ledger can be stale and the agent reads the host.
- **nginx is never removed.** It is the panel's front. Uninstalling the `nginx` app only unmarks it
  as the site web server; the package stays. The `nginx` catalogue entry has no package-removal
  mapping, so there is no argv that could remove it. A removal that the package manager resolves by
  also removing the panel's nginx package would take the panel down: the agent lists what the manager
  will remove with a simulation before it starts the unit and refuses a transaction that includes
  `nginx` or a package Maran's installer depends on.
- **No purge, no autoremove, no force.** The unit never passes `purge`, `autoremove`, `--force-*` or
  `--allowerasing`. The panel's own site configuration and pool files are not owned by the package
  and must outlive a reinstall; a dependency another package still wants is not this unit's to judge.
  The polygon checker refuses a render containing any of them (15 planted controls on the removal
  unit).
- **Exclusive web server.** Exactly one site web server can be installed
  (`AgentWebServerAlreadyInstalled`). Two daemons both wanting ports 80/443 on the same host is a
  fault, not a feature.
- **Concurrency.** Installs and removals are serialised by the package manager's own lock
  (`/var/lib/dpkg/lock-frontend`, the rpm database lock). A second start while one runs fails with the
  manager's lock message and the agent reports it; it does not queue.

### What this does and does not stop

Does: a panel session that is not an administrator cannot install or remove anything; a typo or a
forged app id never becomes a command line; a removal that would take the panel's front or a site
that depends on it is refused before it runs; an install cannot silently remove other packages
(`--no-remove` on the Debian family, no `--allowerasing` on dnf).

Does not: a **malicious or compromised package** runs maintainer scripts as root and can undo any
directive of the units' sandbox; those directives bound accidents and bugs only
(`2026-11-06-php-install-unit-hardening-threat-note.md`). A **compromised agent** can start the unit
with any instance. A **compromised package source** is a compromise of the host. The polygon cannot
show otherwise.

### What the measurement could not establish (OUTSTANDING for the reviewer)

- The removal unit reuses the install unit's sandbox directive for directive. The directives were
  measured for an install on Ubuntu 24.04 and AlmaLinux 9; a removal runs the same package manager
  and the packages' `prerm` and `postrm` scripts, which touch the same things, but the removal was
  not measured on a booted systemd. The polygon installs and removes a real package through the
  units' argv (`docker/polygon/asserts/parts/app-store.sh`) without the sandbox and without PID 1.
- An enforcing SELinux host, the Sury and Remi packages' removal scripts, Node.js packages, and the
  SUSE family's `zypper` argv (the install argv was already marked UNVERIFIED) are unobserved.
- `apt-get remove` can remove packages that depend on the named one. The simulation described above is
  the agent's; it is not in the unit and this note cannot show it from the host side.
- The `nginx` app's "unmark only" behaviour, the in-use checks and the transaction simulation are the
  agent's and backend's. This note states the requirement; their tests are the evidence.

### Tests that hold this note

- `docker/polygon/asserts/parts/app-package-units.sh`: both units rendered for all three families,
  one fixed argv each, 39 planted controls (shell wrapper, `%I`, dropped `--`, purge, autoremove,
  force, `--allowerasing`, `EnvironmentFile=`, `[Install]`, missing timeout and so on), install as
  `644 root:root`, never enabled.
- `docker/polygon/asserts/parts/app-store.sh`: first-run state (nginx present, no site runtime), a
  real install and removal of Apache through the units' argv, a missing package and an
  option-shaped instance refused, nginx still present, the uninstaller removes both units (mutant
  shown to leave them).
- `scripts/lib/check-structure.sh` check 27: the agent's `INSTALL_UNIT_PREFIX` and
  `REMOVE_UNIT_PREFIX`, the installer's unit names and the uninstaller's `rm` lines agree.

### Sites backend: the Node start command and port (panel side)

A Node site carries a customer-supplied start command that a per-site systemd unit will run, and a
loopback port. This is a second command-execution surface beside the package units and is bounded in
two places.

- The panel (`CreateSiteCommandValidator`, `NodeAppLimits`, `NodeVersionFormat`) refuses early: the major
  is one or two digits anchored with `\z`, the port is 1024 to 65535 and not held by any other site of
  any account, and the command is one printable line of at most 1024 characters (no control character, so
  no newline can add a unit directive). These are advice to the customer; the agent validates again and
  is the boundary that matters (`ValidatedAppId` for the runtime, its own start-command type for the
  unit).
- The Node major is accepted only if it is installed as an app (`AppsNotInstalled` otherwise), so a
  request cannot name a runtime the host never installed.
- Not established from the panel side: whether the unit's sandbox confines a hostile command to the
  account's own home, and the port check is a read, not a key, so two simultaneous creations can both
  pass it (the second application then fails to bind). Both are OUTSTANDING for the reviewer; tests:
  `NodeAppLimitsTests`, `CreateSiteCommandValidatorTests`, `CreateSiteCommandHandlerTests`.

### Agent side: the app store rpcs (issue #517)

What `proto/agent/v1/apps.proto` makes the root agent do, and what holds it:

- **The id is the only input.** `ValidatedAppId` (agent-core) parses `nginx`, `apache`,
  `php-<version>` and `nodejs-<major>` and refuses everything else by name, a control character and
  an option-shaped value included. A PHP version is additionally checked against the PHP area's one
  supported list (`apps::app_catalogue::ensure_in_catalogue`) before any package name exists. The
  package is the adapter's answer for that id (`AppFacts`, `PhpFacts`); no caller text reaches a
  unit instance. Tests: `validated_app_id_tests.rs` (core and agent), `validated_action_tests.rs`, `install_app_tests.rs`
  (a version outside the set starts no unit).
- **Removal is checked on disk, not on the panel's word.** `UninstallApp` is refused with
  `APP_IN_USE` while the agent's vhost directory of a web server holds a site, or an account's
  pool sits on a PHP version. The panel's own check answers first; this one stands when the panel's
  rows and the host disagree. Node.js has no per-major marker the agent can read, so that refusal
  is the panel's alone (OUTSTANDING: a per-site unit naming its major would let the agent check it).
- **nginx is never removed or stopped.** It is the panel's front; uninstalling the `nginx` app
  removes nothing and stopping it is refused, because a stopped front cannot be started again from
  the panel. Test: `nginx_is_never_removed_it_is_the_panels_front`,
  `nginx_cannot_be_stopped_because_it_is_the_panels_front_but_can_be_reloaded`.
- **A second site web server is refused.** Installing nginx while Apache is installed, or Apache
  while nginx serves sites, is `WEB_SERVER_ALREADY_INSTALLED`.
- **The service action is one fixed word.** `systemctl start|stop|restart|reload -- <unit>`, the
  unit from the adapter, the subcommand from the enum; an unset action is `INVALID_INPUT`.
- **Writes go through the config-write protocol.** Installing Apache writes the panel's challenge
  virtual host and the include of the agent's directory, each validated with `apachectl -t` and
  undone if Apache refuses it. The challenge host serves only the challenge directory; every other
  path is a 404.
- **Both rpcs that change the host leave an audit line**, and both streaming rpcs are
  run-to-completion with a bounded channel, like `InstallPhpVersion`.

OUTSTANDING for the reviewer: the Debian family offers one `nodejs` package, whose major is read
from `apt-cache policy`; the RHEL and SUSE per-major package names (`nodejs20`) and the SUSE Apache
include path are read from documentation and unmeasured. Apache and nginx both want port 80 on a
host where Apache becomes the site web server while the panel's nginx keeps a port-80 block; moving
that block is the installer's step and is not done by the agent.

### Panel side: the Apps module (issue #517)

- **Only an administrator reaches any write.** `AppsController` carries `AdminOnly` at class level; the one
  route every role may read, `GET /api/v1/apps/stack`, sits in `StackController` because a class-level and
  an action-level policy are cumulative, and it carries a status and version lists, no tenant data.
- **An id is a catalogue member or nothing.** `AppId.TryParse` is ordinal and exact; the validators of the
  three commands and the handlers refuse anything else before a task, a ledger row or an agent call
  exists. The id comes from the route, so the commands carry the server-established guards.
- **The gate is the panel's, not the browser's.** `IStackStatus` is read by the Sites module before it
  creates or changes a site; it counts only apps in the `Installed` state, so a web server mid-install is
  not ready.
- **The ledger yields to the host.** `StartupAppReconciler` settles `apps.InstalledApps` against
  `ListApps` at start-up; a row the host does not have never keeps the stack ready.
- **The audit subject is a catalogue id** (and, for a service action, one of four action words), never
  agent output. Install and removal are journalled twice: the request under the administrator's name and
  the outcome under the panel's system actor.
- **Not established from the panel side:** a removal refusal names up to fifty domains to an
  administrator, which is the point of the refusal and is not tenant-scoped by design.

### Agent side: the site web server choice and nginx's port 80 (issue #517)

- **The marker.** Which web server serves sites is one agent-owned file, `AgentPaths::SITE_WEB_SERVER_MARKER`
  (`/etc/maran/site-web-server`, `nginx` or `apache`), written atomically by `InstallApp` and removed by
  `UninstallApp`; nginx's package on disk says nothing, because nginx is the panel's front. Only the agent
  writes it (root, fixed path, content from a closed two-value type, never from a request), and an unreadable
  or unrecognised marker reads as nginx, the identity of the process that always exists, so a damaged marker
  can never name a program that is not installed.
- **Port 80.** The panel's ACME block is `installer/nginx/maran-port80.conf`, a file of its own. Installing
  Apache parks it at a fixed `*.suspended` path (not `*.conf`, so nginx never opens it) through the config-write
  protocol (`nginx -t`, reload, put back on refusal) BEFORE Apache is enabled; if Apache then fails to start the
  block is put back. Removing Apache restores it after the package is gone and only then clears the marker, so a
  refused nginx check leaves "Apache chosen, block parked" and a retry converges. Both paths are `AgentPaths`
  constants: no panel input reaches them. The installer does not write the block back while a parked copy
  exists, and takes the Debian family's packaged `sites-enabled/default` link out of the enabled set (the link
  only; `sites-available/default` stays).
- **The distribution's own port-80 server.** The RHEL family's packaged `nginx.conf` (and, believed, the SUSE
  one) carries a default `server { listen 80; ... }`. Step 80 comments out every http-level server block with
  an active port-80 `listen`, line by line, with the prefix `# maran-disabled: ` (staged beside the file with
  its owner and mode, flushed, renamed, `nginx -t`, the original put back and the install stopped on a
  refusal; the pristine file is kept once as `nginx.conf.maran-previous`). It is deterministic and idempotent
  (a commented line is not an active directive, so a second pass finds nothing), and uninstall strips exactly
  that prefix, so the distribution's block comes back byte for byte while operator edits elsewhere in the file
  are kept. It runs on every family and is a no-op where the stock file has no such block (the Debian family's
  default is a `sites-enabled` link, which step 80 unlinks). The polygon proves the render on a synthetic file
  with inverse controls (an 8443 server, an `8080` server and a commented block are untouched; round trip and
  second pass are exact) and on each image's real file (nginx loads it, no listener on 80 remains; on RHEL the
  stock file is required to have had one). Not observed: the openSUSE stock file (the SUSE lane runs the
  same assertion, and a file with no such block is a proven no-op), and a real Apache binding port 80 after
  the move.

Second review: OUTSTANDING (#331)
