# Maran Development Docker Setup

**This is development-only.** Production installs never use Docker; see spec §2.

## Files

- **`docker-compose.dev.yml`**: PostgreSQL 16 for backend development and integration tests, and —
  behind the opt-in `agent` profile — the **root agent**, so that `maran dev` can stand up the
  whole product: database, agent, API and SPA.
  - Until this profile existed there was no composition in the repository that ran the agent
    alongside the panel. `maran dev` started a database, an API and an SPA and called that the
    stack, so every local run and every browser pass drove a panel whose agent was permanently
    `unavailable` — and the agent is where nearly every interesting failure in this product
    happens. A defect on its side of the socket could not be exhibited by the only stack anybody
    ran.
  - The agent runs **as root inside the polygon image** (`maran-polygon-ubuntu24`), not on the
    host: it creates system accounts, writes vhosts, drops databases and mounts SFTP jails, and
    on a workstation each of those is destructive and needs a root nobody has non-interactively.
    `maran handshake` runs it unprivileged on the host instead, which is why that check can prove
    the wire and nothing behind it.
  - **The panel reaches the socket with no `chown`.** In production the unit is `User=root`,
    `Group=maran`, so the socket is `root:maran 0660`. The service sets `user: "0:<your gid>"`,
    which is the same mechanism with the developer's group: `srw-rw---- root <you>`, mode
    unchanged, no world access, and `--allow-uid` still narrows it to one uid. Two things here are
    development-only and are stated in the compose file beside the service: `privileged`, and a
    socket directory inside the working tree rather than root-owned `/run/maran`.
  - `docker/agent-entrypoint.sh` starts MariaDB inside the container (the image boots no init) and
    then `exec`s the agent, so the agent is pid 1 and a dead daemon takes the container with it.
    It runs again on every container (re)start, so a `docker restart` brings MariaDB back — a
    hand-started MariaDB was once lost exactly that way, and the first symptom was a misleading
    agent error on a backup. The service's healthcheck asks for both halves inside the container
    (`mariadb-admin ping` AND the bound socket), so `maran dev` waits on `healthy` rather than on
    the socket file, and a MariaDB that dies later shows as `unhealthy` in `docker ps`.
  - The service's container name and socket directory are interpolated
    (`MARAN_AGENT_CONTAINER`, `MARAN_AGENT_SOCKET_DIR`, defaults in `docker/.env.example`) so a
    SECOND instance can run beside the first: `maran dev --selfcheck` stands the whole stack up
    again under its own compose project, ports and names, proves `/health` answers
    `"agent":"connected"` by value, drives one real account creation through the API and reads it
    back with `getent` inside the container, then tears down and asserts nothing is left.
- **`docker-compose.dev.yml`**, database half: PostgreSQL 16 service for backend development and
  integration tests.
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
  - It sources `installer/lib/85-mysql.sh` and `installer/lib/86-sftp.sh` and calls their functions — `verify_mysql_socket_auth`, `install_sftp_prerequisites`, `install_sshd_match_block` — then asserts the result: root authenticates over the unix socket, the `maran-sftp` group exists, `/var/lib/maran-sftp` is `root:root 0700` **and every ancestor of it up to `/` is root-owned and not group- or other-writable**, and sshd_config carries exactly **one** `Match Group maran-sftp` block with its four directives after the installer function has been run **twice**.
  - It also asserts the failure paths, which no positive test reaches: the gate must refuse a root with a password *and* a root with no password at all, each with the right diagnosis, and `install_sshd_match_block` must leave an invalid sshd_config untouched rather than replacing it. To do that it really does set a throwaway root password inside the build layer and really does break sshd_config, restoring both afterwards.
  - Same reasoning as the nginx include above, now for two more areas: **the installer does the work and the image proves it**. Every one of these assertions has been checked by deleting the installer step it covers and watching the image build fail naming it.
  - It also holds the **on-disk boundary that four panel→root ownership defects were closed**. It RUNS `installer/lib/40-user.sh`'s `create_directory_layout` and `installer/lib/50-artifacts.sh`'s `prepare_staging_dir`, and then looks at every directory they made: `/usr/local/maran`, `/etc/maran`, `/var/lib/maran`, `/var/log/maran` (root's, since root and the root nginx master append to files directly inside it), its panel-owned `panel/` subdirectory and its root-only `sites/` subdirectory (`root:root 0750` — the site-log escalation fix, whose whole defence is the ownership and mode of every ancestor), `/var/backups/maran`, `/home/.maran-restore`, `/var/lib/maran-scratch`, `/var/lib/maran-sftp`, `/run/maran` and `/var/lib/maran-artifact-staging`. Each one is asked four separate questions — does it exist, is it a REAL directory rather than a symbolic link to one, which uid and gid own it, and what is its exact mode — and every ancestor up to `/` is required to be root-owned and not group- or other-writable. The fourth defect is the one that could not be fixed by moving anything, so the ancestor walk is aimed at the three log **leaves** root appends to — `install.log`, `nginx-access.log`, `nginx-error.log`, read out of `install.sh`'s own list — and its control puts the defect back by chowning `/var/log/maran` to the panel uid, requires all three refusals by name, restores it and requires all three acceptances. Both the plant and the restore are read back with `stat` and disagreement is a failure: a `chown` that silently did not take would leave the walk looking at a healthy directory and the control reporting itself held while it measured nothing, which is how three mutations in this repository were once scored blind.
    - The symlink question is asked separately from the directory question because `[ -d ]` follows symlinks and `stat` without `-L` reports the link's own owner and mode, so a check that asked only those two would have watched the attack happen. That attack is not hypothetical: the bulk scratch used to live inside `panel`-owned `/var/lib/maran`, and the owner of a directory can rename an entry aside and leave a symlink at that name without ever having permission to enter it — measured delivering a customer's plaintext database dump into a panel-readable file. The ancestor walk is the assertion that actually observes what the fix changed, because all three fixes are relocations: the same mode, moved to a path whose ancestors are root's. The third one is the SFTP jail base. It was `/var/lib/maran/sftp`, `root:root 0755` and asserted to be exactly that — and that assertion stopped one level too early, so nothing here saw that OpenSSH refuses a chroot with any non-root path component and that **no SFTP login had ever worked on a real install**: `Accepted password …` in the daemon's log, then `bad ownership or modes for chroot directory component "/var/lib/maran/"`, then a closed connection. That is why the walk, and not another mode check, is what the jail base is now asked for.
    - The log directory is also where this file states a blind spot **in its own output**, because it has one it cannot close. Neither root writer that made `/var/log/maran` a defect runs in this image, and the nginx one could not be gated even where it does run: the root nginx master creates and re-opens `nginx-access.log` and `nginx-error.log` itself, at the fixed paths the vhost names, on every start, every reload and every `SIGUSR1` — always after `install.sh`'s `harden_log_directory` and after step 40's assertion have finished. The directory's ownership is the entire defence, not a check standing in front of the write, and the assertion prints an `UNOBSERVED HERE` block saying so and naming what a booted host would have to show instead.
    - The walk itself gets an **inverse control**, for the reason every gate here does: `/var/lib` is root-owned on both families, so on a healthy image the walk never refuses anything and would pass with its body deleted. It is handed a root-owned `0700` directory whose parent is `panel`-owned — the SFTP defect's exact shape, the one a mode check cannot see — then a group-writable ancestor and a world-writable one, and must refuse each with a diagnosis naming what it found; then it must accept the real relocated jail base.
    - The expected owners and modes are written out as **literals here on purpose**, which is the opposite of the rule the `create_directory_layout` block in the Dockerfiles follows, and the difference is worth stating. There, calling the installer instead of repeating its `install -d` means the polygon's directories are made the way a real install makes them. Here the literals ARE the check: an assertion that reads its expectation out of the code it is checking agrees with that code by construction and can never fail, which is exactly how a mode gets loosened with nothing going red. Same arrangement as `check-structure.sh` comparing `ReadWritePaths=` against `agent_paths.rs`.
    - Both installer gates get an **inverse control**, because the positive assertions above would pass just as happily with the gates deleted — `install -d` had already made the directories right. Step 40's `assert_root_only_directory` is handed a symlink, a `panel`-owned directory, a group-readable one and a world-writable one and must abort on each with the right diagnosis, then handed the real thing and must accept it. Step 50's gate is inline in `prepare_staging_dir`, which `rm -rf`s and re-creates before it looks at anything, so root always repairs a planted directory and the gate would never see one; it is driven instead by an `install(1)` stand-in placed first on the step's PATH — the same mechanism `run_nginx_step` uses for a hostile nginx — which creates the directory 0777, and then as a symlink. A marker file proves the stand-in was actually reached, so a PATH prefix that failed to take effect cannot pass as a refusal that never happened.
    - The systemd half is asserted as **text, and says so in its own output**: every writable root the two fixes touch appears in `maran-agent.service`'s `ReadWritePaths=`, the release staging directory appears in NO unit's, and `EnvironmentFile=` comes before `Environment=PATH=` — that ordering is what stops a `PATH=` line in `agent.env` from beating the deliberately empty PATH, the unit parses and starts either way, and nothing else in this repository observes it. The assertion then prints an `UNOBSERVED HERE` block naming what a booted host would have to show instead, because this image boots no systemd and a text check that reads like a runtime check is the defect this file exists to avoid.
    - Every one of these has been proved in both directions on both images: ten planted violations — a loosened mode, a symlink in place of a directory, a `panel`-owned scratch, the scratch and the staging directory each moved back to their pre-fix locations, each gate neutered to `return 0`, the two `Environment` lines swapped, a writable root dropped from `ReadWritePaths=` and the staging directory added to it — each one refused with a message naming what it found, and the unmodified tree accepted.
  - It also holds the two assertions the **backup root** owes, one per direction of the same
    promise. `assert_preflight_warns_about_backup_space_without_refusing` drives step 10's
    `check_backup_space` — the real function, unmodified — through a `df` stand-in placed first on
    the step's PATH, the same mechanism the staging gate uses for `install(1)`: one MiB free must
    produce a `PREFLIGHT WARN` naming the number and the backup root, a filesystem with plenty must
    produce `PREFLIGHT OK` (a check mutated to warn unconditionally passes every test that only
    hands it a starved disk), the failure flag must still read 0 afterwards because this is a
    warning and an operator who mounts a backup volume after installing must not be refused an
    install, and a fourth case runs the function against **this family's real `df`** because the
    stand-in's format is this script's belief about `df` rather than the tool. A marker file proves
    the stand-in was reached, so a PATH prefix that failed to take effect cannot pass as a warning
    that never happened.
    `assert_the_uninstaller_keeps_the_backup_root` plants a real account directory and two real
    artifacts at the real path in the layout `ops::backup` writes, runs the uninstaller's deleting
    functions in `main`'s order, and requires the artifact to be there afterwards with its bytes
    unchanged, the transcript to name `/var/backups/maran`, and the count to say two. That set of
    positive assertions would pass just as happily against an uninstaller that never made the
    promise — there is no deletion to catch on a healthy tree — so its inverse control puts the one
    line in: a `sed` copy of `uninstall.sh` carrying `rm -rf /var/backups/maran` beside the three
    siblings `remove_var_lib` really does delete, verified to have landed with `cmp` and a grep
    count before it is scored, which the check must refuse naming the path it lost. It restores the
    layout its deleters removed through step 40 and step 86 and checks all four directories came
    back, and it prints an `UNOBSERVED HERE` block: these are the uninstaller's shell functions
    against a planted file, not an uninstall of a real install, and what would settle it is one
    real install, one real backup taken through the panel, `bash uninstall.sh --yes`, and the
    artifact still on the disk afterwards.
  - Run with **no arguments**, which is how both Dockerfiles run it and the only mode that counts. Named function arguments run just those assertions and label the output `SUBSET RUN … this is not the polygon build gate`; that is for development, never for a verdict.

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

## Starting the whole development stack

```bash
source scripts/dev
maran dev            # database, agent, API, SPA — Ctrl+C stops everything
maran dev --no-agent # the old shape: no agent, /health reports it unavailable
maran dev --stop     # leaves nothing running, agent container included
```

## Starting PostgreSQL alone

```bash
cd /path/to/maran
docker compose -f docker/docker-compose.dev.yml up -d
```

To add the agent to a hand-driven compose stack, opt into its profile and tell it who you are —
`maran dev` exports both for you:

```bash
MARAN_DEV_UID=$(id -u) MARAN_DEV_GID=$(id -g) \
  docker compose -f docker/docker-compose.dev.yml --profile agent up -d
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

Build through the harness, so the image records what it was built FROM. The label is what every
currency check below reads; an image built without it cannot be shown to be current and is refused
by name rather than believed.

**Ubuntu 24.04:**

```bash
. scripts/lib/polygon.sh
polygon_build_labelled "$PWD" ubuntu24 "$(polygon_fingerprint "$PWD" ubuntu24)" \
  maran-polygon-ubuntu24 /dev/stdout
```

**AlmaLinux 9:**

```bash
. scripts/lib/polygon.sh
polygon_build_labelled "$PWD" alma9 "$(polygon_fingerprint "$PWD" alma9)" \
  maran-polygon-alma9 /dev/stdout
```

The equivalent by hand, if you would rather see the whole command — the label is not optional:

```bash
docker build --label "maran.polygon.fingerprint=$(polygon_fingerprint "$PWD" ubuntu24)" \
  -f docker/polygon/ubuntu24.Dockerfile -t maran-polygon-ubuntu24 .
```

### `:latest` carries no currency guarantee — pass `--no-cache` before you score

Both commands above write a FLOATING tag. A tag is a name, and nothing on a developer's machine ties
that name to the Dockerfile that defined it, so an image built yesterday keeps answering to
`:latest` after today's edit and a suite run against it measures the previous tree. That is not
hypothetical here: `maran-polygon-alma9:latest` on one machine was built sixteen hours before
`alma9.Dockerfile` gained its `chmod 0400 /etc/shadow` step, and the FTPS suite run against it
reported **3 passed / 12 failed** — twelve reds with no code defect behind any of them.

Two things follow, and neither is a new check:

- **Rebuild with `--no-cache` before a scoring run, and say that you did.** The build-time assertion
  at the end of `alma9.Dockerfile` cannot help here, and it is worth being exact about why: it
  asserts that the file the build just prepared has the mode the Dockerfile asked for, and it holds
  every time it runs. The failure is that no build ran at all. An assertion inside a build is blind
  to the absence of the build, so a second one would add nothing.
- **The currency question already has an answer in this repository, in one caller.**
  `polygon_fingerprint` (`scripts/lib/polygon.sh`) hashes `docker/polygon/**` plus `installer/**` —
  the images run the installer's own steps at build time, so an installer edit changes what the image
  is — and `polygon_ensure_image` tags the image WITH that hash, so a stale image has a different
  name and simply is not found. It prints `STALE: maran-polygon-<family>:latest exists but was built
  from other sources` and declines to use it. That was `maran mutate --polygon` and nothing else: the
  `:latest` pair these two commands produce, which the run commands below name and which the scoring
  lane consumes, used to be outside it.

  **It is inside it now, and the wiring is a LABEL plus a line in the log.** The build above records
  the fingerprint in the image (`maran.polygon.fingerprint`), so an image can be asked what it was
  built from wherever it travels. `maran polygon stamp <family> [image]` asks that question at RUN
  time — before a container starts — and writes the answer into the run's own log:

  ```bash
  mkdir -p /tmp/polygon-logs
  scripts/maran polygon stamp ubuntu24 | tee -a /tmp/polygon-logs/step-1.log
  # ... then the docker run below, teeing (with -a) into the same file
  ```

  `maran polygon verify` then reads that line back at SCORING time and refuses to score logs whose
  image was built from sources this tree no longer has — `POLYGON VERDICT: ABORTED`, never a pass.
  Both halves exist because neither can see what the other sees: the run can inspect the image and
  cannot know which log will be scored; the score may run days later on a machine that never held
  the image, and can only read what the run wrote down. A log with no stamp line at all is refused
  too, because "produced before this check existed" and "produced against an image nobody checked"
  are the same thing to a reader.

  Its own stated blind spot applies either way: a fingerprint covers the SOURCES in this tree, so
  a matching one may still hold older upstream packages than a fresh build would install. And the
  fingerprint covers `installer/**` entirely, comments included — an unrelated installer edit is
  enough to make a previously built image read as stale. That is deliberate and discussed in
  the fingerprint's own doc comment in `scripts/lib/polygon.sh`: the images execute the installer's
  own steps, so nothing under it can be shown not to matter, and the remedy is a rebuild rather than
  an override. Narrowing the hash would need a hand-maintained second model of what matters, which
  would drift; making rebuilds cheap is the direction that cannot go quietly wrong.

The suite that notices this condition is `ftps_on_a_real_host`, through one case written for it —
`the_shadow_database_can_be_read_for_authentication_in_this_container`, which asks
`/usr/sbin/unix_chkpwd` about a wrong password for `root` and requires a refusal rather than
`PAM_AUTHINFO_UNAVAIL`. It is why a red alma9 polygon was read as a stale image and not as a
regression. `sftp_on_a_real_host` needs no counterpart and has none: measured with `/etc/shadow` put
back to `0000` inside a container of the current image, FTPS reports 12 failures and SFTP reports
**13 passed / 0 failed**, because sshd verifies a password as an unconfined root process while
`unix_chkpwd` is the path the host's AppArmor profile attaches to and withholds `dac_override` from.

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

Thirteen test files are `#[ignore]`d by default and run only inside a polygon:
`sites_on_a_real_host.rs`, `php_pools_on_a_real_host.rs`,
`privileges_on_a_real_host.rs`, `databases_on_a_real_host.rs`,
`monitor_on_a_real_host.rs`, `binary_paths_on_a_real_host.rs`,
`sftp_on_a_real_host.rs`, `account_deletion_on_a_real_host.rs`,
`firewall_on_a_real_host.rs`, `cron_on_a_real_host.rs`,
`backup_on_a_real_host.rs`, `restore_recovery_on_a_real_host.rs` and
`ftps_on_a_real_host.rs`. They create real system accounts and real database
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
    --test restore_recovery_on_a_real_host \
    --no-fail-fast \
    -- --ignored --test-threads=1
```

`restore_recovery_on_a_real_host.rs` is here rather than in the privileged
command because it mounts nothing: it needs a real account, the image's own
MariaDB and a real `/home`, all of which the command above already has. What it
does need is a process it can KILL. Each of its cases re-enters this same test
binary as a child, has the child run a real `restore_backup`, and kills that
child with `SIGKILL` from inside the restore's own progress sink — once between
the two renames that swap the account's home, and once after a rollback dump has
been written. The parent then runs `ops::backup::recover_restores`, the
reconciliation the daemon performs before it binds its socket, and asserts the
customer's home came back with the backup's bytes and
`<account>:<web server group>:750`, that the rollback dumps survived, and that a
COMPLETED restore is not "recovered" by a later start. It is the proof for
privileges audit F-2, where a kill in that window left an account with no home
directory and nothing anywhere that would put it back.

`backup_on_a_real_host.rs` is two suites in one file, and both halves need a
real host. The first drives the archiver with a hostile `gzip` first on `PATH`
and asserts the impostor never ran — a defect no assertion about the ARTIFACT
could see, because the artifact is valid either way. The second drives
`ops::backup`'s four operations end to end against a real account, a real home
and the image's own MariaDB: the artifact is `0600` inside a `0700` directory,
the digest it records is the one `sha256sum` reports for the published file, a
symbolic link to `/etc/shadow` inside the home is archived AS a link and the
decompressed stream holds none of shadow's bytes (a planted string proves the
probe can see inside at all), a restore puts the home and the database back and
leaves the home root reading `<account>:<web server group>:750`, a second
creation under one id reports `AlreadyExists` without rewriting the artifact,
and a delete removes both files while a second delete reports `NotFound`. It is
in the privileged command below rather than this one because of a third thing it
does: `a_dump_is_refused_at_nine_tenths_of_what_the_scratch_filesystem_actually_holds`
dumps the fixture database once to measure how big its dump really is, mounts a
tmpfs of exactly that size plus a little at the bulk scratch root, and requires
the refusal to name nine tenths of what `df` reports for that filesystem — which
is the only place the per-dump ceiling is observed against a filesystem instead
of against a constant. Run without the mount the case does not go red: it prints
`UNOBSERVED HERE` and returns, so an unprivileged run reports a green suite whose
most valuable case never executed.

The other six run separately, because each needs `--privileged`: SFTP, FTPS and
account-deletion for the jail's bind mount, `backup` because one of its cases
mounts a real tmpfs at the bulk scratch root, `firewall` because `nft` cannot
initialise its cache without `NET_ADMIN`, and `cron` because the Debian family's
PAM stack includes `pam_loginuid`:

```bash
docker run --rm --privileged -v "$PWD:/maran" -w /maran/agent -e CARGO_TARGET_DIR=/tmp/target \
  maran-polygon-ubuntu24 \
  cargo test --test sftp_on_a_real_host --test ftps_on_a_real_host \
    --test account_deletion_on_a_real_host \
    --test backup_on_a_real_host \
    --test firewall_on_a_real_host --test cron_on_a_real_host \
    --no-fail-fast \
    -- --ignored --test-threads=1
```

`--no-fail-fast` is a **cargo** argument, so it goes before the bare `--`; after
it, libtest rejects it with `error: Unrecognized option: 'no-fail-fast'` and the
run reports the first suite only. Without it cargo stops after the first target
that goes red and simply does not run the ones behind it, while reporting a
single failure — and nothing else in this repository covers that. `fail-fast:
false` in the CI matrix governs the two *families*; `if: !cancelled()` governs
the *steps*; neither reaches inside one `cargo test` invocation. The difference
is measured, not assumed: with a broken privilege drop planted in
`fork_as_account`, the mutation harness — which has always passed this flag —
printed the named kill plus **25 further red host tests on each family**, the
blast radius of a defect that makes every fork-as-account path fail. The same
run without the flag would have stopped at the first of those and reported one
failure.

**CI does not read the exit code, and neither should you.** The three polygon steps in
`.github/workflows/agent.yml` tee their container output to `$RUNNER_TEMP/polygon-logs`
and a final step runs

```bash
maran polygon verify --statuses "$RUNNER_TEMP/polygon-logs/statuses.txt" \
  "$RUNNER_TEMP/polygon-logs"/step-*.log
```

which is the same scoring `maran mutate --polygon` uses (`scripts/lib/polygon.sh`). Per
suite it requires a `Running tests/<suite>.rs` line, a `test result:` line, and
`passed + failed` **equal to** the `ignored` column `scripts/test-baseline.txt` records
for that suite; over the run it requires at least one test executed, names every failure
and reconciles the named list against the totals. The suite list it checks against is
discovered from `agent/crates/*/tests`, never written down — so a suite missing from the
workflow's `--test` lists fails CI **by name**, which is the gap `maran structure`'s
check on this file could not cover (it checks this README, not the workflow).

It runs nothing itself: no docker, no root, no capabilities. That is why the capability
split above is untouched — only the scoring moved, and the running stayed where it
belongs. The same command scores a run you took by hand: tee the command above into a
file and pass it.

Why it exists at all: measured in this repository on one day, a `docker stop` on a
running suite gave **exit 0 for zero tests executed**; a container that executed nothing
exited 0 printing `Finished` and naming a suite executable; and a `cargo` invocation
exited 0 having run nothing. Until this step existed, CI's only verdict was that exit
code.

**The privileged six are one command here and two steps in CI, deliberately.**
`.github/workflows/agent.yml` runs `sftp` + `ftps` + `account_deletion` + `backup`
in one step and `cron` + `firewall` in another, because the two groups need
`--privileged` for different reasons and a merged step attributes a wrongly-started
runner to whichever suite ran first. The command above is the same six suites
with the same flags; it is not byte-for-byte the same invocation, so a run that is
green here is evidence about the suites, not proof that CI's step boundaries hold.

**Both commands name every suite explicitly, and that is the hazard this note
exists for.** An earlier version of this file listed six of the then ten suites, and
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

`databases_on_a_real_host.rs` is eight cases against the image's own MariaDB, and
two of them are there because of a geometry the other six cannot reach.
`a_grant_for_an_account_whose_name_holds_the_separator_cannot_reach_a_same_length_neighbours_database`
exists because the database-name position of a database-level `GRANT` is a
LIKE-style **pattern** and not an identifier: `_` matches any single character
there and backtick-quoting does not turn it off, so account `polydbgrant_ne`'s
grant on `polydbgrant_ne_shop` is the pattern `polydbgrant?ne?shop` and reaches
account `polydbgrantone`'s `polydbgrantone_shop`. That is a fact about the
server's grammar which no `DbHost` fake can hold an opinion about, so only a real
MariaDB can settle it, and it was measured here: with the escape removed the
attacker's own credential reads and writes the victim's table and reaches
databases created after the grant, and with `_` written as `\_` the server answers
`ERROR 1142 ... SELECT command denied` while the owner's own grant keeps working.

**A pattern whose only metacharacter is `_` matches only strings of its OWN
length, and that is the trap this case is built around.** The suite's other
cross-tenant case uses `polydbstwo_shop` (fifteen characters) and
`polydbsthree_shop` (seventeen), which cannot collide whatever the server does —
so it passed for the whole life of the defect while looking exactly like the
assertion that would have caught it. The colliding case therefore calls
`the_geometry_that_makes_a_collision_possible` as its FIRST statement, before a
single account exists: it asserts that the two account-name constants are the same
length, differ at exactly one position, and that the attacker holds `_` there
while the victim does not. Renaming either constant without preserving all three
fails that assertion by name instead of quietly restoring the geometry in which
the wildcard is invisible. The background is
`docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md`.

The eighth case,
`the_repair_narrows_a_wildcard_grant_an_older_agent_issued_and_touches_nothing_else`,
is the other half of that story: the escape was forward-only, so every host that
created a database before it carries an unescaped row in `mysql.db` that is still
matched at every connection. It seeds exactly that state — with the CLIENT and not
with the code under test, so the fixture is not the thing being measured — runs
`repair_grants`, and then asks the server four questions: the colliding credential
is denied, both owners still reach their own databases, a second run leaves the
grant table byte-identical, and a grant the panel did not issue is still stored
exactly as the operator wrote it while being reported as refused with its reason.
A positive control runs BEFORE the repair and asserts the seeded grant really does
return the victim's planted row — without it a mistyped seed would produce the
same "denied" afterwards and measure a repair of nothing.

**It carries its own colliding pair, and its own geometry:** `polydbrepairone` and
`polydbrepair_ne`, both FIFTEEN characters, differing at index twelve where the
attacker holds `_`. Two cases in one binary run in parallel and cannot share a
system account, so the geometry assertion is parameterised over the pair and both
cases call it with their own constants before creating anything.

`ftps_on_a_real_host.rs` drives a real vsftpd the agent itself configured: a
login is refused in plain text and accepted over TLS with the same credential
(the inverse control without which "plaintext is refused" is satisfied by a
daemon that refuses everything), the session's own root holds the bind mount and
nothing else, a file it uploads lands in the customer's real home owned by the
ACCOUNT, membership of the FTPS group is the entire authorization, and a
suspended login is refused by the daemon while a password change inside the
suspension leaves it refused.

Two of its cases are about TRUST rather than about encryption, and they are the
only ones in the file whose client verifies anything. Every other client here
passes `curl -k`, which is right for what those tests are about and proves nothing
about a certificate: a daemon serving expired material, material for another name,
or material signed by nobody would satisfy all of them.
`a_client_that_verifies_the_chain_against_the_panels_root_is_accepted_and_a_wrong_root_is_not`
therefore has the test own a throwaway certificate authority
(`tests/fixtures/polygon_certificate_authority.rs`), issue a leaf for the suite's
hostname INTO the panel's own store paths, and run `curl` with **no `-k`** and
`--cacert` naming that authority — then run the SAME client against a second,
unrelated authority's root, which must refuse with `SSL certificate problem`. That
second half is the point: with verification silently switched off, the first half
passes and only the refusal can tell. `the_daemon_serves_the_certificate_the_panel_installed_and_not_some_other_file`
asks the running daemon what it presents (`openssl s_client -starttls ftp`) and
requires it to be the leaf from the store file, which is the one assertion that
would notice an agent rendering one certificate path while the unit starts the
daemon against another.

**UNOBSERVED HERE: that a PUBLIC client trusts a real server.** That is a fact
about other people's trust stores and about a certificate no isolated container
can obtain, and no test on this host can observe it. What is observed is the
mechanism a publicly-issued certificate would also travel.

Two of its cases do what no `curl` can, because `curl` authenticates, does one
thing and hangs up. `an_authenticated_session_is_ended_when_the_account_is_suspended`
holds TWO control sessions open — `openssl s_client -starttls ftp`, in
`tests/fixtures/ftps_control_session.rs` — across a real suspension, and measures
that the suspended account's session is GONE while the other account's session
goes on moving bytes through the same daemon at the same moment. That second
session is both the cross-tenant control (the cull must not reach another uid) and
the daemon's own liveness proof (a daemon that had stopped serving everything
would make the first assertion vacuous). A NEW session with the same credential is
still refused, which is the half of the promise the cull must not be allowed to
replace. **This case used to assert the opposite** —
`an_authenticated_session_keeps_transferring_after_the_account_is_suspended`,
which pinned the behaviour before the cull existed and said in its own failure
message how to re-pin it.
`a_deleted_accounts_ftps_credential_is_refused_by_the_daemon_after_the_name_is_recycled`
is the protocol half of the cascade `account_deletion_on_a_real_host.rs` proves
on the machine: the dead password is refused by the real daemon after the account
name has been recycled, and the successor's own password works, which is what
tells a refusal about the credential apart from a daemon refusing everything.

`sftp_on_a_real_host.rs` carries the OTHER protocol's answer to that same
question, and it is there because the comparison is the finding.
`an_authenticated_sftp_session_is_ended_when_the_account_is_suspended` holds TWO
SFTP sessions open — the image's own client in batch mode reading from a pipe, in
`tests/fixtures/sftp_control_session.rs`, which keeps one ssh connection and one
subsystem channel alive — across `AccountOperations::suspend` plus
`set_account_logins_locked`, and measures that the suspended account's session is
gone while a second account's session still transfers. **The two protocols do not
differ**, and that is now a property of the cull rather than of the gap: the agent
signals every process running as the suspended account's uid, which is one
question about one uid whichever daemon was serving the session. A panel that
closed one protocol and not the other would be keeping half of a promise. **This
case used to assert the opposite too**
(`an_authenticated_sftp_session_keeps_transferring_after_the_account_is_suspended`).

### What suspending an account now does to an open transfer

The sentence an operator needs, because this one costs the customer something:

> Suspending an account now ends its open SFTP and FTPS sessions as well as
> refusing new ones. An upload that was in flight is cut at whatever byte it had
> reached, so the customer's home can be left holding a **partial file** — a
> truncated archive, half a database dump, a half-written `.zip` that will not
> open. Nothing deletes it and nothing marks it: it is a file of the customer's
> that is shorter than it should be. If you are suspending for non-payment rather
> than for abuse, that is the price of the access stopping immediately, and it is
> paid in the customer's data.

Nothing in the panel says this today. The cull is a privileged surface carrying an
**outstanding second review**; what it does, how the uid is confined, and what it
does not close are in
`docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`.

On **survival** the two protocols agree, and they now agree that a suspended
session does not survive. On the **idle bound** — what happens to a session nobody
suspended — they still differ, and two cases added 2026-09-11 are the only things
that observe it, one in each suite.
`sftp_on_a_real_host.rs::the_sshd_configuration_this_product_writes_puts_no_idle_bound_on_an_sftp_session`
asks the installed `sshd` for its effective configuration for a real SFTP login
(`sshd -T -C user=…`, so the installer's `Match Group` block is evaluated) and
requires `ClientAliveInterval 0` — no bound, and a distribution default rather
than a value this repository sets anywhere. Its positive control is that the dump
carries `forcecommand internal-sftp`, which lives only inside that block: a `-C`
that matched nothing would report no bound and pass while reading the wrong half
of the file. Its inverse control hands the same instrument
`-o ClientAliveInterval=300` and requires it to report `300`.
`ftps_on_a_real_host.rs::the_vsftpd_configuration_this_product_writes_bounds_an_idle_ftps_session_at_ten_minutes`
reads `idle_session_timeout` out of the live `vsftpd.conf` the daemon was started
against, taking the LAST occurrence because that is the one vsftpd obeys, and its
inverse control appends `idle_session_timeout=0` and requires the same reader to
see it before the re-enable puts `600` back. Both were broken on purpose and went
red by name. The asymmetry they pin — ten minutes on FTPS, nothing on SFTP — and
the four options for it are in
`docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`
("Correction, 2026-09-11 (second)"). **The cull did not make either case
redundant and neither was changed:** they are about a session nobody suspended,
which is the only kind the cull never touches, and `idle_session_timeout=600` is
still the only bound this product sets on one.

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
