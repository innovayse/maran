# PHP extensions, OPcache and end-of-life versions

This page covers what an administrator can do with the PHP versions installed on a server: switch
extensions on or off, what the shared OPcache is set to, and how the panel flags a PHP version that no
longer receives security fixes. Customers see the same information read-only.

Status of the change this page describes (issue #157): the second security review of the privileged
agent changes is OUTSTANDING and the branch must not merge to `main` until a reviewer has read it. The
threat note is `docs/superpowers/notes/2026-11-07-php-extensions-threat-note.md`.

## Extensions

Thirteen extensions can be switched, per PHP version: `bcmath`, `curl`, `gd`, `gmp`, `imagick`,
`intl`, `mbstring`, `mysql`, `opcache`, `redis`, `soap`, `xml`, `zip`. The list is closed in the agent;
the panel cannot ask for any other, and it cannot name a package. Open **Settings, PHP versions** and
choose **Extensions** on a version.

- **Switching on** installs the extension's package through the same root install unit that installs a
  PHP version, enables it for php-fpm, checks the configuration (`php-fpm -t`), reloads the version's
  php-fpm and then reads the loaded modules back. An extension that is still not loaded afterwards is
  reported as a failure.
- **Switching off** disables it for php-fpm the same way (check, reload, read back). If php-fpm refuses
  the result, the previous state is restored.
- The state shown is what the running php-fpm of that version has loaded (`php-fpm -m`), not a file
  listing.
- Switching an extension already in the requested state does nothing and succeeds.
- **The change is host-wide.** It affects every account's pool on that PHP version at the reload, and
  only an administrator can make it. A customer sees a read-only list.

### What differs between server families

| | Debian family (Ubuntu, Debian) | RHEL family (AlmaLinux, Rocky) |
|---|---|---|
| Source of packages | the PHP repository the installer configures | the PHP repository the installer configures |
| Switching on | install the package, then `phpenmod` for php-fpm | install the package (it loads by itself) |
| Switching off | `phpdismod` for php-fpm | **not offered**: the packages ship plain ini files and no switch the panel can rely on |
| `curl` | package | built into the base package: shown as loaded, nothing to install |

On the RHEL family an extension that is on stays on from the panel's side; the panel shows why. This is
the fallback chosen because a clean disable could not be measured in the session that wrote the code;
it is revisited when it is.

Known limits:

- The package names are the author's reading of the two repositories and were not checked against a
  live repository; a name the repository does not carry shows up as a failed install with the package
  manager's reason in the task, and nothing else changes.
- Whether a package upgrade turns a switched-off extension back on has not been measured on either
  family.
- `mysql` stands for the MySQL extensions as one switch (`mysqli`, `pdo_mysql`, `mysqlnd`); on the
  Debian family the switch is applied to `mysqli`, and whether `pdo_mysql` follows is unmeasured.

## OPcache

OPcache is process-global: one php-fpm master serves every account's pool of a version, so its shared
memory is shared by every customer on that version. Maran therefore writes a hardened drop-in,
`99-maran-opcache.ini`, in the version's additional-ini directory (`/etc/php/<version>/fpm/conf.d` on
the Debian family, `/etc/opt/remi/php<version without dot>/php.d` on the RHEL family):

- `opcache.validate_permission=1`: a cached script is served only to a process that could read the file.
- `opcache.validate_root=1`.
- `opcache.restrict_api` set to a path prefix no customer script starts with. An empty value would mean
  unrestricted, so a prefix that matches nothing is used: customers cannot call `opcache_reset()`,
  `opcache_invalidate()` or `opcache_get_status()` (which lists other accounts' cached files).
- `opcache.enable_cli=0`, `opcache.save_comments=1`, `opcache.validate_timestamps=1`,
  `opcache.revalidate_freq=2`, and Maran-chosen memory figures (128 MiB cache, 16 MiB interned strings,
  10 000 files).

The file is written when a PHP version is installed and converged every time the agent starts. **A hand
edit of it is overwritten at the next start.** OPcache on or off is the `opcache` extension in the list
above (so, on the RHEL family, it can be switched on but not off from the panel). Tuning the figures is
not offered in this release.

## End-of-life versions

Every PHP version carries a support status computed by the panel from a static table that ships with
the panel (no outbound request is made to learn it):

| Status | Meaning |
|---|---|
| Supported | the version still receives bug fixes |
| Security fixes only | the version receives only security fixes; the days left until they end are shown |
| End of life | the version no longer receives fixes |
| Unknown | the table has no row for the version; it is never shown as supported |

The status appears as a badge (a word and an icon, never colour alone) on the PHP versions page. An
administrator also sees how many sites use each version, a warning above the table when a site still
runs an end-of-life version, and a line in the dashboard's attention block and its count in the
navigation badge. The table changes with a panel release, and the dates in it must be checked against
the PHP project's supported-versions page when they are edited: the dates written in this release are
marked in the code as to be verified.

## PHP on the command line, and Composer

An account has no login shell (its login is SFTP only), so PHP's command line and Composer can run
only from a **cron job**, which the Cron module runs as the account.

- Use the version's own CLI binary by its full path. The path is a fact of the server's family: the
  Debian family names it `php<version>` (for example `/usr/bin/php8.3`), the RHEL family keeps each
  version under `/opt/remi/php<version without dot>/root/usr/bin/php`.
- Composer is a PHP archive: download `composer.phar` into the account's home and run it with the
  version's CLI binary in a cron job.
- The command line does not use the pool: the pool's `open_basedir`, `disable_functions` and limits do
  not apply to a cron job, and the CLI reads its own `php.ini`. Do not rely on either the presence or
  the absence of those restrictions in a cron job.
