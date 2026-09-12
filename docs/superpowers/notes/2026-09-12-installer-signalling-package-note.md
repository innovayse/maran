# Note — the installer gains a package, and it is `pkill`

**Surface:** `installer/lib/20-dependencies.sh`, step 20 of the native install. The step runs as
root and installs OS packages, so it is one of the installer's privileged steps and
rules/security.md "Sensitive change escalation" applies: a second reviewer and a note.

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from an agent session and no agent
can be one. Per rules/security.md this change may live on the branch `fix/live-findings` and **MUST
NOT merge to `main`** until a second reviewer has read it. The owner is to be told at hand-off that
this step carries an outstanding review.

## Why the change is being made

`docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md` added the session cull:
suspending a hosting account locks its logins and then ends the sessions already open, because a
lock that a connected SFTP or FTPS client never notices is a suspension in name only.
`ops::logins::end_account_sessions` does that by spawning the path
`DistroAdapter::pkill_binary()` declares — `/usr/bin/pkill` on both families — with
`--signal KILL --count --uid <uid>`.

Nothing installed that program. The panel's own package set (`base_packages_for_family`) covers what
the PANEL needs; `agent_tooling_packages_for_family` covers the programs the AGENT execs and named
the shadow suite, the quota tools and the archive tools. It did not name this one.

**Measured 2026-09-12, in the two pinned polygon base images rather than read off one machine:**

| family | image | finding |
|---|---|---|
| Debian | `ubuntu:24.04@sha256:33ceb719…` | `dpkg -S /usr/bin/pkill` -> `procps`, `Priority: required`; `/usr/bin/pkill` is present (a symlink to `pgrep`) |
| RHEL | `almalinux:9@sha256:d2515c76…` | `/usr/bin/pkill` **does not exist**; `procps-ng` is not installed; `dnf repoquery --whatprovides /usr/bin/pkill` -> `procps-ng` |

And the absence is not repaired by anything else this product installs: `nginx openssh-server
openssh-clients cronie quota passwd shadow-utils vsftpd tar gzip` installed together in that image
pull `procps-ng` in through no dependency of any of them.

So on the whole RHEL family a suspension would have ended at `LoginsError::SpawnFailed` — the panel
recording a suspension it could not carry out while the customer's open transfers went on. This is
the `passwd` row's failure repeated: the family that has the program for free cannot notice, and
the other family fails at exec time, in billing.

## What the change does

1. `signalling_packages_for_family()` — a new per-family accessor: `procps` on the Debian family,
   `procps-ng` on the RHEL family, and a named refusal for anything else.
   `agent_tooling_packages_for_family` appends it on both arms.
2. `assert_agent_tooling` gains `/usr/bin/pkill`, so the step proves the program is where the agent
   execs it on THIS host rather than trusting that the package manager succeeded.
3. `docker/polygon/assert-installer-steps.sh` gains
   `assert_every_supported_family_installs_the_process_signalling_tool` — a census, not a comparison
   of two named copies: the family list is read out of `pkg_install`'s own case arms, so a third
   family added tomorrow with no signalling arm is named rather than passed over. It also installs
   through the installer's own function and runs the operation's own argv against a uid that matches
   nothing, because the two families ship different major versions of this tool and the flags are
   the thing text cannot check.

## What a reviewer must check

- **That the package names are right for every version in the matrix**, not only for the two
  polygon images. `procps` on Debian 12/13 and Ubuntu 22.04, `procps-ng` on AlmaLinux 10 and Rocky
  9/10, were read from the families' package archives and not measured on a host here.
- **That installing `procps` / `procps-ng` adds nothing else to a server's surface** that matters.
  It is a process-inspection toolset (`ps`, `top`, `pgrep`, `pkill`, `sysctl` on the RHEL package);
  it starts no daemon and opens no port. A reviewer should confirm that reading, in particular that
  no family's build of it enables a unit.
- **That the cull's confinement is unchanged by this.** This note adds a program to a host; the
  argument that the program cannot be aimed at anything but the account's uid is the earlier note's,
  and nothing here widens it.
- **That `--signal KILL --count --uid` is accepted by the version each supported release ships.**
  Verified here on procps-ng 3.3.17 (AlmaLinux 9) and on the Debian family's procps 4.x, by the
  census, on those two releases only.

## What the author could not verify

- No real install on any of the eight supported releases: the evidence is two pinned container
  images plus the polygon builds.
- Nothing here observes a real session being culled. That is what the `#[ignore]`d polygon suites
  do (`agent/crates/agent/tests/sftp_on_a_real_host.rs`, `ftps_on_a_real_host.rs`); before this
  change those suites on the RHEL family would have failed with a message about a cull rather than
  about a missing program, which is the confusion this step removes.
