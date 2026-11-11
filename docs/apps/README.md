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

**One interpreter on the Debian and RHEL families.** Those families install Node.js as one
`nodejs` package (Debian) or one active module stream (RHEL), so every Node.js app id resolves to
the same `/usr/bin/node`, and only the major that package currently carries is installed. A package
upgrade that moves it from one major to another (an `apt-get upgrade`, a `dnf module` switch run by
hand) changes the runtime of EVERY Node.js site on the host at once, and a site's unit does not
compare `node --version` with the major it declares. The panel never upgrades across majors: the
installer holds `nodejs*` (and the other app packages) out of the unattended OS security updates
(`MARAN_OS_UPDATE_HOLD`, `installer/lib/00-common.sh`), so only an administrator's manual upgrade
can do it, and after one the installed probe reports the major that answers. Only the SUSE
family has a package per major (`nodejs<major>` with `npm<major>`), where sites on different
majors run side by side.

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
- Polygon images for ubuntu24 / alma9 start with nothing preinstalled for sites; assertions cover
  the first-run gate and each app install / uninstall.

## Threat note

Installing and removing system packages from the panel — see
`docs/superpowers/notes/2026-11-10-app-install-uninstall-threat-note.md`, section "App install / uninstall". Second review: OUTSTANDING (#331).
