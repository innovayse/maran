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
  - `/etc/nginx/nginx.conf` includes `/etc/maran/nginx/sites`, the one line an installer adds on a real server.
  - Creates `/run/maran`, `/run/maran/php` and `/etc/maran/certificates`.

- **`polygon/alma9.Dockerfile`**: AlmaLinux 9 test container for the Rust agent.
  - nginx from AlmaLinux, php-fpm 8.3 from Remi (`php83`), with EPEL enabled because Remi requires it and CRB enabled for `gcc`, `make` and `unzip`.
  - **`protoc` is downloaded from GitHub, not installed from a repository** — this is the build's only outbound binary fetch. AlmaLinux 9 ships protobuf 3.14, which predates proto3 `optional` and refuses `php.proto` outright. The zip is pinned by version *and* checked against its sha256.
  - Otherwise identical in shape to the Ubuntu image, including the nginx include line.

- **`polygon/systemctl-stand-in.sh`**: installed at `/usr/bin/systemctl` in both images.
  - A container has no init system, so the `systemctl reload nginx` the agent runs has nothing to talk to and every config write would roll back before `nginx -t` was ever reached.
  - It turns a reload into `nginx -s reload` when an nginx master is running and succeeds silently when none is. It starts nothing and enables nothing; every other subcommand exits 0.
  - The consequence, stated rather than hidden: **the reload half of the config-write protocol cannot fail in the polygon**, so `ReloadFailed` and its rollback stay covered by the `ops::safe_write` unit tests, not by any polygon test.

- **`polygon/setquota-stand-in.sh`**: installed at `/usr/local/bin/setquota` in both images.
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
docker build -f docker/polygon/ubuntu24.Dockerfile -t maran-polygon-ubuntu24 docker/polygon
```

**AlmaLinux 9:**

```bash
docker build -f docker/polygon/alma9.Dockerfile -t maran-polygon-alma9 docker/polygon
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

Three test files are `#[ignore]`d by default and run only inside a polygon:
`sites_on_a_real_host.rs`, `php_pools_on_a_real_host.rs` and
`privileges_on_a_real_host.rs`. They create real system accounts, write real
vhosts and pools, and drop real privileges, so they refuse to run unless the
image's `MARAN_POLYGON` marker is set and the process is root — asked to run
anywhere else they fail loudly rather than skipping, because a skip reads as a
pass.

From the repository root, per family:

```bash
docker run --rm -v "$PWD:/maran" -w /maran/agent -e CARGO_TARGET_DIR=/tmp/target \
  maran-polygon-ubuntu24 \
  cargo test --test sites_on_a_real_host --test php_pools_on_a_real_host \
    --test privileges_on_a_real_host -- --ignored --test-threads=1
```

`--test-threads=1` is not a workaround: the suites share one nginx tree, one
php-fpm pool directory and one system user database, which is not a fixture two
tests may hold at once.

## Integration Testing

PostgreSQL should be running (see "Starting PostgreSQL" above). Tests can connect to `postgres://maran_dev:maran_dev@localhost:5432/maran_dev`.

For distro-specific agent testing, build an image, then mount your agent binary and test command as shown above.
