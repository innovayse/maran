# Public demo deployment

Everything that is specific to running the public demonstration (`Maran.Host.Demo`, a simulated
agent and the demo guard) on its own disposable server. Nothing here is part of a product install:
the installer, the root `maran` command and `maran update` never read this folder, and a release
bundle never contains it.

| Path | What it is |
|---|---|
| `panel.env.example` | Names, comments and safe placeholders of every variable the service reads |
| `seed.env.example` | The one-time setup token, read by the deploy script for the seed only; the service never loads it |
| `nginx/maran-demo.conf.template` | The TLS edge: static SPA, API proxy, connection caps, no client address in any log |
| `systemd/` | The API unit (no network egress), the reset service and timer, the socket directory |
| `bin/maran-demo-deploy` | Verifies a demo bundle against the demo key, installs it, builds the seed template |
| `bin/maran-demo-reset` | Returns the demonstration to the seed; run by the timer |
| `bin/maran-demo-egress-check` | Proves the API unit cannot open an outbound connection, with a control |
| `keys/demo-release-signing.pub` | Placeholder for the demo bundle's own public key (no key material is committed) |

The full procedure, the threat model and what is not verified are in `docs/operations/demo.md`.
