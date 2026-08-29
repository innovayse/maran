# Maran Development Docker Setup

**This is development-only.** Production installs never use Docker; see spec §2.

## Files

- **`docker-compose.dev.yml`**: PostgreSQL 16 service for backend development and integration tests.
  - Database: `maran_dev`
  - User/Password: `maran_dev` / `maran_dev` (dev-only trivial credentials)
  - Port: `localhost:5432`
  - Includes a healthcheck for deterministic test startup.

- **`polygon/ubuntu24.Dockerfile`**: Minimal Ubuntu 24.04 test container for the Rust agent.
  - Installs only `ca-certificates`.
  - Creates `/run/maran` socket directory.
  - No compilers, toolchains, or build tools.
  - Agent binary mounted at runtime.

- **`polygon/alma9.Dockerfile`**: Minimal AlmaLinux 9 test container for the Rust agent.
  - Installs only `ca-certificates`.
  - Creates `/run/maran` socket directory.
  - No compilers, toolchains, or build tools.
  - Agent binary mounted at runtime.

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

## Integration Testing

PostgreSQL should be running (see "Starting PostgreSQL" above). Tests can connect to `postgres://maran_dev:maran_dev@localhost:5432/maran_dev`.

For distro-specific agent testing, build an image, then mount your agent binary and test command as shown above.
