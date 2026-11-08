# Threat note: PHP extensions, the OPcache drop-in and end-of-life data (issue #157)

`rules/security.md` requires a threat note, written first, for a change that lets the root agent change
what the host's PHP runtime loads for every tenant. This note covers three agent capabilities and one
panel-side data table: installing an extension package through the existing root install unit,
switching an extension on or off for one PHP version's php-fpm, writing a hardened OPcache drop-in, and
the static end-of-life table the panel keeps per PHP version. It is written BEFORE the code: every
statement below is a requirement the code must meet, not a description of code that exists. Where the
code lands and differs, the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (rules/security.md), the branch must not merge to main.** The work is
carried out by agent sessions, which cannot obtain a second reviewer. The branch MUST NOT merge to
`main` until a human reviewer has read it (rules/security.md "Sensitive change escalation"). The owner
is told at hand-off that this surface carries an outstanding review. Tracking issue: #331.

## What is added

1. **`ListPhpExtensions(version)`** (read-only) and **`SetPhpExtension(version, extension, enabled)`**
   (mutating, server-streaming, class "run to completion") on `PhpService`.
2. **A closed catalogue of thirteen extension names** in the agent
   (`agent-core::validation::web::PhpExtensionName`): `bcmath`, `curl`, `gd`, `gmp`, `imagick`, `intl`,
   `mbstring`, `mysql`, `opcache`, `redis`, `soap`, `xml`, `zip`. A caller names one of these or is
   refused; the agent never takes a package name from a caller.
3. **A new distro concern trait `PhpExtensionFacts`** answering, per family, the package that provides
   an extension for a version, and the program and argv that switch it for the `fpm` SAPI.
4. **A host-level OPcache drop-in** (`99-maran-opcache.ini` in the version's `php.d`/`conf.d`), rendered
   from typed numbers and fixed isolation lines, written when a version is installed and by a start-up
   pass over the installed versions.
5. **A static end-of-life table in the panel** (`Sites/Domain/Policies/PhpLifecycle`) that turns a
   version and the clock into a status, and a dashboard attention source built on it. No agent code, no
   outbound call.

## Attacker goal

- Make the root agent install an arbitrary package. The install unit (`maran-php-install@.service`)
  installs whatever its instance names, so whatever reaches it IS the package list.
- Make php-fpm load an attacker-chosen shared object (`extension=/path.so`) or a path the attacker can
  write.
- Weaken the isolation of a multi-tenant OPcache: one php-fpm master serves every account's pool of a
  version, and its OPcache shared memory is shared by all of them.
- Turn off an extension another tenant's site needs (a host-wide effect from one request).
- Reach the three capabilities as a customer, or through the panel's own bugs.
- For the lifecycle table: make the panel report a dead PHP version as supported (a false assurance),
  or make it call out to a feed to learn the dates.

## Why it is safe now

1. **The extension name is a closed catalogue, and the package is derived, never supplied.**
   `PhpExtensionName::parse` accepts exactly the thirteen names, rejecting control characters first and
   by name. The install unit's instance is `distro.php_extension_package(version, name)`, a string the
   adapter builds from a `PhpVersion` (two ASCII-digit components) and a catalogue name, so the claim
   in `install_php_version` that the unit "installs whatever it is named" stays harmless: the agent only
   ever names a catalogue package. A name the family has no package for answers a typed refusal
   (`ExtensionNotPackaged`), it is never passed on.
2. **No path is built from a request.** Switching uses the family's own tool with fixed absolute path
   from the adapter and an argv built from the validated version and the catalogue name
   (Debian family: `phpenmod` / `phpdismod`, `-v <version> -s fpm <name>`). There is no shell, no
   `extension=` line written by the agent, no file the agent creates for a caller. The RHEL family has no
   such tool in the packages; its disable answers `ExtensionSwitchUnsupported` rather than the agent
   renaming or editing the distribution's ini files (see "Not verified" below).
3. **The result is validated and observed.** After a switch the agent runs `php-fpm -t` and reloads
   through the same validator and reload the pool writes use; on a refusal it restores the previous
   switch state (the inverse argv) and answers the typed validation error. State is reported from what
   the daemon loads (`php-fpm<ver> -m`), not from a file listing, so a check observes the artefact the
   system actually uses (rules/testing.md).
4. **The OPcache drop-in is rendered from typed numbers and fixed text** by the templates crate and
   written through `ops::safe_write` (render, swap, `php-fpm -t`, reload, rollback). It carries
   `opcache.validate_permission=1` (a cached script is served to a process only if that process could
   read the file), `opcache.validate_root=1`, and a `opcache.restrict_api` set to a path prefix no
   tenant script path begins with. **Correction to the plan this note was written from:** the plan said
   "restrict_api set to an empty value"; PHP documents the empty default as NO restriction, so an empty
   value would leave `opcache_reset()`, `opcache_invalidate()` and `opcache_get_status()` (which lists
   other tenants' cached file paths) callable by every tenant. The drop-in therefore names a prefix that
   matches nothing a tenant owns.
5. **Every rpc is admin-only on the panel side and audited on the agent side.** `SetPhpExtension` is in
   `audit/mutating_rpcs.rs`, `ListPhpExtensions` in `read_only_rpcs.rs`; the handler takes the audit slot
   and settles it with the same `Result` that becomes the terminal message. A customer has no endpoint
   for the toggle; the read list is the only thing a customer can reach, and it carries no package
   names.
6. **Idempotent.** Switching an extension to the state it is already in succeeds with nothing run.
7. **The lifecycle table is static data in the panel binary.** No feed, no outbound call (rules/security.md
   item 10). The dates are marked in the code as TO BE VERIFIED against the PHP project's
   supported-versions page, because the session that wrote them had no network access; a version with no
   row reads as `Unknown`, never as supported.

## Host-wide effect, stated

An extension toggle changes every account's pool on that PHP version at the reload. It is an
administrator action by design. The OPcache drop-in changes the shared cache of every tenant of a
version, and it is applied by the agent on its own at start-up, so a hand edit of the file is
overwritten by the next start (a documented consequence, not a defect).

## What a reviewer must check

- That `PhpExtensionName` cannot be constructed outside the catalogue and that nothing else turns a
  string into a package argument: `grep -rn "php_extension_package" agent/crates` must show callers that
  pass only a `PhpExtensionName`.
- That `phpenmod`/`phpdismod` are spawned argv-only through `AdapterBinary` and that the argv holds only
  the validated version and the catalogue name.
- The catalogue-to-package mapping on both families against the repositories' own package lists
  (Sury for Ubuntu 22.04/24.04 and Debian 12/13; Remi for Alma/Rocky 9 and 10), not from memory: the
  mapping in `debian_php_extensions.rs` and `rhel_php_extensions.rs` is the author's reading and was not
  checked against a live repository.
- That the drop-in's isolation lines are present on every render (a golden) and that a mutation removing
  `validate_permission` turns a named test red.
- That a refused validation after a switch restores the previous state, and that `Bound` (a deadline) is
  never reported as a refusal.
- That the dashboard attention source and the version page read the lifecycle only from the panel's own
  table.

## Not verified by the author

- **The disable mechanism on the RHEL family.** The Remi packages ship plain ini files and no
  `phpdismod`. The author could not measure a clean mechanism (no container or network in the session),
  so the RHEL adapter answers `None` for the switch and the agent answers `ExtensionSwitchUnsupported`;
  on that family an extension can be switched ON (by installing its package) and the UI offers no OFF.
  This is the fallback the plan named (owner decision O5), applied because the measurement is open.
- **Whether a package upgrade re-enables a disabled extension** on either family (the Debian
  maintainer scripts enable on a fresh install; whether an upgrade preserves a `phpdismod` is unmeasured
  here).
- **The package names** for `imagick`, `redis`, `mysql` and `zip` on the RHEL family (Remi's `pecl`
  naming moves between releases) and whether `curl` and `opcache` are separate packages on a given
  version.
- **`opcache.validate_permission` behaviour with several pools under different uids**, and any
  SELinux-enforcing host.
- **The end-of-life dates** (see 7 above): marked TO BE VERIFIED in the code.
- That `php-fpm<ver> -m` lists the OPcache extension under the name the catalogue matches
  (`Zend OPcache`); the match is case-insensitive and is covered by a test against a recorded-shape
  listing, not a live run.
