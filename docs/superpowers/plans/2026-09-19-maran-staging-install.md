# Maran Staging Install — Closing Issue #28's Real-Host Section

**Goal:** Run the installer on one disposable VPS and observe, with a positive check and its
inverse wherever one exists, the three mechanisms the polygon cannot exercise: systemd unit
control, filesystem quotas, and an ACME issuance against a real (staging) authority. Then prove
restore against data an operator would miss, and tear the box down clean. This is a document, not
code — no source file changes, no `maran test`/`maran mutate`/build.

**Why this run and not another polygon pass:** `docker/polygon/systemctl-stand-in.sh` records
`start|stop|restart` and starts or stops nothing (`docker/polygon/systemctl-stand-in.sh:22`).
`docker/polygon/setquota-stand-in.sh` "accepts and does nothing" because a container's overlay
filesystem has no quota support to apply one to (`docker/polygon/setquota-stand-in.sh:12-22`). The
ACME client "has never completed an issuance against a real authority" — it reaches Let's
Encrypt's staging directory and nonce endpoint and is refused at account registration without a
contact address (`README.md:214-222`). None of the three has ever run past that point outside a
fake authority in tests. Production never uses Docker at all (`docker/README.md:3`, spec §2), so
nothing short of a real host closes this.

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md` §4 (OS matrix), §11 (SSL, Backups),
§12 (provisioning). **Issue:** #28, section on mechanisms only a real host can exercise, plus
section C (restore against data an operator would miss).

**Scope:** one operator, one VPS, following this document top to bottom. No implementation task
list — every step below is either a command to run or an observation to make, and the two are
kept apart throughout, per `rules/testing.md`: "a check must be able to observe what it reports
on."

---

## 0. What this plan cannot promise before it is run

Two prerequisites below are not automated anywhere in this tree today. Both are named here,
first, rather than discovered mid-run:

- **There is no release artifact to install.** `installer/lib/50-artifacts.sh` fetches a
  manifest and three component archives from `https://releases.maran.innovayse.com/<channel>/manifest.json`
  (`installer/lib/50-artifacts.sh:10,36-37`) and verifies their Ed25519 signature against a key
  baked into the installer package at `installer/keys/release-signing.pub`
  (`installer/lib/50-artifacts.sh:23-29`). **That directory does not exist in this repository** —
  `ls installer/` (run during this research) shows `agent.env.example`, `install.sh`, `lib/`,
  `logrotate/`, `nginx/`, `panel.env.example`, `systemd/`, `uninstall.sh`, and no `keys/`. There is
  also no script under `scripts/` that builds a manifest, signs one, or produces the three
  component tarballs (`api.tar.gz`, `agent.tar.gz`, `frontend.tar.gz`) the offline path expects
  (searched `scripts/` and `docs/` for "offline-tarball", "manifest.json.sig", "release-signing":
  no hits). **NOT FOUND in the tree: a documented or scripted way to produce a signed release
  bundle.** Section 1 below states what the operator has to assemble by hand before step 1 of the
  install can run at all — this is new work this plan adds, because issue #28 assumed an
  installable artifact exists.
- **The product has never issued a real signed release.** `README.md:2-3` says the project is
  still "in active development toward the first release." `releases.maran.innovayse.com` is very unlikely to
  answer for the `stable` or `beta` channel today. Confirming that is Section 1, Step 1.1 below; if
  it does answer, most of Section 1's manual-bundle work is unnecessary and the plan says so at
  that step.

Everything else below rests on files read in this tree; each claim carries its `path:line`.

---

## 1. Prerequisites, exactly

### 1.1 Confirm which artifact path is live

```
curl -fsSI https://releases.maran.innovayse.com/stable/manifest.json
```

- **If this returns 200:** a real release exists; skip to 1.3 and use `install.sh` with no
  `--offline-tarball` flag. `install.sh`'s usage comment (`installer/install.sh:4`) and
  `MARAN_CHANNEL` default (`installer/install.sh:105`, value `stable`) mean the online path needs
  no flags at all.
- **If this fails (expected, per Section 0):** the operator must build an offline bundle. That
  bundle's exact required shape, read out of the code that will verify it
  (`installer/lib/50-artifacts.sh:155-184`):
  - A single `.tar.gz` that untars to: `manifest.json`, `manifest.json.sig`, and one
    `<component>.tar.gz` per `component` in `api`, `agent`, `frontend`
    (`installer/lib/50-artifacts.sh:175-176`).
  - `manifest.json` shape: `{"artifacts": {"<component>-<arch>": {"url": "...", "sha256":
    "..."}}}` (`installer/lib/50-artifacts.sh:97`) — `<arch>` is `x86_64` or `aarch64`
    (`installer/install.sh:310-314`). The `url` field is read but unused by the offline path
    (`use_offline_tarball` never calls `fetch`); only `sha256` is checked
    (`installer/lib/50-artifacts.sh:175-178`).
  - `manifest.json.sig` must be a detached Ed25519 signature over `manifest.json`, verifiable with
    `openssl pkeyutl -verify ... -sigfile` (`installer/lib/50-artifacts.sh:82-85`) against
    whichever public key the operator places at `installer/keys/release-signing.pub` relative to
    wherever `install.sh` is run from (`SCRIPT_DIR`, `installer/install.sh:21-25`;
    `installer/lib/50-artifacts.sh:28-29`).
  - This means the operator generates their own Ed25519 keypair for this run (there is none in the
    tree — `installer/keys/` does not exist), writes the public half to
    `installer/keys/release-signing.pub` in the copy of the installer package that will be
    unpacked on the VPS, signs the manifest with the private half, and packages `api.tar.gz`,
    `agent.tar.gz`, `frontend.tar.gz` from real build output (`dotnet publish` for the API,
    `cargo build --release` for the agent, the SPA's `npm run build` output for the frontend).
    **This is new scripting this plan does not supply** — it is a real gap, not an oversight to
    paper over, and closing it is the single largest unknown in this run. Record whatever script
    or manual steps are used, because nothing here does it for you.
  - Run with `sudo bash install.sh --offline-tarball /path/to/bundle.tar.gz` (usage:
    `installer/install.sh:4`).

### 1.2 OS and architecture (from the installer, not memory)

`installer/lib/10-preflight.sh:41` is the authoritative matrix, sourced from the spec:

```
ubuntu:22.04 ubuntu:24.04 debian:12 debian:13 almalinux:9 almalinux:10 rocky:9 rocky:10
```

This matches the spec's table exactly (`docs/superpowers/specs/2026-08-29-maran-design.md:40-41`:
"Debian | Ubuntu 22.04/24.04 LTS, Debian 12/13 | Sury", "RHEL | AlmaLinux 9/10, Rocky 9/10 |
Remi"). Architecture: `x86_64` or `aarch64` only (`installer/install.sh:308-314`,
`installer/lib/10-preflight.sh:127-134`). **Choose one image for this run and say so in the run
log** — this plan does not itself pick one; Ubuntu 24.04 x86_64 is the natural default because it
is also the PR-gate polygon target (`README.md`/spec reference to "PR — Ubuntu 24 + Alma 9").

### 1.3 Minimum resources (from `10-preflight.sh`, stated as a floor, not a recommendation)

- RAM: 1024 MiB (`installer/lib/10-preflight.sh:9`, checked at `10-preflight.sh:144-155`).
- Disk free on `/`: 2048 MiB (`installer/lib/10-preflight.sh:10`, checked at
  `10-preflight.sh:160-169`).
- Disk free on the filesystem that will hold `/var/backups/maran`: 10240 MiB, but this one is a
  **warning, never a refusal** (`installer/lib/10-preflight.sh:23,94-104`) — the install proceeds
  under the floor. For the restore drill in Section 5, provision comfortably above 10 GiB free on
  that filesystem so the check reports `PREFLIGHT OK` rather than `PREFLIGHT WARN`, since a warned
  disk is exactly the state where a restore of a nontrivial account can fill it.
- Ports free: whatever `MARAN_PANEL_PORT` is set to in `installer/install.sh:87` — **8443** — and
  no other port check exists in preflight (`installer/lib/10-preflight.sh:25-27`: PostgreSQL is
  unix-socket only and deliberately excluded).
- No conflicting panel footprint: `/usr/local/cpanel`, `/usr/local/directadmin`, `/usr/local/psa`,
  `/etc/webmin`, `/usr/local/lsws` must all be absent (`installer/lib/10-preflight.sh:195-205`) —
  true by construction on a fresh disposable VPS.

### 1.4 DNS that must already be true

`README.md:212`: "The domain must already resolve to the server" — SSL is HTTP-01 only, no
DNS-01, no wildcards. **Point one real domain or subdomain's A (and AAAA, if the VPS has an IPv6
address) record at the VPS's public IP before starting Section 4.** Nothing in the installer
checks this — `10-preflight.sh` has no DNS check at all (confirmed by reading the whole file;
its checks are root, OS, arch, RAM, disk, backup space, existing-install state, ports, conflicting
panels). The ACME order will fail at the HTTP-01 challenge if DNS is wrong, and that failure will
look identical to a firewall or nginx misconfiguration unless DNS is verified first, by hand,
with `dig +short <domain>` from a machine outside the VPS.

### 1.5 Values the operator must set, and which are secrets

Everything below is generated by the installer itself in `installer/lib/60-config.sh` **except**
the two marked "operator sets by hand, after install":

| Value | File | Secret? | Set by |
|---|---|---|---|
| `Security__EncryptionKey` | `/etc/maran/panel.env` | **Yes** | installer, `60-config.sh:588-591` |
| `Jwt__SigningKey` | `/etc/maran/panel.env` | **Yes** | installer, `60-config.sh:594-595` |
| `Setup__Token` | `/etc/maran/panel.env` | **Yes**, one-time | installer, `60-config.sh:597-598`; printed on the terminal only, never logged (`installer/lib/90-finish.sh:24-27`) |
| `Database__*` | `/etc/maran/panel.env` | No (peer auth, no password) | installer |
| `ASPNETCORE_URLS` | `/etc/maran/panel.env` | No | installer |
| `ReverseProxy__PeerUid` | `/etc/maran/panel.env` | No | installer, resolved from the distro's nginx user (`panel.env.example:64-66`) |
| `Firewall__SshPorts`, `Firewall__PanelPort`, `Firewall__SeedWhitelistCidr` | `/etc/maran/panel.env` | No | installer, from host facts (`panel.env.example:74-139`) |
| `MARAN_AGENT_ALLOW_UID` | `/etc/maran/agent.env` | No (not a secret — `agent.env.example:3`) | installer, `useradd`'s assigned uid |
| **`Acme__ContactEmail`** | `/etc/maran/panel.env` | No, but required | **operator, by hand, after install** |
| `Acme__DirectoryUrl` | `/etc/maran/panel.env` | No | defaults to Let's Encrypt **staging** already (`backend/src/Maran.Modules/Ssl/Options/AcmeOptions.cs:33`) — see 1.6 |

`60-config.sh`'s generated `panel.env` contains no `Acme__*` line at all — confirmed by reading
`write_config()` in full (`installer/lib/60-config.sh:563-`… through the socket/proxy-uid section)
and by `grep -n "Acme__" installer/lib/60-config.sh` returning nothing. So the panel boots on
`AcmeOptions`'s C# defaults: `ContactEmail = "admin@localhost"`
(`backend/src/Maran.Modules/Ssl/Options/AcmeOptions.cs:47`) — a syntactically valid but
unusable address that Let's Encrypt's staging authority will refuse at account registration,
exactly as `README.md:216-217` describes. **Before Section 4, the operator must append a real,
readable contact address to `/etc/maran/panel.env`:**

```
echo "Acme__ContactEmail=<a real address you read>" >> /etc/maran/panel.env
sudo systemctl restart maran-api.service
```

The file is `root:maran 0640` (`rules/security.md` item 8; confirmed by `panel.env.example:1-6`),
so this edit needs root or membership in the `maran` group.

### 1.6 Pointing ACME at staging rather than production

No action needed for a first run: `AcmeOptions.DirectoryUrl` already defaults to
`https://acme-staging-v02.api.letsencrypt.org/directory`
(`backend/src/Maran.Modules/Ssl/Options/AcmeOptions.cs:33`), and the class's own remarks say why —
staging is the default *because* production issuance is rate-limited per registered domain per
week, and a developer or an operator looping on a bug would burn that budget
(`AcmeOptions.cs:10-18`). The installer's shipped example for production deployments sets
`Acme__DirectoryUrl=https://acme-v02.api.letsencrypt.org/directory` in
`installer/panel.env.example:146`, but that file is documentation only — `60-config.sh` does not
copy it onto a real server (confirmed above) — so an installed panel is on staging until an
operator deliberately overrides it. **For this run, leave it unset and confirm the default by
reading it back:** `grep Acme /etc/maran/panel.env` should show only the `ContactEmail` line just
added; the absence of `Acme__DirectoryUrl` there is the positive confirmation that staging is in
effect, because the option's own default (not a written line) is what's serving.

### 1.7 Rate limits and what they mean for retries

Not stated as a number anywhere in the C# `AcmeOptions` or its validator — the file's own
authority on *why* staging is the default is the closest source
(`AcmeOptions.cs:10-18`, "production issuance is rate-limited per registered domain per week").
**NOT FOUND in the tree: a specific rate-limit figure for Let's Encrypt staging vs. production.**
Treat Let's Encrypt's publicly documented staging limits (which this repository does not encode
or depend on) as the ceiling for how many times Section 4's issuance and Section 4's renewal can
be repeated against the same domain in a day; if the run needs many retries, do not switch to the
production directory to "get past" a staging rate limit — that spends the production budget this
default exists to protect (`AcmeOptions.cs:13-14`), on a box whose only purpose is testing.

### 1.8 Packages the installer expects to be able to install

`installer/lib/20-dependencies.sh:67-77,101,104,138` names the two families' packages including,
load-bearing for Section 3: `quota` (Debian) / `quota` (RHEL) providing `setquota`,
`quotacheck`, `quota` binaries at `/usr/sbin/setquota` etc.
(`installer/lib/20-dependencies.sh:170`). Nothing in this step, or anywhere else in the installer,
enables quotas on the filesystem — see Section 3.1, which is the honest continuation of this line.

---

## 2. The run itself

Every command below is exact; "what it prints when it works" is quoted or closely paraphrased
from the step files' own `ok`/`echo` lines, not invented.

1. **Provision the VPS** per Section 1.2–1.4: chosen OS image, DNS record already resolving,
   inbound SSH reachable.
2. **Get the installer package onto the box** (git clone of this repository, or an unpacked
   release tarball containing `installer/`) and, if using the offline path, drop the built bundle
   from Section 1.1 somewhere reachable.
3. **Run it:**
   ```
   sudo bash installer/install.sh --offline-tarball /path/to/bundle.tar.gz
   ```
   or, if `releases.maran.innovayse.com` answered in 1.1:
   ```
   sudo bash installer/install.sh
   ```
4. **What it prints, in order** (`installer/install.sh:332-377`, each `run_step` echoing
   `==> <file>` first — `installer/install.sh:323`):
   - `Maran installer starting: <UTC timestamp>`
   - `Detected: <id> <version> (<family> family), arch <arch>`
   - `==> 10-preflight.sh` then a `PREFLIGHT OK:`/`PREFLIGHT WARN:` line per check
     (`installer/lib/10-preflight.sh:54-66`), ending `Preflight passed.`
     (`installer/lib/10-preflight.sh:238`) — or, on failure, every `PREFLIGHT FAIL:` collected
     before exit (`10-preflight.sh:234-237`), never a single-failure abort.
   - `==> 15-identity.sh` — a no-op on a fresh host (only acts on a pre-rename install).
   - `==> 20-dependencies.sh`, `==> 30-postgresql.sh`, `==> 40-user.sh`, `==> 50-artifacts.sh`
     (ending `Artifacts installed under /usr/local/maran.` —
     `installer/lib/50-artifacts.sh:224`), `==> 60-config.sh`, `==> 70-services.sh`,
     `==> 80-nginx.sh`, `==> 81-site-logs.sh`, `==> 85-mysql.sh`, `==> 86-sftp.sh`,
     `==> 87-firewall.sh`, `==> 88-cron.sh`, `==> 89-ftps.sh`.
   - `==> 90-finish.sh`, printing the setup URL and, on its own line, the setup token
     (`installer/lib/90-finish.sh:24-27` — deliberately not query-string-embedded, per that
     file's own threat-note citation).
   - `Maran installer finished: <UTC timestamp>` (`installer/install.sh:377`).
5. **Read the log**: `/var/log/maran/install.log` (`installer/install.sh:28`) holds everything
   above, timestamped, because `setup_logging` tees stdout/stderr through `awk` into it
   (`installer/install.sh:221-236`) — except the setup token, which is written only to the
   terminal's original fd 3 and is absent from the log by design (`installer/install.sh:223-227`,
   `installer/lib/90-finish.sh:24-27`).
6. **First sign-in**: open the printed URL, paste the token, create the administrator. Confirm
   `Setup__Token` in `/etc/maran/panel.env` no longer authorizes anything once used
   (`panel.env.example:42`: "The token stops working once the first administrator exists" — this
   plan does not re-derive that from the C# handler; it is stated, not re-verified, and is a good
   candidate for the operator to spot-check by re-visiting `/setup` with the same token and
   observing a refusal).

---

## 3. Observation, separated from the absence of an error — the three mechanisms

### 3.1 systemd

**What "the service started" must NOT be accepted as:** a `0` exit code from `systemctl start`
alone. `rules/testing.md`'s law applies directly here because the polygon's own stand-in already
demonstrates the failure mode: before its `show` arm existed, "EVERY subcommand it did not
recognise fell through to `*) exit 0` printing nothing at all... there was no invocation of this
stand-in that could produce a Stopped" (`docker/polygon/systemctl-stand-in.sh:39-44`) — a check
against a tool that cannot say no is not a check.

**Positive observation — stopped:**
```
sudo systemctl stop maran-api.service
systemctl is-active maran-api.service   # expect: "inactive", exit status 3
curl -sk https://<domain>:8443/         # expect: connection refused or 502 from nginx (upstream gone)
```
Also open the panel's own account-facing status if the agent's monitor surfaces
`maran-api.service`'s state (`agent/crates/ops/src/monitor/get_service_statuses.rs`,
`ServiceState::{Running, Stopped, Unknown}` — cited from the cron/firewall/monitoring plan,
`docs/superpowers/plans/2026-09-02-maran-cron-firewall-monitoring.md:386`) and confirm it also
reports Stopped — this is the inverse the polygon cannot produce, because until real `systemctl
show` answers with a real `ActiveState`, "the panel's own status agrees" has never been observed
against a real init system.

**Positive observation — started, and it actually serves:**
```
sudo systemctl start maran-api.service
systemctl is-active maran-api.service   # expect: "active", exit status 0
curl -sk https://<domain>:8443/api/v1/... (an authenticated, known-good endpoint)
```
The second command is the load-bearing one: `is-active` says systemd's opinion, a request answered
correctly is the panel's own behaviour agreeing. Do both — this is exactly the "not X but Y AND Z"
the task requires.

**What this run can prove that the polygon cannot, stated in the stand-in's own words:** "a unit
comes back after a reboot" is explicitly out of the polygon's reach ("a container has no reboot and
no init to ask... Only a real host settles that" — `docker/polygon/systemctl-stand-in.sh:69-73`).
**Add one more observation this plan's Section 2 does not otherwise force: reboot the VPS
(`sudo reboot`) and, after it comes back, run `systemctl is-active maran-api.service
maran-agent.service` and `systemctl is-enabled` on both**, expecting `active`/`enabled` on both
without any manual start. This is the one check that only a real boot answers, named as such by
the file whose limits this plan exists to remove.

### 3.2 Quotas

**The failure mode named up front, because the installer and agent do not check for it:** a quota
can be "applied" by `setquota` succeeding and still hold nothing, if the filesystem was not
mounted with quota accounting enabled. Evidence this is unhandled in this tree:

- `installer/lib/20-dependencies.sh:67-77,138,170` installs the `quota` package family (providing
  `setquota`, `quotacheck`, `quota`) on both distro families. **Nothing in `installer/lib/`
  mounts, remounts, or edits `/etc/fstab` for the filesystem holding `/home`, and nothing runs
  `quotacheck` or `quotaon`.** Confirmed by `grep -rn "quotaon\|quotacheck\|usrquota\|grpquota" agent/
  backend/ installer/ docs/` returning zero hits anywhere in the product or its docs.
- The agent's own `apply_quota` just calls `setquota -u <user> <soft> <hard> 0 0
  <ACCOUNT_HOME_ROOT>` (`agent/crates/ops/src/accounts/account_operations.rs:631-650`) and surfaces
  whatever `setquota` answers as `AccountError::CommandFailed`
  (`account_operations.rs:601-602,636-637`) — it does not check mount options first, and a
  filesystem mounted without `usrquota` will make `setquota` itself refuse (this is exactly what
  the polygon stand-in exists to paper over: "a container's overlay filesystem has no quota
  support to apply one to — `setquota` refuses on any host where the filesystem was not mounted
  with quotas enabled," `docker/polygon/setquota-stand-in.sh:12-14`).
- `QuotaBlocks::parse_hard_limit` reads `quota -u -w` and treats "no quota line" as `None`, which
  the caller folds to `quota_bytes: 0` — "not an error, just no limit"
  (`agent/crates/ops/src/accounts/quota_blocks.rs:26-32`, `account_operations.rs:617-623`). **This
  is the exact silent-failure shape the task warns about**: a quota that was never applied and one
  that was applied and then removed both report as "no limit," indistinguishable from the panel's
  side.

**What the operator must check BEFORE trusting any quota result, and how — NOT automated
anywhere in this tree:**
```
mount | grep " / \| /home "        # find the filesystem holding /home
grep  -E "usrquota|uquota"  /proc/mounts   # or: findmnt -no OPTIONS /home
```
If `usrquota` (ext4) or `uquota`/`usrjquota` (xfs) is absent from that filesystem's mount options,
quotas cannot work at all regardless of what `setquota` or the panel report, and the fix is a
`mount -o remount,usrquota <fs>` (or an `/etc/fstab` edit plus reboot) followed by
`quotacheck -cum <mountpoint>` and `quotaon <mountpoint>` — none of which this installer performs.
**Do this check first, before creating any account in Section 4**, and record its output in the
run log; a positive quota observation taken without it is worthless.

**Positive observation, and its inverse, once the mount option is confirmed:**
```
# Create a test account with a small quota through the panel (e.g. 50 MiB), then, as that account:
dd if=/dev/zero of=/home/<account>/toobig bs=1M count=60   # expect: write fails partway, "Disk quota exceeded"
quota -u <account>                                          # expect: usage near the hard limit, an asterisk on the exceeded line
```
The inverse — the write refused by the filesystem, not by the panel's own accounting — is the
actual proof; a panel dashboard number moving is not evidence the filesystem itself is bounding
anything, because `usage()`'s `quota_bytes` comes from parsing the same `quota -u -w` output the
kernel would refuse to populate on an unmounted-for-quotas filesystem
(`account_operations.rs:608-624`). Then confirm the panel's own usage report
(`AccountUsage.used_bytes`/`quota_bytes`) agrees with what `quota -u` printed directly — that is
"the panel reports the usage the filesystem reports," not a separate number the panel invented.

### 3.3 ACME against Let's Encrypt staging

**How far the client is known to reach today, stated exactly as the README states it**
(`README.md:214-222`): it reaches the staging directory and its nonce endpoint, and is refused at
account registration when no contact address is configured. Ordering, challenge validation,
finalising and downloading "have been exercised only against a fake authority in tests." This plan
exists to move that line.

**What the operator must configure — already covered in 1.5/1.6:** `Acme__ContactEmail` set to a
real, monitored address (`AcmeOptions.cs:36-47`; validated for shape by
`backend/src/Maran.Modules/Ssl/Validators/AcmeOptionsValidator.cs` against the panel's one email
rule, per that file's own remarks). Without it, registration fails exactly where the README says
it does, every time — this is the check to run FIRST, deliberately reproducing the known failure,
before fixing it and moving on:
```
# Before setting Acme__ContactEmail: order a certificate through the panel for the DNS-verified domain.
# Expect: a registration failure, surfaced to an administrator caller as the underlying detail
# (rules/security.md's role-aware error mapping — an admin sees it, a customer would not).
```

**Positive observation chain, once the contact address is set (1.5) — each link separately, not
"it worked":**
1. **Order accepted, not just requested.** The panel's audit journal
   (`Maran.Modules.Ssl/Services/CertificateAuditJournal.cs`) records an issuance attempt; check it
   shows progress past account registration this time.
2. **HTTP-01 challenge served.** The token file lands under the account's own document root at
   `.well-known/acme-challenge/<token>`, written by the agent as the owning account, mode `0644`
   (`backend/src/Maran.Modules/Ssl/Services/AcmeChallengeWriter.cs:22-31,50-58`). Confirm it with:
   `curl -s http://<domain>/.well-known/acme-challenge/<token>` from outside the VPS, independent
   of the panel's own claim that it wrote the file — the file existing on disk and the file being
   reachable over HTTP are two different facts, and only the second is what Let's Encrypt's own
   validator will see.
3. **A certificate exists on disk.** `Acme__CertificateStorePath` (default
   `/var/lib/maran/certs`, `panel.env.example:154`) should contain new material after the order
   completes; check ownership (root-only, outside every account's home — `AcmeOptions.cs:50-53`)
   with `ls -l` and `stat`, not just presence.
4. **Its chain validates, and it is the STAGING chain — say so explicitly.** `openssl x509 -in
   <cert> -noout -issuer` should name Let's Encrypt's staging intermediate ("(STAGING)" in the
   issuer CN is Let's Encrypt's own convention). This is the positive proof staging, not
   production, was used — a browser will separately show "not trusted," which is the expected,
   correct staging behaviour (`AcmeOptions.cs:15-16`: "Staging issues untrusted certificates, which
   is exactly the right feedback").
5. **The vhost serves it.** `curl -sk https://<domain>:8443/` (or the site's own vhost port, if
   different) followed by `openssl s_client -connect <domain>:<port> -servername <domain> </dev/null
   2>/dev/null | openssl x509 -noout -issuer` to confirm the SERVED certificate, not just the one on
   disk, is the staging one just issued — two different observations again, because a stale
   previously-served cert and a freshly issued unserved one look the same from "a cert exists."
6. **A renewal run replaces it.** `CertificateRenewalHandler` re-orders anything expiring within
   thirty days (`backend/src/Maran.Modules/Ssl/Jobs/CertificateRenewalHandler.cs:15-16,32-34`) —
   it is a scheduled Wolverine message handler, not a daemon
   (`CertificateRenewalHandler.cs:22-24`), so to observe a renewal inside this run's timeframe the
   operator must either wait for the schedule or trigger the handler directly (however this
   backend exposes manual invocation — **NOT FOUND in the tree during this research pass**: no
   admin endpoint named "renew now" was located in `Maran.Modules.Ssl/Controllers/`; if none
   exists, shorten a test certificate's recorded expiry in the database by hand, or reduce
   the thirty-day window in configuration for this run only, and say in the run log which was
   done). The positive observation is a NEW certificate file (different serial number via
   `openssl x509 -noout -serial`) after the handler runs, with the OLD one still valid at
   observation time — proving replacement, not merely reissuance-after-expiry.

**What rate limits mean for retries — restated from 1.7:** staging's limits are not encoded in
this repository; do not exhaust retries by looping blindly. If account registration or ordering
fails for a reason other than the missing contact address, fix the configuration and wait before
retrying rather than repeating the same request — the code's own comment on `badNonce` retries
("One retry, and only for badNonce," `backend/src/Maran.Modules/Ssl/Services/AcmeSession.cs:148`)
shows the client already limits its own automatic retries to that single case; anything else is an
operator decision, not the client's.

---

## 4. Failure ladder

For each step: what a failure looks like, where the evidence is, and whether the box must be
rebuilt. `run_step` itself (`installer/install.sh:321-327`) is minimal — it sources one file and
calls one function; it adds no retry, no state file, and no rollback of its own. Idempotence is a
per-step property the header comment claims ("Every step is idempotent... so the whole script is
safe to re-run after an interrupted install," `installer/install.sh:14-16`) but which this plan
verifies per step below rather than accepting as one blanket guarantee, because that is exactly
the kind of confidently-stated fact this project has produced and later had to correct
(`rules/README.md`'s own accounting of stale claims).

| Step | Failure looks like | Evidence | Re-run safe? | Basis |
|---|---|---|---|---|
| `10-preflight.sh` | `PREFLIGHT FAIL:` lines, exit 1, **no changes made** | terminal output; nothing written yet | Yes, trivially — nothing happened | `10-preflight.sh:234-237`: "No changes were made." |
| `15-identity.sh` | N/A on a fresh host (only acts on pre-rename installs) | — | Yes — no-op when nothing to migrate | `install.sh:340-344` comment: "does nothing at all on a fresh host" |
| `20-dependencies.sh` | package manager error (bad mirror, network) | package manager's own stderr, captured in `install.log` | Yes — package managers are themselves idempotent on already-installed packages | inferred from package-manager semantics, not separately asserted in this file — **treat as probably safe, not proven** |
| `30-postgresql.sh` | role/db already exists in a bad state, or service fails to start | `install.log`; `sudo -u postgres psql -c '\du'` | Likely yes — not directly verified in this research pass; **NOT FOUND**: no explicit idempotence comment read in this file | — |
| `40-user.sh` | account already exists and is NOT this installer's own (no GECOS marker) | `install.log`: "an account named '...' already exists on this host and this installer did not create it" | **No** for that specific case — installer refuses rather than adopting a stranger's account (`installer/lib/40-user.sh:71` context) | `installer/lib/40-user.sh:26-71` |
| `50-artifacts.sh` | signature verification failure, checksum mismatch, missing files in the offline bundle | `install.log`: explicit "Aborting install" lines naming the reason (`50-artifacts.sh:86-89,159-183`) | Yes — `prepare_staging_dir` `rm -rf`s and recreates the staging directory every run before trusting it (`50-artifacts.sh` comment on `prepare_staging_dir`) | `installer/lib/50-artifacts.sh:41-` (staging dir rebuilt fresh each run) |
| `60-config.sh` | `panel.env` write fails mid-file under `set -u` | would leave a `.panel.env.XXXXXX` temp file in `/etc/maran`, never the real name, because the file is built into a `mktemp` target and only later becomes `panel.env` | Yes — encryption key and signing key are **preserved** on re-run rather than regenerated (`60-config.sh` comments at `Security__EncryptionKey`/`Jwt__SigningKey`, and `write_config` docstring: "Preserves an already-generated encryption key on re-run") | `installer/lib/60-config.sh:560-563` |
| `70-services.sh`–`89-ftps.sh` | unit fails to start, `nginx -t` fails, package/service specific errors | `install.log`; `systemctl status <unit>` | Each step's own idempotence is **not individually re-verified in this research pass** beyond what the header comments of `install.sh` claim generally (`install.sh:14-16`) — treat each as claimed-idempotent, confirm empirically on the run by re-running the whole installer once after a deliberate mid-step `SIGKILL` (see below) and diffing `install.log` for a clean second pass | `install.sh:14-16` (general claim only) |
| `90-finish.sh` | reads a token that no longer exists (rare: only after a prior successful finish already consumed the file semantics — see 90-finish.sh docstring) | `install.log`; terminal | Yes — it only reads back existing state, writes nothing new | `installer/lib/90-finish.sh:20-27` |

**Named test of the re-run promise, added by this plan rather than assumed:** deliberately
`SIGKILL` the installer partway through `70-services.sh` (a step file placed after several
steps that write real state) and re-run `install.sh` unmodified. Record whether the second run
completes cleanly or whether any step's `PREFLIGHT NOTE:`/`ok`/idempotence line reports something
unexpected. This is the one thing `rules/README.md`'s standard for "verified by deliberately
writing a violation" asks for, applied to the installer's own central claim, on a mechanism no
polygon container can exercise (a `SIGKILL` mid-service-start is exactly the case
`docker/polygon/systemctl-stand-in.sh` cannot answer, since it starts and stops nothing).

**Where the evidence lives, in every case:** `/var/log/maran/install.log`
(`installer/install.sh:28`), append-only across re-runs (`tee -a`,
`installer/install.sh:231`, "so a re-run after an interrupted install appends rather than
truncates"). Root-hardened (`harden_log_directory`, `installer/install.sh:168-212`) against a
planted symlink; if the run ever sees a `SECURITY:` line from that function in the log, treat the
host as compromised before this plan started and do not continue the run on it.

---

## 5. Restore, on data somebody would miss

### 5.1 What "replace" means here, precisely

`RestoreBackupCommand`'s own doc comment is unambiguous: "This is the operation that can destroy a
working account... It is replace-within-scope, not undo: what the archive holds replaces what is
there, and what the archive does not hold is untouched — the vhosts, the certificates, the crontab
and the firewall rules stay exactly as they are"
(`backend/src/Maran.Modules/Backups/Commands/RestoreBackup/RestoreBackupCommand.cs:14-19`). The
confirmation is typing the account's own system username, compared against the TARGET account's
name, never the caller's (`RestoreBackupCommand.cs:21-27`) — verify this is actually enforced by
attempting a restore with the wrong username typed and expecting a refusal before the agent is
asked for anything.

### 5.2 The deliberate-destruction drill

1. Create an account, one site under it, one database with a distinguishable row of data
   (`POST /api/v1/accounts`, `POST /api/v1/sites`, `POST /api/v1/databases` —
   `backend/src/Maran.Modules/Accounts/Controllers/AccountsController.cs:75`,
   `backend/src/Maran.Modules/Sites/Controllers/SitesController.cs:90`,
   `backend/src/Maran.Modules/Databases/Controllers/DatabasesController.cs:80`). Write a marker
   file inside the site's document root and a marker row inside the database. Record: file path,
   content, checksum (`sha256sum`); every file's owner/group/mode under the home
   (`find /home/<account> -printf '%p %u %g %m\n'`); the database's row count and the marker row's
   content.
2. Take a backup (`POST /api/v1/backups` — `BackupsController.cs:82`).
3. **Destroy on purpose**: delete the marker file, drop the marker row (or the whole database, if
   that is closer to what an operator would actually lose), and change a file's permissions to
   something wrong, so the restore has more than one kind of damage to repair.
4. Restore from the backup taken in step 2, typing the confirmation username
   (`POST /api/v1/backups/{id}/restore` — `BackupsController.cs:127`).
5. **Compare against step 1's record, not against "it looks fine":**
   - File content and checksum match exactly.
   - Ownership and mode match exactly — this is the check most likely to silently regress, because
     the agent's restore path explicitly re-applies owner/group/mode from a marker document rather
     than trusting whatever the extraction produced (`agent/crates/ops/src/backup/restore_backup.rs`
     `Placement.owner`/`Placement.group`/`HOME_MODE` fields, `restore_backup.rs:63,109,60-65`).
   - Database contents and row count match exactly.
   - Everything the archive does NOT hold — the vhost config, the certificate, the crontab, the
     firewall rule — is **unchanged** from before the restore, per 5.1's own stated scope; check at
     least one of these explicitly rather than assuming the doc comment is honored.

### 5.3 The interrupted restore — one, and its outcome must be unambiguous

The agent's own threat note is the direct source for this drill; read in full at
`docs/superpowers/notes/2026-09-09-restore-interruption-recovery-threat-note.md`. Key facts,
cited:

- The restore parks the live home, then renames a staged replacement into place; a kill in the
  window between those two renames is "the highest-risk shape in this repository," and recovery is
  driven by a marker document the agent itself writes, never by parsing a directory name
  (threat note, "The surface" and "Recognising an interrupted swap as a fact" sections, lines
  43-66 and 117-134 of that file).
- The stated design decision is **finish forward, not roll back**: because the marker is written
  only after every database the restore was asked to replace has already been replaced, the only
  safe completion is finishing the file-side swap, never reverting to old files paired with new
  databases (threat note, "The decision," lines 80-108).
- The recovery table (threat note lines 141-151) enumerates nine states by three booleans
  (marker/staging/parked) plus the live home, each with a named, tested outcome — states 1-4 are
  the ordinary recoverable cases, state 5 is refused outright (leaves the parked tree for a human,
  never guesses), states 8-9 are the "no marker, or an unreadable one" cases, which are left
  untouched rather than acted on.
- This reconciliation "runs before the socket is bound" (threat note, "Concurrency," line 163) —
  i.e., on the **next agent start**, not automatically the instant it is killed.

**The drill:** start a restore on an account with a nontrivial home (large enough that the swap
window is observable, not instantaneous), and `sudo systemctl kill -s SIGKILL maran-agent.service`
during it — timed to land inside the window between the two renames if possible (the threat
note's own author states this exact case, a real `SIGKILL`, as measured on both polygon families
during that change's own verification, though not on a booted real host — "I have not observed a
real power cut... What IS measured, on both polygon families, is the `SIGKILL` case," threat note
line 34-39). Then start the agent again (`sudo systemctl start maran-agent.service`) and inspect,
before touching anything by hand:

- `journalctl -u maran-agent.service` for the `warn`-level "one line per recovery naming the
  account and what was completed" the reconciler is stated to emit (threat note, "The decision,"
  lines 113-115).
- The account's home: it must be **either** exactly the completed restore (files match the
  archive, ownership/mode match the account, as in 5.2's checks) **or** exactly the pre-restore
  home untouched — **never a mix of old files and new database contents**, which is precisely the
  outcome the threat note argues against allowing (lines 90-94). If the outcome is ambiguous — the
  home reflects the archive but the agent log names no completed recovery, or vice versa — that is
  a finding to report, not a state to paper over by re-running the restore.
- The database(s) the restore targeted: their contents must agree with whichever outcome the home
  reflects, per the same finish-forward-or-untouched rule.

**What the operator inspects to tell "complete" from "cleanly refused" apart:** the presence or
absence of a `warn` log line naming the account, cross-checked against the file contents and
database state directly — never one signal alone. A cleanly refused case (table states 5, 8, 9)
leaves an `error`-level log line naming what was left for a human, and leaves the pre-restore data
provably intact under 5.2's own comparison method; a completed case leaves an equivalent `warn`
line and the restored data intact. Anything else — no log line at all, or a log line whose claim
the filesystem contradicts — is the defect this drill exists to catch, and is worth its own
finding rather than a retry.

---

## 6. Teardown

`installer/uninstall.sh:1-6` states its own promise: stop and remove everything the installer
created, but never customer data (PostgreSQL database, `/home/*` hosting accounts, backups)
"without an explicit, separate confirmation for each category." Read in full for this run:

- **Removed, unconditionally (no confirmation needed):** systemd units and their tmpfiles snippet
  and runtime directory (`uninstall.sh:56-67`); the nginx vhost and its two working-swap names
  `.candidate`/`.previous`/`.adopted`, plus the sites-include file
  (`uninstall.sh:69-98`) — but explicitly **not** any `*.conf.foreign*` file the installer itself
  found and could not account for during install, which the uninstaller documents as "never ours
  to write and... not ours to remove" (`uninstall.sh:81-91`).
- **Left behind by design, and what that means for a second attempt on the same box:** anything
  under `/home/*` (hosting accounts), the PostgreSQL database itself, and everything under
  `/var/backups/maran`, unless the operator separately confirms each — per the confirm() gate at
  `uninstall.sh:40-48`, bypassable non-interactively only with `--yes`/`-y`
  (`uninstall.sh:30-35`), which this plan's own disposable-VPS teardown should use, since nothing
  on this box is worth preserving once the drills above are done: `sudo bash installer/uninstall.sh
  --yes`.
- **What "cheap to abandon" means for a FAILED attempt specifically:** since preflight makes no
  changes at all before passing (`10-preflight.sh:234-237`), any run that never got past preflight
  needs no uninstall at all — destroy the VM. For a run that failed partway through
  `50-artifacts.sh` or later, re-running `install.sh` unmodified is the documented path (Section
  4's failure ladder), not `uninstall.sh` followed by a fresh `install.sh` — the uninstaller is for
  when the operator is done with the box, not for recovering from a failed step.
- **For this plan's own disposable box:** since nothing here should survive to poison a second
  attempt and nothing on it is real customer data, run `sudo bash installer/uninstall.sh --yes`
  and then, if the box will be reused rather than destroyed, verify by hand that
  `/etc/maran`, `/var/lib/maran*`, `/var/backups/maran`, `/home/*` accounts created during this
  run, and the systemd units are all gone — the uninstaller's own confirmation gates mean a
  `--yes` run answers "destroy everything" to every one of them, so a lingering directory after
  that is itself a finding, not an expected leftover.

---

## Summary of unknowns carried forward

- **NOT FOUND:** a script or documented procedure to build a signed offline release bundle
  (Section 1.1) — this is new work, not an oversight this plan can route around.
- **NOT FOUND:** any mount-option check or `quotaon`/`quotacheck` step anywhere in the installer
  or the agent (Section 3.2) — the operator must verify and, if needed, enable quota accounting on
  the filesystem holding `/home` themselves, before Section 4, or every quota observation in this
  run is meaningless.
- **NOT FOUND:** a specific Let's Encrypt staging rate-limit figure encoded anywhere in this
  repository (Section 1.7/3.3) — treat the publicly documented Let's Encrypt limits as external
  fact, not something this codebase tracks or protects against beyond the one `badNonce` retry.
- **NOT FOUND:** an admin-facing "renew now" trigger for `CertificateRenewalHandler` in
  `Maran.Modules.Ssl/Controllers/` (Section 3.3, step 6) — the renewal observation may require
  either waiting for the schedule or a deliberate, logged workaround (shortened expiry or
  configuration), which the run log must name.
- **Per-step idempotence beyond `10-preflight.sh`, `40-user.sh`, `50-artifacts.sh`, `60-config.sh`
  and `90-finish.sh` is claimed at the file-header level (`install.sh:14-16`) but not
  individually re-verified in this research pass** (Section 4) — the `SIGKILL`-and-rerun drill in
  Section 4 is this plan's way of turning that claim into an observation rather than accepting it.
- **Whether a real reboot survives with both units `active` and `enabled` (Section 3.1) has never
  been observed on a real host per the systemd stand-in's own stated limit** — this run's Section
  3.1 reboot step is the first observation of it.
