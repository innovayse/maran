# Maran Development Docker Setup

**This is development-only.** Production installs never use Docker; see spec §2.

## Files

- **`docker-compose.dev.yml`**: PostgreSQL 16 service for backend development and integration tests.
  - Database: `maran_dev`
  - User/Password: `maran_dev` / `maran_dev` (dev-only trivial credentials)
  - Port: `localhost:5432`
  - Includes a healthcheck for deterministic test startup.

- **`polygon/ubuntu24.Dockerfile`**: Ubuntu 24.04 test container for the Rust agent.
  - nginx from Ubuntu, php-fpm 8.3 from Sury — the repository the Debian adapter's package and service names are written against.
  - A pinned Rust toolchain, `protoc` and a C linker, so the agent's tests compile *inside* the image.
  - nginx includes `/etc/maran/nginx/sites` because the image RUNS `installer/lib/80-nginx.sh`'s `install_agent_config_include` — the installer's own code, not a copy of it. That is deliberate: the image used to make the edit itself, so every site test asserted a precondition the image had manufactured, and the fact that the installer did neither the directory nor the include went unseen by the whole suite. A build of these images is now the check that the installer still does it.
  - Creates `/run/maran` and `/run/maran/php`; `/etc/maran/nginx/sites` and `/etc/maran/certificates` come from the installer step above.
  - MariaDB and OpenSSH the same way: the packages come from `installer/lib/85-mysql.sh`'s own `mysql_packages_for_family`, and the SFTP group, jail base directory and sshd `Match` block come from running `installer/lib/86-sftp.sh`'s `install_sftp_prerequisites` — see `polygon/assert-installer-steps.sh` below.
  - **The build context is the repository root**, not `docker/polygon`, because the image copies a file out of `installer/`. See the build commands below.

- **`polygon/alma9.Dockerfile`**: AlmaLinux 9 test container for the Rust agent.
  - nginx from AlmaLinux, php-fpm 8.3 from Remi (`php83`), with EPEL enabled because Remi requires it and CRB enabled for `gcc`, `make` and `unzip`.
  - **`protoc` is downloaded from GitHub, not installed from a repository** — this is the build's only outbound binary fetch. AlmaLinux 9 ships protobuf 3.14, which predates proto3 `optional` and refuses `php.proto` outright. The zip is pinned by version *and* checked against its sha256.
  - Otherwise identical in shape to the Ubuntu image, including running the installer step for the nginx include.

- **`polygon/assert-installer-steps.sh`**: run at **build** time by both images, after they have installed the packages and started MariaDB.
  - It sources `installer/lib/85-mysql.sh` and `installer/lib/86-sftp.sh` and calls their functions — `verify_mysql_socket_auth`, `install_sftp_prerequisites`, `install_sshd_match_block` — then asserts the result: root authenticates over the unix socket, the `maran-sftp` group exists, `/var/lib/maran/sftp` is `root:root 0755`, and sshd_config carries exactly **one** `Match Group maran-sftp` block with its four directives after the installer function has been run **twice**.
  - It also asserts the failure paths, which no positive test reaches: the gate must refuse a root with a password *and* a root with no password at all, each with the right diagnosis, and `install_sshd_match_block` must leave an invalid sshd_config untouched rather than replacing it. To do that it really does set a throwaway root password inside the build layer and really does break sshd_config, restoring both afterwards.
  - Same reasoning as the nginx include above, now for two more areas: **the installer does the work and the image proves it**. Every one of these assertions has been checked by deleting the installer step it covers and watching the image build fail naming it.

- **`polygon/systemctl-stand-in.sh`**: installed at `/usr/bin/systemctl` in both images.
  - A container has no init system, so the `systemctl reload nginx` the agent runs has nothing to talk to and every config write would roll back before `nginx -t` was ever reached.
  - It turns a reload into `nginx -s reload` when an nginx master is running and succeeds silently when none is. It starts no service and enables none at boot; every other subcommand exits 0.
  - **Two exceptions, added for the SFTP area: `enable --now <name>.mount` really performs the bind mount the unit describes, and `disable --now <name>.mount` really unmounts it again** — after checking the unit the way systemd checks it. A `.mount` unit's file name must be systemd's escaping of its own `Where=` or systemd refuses to load it, and the stand-in refuses the same way, using `systemd-escape` — systemd's own tool — so the expectation never comes from the code under test. Without the mount actually happening the account's home would not be inside the jail, and the SFTP suite could not tell a working jail from an empty one.
  - The `disable` arm is what the account-deletion cascade needs: it takes the mount down before `userdel` removes the home the mount points at. Without it the stand-in would succeed silently, the mount would survive, and the cascade's non-recursive `rmdir` of the mount point — which is what stops a still-mounted jail from being deleted recursively — would refuse with `EBUSY`. It checks the unit is there the way systemd does, and treats "not mounted" as success so a repeated deletion converges.
  - Both arms need privileges a default container does not have; run the SFTP and account-deletion suites with `--privileged` (below). Without it the mount fails, `create_sftp_user` returns `JailFailed`, and the suite goes **red** rather than passing on a jail that was never filled.
  - The consequence, stated rather than hidden: **the reload half of the config-write protocol cannot fail in the polygon**, so `ReloadFailed` and its rollback stay covered by the `ops::safe_write` unit tests, not by any polygon test. And the mount arm is an emulation of systemd's load-time check, not systemd itself: what the polygon proves is that the unit name and the mount are right, not that a real `systemd` would schedule the unit at boot.

- **`polygon/setquota-stand-in.sh`**: installed at `/usr/sbin/setquota` in both images — the path the distro adapter names, since the agent no longer resolves the tool through `PATH`.
  - A container's overlay filesystem has no quota support, and creating an account applies a quota — so without this every polygon test would fail at account creation, before reaching the privilege or nginx behaviour it exists for.
  - It accepts every invocation and does nothing.
  - The consequence, stated rather than hidden: **quota behaviour is exercised nowhere in the polygon.** `AccountError::CommandFailed` from a refusing `setquota` stays covered by the `ops::accounts` unit tests, which assert the argv rather than the effect.

### What is pinned, and what is deliberately not

Both images build the binary whose tests decide whether a root daemon is safe, so
everything they fetch is pinned by identity and verified before use:

| Input | How it is pinned |
|---|---|
| Base image | by digest (`ubuntu:24.04@sha256:…`, `almalinux:9@sha256:…`) |
| Rust toolchain | `RUST_VERSION`, installed by a versioned `rustup-init` checked against `RUSTUP_SHA256` — never `curl … \| sh` |
| protoc (Alma only) | `PROTOC_VERSION` **and** `PROTOC_SHA256`; a GitHub release asset can be replaced in place, so a version alone is not a file |
| Sury signing key | by fingerprint (`SURY_FINGERPRINT`), checked before the key becomes the repository's `signed-by` |
| EPEL and Remi release rpms | by sha256; their URLs float because "latest" is the only address upstream publishes |

Deliberately floating, with the reason: **the nginx and php-fpm package versions.**
They are what each family ships on the day of the build, which is what the agent
will meet on a customer's server; pinning them would make the polygon test a
configuration nobody runs.

A checksum that stops matching means upstream moved the artefact. Re-pin it
deliberately, having looked at what changed — never delete the check to make a
build go green.

### Why the packages are there

The polygon is not a server. nginx and php-fpm are installed so that the agent's
*own* validation can be exercised: `safe_write` renames a rendered vhost into
place and then runs the real `nginx -t` against the real configuration tree, and
`write_pool` does the same with the real `php-fpm -t`. A fake `ConfigHost` can
only ever prove that the protocol reacts correctly to an answer nobody asked
nginx for — and the php-fpm half was worse than untested: the binary path the
RHEL adapter named (`/usr/sbin/php-fpm83`) does not exist on a Remi host at all,
which the polygon caught the first time it ran that binary.

The Rust toolchain is in the image, not mounted, because the two families ship
different glibc versions (Ubuntu 24.04 has 2.39, AlmaLinux 9 has 2.34): a test
binary compiled on the runner starts on one of them and refuses to start on the
other. The agent *binary* is still built on the host and mounted, as below.

## Starting PostgreSQL

```bash
cd /path/to/maran
docker compose -f docker/docker-compose.dev.yml up -d
```

Verify the service is healthy:

```bash
docker compose -f docker/docker-compose.dev.yml ps
```

Wait for the healthcheck to pass (STATUS should show `healthy`).

To stop (optional; leave running for integration tests):

```bash
docker compose -f docker/docker-compose.dev.yml down
```

## Building the Polygon Images

**Ubuntu 24.04:**

```bash
docker build -f docker/polygon/ubuntu24.Dockerfile -t maran-polygon-ubuntu24 .
```

**AlmaLinux 9:**

```bash
docker build -f docker/polygon/alma9.Dockerfile -t maran-polygon-alma9 .
```

## Running the Agent in a Polygon

After building the host agent binary (e.g., `cargo build --release` in `agent/`), mount it into a container:

**Ubuntu 24.04:**

```bash
docker run --rm \
  -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" \
  maran-polygon-ubuntu24 \
  maran-agent --socket /run/maran/agent.sock --allow-uid 0
```

**AlmaLinux 9:**

```bash
docker run --rm \
  -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" \
  maran-polygon-alma9 \
  maran-agent --socket /run/maran/agent.sock --allow-uid 0
```

## Running the Polygon Suites

Ten test files are `#[ignore]`d by default and run only inside a polygon:
`sites_on_a_real_host.rs`, `php_pools_on_a_real_host.rs`,
`privileges_on_a_real_host.rs`, `databases_on_a_real_host.rs`,
`monitor_on_a_real_host.rs`, `binary_paths_on_a_real_host.rs`,
`sftp_on_a_real_host.rs`, `account_deletion_on_a_real_host.rs`,
`firewall_on_a_real_host.rs` and `cron_on_a_real_host.rs`. They create real system accounts and real database
users, write real vhosts and pools, mount real filesystems, log in to a real
sshd and drop real privileges, so they refuse to run unless the image's
`MARAN_POLYGON` marker is set and the process is root — asked to run anywhere
else they fail loudly rather than skipping, because a skip reads as a pass.

From the repository root, per family:

```bash
docker run --rm -v "$PWD:/maran" -w /maran/agent -e CARGO_TARGET_DIR=/tmp/target \
  maran-polygon-ubuntu24 \
  cargo test --test sites_on_a_real_host --test php_pools_on_a_real_host \
    --test privileges_on_a_real_host --test databases_on_a_real_host \
    --test monitor_on_a_real_host --test binary_paths_on_a_real_host \
    -- --ignored --test-threads=1
```

The other four run separately, because each needs `--privileged`: SFTP and
account-deletion for the jail's bind mount, `firewall` because `nft` cannot
initialise its cache without `NET_ADMIN`, and `cron` because the Debian family's
PAM stack includes `pam_loginuid`:

```bash
docker run --rm --privileged -v "$PWD:/maran" -w /maran/agent -e CARGO_TARGET_DIR=/tmp/target \
  maran-polygon-ubuntu24 \
  cargo test --test sftp_on_a_real_host --test account_deletion_on_a_real_host \
    --test firewall_on_a_real_host --test cron_on_a_real_host \
    -- --ignored --test-threads=1
```

**Both commands name every suite explicitly, and that is the hazard this note
exists for.** An earlier version of this file listed six of the ten suites, and
the four it omitted — `cron`, `firewall`, `monitor`, `binary_paths` — were the
newest ones. Because the command passes an explicit `--test` list rather than
running everything ignored, anybody following these instructions ran six suites
while believing they had run the polygon. A suite absent from this list is a
suite nobody runs, so adding a `*_on_a_real_host.rs` file means editing here in
the same change; `maran structure` checks that the two agree.

`account_deletion_on_a_real_host.rs` is the account-deletion cascade: it gives an
account a real database and a real SFTP login, deletes the account, **creates one
again under the same name** — system user names are recycled — and asserts the new
one inherits nothing. The database is gone from the server, the old credential is
REFUSED rather than merely unlisted, the old SFTP login is refused by the real
daemon in a real session, the previous tenant's crontab is gone from the spool,
and the jail's bind mount is down. The crontab claim needs a real host on both
counts: neither family's `userdel` removes the spool file, and the two families
keep it in different places under different ownership, so only a real
`crontab(1)` can answer whether a re-created account has a table. It also proves the
cascade does not reach past the account: a neighbour named `polycascade_two`
keeps its database, its login and its mount, which is a case no unit test could
express, because `polycascade_two` is simultaneously a valid account name and the
spelling of `polycascade`'s login `two`.

That is not a convenience. The suite makes a **real bind mount** of an account's
home into its jail, which a container cannot do without `CAP_SYS_ADMIN` and an
unrestricted seccomp profile — and the mount is the whole point: without it the
home is not inside the jail, and no assertion could tell a working jail from an
empty directory. Run without `--privileged` the suite does not skip; the mount
fails, `create_sftp_user` returns `JailFailed`, and the tests go red.

Neither MariaDB nor sshd is baked in running: the images ship a data directory
and host keys, and each suite starts what it needs in its own fixture. A
container that shipped a running daemon would be pretending to be a host.

`--test-threads=1` is not a workaround: the suites share one nginx tree, one
php-fpm pool directory, one system user database, one database server and one
sshd, which is not a fixture two tests may hold at once.

## Integration Testing

PostgreSQL should be running (see "Starting PostgreSQL" above). Tests can connect to `postgres://maran_dev:maran_dev@localhost:5432/maran_dev`.

For distro-specific agent testing, build an image, then mount your agent binary and test command as shown above.
