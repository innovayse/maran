# The marketplace: installing, updating and removing modules

The marketplace is the part of the panel that lists modules published for it, and installs, updates, rolls back and
removes the ones an administrator chooses. It is part of the panel itself (the `marketplace` module, administrators
only), it is switched off until an administrator turns it on, and it talks to exactly one host. This page says what
that conversation contains, how to point it elsewhere or switch it off, what each state and failure means, and what the
marketplace does NOT promise. The contract the host implements is `docs/marketplace/api-v1.md`; the security analysis is
`docs/superpowers/notes/2026-11-07-marketplace-loader-threat-note.md`.

## What this release can and cannot do

One part of the marketplace is not in the open repository, and an honest description has to start there:

- **The loader.** Verifying a package and loading it into the panel is done by a separately distributed component
  behind the `IModuleLoader` interface. The open build ships a loader that refuses every package
  (`NullModuleLoader`). On a panel without the real loader the marketplace can list modules, but nothing can be
  installed.

The licence lease credential, which a download needs, is in the open panel: the licence component presents the installed
licence's lease (the signed lease the licence service issued) with each request that needs it, and only while that
lease is good. A panel with no licence, with an offline licence, or whose lease is missing, ended or untrusted sends no
download request and shows "this module needs an active licence". Browsing does not need it (see "What is sent").
What is not in the open repository is the daily renewal that fetches a new lease and writes
`/var/lib/maran/lease.json`; until it is, a panel holds a lease only when one has been placed there.

Everything below describes the marketplace's behaviour as written; where a step depends on the loader, it says so.

## When the panel contacts the marketplace

The panel process (`maran-api`, running as the unprivileged system user `maran`) contacts the marketplace host,
`api-marketplace-maran.innovayse.com`, over HTTPS in exactly these situations, and only after an administrator has switched the
marketplace on:

- the administrator opens or refreshes the catalogue, searches it, or opens one module's details;
- the administrator presses the button that checks installed modules for updates;
- the administrator installs or updates a module (the panel asks for a download grant, then downloads the package).

It never contacts the host from a timer, at start-up, in the background, or because of a request by anyone who is not an
administrator. The root `maran` command and the agent never contact it at all. While the marketplace is off, the
Marketplace screen shows the switch and nothing else and makes no request to the host; a panel that never switches it
on never opens a connection to it, whatever `panel.env` says.

A page the panel fetched is kept in memory for ten minutes, so reopening the screen inside that time may not make a
request. The cache is not read while the marketplace is off.

The other two documented outbound destinations are separate: the release host for `maran update`
(`docs/operations/panel-update.md`) and the licence service for the lease (`docs/operations/licensing.md`).

## What is sent

The marketplace sends this and nothing else:

- the panel's version, in a request header, on every request except the package download;
- the interface language (`en`, `ru` or `hy`; a language outside these three is sent as `en`);
- the release channel (`stable` unless configured);
- the text the administrator typed into the catalogue search, and the page cursor of the next page;
- the id and version of the module being looked at or installed;
- on the update check only, the id, installed version and package fingerprint (SHA-256) of each module it is asked
  about;
- the licence lease credential, but only to show what the licence entitles when a catalogue, module or update request is
  made (optional: without it the request still works and entitlements read "unknown"), and to ask for a download grant
  (required). It is never sent with the request for the host's signing keys or with the package download itself;
- what any HTTPS connection implies: the server's source address and the TLS handshake. The panel sends no cookies and
  asks for no compression.

It sends no host name, account, site, domain, user or customer data, no list of the server's other modules, and no
secret. The marketplace screen shows this list in the panel's language when an administrator is about to switch the
marketplace on.

## Switching it on and off

The marketplace is **off by default**, including on a panel that has just been updated. An administrator turns it on
from the Marketplace screen under Settings after reading the list above, and has to type their current password again to
do so. The switch is stored in the panel's database, not in `panel.env`: no environment variable can turn it on or off,
so a mistyped setting cannot start the conversation. Switching it on and off is written to the audit log, as is every
install, update, remove and rollback request and every refusal of one.

To disable it, switch it off on the same screen (the password is asked again). To be certain that no connection can be
made at all, leave it off, and, if you want the network to enforce it too, refuse outbound connections from the server
to the host. Changing `Marketplace__BaseUrl` does not disable it; it points it somewhere else.

## Pointing the panel at another host

The host is a setting, not a constant in the program. The default is `https://api-marketplace-maran.innovayse.com`. To use another
marketplace, set these in `/etc/maran/panel.env` (documented, commented, in `installer/panel.env.example`) and restart
the panel service (`systemctl restart maran-api`):

| Setting | Default | Meaning |
|---|---|---|
| `Marketplace__BaseUrl` | `https://api-marketplace-maran.innovayse.com` | The one host the panel talks to. An absolute `https` address with a host, no user information, query or fragment. A redirect or a download address on any other host or port is refused. |
| `Marketplace__MetadataKeyPath` | `/usr/local/maran/keys/marketplace-metadata.pub` | The public key (or keys) the panel verifies the host's answers against. |
| `Marketplace__PackageDirectory` | `/var/lib/maran/marketplace` | Where a download is stored while it is checked. Created by the installer, mode 0700, owned by the service account. |
| `Marketplace__Channel` | `stable` | The release channel asked for. |

The panel also holds the host to size and time limits (`RequestTimeoutSeconds` 30, `MaxCatalogueBytes` 4 MiB,
`MaxGrantBytes` 16 KiB, `MaxPackageBytes` 256 MiB at most, `CatalogueMaxAgeHours` 24, `CacheMinutes` 10), each a
`Marketplace__` setting with a ceiling it cannot be raised past (`backend/src/Maran.Modules/Marketplace/Options/MarketplaceOptions.cs`).

The settings are checked the first time an administrator uses the marketplace, never at start-up: a server whose
`panel.env` carries a mistake still starts, and the marketplace then answers "The marketplace address is not set up
correctly on this server" instead of connecting.

### The download address must be on the same origin

A download is a separate request to the address the signed grant carries. The panel accepts that address only when it is
`https` on the same host name and port as `Marketplace__BaseUrl`, with no user information; anything else is refused with
"The marketplace sent a download address whose scheme, host or port differs from the marketplace address configured on
this server" and nothing is downloaded. The service builds the address from its own `Marketplace__PublicBaseUrl`, so
the two settings must name **the same origin** in every environment:

| Service `Marketplace__PublicBaseUrl` | Panel `Marketplace__BaseUrl` | Result |
|---|---|---|
| `https://api-marketplace-maran.innovayse.com` | `https://api-marketplace-maran.innovayse.com` | Works. |
| `https://marketplace.internal:8443` | `https://marketplace.internal:8443` | Works (the port is part of the origin). |
| `https://marketplace.internal` | `https://marketplace.internal:8443` | Every download is refused: the port differs. |
| `https://other-host.example` | `https://api-marketplace-maran.innovayse.com` | Every download is refused: the host differs. |

The service requires `PublicBaseUrl` to be an origin (scheme, host, optional port) with no path, user information, query
or fragment and refuses to start otherwise, because the grant address is formed from the origin alone and a path prefix
would be dropped silently. A service behind a reverse proxy that serves it under a path prefix therefore cannot hand out
a working grant address; publish it at the root of its host.

A different host also needs its own signing key: the panel believes only answers signed by the key in
`Marketplace__MetadataKeyPath` (below), so the host you point it at must sign with a key you have installed there.

## How an answer is verified

Every catalogue, module, update and key-list answer, and every download grant, carries a signature over its exact bytes,
made with the marketplace's metadata key (ECDSA P-384 with SHA-384), and a date. The panel checks the signature before it
reads the content, and refuses an answer signed longer ago than 24 hours or past its own expiry. The matching public key
is shipped with the installer as `installer/keys/marketplace-metadata.pub` and installed to
`/usr/local/maran/keys/marketplace-metadata.pub`; the repository itself commits no key, so it is supplied with the
release. **If the installer bundle you used did not contain the key, the installer printed "marketplace key not bundled:
the marketplace will show catalogues as unverified" and the marketplace cannot verify anything** until the file exists
(the panel reads it at each check, so copying it into place needs no restart, but an unverified catalogue never allows an
install).

The package itself is a different check with a different key: the loader verifies the package's signature against the
module-signing key before anything is loaded (`docs/marketplace/ipm-format.md`). Two keys, two jobs: the metadata key
can say what exists, and only the module-signing key can make a package acceptable.

## Installing a module

The catalogue shows, for each module, what it is, who published it, its price text, the newest version that fits this
panel, and the licence state the host reports: not required, entitled, not purchased, lease expired or unknown. The panel
decides whether the install button is offered (entitlement, panel version range, revocation, a verified catalogue, the
marketplace being on, the module not being installed already); the screen only shows the answer and the reason.

Before installing, the dialog shows the host's copy of the module's signed manifest: publisher, licence tier, the
minimum panel version, the database schema it will own, the modules it depends on, the parts of the agent it may drive
and the permissions its endpoints use. Text from inside the package is never shown. You tick that you have read it and
type your password.

The install runs as a task you can follow on the task list: checking the entitlement, downloading, verifying the
signature, staging. The panel downloads into `Marketplace__PackageDirectory`, counting the bytes and computing the
SHA-256 as they arrive, stops at one byte more than the size announced, compares the fingerprint with the signed
catalogue, and only then hands the file to the loader, which verifies the package's own signature and fit. A file that
is the wrong size, has the wrong fingerprint, or comes from an address on another host is discarded and nothing is
installed. The downloaded file is deleted when the task ends.

### "Restart required"

A successful install does **not** make the module appear. It ends in the state *installed*, shown as "restart
required": the package is verified and kept on disk, and the module activates the next time the panel starts. The reason
is structural: message handlers, controllers and database migrations are taken from the list of modules when the panel
process starts, and cannot be added to a running one. The screen offers a **restart** action, which asks the panel to
restart itself (the process exits with status 75 and systemd, `Restart=on-failure`, starts it again). The interface is
unreachable for a few seconds; the screen waits and reconnects. Migrations of the new module run on that start, and its
menu items and routes appear after it. Every installed package is verified again at every start, so what runs is what
passed the checks this time.

## Updating, rolling back and removing

- **Check for updates** sends the update request described above, once, when you press it. It marks modules that have a
  newer compatible version and modules whose installed version the host has withdrawn (revoked). A revoked module is
  marked, never removed or stopped for you; you decide.
- **Update** installs a newer version the same way as an install (password, task, restart required). The previous
  version stays recorded so that a rollback has something to return to. The panel refuses a catalogue version older than
  the highest it has already seen for that module.
- **Roll back** returns a module to the version it ran before its last update; it is also an administrator action with
  the password and a restart.
- **Remove** unloads the module and removes its package. **It keeps the module's data**: the module's database schema
  stays where it is and the list marks the module "data kept". There is no "delete the data" button; removing a module's
  tables is a separate, deliberate operation. Installing the same module again finds its data.

Only one operation on a module can run at a time; a second one is refused with "Another operation on this module is
still running". Starting the same install twice returns the task already running instead of making a second.

## When something fails

A failure never changes a module that is already installed: they keep running exactly as they were. The wording below is
the panel's own text; the host's sentences are never shown.

| What you see | What it means |
|---|---|
| The marketplace is switched off. | An administrator has not switched it on (or has switched it off). |
| The marketplace address is not set up correctly on this server. | `Marketplace__BaseUrl` or another setting fails its check. Fix `panel.env` and restart the panel service. |
| The marketplace could not be reached. | Network failure, a timeout, or the host answered with a server error or "try again later". A read is retried twice before this is shown. When the host says when to come back (`Retry-After`), the panel waits for that long, but never more than 2.5 seconds before each retry and 5 seconds in all, so a host that asks for more cannot keep the page waiting. |
| The marketplace answer could not be verified. | The answer carried no valid signature from a key the panel trusts. Nothing was used. |
| The marketplace answer is too old to be trusted. | The signature is genuine but older than 24 hours, dated in the future, or past its expiry. Check again in a few minutes. |
| This server has no marketplace verification key. | The key file is missing or unreadable. Restore `/usr/local/maran/keys/marketplace-metadata.pub`. |
| This module needs an active licence on this server. | The panel has no lease that is good now to present: no licence, an offline licence, or a lease that is missing, ended or not trusted. |
| Your licence does not include this module. | The host says the licence does not cover it. Buying happens in the vendor's own account, never in the panel. |
| This version of the module does not work with the version of the panel. | The version is outside the panel version range. |
| This version of the module has been withdrawn by its publisher. | The version is revoked. |
| The marketplace sent a download address whose scheme, host or port differs from the marketplace address configured on this server (Marketplace__BaseUrl), so nothing was downloaded. The service's public address and this setting must name the same origin. | The grant pointed at another origin than `Marketplace__BaseUrl`. Nothing was downloaded. Make the service's `Marketplace__PublicBaseUrl` and this setting name the same origin (above). |
| The marketplace no longer knows this server's licence. | The service has no record of the licence id in the lease (`licence_unknown`). Install the licence again. |
| This server's licence has been revoked. | The service revoked the licence (`licence_revoked`). Contact the licence issuer. |
| The marketplace is receiving too many requests from this server. | The host answered "too many requests" (`rate_limited`). Wait and try again. |
| The marketplace no longer accepts this download. | The grant expired or was already used (`not_found` or `410` on the download). Start the installation again. |
| The downloaded package is not the size / does not match the fingerprint the marketplace announced. | The download was discarded. Nothing was installed. |
| The marketplace tried to send this panel to another address / sent more data than this panel accepts / sent an answer this panel could not understand / refused the request. | The conversation was stopped on the panel's side. |
| Confirm that you have read what this module is allowed to do. | The acknowledgement box was not ticked. |
| The password you entered is not correct. | A wrong password, a locked sign-in and an account that no longer exists all give this one answer. A wrong password counts toward the same sign-in lockout as a wrong password at the sign-in page. |

An unverified or stale catalogue is shown as "could not verify" with the installed list still visible; it cannot start an
install.

## Installing from a package file, with no internet access

A server that must not reach the internet should leave the marketplace off. Being honest about what exists today:

- The seam for a package that did not come from the marketplace exists as an interface: the loader's install takes any
  package source (`IModuleLoader.InstallAsync`), and verifies the package's signature, fit and downgrade rules the same way
  whatever it came from.
- **This repository has no panel screen and no `maran` command that installs a package file.** The open loader refuses
  every package, and the screen to pick a file belongs to the same separately distributed loader. Until that exists, an
  offline server cannot add a module from this repository's tooling.
- What you can do on any machine, with no panel and no network, is check a file before you carry it:
  `maran ipm verify` (below) tells you whether a package is signed by the module-signing key you hold and fits a panel
  version.

## What is not promised

- **A module is trusted code.** A signed module runs inside the panel process with the privileges of the unprivileged
  service account: it can read what that account can read (including `panel.env` and the database through the panel's own
  connection), act as the panel, and reach the network. It is not sandboxed. The checks limit WHO can sign and WHAT the
  loader accepts; they do not contain a module that is signed and malicious. Install modules from publishers you trust.
- **The root agent is never extended.** Downloaded code runs in the panel process only. A module can use only the agent
  operations the panel already has, only those it declared, and the agent loads no external code
  (`rules/architecture.md`, `rules/security.md` item 13, which also states where that check stops).
- **A module's screen is isolated, its backend is not.** If a module has a screen, the panel shows it in an isolated frame
  with no access to the administrator's session; that protects the session from the screen, not the panel from the
  module's code (`rules/security.md` item 14).
- **Licence encryption is access control, not secrecy.** A package may be encrypted for the licence that bought it. That
  keeps a paid package from being freely downloadable; it is not integrity (the signatures are) and it does not hide the
  code from the licensee, because the panel is source-available.
- **A lapsed licence removes nothing.** After the grace period only paid modules stop answering (they return the licence
  response, their jobs stop); their data is kept and the core of the panel is unaffected. The marketplace screen still
  lists and removes.
- **The second review of this surface is outstanding.** The privileged changes behind the marketplace (package
  verification and loading, the download path, the framed screens and the installer's key and directory step) are
  recorded in the threat note with a second review that has not been done yet (issue #331).

## Module authors

Building, packaging and signing a module is a developer task, done with the toolbox command `maran ipm` (the
`scripts/maran` entry point of this repository, not the root command on a server):

```
maran ipm package --project <csproj> --out <dir> [--spa <dir>] [--source-date-epoch <n>]
maran ipm sign --package <file.ipm> [--key <pem>]
maran ipm verify --package <file.ipm> --pubkey <pem> [--panel-version <semver>]
maran ipm selftest
```

`package` writes `<dir>/<id>-<version>.ipm`, unsigned and byte-for-byte reproducible; `sign` adds the signature with the
key in `--key` or `$MARAN_MODULE_SIGNING_KEY` and never prints it; `verify` asks the open verification library whether the
package is acceptable and takes the module-signing PUBLIC key; `selftest` packages the sample module twice, signs it with a
throwaway key and proves that each of several defects is refused. The package layout, the manifest members, the signature
and the size caps are specified in [`docs/marketplace/ipm-format.md`](../marketplace/ipm-format.md); what a module declares
about itself is in [`docs/modules/module-contract.md`](../modules/module-contract.md); a working sample is
`backend/samples/hello-module`. The module-signing private key never lives in this repository, and a published module is
signed by its holder, not by the marketplace.
