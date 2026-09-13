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
- Both sides tolerate unknown fields; version skew across one release is covered by the `GetAgentInfo` handshake and additive rules.

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

## Codegen

- C#: `Grpc.Tools` in `Maran.Agent.Client` only — the rest of the backend consumes its typed wrappers, never raw generated clients.
- Rust: `tonic-build` in `agent/crates/agent/build.rs`.
- Generated code is never committed and never edited.
