# Proto Contract Rules (API ↔ agent)

Normative. `proto/agent/v1/` is the single source of truth for the C#↔Rust boundary; both sides generate from it and MUST NOT hand-write contract types.

## Layout & naming

- Package `maran.agent.v1`; one file per domain: `system.proto`, `sites.proto`, `ssl.proto`, `db.proto`, `ftp.proto`, `files.proto`, `cron.proto`, `firewall.proto`, `backup.proto`, `monitor.proto`, plus `common.proto`.
- Services `PascalCase` + `Service` (`SitesService`); rpcs are verbs (`CreateSite`); messages are `<Rpc>Request` / `<Rpc>Response`; fields `snake_case`.
- Every rpc, message, and field carries a doc comment: meaning, units, and validation expectations.

## Response shape

Transport-level gRPC status is reserved for transport problems. Domain outcomes ride in the response payload so they survive language boundaries and are testable as data:

```proto
// common.proto
// Typed failure of an agent operation. `code` drives API behavior; `message`
// is operator-facing English, never shown raw to customers.
message AgentError {
  ErrorCode code = 1;
  string message = 2;
  // Excerpt of the failing tool's output (e.g. `nginx -t` stderr), max 4 KiB.
  string tool_output = 3;
}

enum ErrorCode {
  ERROR_CODE_UNSPECIFIED = 0;
  ERROR_CODE_INVALID_INPUT = 1;
  ERROR_CODE_ALREADY_EXISTS = 2;
  ERROR_CODE_NOT_FOUND = 3;
  ERROR_CODE_VALIDATION_FAILED = 4;  // rendered config failed its validator; state rolled back
  ERROR_CODE_SYSTEM_FAILURE = 5;
}
```

```proto
// sites.proto — every Response is a oneof over ok/error:
message CreateSiteResponse {
  oneof result {
    CreateSiteOk ok = 1;
    maran.agent.v1.AgentError error = 2;
  }
}
```

- Long-running rpcs (backup, restore, package install) are server-streaming and emit `Progress { uint32 percent = 1; string stage = 2; }` from `common.proto`, ending with the terminal ok/error message.

## Evolution — additive only

Within `v1`:

- MUST NOT: remove or rename fields/rpcs, change a field's number or type, reuse numbers or names of removed fields.
- Removals: mark `reserved` (number and name) and leave a comment with date and reason.
- New fields MUST be optional-semantics (proto3 defaults must mean "absent/old caller"); new rpcs are always allowed.
- Both sides tolerate unknown fields. Version skew between the panel and the agent is not tolerated silently: the `GetAgentInfo` handshake refuses a skewed pair and reports it as incompatible (see "The contract revision" below).

Breaking anything above = new directory `proto/agent/v2/` — a deliberate, planned event, not a PR side effect.

### How the additive law is checked

`maran proto` compiles the contract and then compares it against
**`proto/agent/v1/contract-baseline.txt`** — a sorted, text inventory of every message, enum,
service, rpc, field, enum value and `reserved` clause, rendered from protoc's own descriptor set,
so the comparison sees the compiled contract and not the source text. It refuses, by name: a
removed or renamed message/enum/service/rpc/field/enum value; a changed field number, type, label
or `oneof` membership; a number whose owner changed; a field taking a reserved number or name —
including the case protoc cannot see, where the `reserved` clause was deleted in the same change.
Additions pass with no ceremony.

The check NEVER writes the baseline. Recording an accepted change is a separate command that a
developer types and CI never runs:

```
maran proto --accept
```

The baseline is text for one reason: regenerating it to bury a breaking change then shows up in
the pull request as deleted and altered inventory lines, in front of a reviewer, instead of an
opaque binary blob. Refreshing it belongs in its own reviewed commit.

### The contract revision

Additive rules keep an old reader from breaking on a new message; they do not keep it from
misreading one. Protobuf turns a field the sender never set into its zero value without a word, so
a panel driving an agent older than itself acts on zeros it never received, and one driving a newer
agent cannot know what changed. `GetAgentInfo.proto_version` is the one number on the wire that
says which contract an agent speaks, and it is only worth reading while it moves with the contract:

- **The agent's `PROTO_VERSION`** (`agent/crates/agent/src/services/system/system_service.rs`) is
  the revision of `proto/agent/v1/` the binary is built from. Every change that `maran proto
  --accept` records in `contract-baseline.txt` raises it by one, in the same pull request. A
  comment-only edit changes no inventory line and raises nothing.
- **The panel's window** (`backend/src/Maran.Agent.Client/Services/SystemService/AgentProtoVersionWindow.cs`)
  moves in that same pull request: `Maximum` to the new revision, and `Minimum` with it, unless the
  change states in that file why the panel reads nothing the older revision lacks. A one-revision
  window is the default because a pair that differs is an upgrade that replaced one side and not
  the other.
- **Revision 1 names no contract.** The constant stayed at 1 through every contract change before
  this rule existed, so no window contains 1. Revision 2 is the contract as of the change that
  introduced the rule.
- **What a refusal does, and what it does not.** The handshake fails as `AgentIncompatible`,
  `/health` reports the agent `incompatible`, `/health/ready` answers 503, and the panel logs the
  mismatch once at startup. The panel never exits over it: `maran-api.service` either restarts an
  exited panel until systemd stops trying after five starts in a minute, or does not restart a clean
  exit at all, so exiting turns a mismatch into a panel that does not run. The verdict is asked again
  on every health request and never cached, so finishing the upgrade clears it without a restart.
  Operations are NOT re-checked per call: an agent swapped while the panel runs is driven until a
  health request asks — which is why a module that reads a field an older agent lacks still owes its
  own reading of an absent value (`UnmanagedLoginPolicy` is the worked example).
- **What holds it up, and what does not.** `maran handshake` builds the agent and the panel from one
  tree and fails unless `/health` reports the agent `connected` (`cross.yml`), so a revision and a
  window moved apart fail CI. A contract change that moves NEITHER passes that gate, and is caught
  by the agent's own test `a_contract_baseline_change_comes_with_a_proto_version_bump`, which
  compares a digest of `contract-baseline.txt` with the newest row of its `REVISIONS` table and with
  `PROTO_VERSION`: raising the revision means appending a row there, in the same pull request.

## Codegen

- C#: `Grpc.Tools` in `Maran.Agent.Client` only — the rest of the backend consumes its typed wrappers, never raw generated clients.
- Rust: `tonic-build` in `agent/crates/agent/build.rs`.
- Generated code is never committed and never edited.
