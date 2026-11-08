# The public demo

`demo-maran.innovayse.com` runs the real panel against a **simulated server**, with two published
sign-ins and a reset every few hours. It exists to let someone try every screen without installing
anything. It is not a product install, and it never touches a real host.

## What it is

| Part | What it does |
|---|---|
| `backend/src/Maran.Host.Demo` | A second composition root: the same modules as `Maran.Host`, with the agent replaced and the demo guard in front of the write surface. `Maran.Host` does not reference it, and no product release contains it. |
| `backend/src/Maran.Agent.Simulated` | An in-memory implementation of every `IAgent*Client` contract. It spawns nothing, writes no file, opens no socket; state is bounded by `SimulatedAgentLimits`. A new method on any client breaks its build, so the simulation cannot fall behind the contract. |
| `demo/` | The deployment: nginx vhost, systemd units, the deploy, reset and egress-check scripts, the placeholder for the demo's public key. |
| `scripts/maran demo` | `build`, `sign`, `verify`, `selftest`, `seed`, `smoke`. |
| `.github/workflows/demo.yml` | Builds and signs the demo bundle. Manual dispatch only. |

The agent is not run at all on the demo server: there is no `maran-agent` unit, and the API unit denies all
network egress (`IPAddressDeny=any`). The three-process law in `rules/architecture.md` describes a product
install; the demo is deliberately not one.

## The world a visitor sees

The world is defined once (`World/StandardDemoWorld`) and read by two consumers that therefore cannot
disagree: the database seed (`demo-seed`, which drives the real commands as an administrator) and the
simulated agent's seed (`DemoWorldAgentSeed` for system users and PHP versions, `DemoWorldHostingSeed` for
virtual hosts, certificates, databases, SFTP and FTPS logins, crontabs, backup artifacts and firewall
rules and bans). The simulated agent's state is in memory and is rebuilt from the world every time the API
starts, so server-read lists are populated after a restart. The run that executes `demo-seed` is the
exception: it creates the world through the real commands, and a simulated server that already held it would
answer every create with `AgentAlreadyExists`, so that run starts the simulation empty (the `Demo:SimulatedWorld`
setting, switched off for the verb by `Program`).

The guard's protected set (the administrator, the featured customer's account and plan, the resellers and
what they own) lives in memory too, and the seed ran in another process, so the API fills it again when it
starts (`DemoProtectedResourceStartup`). An empty set silently allows everything it guards, so a seed that
protects nothing fails.

## Deploying

1. Create the demo signing key (ECDSA P-384) outside this repository. Commit only its public half as
   `demo/keys/demo-release-signing.pub`, and store the private half as the secret
   `MARAN_DEMO_SIGNING_KEY` of the protected GitHub environment `demo-release`. It must not be the product
   release key, and the repository holds no copy of either.
2. Run the `demo` workflow with a version. It runs `maran demo selftest` first, builds, and signs in the
   protected environment. Download the `demo-signed` artifact.
3. On the demo server: install nginx and PostgreSQL, copy `demo/panel.env.example` to
   `/etc/maran-demo/panel.env` and fill it in (a fresh key per deployment; use plain alphanumeric
   passwords, the reset script reads the file with a shell), copy `demo/seed.env.example` to
   `/etc/maran-demo/seed.env` (root-only; only the deploy script reads it, the service does not) and set a new
   one-time setup token, install the public key at
   `/usr/local/maran-demo/keys/demo-release-signing.pub`, then run
   `demo/bin/maran-demo-deploy <bundle-dir>`.

`maran-demo-deploy` verifies the manifest signature against the demo key and every archive against the
signed checksum before it unpacks anything, builds the template database `maran_demo_seed` (schema, then
`demo-seed`), and runs a reset. Nothing in the repository holds a credential that reaches the server.

## Resetting

`maran-demo-reset.timer` wakes the reset script every ten minutes; the script resets only when the last
reset is older than `Demo__ResetIntervalHours`, so the interval has one source. A reset stops the API,
recreates the live database from the template, runs `Maran.Host.Demo --cli demo-reset` (which refuses and
leaves the API stopped if the database does not hold the world, and otherwise records the moment the banner
shows), and starts the API. `maran-demo-reset --now` resets on demand.

## What keeps a published sign-in safe

The sign-ins are public, so anything a visitor can do, every visitor can do. The guard
(`Maran.Host.Demo/Guard`) refuses every action that creates or changes a credential, a session, a mail
setting, a licence, a certificate setting, the security policy, an API key or a whitelist, and the
architecture census holds the table to the controller list. The seeded security policy has no per-user
lockout, so a visitor cannot lock the others out. An account cannot be created with a password the visitor
chose. Visitors' addresses and user agents are replaced by a keyed stand-in before anything reads them; an
IPv6 visitor is counted as its /64 network, in the API and in nginx, so owning a whole network is not owning
many visitors. Sign-ins, refreshes and sign-outs have allowances of their own (per visitor and for the whole
demo, renewed every window), apart from the mutation budgets. The API refuses to start, and until the
protected set is loaded the guard refuses every action that depends on it, if the database holds no
demonstration. Mail goes to an in-memory sink, ACME and the licence are
simulated in process, and the API unit has no network. nginx logs no client address
(`demo/nginx/maran-demo.conf.template`).

## Why the demo bundle is refused by a real installer

The demo bundle has two components (no agent), a manifest with `"flavour": "demo"` and no channel, and a
signature from the demo key. The installer and `maran update` verify only against
`installer/keys/release-signing.pub`. `scripts/maran demo selftest` builds a throwaway bundle and requires
that the product key refuses it, that the signing key's own public half accepts it, that a tampered
manifest is refused, and that the committed demo key differs from the product key.

## Measured and not measured

- Wolverine's handler types are generated at runtime (`TypeLoadMode.Auto`, as in `Maran.Host`, whose
  committed `Internal/Generated` is excluded from compilation), and `Maran.Host.Demo` declares no handler,
  so the entry-assembly scan finds nothing. The first start therefore pays the same generation cost as a
  product install. It has not been measured here.
- `maran-demo-egress-check` needs a systemd host; it asserts the sandboxed probe fails and prints that
  the control (the same probe without the restriction) succeeded. The web server's caps and the server's
  routes to internal networks are measured at the first deployment.
- The demo API unit is not yet scored by `maran units`, which reads only `installer/systemd`.
- The demo's second security review is outstanding (issue #331); the change must not merge to `main`
  before it.

## Website documentation needed

A "Try the demo" link to `https://demo-maran.innovayse.com`, and a short page saying what the demo is and is
not: a simulated server, reset every few hours, never to be given real data. The sign-ins are shown on the
demo's login page and are not copied into the site.
