# Polygon images

One folder per **version under test**, because "which distributions has this actually run on?" is a
question the layout should answer without reading any script.

```
images/
  install-debian.Dockerfile     the Debian-family fresh-server template (BASE_IMAGE arg)
  install-rhel.Dockerfile       the RHEL-family fresh-server template (BASE_IMAGE arg)
  install-suse.Dockerfile       the SUSE-family fresh-server template (BASE_IMAGE arg)
  <version>/base.txt            the pinned base image for that version — the one per-version FACT
  <version>/suite.Dockerfile    where it exists: the older agent/installer suite image
  <version>/systemd.Dockerfile  where it exists: the narrow "does systemctl start nginx" image
```

| folder | distribution | glibc | notes |
|---|---|---|---|
| `ubuntu22/` | Ubuntu 22.04 LTS | 2.35 | |
| `ubuntu24/` | Ubuntu 24.04 LTS | 2.39 | carries the agent suite image and the systemd image |
| `ubuntu26/` | Ubuntu 26.04 LTS | 2.43 | |
| `debian12/` | Debian 12 | 2.36 | |
| `debian13/` | Debian 13 | 2.41 | |
| `alma8/` | AlmaLinux 8 | **2.28** | the FLOOR — the agent is built against it (issue #48) |
| `alma9/` | AlmaLinux 9 | 2.34 | carries the agent suite image |
| `alma10/` | AlmaLinux 10 | 2.39 | |
| `rocky8/` | Rocky Linux 8 | 2.28 | |
| `rocky9/` | Rocky Linux 9 | 2.34 | |
| `oracle8/` | Oracle Linux 8 | 2.28 | |
| `oracle9/` | Oracle Linux 9 | 2.34 | |
| `oracle10/` | Oracle Linux 10 | 2.39 | |
| `leap16/` | openSUSE Leap 16.1 | to be measured with `ldd --version` in the pinned image | the SUSE family; carries the agent suite image; **nightly only** (owner decision 223), the pull-request smoke matrix stays `[ubuntu24, alma9]`; base pinned by TAG, the digest pin is owed (below) |

Every glibc above is measured from the pinned digest rather than looked up, except the `leap16/` row,
which has not been measured at all (nothing about this family was run when it was written) and says
so in its cell instead of carrying a number:

    docker run --rm --entrypoint sh "$(head -1 <version>/base.txt | sed 's/^FROM //')" -c 'ldd --version | head -1'

## The SUSE row, and what is still owed on it

`leap16/` is the family's only folder, and it is the polygon's claim for the matrix pairs
`opensuse-leap:16.0`, `opensuse-leap:16.1`, `sles:16.0` and `sles:16.1` (`installer/lib/10-preflight.sh`).

- **SLES 16 is claimed through Leap 16**, the same way RHEL is claimed through its rebuilds: SLES
  needs a subscription, so the polygon cannot pull it, and Leap 16 is its community sibling on the
  same base. That is an argument for the risk being low, not a measurement of SLES, and the two are
  different claims. **SLES 15 is not claimed at all**: the matrix does not admit it (SLES 15 SP6 is out
  of general support and SP7 cannot be tested without a subscription), so there is no folder for it.
- **The digest pin is owed.** `leap16/base.txt` and the `FROM` line of `leap16/suite.Dockerfile` carry
  `registry.opensuse.org/opensuse/leap:16.1`, a moving tag, because the digest could not be read when
  the folder was written and one written from memory would be an invention. Every other base here is
  pinned by digest so that a file's meaning cannot drift with no diff to review; until this one is, a
  rebuild of the SUSE row can change what it measures. Settle it with
  `docker buildx imagetools inspect registry.opensuse.org/opensuse/leap:16.1`, write the manifest-list
  digest (so both architectures are covered) into both places, and delete this bullet.
- **Nothing about this family was measured by us.** Every package name, unit name and path in
  `install-suse.Dockerfile` and `leap16/suite.Dockerfile` is marked UNVERIFIED beside the line that
  uses it, with the command in the pinned image that settles it. The first nightly run turns each of
  them into a fact or a named failure; until then a green SUSE row is a claim about nothing.
- **FTPS and mail are not offered on this family** (vsftpd is not packaged for Leap 16; the mail stack
  is not laid out until measured). The row therefore proves the installer SKIPS both in plain words
  and leaves no unit behind (`scripts/lib/install-polygon-suse.sh`), and the build-time assertions
  that exercise vsftpd, the PAM witness and the mail masks do not run on it. The agent's FTPS suite
  having nothing to run against here is a question for the agent's suite list, not answered by this
  folder.

## Not supported, and why no folder exists

Two families are deliberately absent, and the reasons are recorded here beside the CentOS 7 one below
so that nobody re-opens them by adding a folder:

- **Fedora** is declined (owner decision). Its releases live about thirteen months, so a Fedora
  row would turn the matrix over every six months; a polygon image would test a release that is
  already end-of-life by the time the installer is next touched, and a pin that is always stale is
  worse than no pin.
- **Alpine** is declined. It has no systemd, and the whole install polygon exists to run the real init
  as PID 1; and it is built on musl, which has no NSS, while the agent's `privs/account_ids.rs` calls
  `getpwnam_r` deliberately to resolve accounts through the host's name-service configuration. An
  Alpine row would exercise neither.

The installer refuses both (`installer/lib/10-preflight.sh` admits only the pairs it lists), and the
agent's os-release detection does not recognise them either.

## Four pairs the matrix accepts and no folder covers

`installer/lib/10-preflight.sh` accepts seventeen `id:version` pairs from the Debian and RHEL families
(the three SUSE pairs are covered by the section above). Thirteen have a folder above.
The four that do not are named here rather than left to be discovered by counting, because a gap
between what the installer admits and what has been run is exactly the thing this layout exists to
make visible:

- **`rocky:10`** — `rockylinux:10` is not published in the `rockylinux` Docker Hub repository that
  `rocky8/` and `rocky9/` pin; Rocky 10 is published as `rockylinux/rockylinux:10`. Measured, not
  assumed: `docker manifest inspect rockylinux:10` fails and `docker manifest inspect
  rockylinux/rockylinux:10` succeeds. Adding the folder is a one-line `base.txt` against that
  repository.
- **`rhel:8`, `rhel:9`, `rhel:10`** — Red Hat does not publish a freely pullable RHEL image. What is
  pullable is UBI (`redhat/ubi9` and friends), and a UBI is not the product the matrix names, so a
  folder built from one would report a pass about something else.

All four are EL rebuilds that run the same code path once preflight has matched them, and the Alma
folders exercise that path on all three EL versions. That is an argument for the risk being low, not
for the pairs being covered, and the two are different claims.

CentOS 7 is absent for a reason worth recording: it is EOL, and it cannot run the panel at all —
measured, `/api/Maran.Host: /lib64/libstdc++.so.6: version 'GLIBCXX_3.4.20' not found`. glibc 2.17 is
below what a self-contained .NET 9 publish needs.

## Both architectures

    maran installer --os alma9 --arch aarch64

Every base above except `leap16/` (pinned by tag, digest owed) is digest-pinned to a manifest **list** covering x86_64 and aarch64, so the same
`base.txt` serves both and `--arch` changes only the docker platform. The image and container names
carry the architecture, so an aarch64 run cannot quietly reuse a cached x86_64 image and report a
pass about the wrong machine.

On an x86_64 host the aarch64 run is emulated, which needs the handlers registered once:

    docker run --privileged --rm tonistiigi/binfmt --install arm64

What that proves and does not: the packaging, the units, the namespaces, the sockets and the panel's
own answers are all real — the binaries genuinely execute as aarch64 code on glibc 2.28. Performance
is not, and neither is anything about real ARM server hardware.

**One step cannot complete under emulation, measured rather than assumed.** On AlmaLinux 9 aarch64
the install reaches step 87 with the panel and the agent healthy (`"agent":"connected"`,
`"database":"reachable"`, `/setup` 200) and then fails:

```
Note: nft cannot reach the kernel here, so /etc/sysconfig/nftables.conf was not fully syntax-checked.
Job for nftables.service failed because the control process exited with error code.
```

The same step on the same distribution passes on x86_64 with no such note, so the variable is the
emulated architecture: an aarch64 `nft` binary cannot drive the netlink interface of the x86_64
kernel it is running on. The verdict is therefore left RED rather than special-cased — the install
genuinely did not complete, and a harness that reported OK here would be the kind of check this
project keeps deleting. Closing ARM64 needs one run on a real aarch64 host; everything before
step 87 is already proven. `installer/lib/10-preflight.sh` has
accepted `aarch64` from the beginning while the release built only x86_64 (issue #53), so an ARM64
operator passed the gate and found nothing to install; these runs are what make that gate's promise
true rather than aspirational.

## Why a template per family instead of a Dockerfile per version

The body of a fresh-server image is identical across versions of one family; only the base differs.
Fourteen copies would be fourteen places for a change to be applied thirteen times. So the per-version fact —
which digest is under test — lives in that version's `base.txt`, and the per-family logic lives in
one template. `scripts/lib/install-polygon.sh` reads the first and builds with the second.

## What these images prove, and what they cannot

The install images boot **real systemd as PID 1** and carry **nothing Maran needs** — no PostgreSQL,
no nginx, no openssh-server, no .NET, no python. (The SUSE template adds one thing that is the
distribution's default and not Maran's: `firewalld`, installed and enabled, because the installer must
retire and mask it and a polygon without it could not tell a masked firewalld from an absent one.) That is the whole point: the installer must bring
its own dependencies exactly as it must on an operator's fresh server. `curl`, `ca-certificates` and
`sudo` are the reader's starting point, because the documented first command is
`curl -sSL https://get.maran.innovayse.com | sudo bash`.

They exist because CI could not fail on any defect that lives in systemd itself. The `suite.Dockerfile`
images run no init at all, which is why `../stand-ins/systemctl-stand-in.sh` had to exist — and its
own header admits it "starts or stops NOTHING". Four defects hid behind exactly that and were found
by hand on a real server: a framework-dependent publish (#39), `MemoryDenyWriteExecute` against a JIT
(#40), a mandatory `ReadWritePaths=` target that did not exist yet (#41), and a step that started the
root daemon without checking it (#42).

What they still cannot settle: the kernel is the build machine's, shared with the host, so quota,
SELinux, firewall rules and enablement-at-boot remain claims only a real host settles. There is no
bootloader, no initramfs and no real device enumeration. They settle what was missing — units load,
namespaces build, binaries execute, sockets bind, the panel answers.

Every base is digest-pinned so that a file's meaning cannot drift with a moving point release and no
diff to review — with the one named exception, `leap16/`, whose pin is owed (see above).
