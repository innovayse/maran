# Polygon images

One folder per **version under test**, because "which distributions has this actually run on?" is a
question the layout should answer without reading any script.

```
images/
  install-debian.Dockerfile     the Debian-family fresh-server template (BASE_IMAGE arg)
  install-rhel.Dockerfile       the RHEL-family fresh-server template (BASE_IMAGE arg)
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

Every glibc above is measured from the pinned digest rather than looked up:

    docker run --rm --entrypoint sh "$(head -1 <version>/base.txt | sed 's/^FROM //')" -c 'ldd --version | head -1'

## Four pairs the matrix accepts and no folder covers

`installer/lib/10-preflight.sh` accepts seventeen `id:version` pairs. Thirteen have a folder above.
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

Every base above is digest-pinned to a manifest **list** covering x86_64 and aarch64, so the same
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
Thirteen copies would be thirteen places for a change to be applied twelve times. So the per-version fact —
which digest is under test — lives in that version's `base.txt`, and the per-family logic lives in
one template. `scripts/lib/install-polygon.sh` reads the first and builds with the second.

## What these images prove, and what they cannot

The install images boot **real systemd as PID 1** and carry **nothing Maran needs** — no PostgreSQL,
no nginx, no openssh-server, no .NET, no python. That is the whole point: the installer must bring
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
diff to review.
