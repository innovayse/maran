# App store and first-run stack setup (issue #517)

The panel no longer installs or assumes a site runtime. Web servers and runtimes are **apps**:
installed, removed and controlled from the panel, and a fresh host must have a web server chosen
before sites can exist.

"App" is deliberately a different word from "module" (`docs/marketplace/`, issue #186): a module is
a panel extension in an `.ipm` package; an app is an operating-system package set the panel runs
for sites.

## Scope of the panel's own front

nginx stays the panel's own front on port 8443 and is still installed by the installer
(`installer/lib/80-nginx.sh`). That nginx is infrastructure, not an app: it is never offered for
removal. What becomes a choice is the **site web server**, the process that owns ports 80/443 for
hosted sites:

| Site web server | Ports 80/443 | Panel 8443 | Port-80 ACME block for the panel cert |
|---|---|---|---|
| `nginx` app | nginx (same daemon as the panel) | nginx | nginx `maran.conf` port-80 server |
| `apache` app | Apache | nginx | Apache `maran-acme.conf` alias of the same webroot |

Exactly one site web server can be installed at a time. Switching is uninstall + install, and
uninstall is refused while sites exist.

## App catalogue

Closed set of app kinds, versions from the host's package sources (issue #466), never a static
list:

| App id | Kind | Versions | Notes |
|---|---|---|---|
| `nginx` | web server | single (distro) | installing marks nginx as the site web server; the package itself is already present |
| `apache` | web server | single (distro) | `apache2` (Debian), `httpd` (RHEL), `apache2` (SUSE) |
| `php-<ver>` | runtime | `7.4`–`8.4` (existing `supported_versions.rs`) | FPM; existing install path reused |
| `nodejs-<major>` | runtime | `18`, `20`, `22`, `24` | distro / NodeSource-free: only what the configured sources offer |

**One interpreter on Debian and on the RHEL 9 generation.** Debian installs Node.js as one `nodejs`
package and RHEL 9 as one active **module stream**, so every Node.js app id resolves to the same
`/usr/bin/node`, and only one major is installed at a time. The agent refuses installing a different major while one
answers (the panel code `apps.nodeMajorActive`; an unmanaged `node` counts, so a host that carries a
hand-installed 16 refuses every other major); installing the same major again is a no-op. On RHEL the
majors offered are the streams of `dnf -C module list nodejs` that have the `common` profile and are
supported, and the install and removal run through two root units, one per verb, that the installer lays down only on a release with module streams (the RHEL family before 10; the 10 generation gets neither, and an uninstall removes them wherever present):
`maran-app-nodejs-install@<major>` (reset, then `dnf module install nodejs:<major>/common`) and
`maran-app-nodejs-remove@<major>` (`dnf module remove`, then reset, so no stream stays enabled and
any major may follow). A package upgrade that moves the single package between majors (an
`apt-get upgrade`, a `dnf module` switch run by hand) changes the runtime of EVERY Node.js site on
the host at once, and a site's unit does not compare `node --version` with the major it declares.
The panel never upgrades across majors: the installer holds `nodejs*` (and the other app packages)
out of the unattended OS security updates (`MARAN_OS_UPDATE_HOLD`, `installer/lib/00-common.sh`), so
only an administrator's manual upgrade can do it, and after one the installed probe reports the
major that answers. The SUSE family and the RHEL 10 generation have a package per major, where sites on
different majors run side by side. SUSE: `nodejs<major>` with `npm<major>`, interpreter
`/usr/bin/node<major>`. RHEL 10 (AlmaLinux, Rocky and Oracle Linux; observed on AlmaLinux 10.2): `nodejs` is the
default major 22 and `nodejs<major>` the others (`nodejs24`), each with its npm package (`nodejs-npm`,
`nodejs24-npm`), the interpreter of a major is `/usr/bin/node-<major>` and its launcher
`/usr/bin/npm-<major>` (the unversioned `/usr/bin/node` and `npm` belong to `nodejs` and are not
there with `nodejs24` alone), and the majors the sources do not carry (18 and 20) are not offered. The
agent picks this from the release (the os-release `VERSION_ID` major, 10 and above) and installs and
removes them through the generic package units, so the apps of a RHEL 10 host are never refused as
`apps.nodeMajorActive`.

**Node.js on the RHEL 9 generation, edge cases.** A host keeps one module stream. The agent installs and removes
Node.js only through a stream, one operation at a time (a second one is refused until the first ends).
A Node.js that was installed some other way (`dnf install nodejs`, which gives Node.js 16 on AlmaLinux 9,
with no stream enabled) is not the panel's: installing another major or removing that one is refused,
and the refusal says to remove it yourself with `dnf remove nodejs`. A stream an administrator enabled
with `dnf module enable nodejs:<n>` and installed nothing from is not reset by the panel either: the
install is refused and names `dnf module reset nodejs`. An interpreter that is on the host but does not
report a version refuses both operations. If the module listing of a 9 host gives no answer (its sources
carry no module streams, or the listing could not be read) no Node.js app that is not installed is
offered; nginx, Apache and PHP are listed as usual, and the app store says why Node.js is missing. The
RHEL 10 generation ships no module streams and is not asked about them: see above. Each refusal the
administrator must act on has its own code and text: `AgentNodeMajorAlreadyInstalled` (another major is
installed: uninstall it in the panel), `AgentNodeUnmanagedInstall` (`dnf remove nodejs`),
`AgentNodeStreamEnabledByOperator` (`dnf module reset nodejs`), `AgentNodeInterpreterUnreadable` and
`AgentNodeOperationInProgress` (retry once the other operation ends).

Each installed app has: id, kind, version, state (`installing`, `installed`, `failed`,
`removing`), service status (`active`, `inactive`, `failed`, `none`), installed-at.

## Agent contract (proto revision 27)

New service `proto/agent/v1/apps.proto`:

- `ListApps` → every app the host has, with version and service status (probed, not remembered).
- `ListInstallableApps` → per app id, the versions the package sources offer.
- `InstallApp(app_id)` → server stream of `Progress` (same message shape as `php.proto`), runs
  through the root install unit (generalised from `maran-php-install@` to `maran-app-install@`).
- `UninstallApp(app_id)` → server stream of `Progress`; root uninstall unit `maran-app-remove@`.
- `ControlAppService(app_id, action ∈ {START, STOP, RESTART, RELOAD})`.

`app_id` is a validated type (`ValidatedAppId`) parsed against the closed catalogue above; nothing
the panel sends reaches a command line except through it. New error codes:
`ERROR_CODE_APP_NOT_INSTALLED`, `ERROR_CODE_APP_IN_USE`, `ERROR_CODE_WEB_SERVER_ALREADY_INSTALLED`.
Revision 30 adds `ListInstallableAppsOk.nodejs_unavailable_reason` and the four Node.js refusal codes
(`ERROR_CODE_NODE_UNMANAGED_INSTALL`, `..._STREAM_ENABLED_BY_OPERATOR`, `..._INTERPRETER_UNREADABLE`,
`..._OPERATION_IN_PROGRESS`).

Sites: `CreateSite` gains `web_server` (`NGINX` | `APACHE`) and a `node` backend
(`node_version`, `app_port`, start command run under a per-site systemd unit). Apache templates
live beside the nginx ones in `templates/src/apache/` (static, php via `proxy_fcgi`, proxy,
redirect, ssl).

## Backend

New module `Maran.Modules/Apps`:

- `InstalledApp` entity (ledger of what the panel installed and its state), reconciled against
  `ListApps` at startup.
- Commands `InstallApp`, `UninstallApp`, `ControlAppService`; installs run as recorded tasks
  (`Tasks` module) with progress and logs.
- Queries `ListApps`, `ListInstallableApps`, `GetStackStatus`.
- `UninstallApp` is refused (`Conflict`, `AppsInUse`) listing the sites that use the app (web
  server: any site; `php-x`: sites on PHP x; `nodejs-x`: sites on Node x).
- **Stack gate**: `StackStatus.IsReady` = a site web server is installed. `CreateSite` and every
  site-mutating command refuse with `Conflict` `AppsStackNotReady` while it is false. This is the
  enforcement; the SPA only reflects it (`rules/security.md`).
- Endpoints (admin): `GET /api/v1/apps`, `GET /api/v1/apps/installable`,
  `POST /api/v1/apps/{id}/install`, `POST /api/v1/apps/{id}/uninstall`,
  `POST /api/v1/apps/{id}/service/{action}`, `GET /api/v1/apps/stack` (any signed-in role).
- PHP endpoints keep working but report `AppsNotInstalled` when no PHP app is installed.

## SPA

- Route guard `stackSetupGuard` after the auth guard: admin with `stack.isReady === false` is
  sent to `/setup/stack`; non-admins see a "panel is being set up" page.
- `/setup/stack` wizard: web server (required) → runtimes (PHP versions, Node versions, none) →
  review → installs with live progress; leaves only when the web server install finished.
- `/apps` app store page (admin, nav group Settings): table with server-side search, install /
  uninstall / start / stop / restart, filter sheet (kind, state).
- PHP menu entries and the site form's PHP / Node backends appear only for installed runtimes.

## Installer and polygon

- The installer installs no PHP and no site web server choice; the `maran-php-install@` unit is
  replaced by `maran-app-install@` / `maran-app-remove@` with the same sandbox reasoning.
- Polygon images for ubuntu24 / alma9 / leap16 start with nothing preinstalled for sites; assertions cover
  the first-run gate and each app install / uninstall. The Node.js assertions follow the release: on a
  release with module streams (alma9) a stream is installed and removed, on one without (alma10) two
  majors are installed as plain packages beside each other, each run from its own interpreter, and
  removed in the agent's order; each skips on the other kind of release.

## Threat note

Installing and removing system packages from the panel — see
`docs/superpowers/notes/2026-11-10-app-install-uninstall-threat-note.md`, section "App install / uninstall". Second review: OUTSTANDING (#331).
