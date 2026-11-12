# Plan: the SUSE family (issue #59)

Date: 2026-11-09. Companion documents: the threat note
`docs/superpowers/notes/2026-11-09-suse-platform-threat-note.md` (second review OUTSTANDING, #331) and the
supported-systems statement in `docs/superpowers/specs/2026-08-29-maran-design.md` section 4.

Issue #59 recorded three unclosed rows of the support matrix (SUSE, Fedora, Alpine). This plan records
what was built, what was declined, and what is still owed. **Nothing built here was run on a SUSE
system.** Every task below is built and UNVERIFIED until the first `leap16` polygon run.

## Decisions that shaped it (owner decisions 217-224)

- Rows are openSUSE Leap 16.0 and 16.1 (16.0 kept until its vendor date is confirmed, #559) and SLES 16.0 and 16.1, claimed through Leap. Leap 15.6 (end of support
  2026-04-30), SLES 15 SP6 (paid extended support only) and SLES 15 SP7 (untestable without a
  subscription) are left out. Tumbleweed, MicroOS, SLED and SLES for SAP are refused although their
  `ID_LIKE` says SUSE.
- Fedora (13-month life, matrix churn every six months) and Alpine (no systemd; musl has no NSS) are
  declined, recorded, and refused by preflight and by the agent.
- Corrections to the first draft of the plan: Leap 16 uses SELinux, not AppArmor; the distribution ships
  ONE PHP minor (8.4) and no third-party repository is added; vsftpd is not packaged, so no FTPS; mail
  is not offered until measured; the polygon runs nightly only; the wire value is forced.

## Tasks

| Task | What was built | Status |
|---|---|---|
| T0. Spec and records | Spec section 4 gains the SUSE rows, family limits and the declined platforms; this plan; the threat note. | Built. |
| T1a. Agent adapter, detection, selector | `DistroFamily::Suse`; `agent/crates/distro/src/suse/` (`suse_adapter.rs` implements every concern trait; `suse_paths.rs`, `suse_services.rs`, `suse_account_programs.rs`, `suse_mail_services.rs`, `suse_dns_services.rs`, `suse_php_extensions.rs`); one arm in `adapter_for`; `opensuse-leap` and `sles` in `os_release.rs`; tests under `src/tests/suse/`, a Tumbleweed refusal, `alpine` and `fedora` refusals. | Built, UNVERIFIED. |
| T1b. Package facts and `zypper` reading | `suse_packages.rs` (names, install-failure classification, pending updates, security patches, reboot probe, mail facts that answer `None`) and `suse_package_candidates.rs` (the cache-only search and its grammar). Fail closed on every line outside the grammar. Tests use text constructed from `man zypper`. | Built, UNVERIFIED. Recorded fixtures owed. |
| T2a. Installer core steps | `install.sh` detection and web-server identity; `10-preflight.sh` matrix pairs; `20-dependencies.sh`, `30-postgresql.sh`, `68-os-updates.sh` (a Maran-owned unattended security-patch timer, private `zypp.conf`, locks file honouring the hold list), `70-services.sh`, `80-nginx.sh` (adds the `conf.d` include to `nginx.conf`, validated and reversible), `uninstall.sh`. | Built, UNVERIFIED. |
| T2b. Installer daemon steps | `85-mysql.sh`, `86-sftp.sh`, `87-firewall.sh` (SSH gate in the loaded ruleset, then firewalld disabled and masked), `88-cron.sh`; `89-ftps.sh` and `89-mail.sh` skip with a plain message and exit 0. | Built, UNVERIFIED. |
| T3. Polygon image and CI proof | `docker/polygon/images/leap16/` (`base.txt`, `suite.Dockerfile`), `install-suse.Dockerfile`, `docker/polygon/asserts/parts/suse-family.sh`, `scripts/lib/install-polygon-suse.sh`; a `leap16` leg in the agent workflow and in the weekly matrix of `nightly.yml`. PR smoke stays `[ubuntu24, alma9]`. | Built, UNVERIFIED. Never run. |
| T4. Rules, map, release notes | `rules/architecture.md`, `rules/rust.md`, `rules/testing.md`, `agent/CLAUDE.md`, `docker/README.md`, `CHANGELOG.md`. | Built. Rule text needs owner approval. |
| T5. Real-host test arms | A SUSE arm where an `*_on_a_real_host.rs` test names a family (the binary paths, firewall, monitor, PHP extensions and PHP install tests and the cron fixture carry one), to be run on the Leap image by the agent suite. | Built, UNVERIFIED. |
| T6. Fedora | Declined; no code. | Done (a refusal test). |
| T7. Alpine | Declined; no code. The reason is recorded in the spec and in `docker/polygon/images/README.md`. | Done (a refusal test). |
| Structure | `scripts/lib/check-structure.sh` knows the `suse_*` concern files and compares the families the installer names with the `DistroFamily` variants and the `adapter_for` arms; `scripts/test-baseline.txt` counts the new tests. | Built (in progress with the gate). |
| Wire | `DISTRO_FAMILY_SUSE = 3`, `PROTO_VERSION` 25, `contract-baseline.txt`, C# `AgentDistroFamily.Suse`. Forced: `AgentInfo.family` is mandatory. | Built. |

## Owed (nothing below can be done without a SUSE system)

1. **A digest pin for the Leap 16 base image.** `docker/polygon/images/leap16/base.txt` names a tag
   (`registry.opensuse.org/opensuse/leap:16.1`); every other folder pins a digest. Pin it, and record the
   measured glibc, in `docker/polygon/images/README.md`.
2. **Recorded `zypper` fixtures.** Record `list-updates` (with updates, with none, with a skipped
   repository), `list-patches --category security`, `search --details --match-exact`, a failed install
   (404, no network, unknown package) and `needs-rebooting` on a real Leap 16 container into
   `agent/crates/distro/tests/fixtures/package_manager/`, bytes as printed, and add the tests that read
   them with a negative control.
3. **The first `leap16` polygon run** (the install polygon and the agent suite). Each UNVERIFIED claim in
   the code becomes a fact or a named failure; the threat note's "could not verify" list is the checklist.
   The `timeout-minutes` of the new legs are estimates until then.
4. **An aarch64 run on real hardware**, through issue #53.
5. The `*_on_a_real_host.rs` arms are only as good as that first run; a family that is named in a test and
   never run is not verified.
6. The second review of the threat note (#331), before anything merges to `main`.

## Not in scope

PHP side by side (the distribution ships one minor), FTPS, the mail stack, Tumbleweed, MicroOS, SLES 15,
Leap 15.x, Fedora and Alpine.
