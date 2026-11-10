# Threat note: customer PHP settings and the pool rewrite rpc (issue #138)

`rules/security.md` requires a threat note, written first, for a change to the privileged request
path, and a second reviewer. This change adds one rpc to the root agent (`ApplyPhpPoolSettings`) that
rewrites an account's php-fpm pool from customer-influenced values, and stores those values and their
plan bounds in the panel. This note is written BEFORE the change: every statement below is a
requirement the code must meet, not a description of code that exists. Where the code lands and
differs, the code is wrong or this note is amended in the same change.

**Second review: OUTSTANDING — must not merge to main.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch carrying issue #138 MUST NOT merge to `main` until a
human reviewer has read this surface (rules/security.md "Sensitive change escalation"; tracking issue
#331). The owner is told at hand-off that this surface carries an outstanding review. Nothing here
discharges it.

## What is added

1. `PhpService.ApplyPhpPoolSettings` in `proto/agent/v1/php.proto`, a closed, typed, idempotent,
   audited rpc: account name, PHP version, worker budget and a list of `PhpSetting` (name, value).
   It rewrites the one pool file of that account at that version through the existing `write_pool`
   protocol (render, temporary file, rename, `php-fpm -t`, reload, rollback).
2. In the panel: a per-account store of the customer's chosen settings, four plan-defined bounds
   (`PhpLimits` on the plan), and an endpoint pair `GET/PUT api/v1/php-settings` for the customer.
3. The panel's start-up PHP reconciler, the site-creation and the version-switch handlers now send
   the stored settings (clamped to the plan's bounds) instead of an empty list; a plan change
   re-applies every affected account's pools.

Not added: no daemon, no port, no outbound call, no new dependency, no new file written outside the
pool file the agent already owns, no change to the settings whitelist.

## Attacker goals, and why each is closed

- **Put a directive of their choosing into `pool.d/<account>.conf`** (read by php-fpm as root, then
  its workers run as the account). The setting NAME is never the caller's bytes: `PhpOverride::parse`
  looks the name up in the agent's own `ALLOWED` table and stores that table's `&'static str`. The
  VALUE is checked for control characters first and by name, then against that setting's shape and
  bound, so no value can end the line it is written on (rules/security.md item 4). The rpc adds no
  name to the table, so `php_admin_value`, `open_basedir`, `disable_functions` and `cgi.fix_pathinfo`
  stay unreachable: the template writes them with `php_admin_*`, which no `php_value` line can
  countermand at any position.
- **Aim the rpc at another account, or create a pool that has no site.** The rpc takes the account as
  an `AccountName` and the version as a `PhpVersion` (closed grammars) and writes only
  `PoolPaths::for_pool(account, version)`. It answers `NOT_FOUND` when that pool file is absent, so
  it can never create a pool: pool creation stays with `CreateSite` and `UpdateSitePhpVersion`.
- **Lift `memory_limit`, `max_execution_time`, `post_max_size` and the rest past what the plan
  sells.** The plan bound is a PANEL control: the panel clamps the stored value to the plan's maximum
  before it sends it, and never raises a stored value. The agent keeps its absolute ceilings (512
  MiB for the three size settings, 300 s, 10 000 input variables) and refuses a request above them.
  **Stated, not hidden:** a compromised or buggy panel can write any value up to the agent's ceilings
  for any account; per-plan enforcement is advice to the agent, the agent's wall is the absolute
  ceiling. This is the same trust model as `max_children`, which is also a plan figure the panel
  supplies.
- **Make a pool refuse every upload.** `post_max_size` below `upload_max_filesize` makes PHP discard
  a body without an error. The agent already refuses that combination
  (`PhpOverride::ensure_consistent`, issue #434) and the panel's validator states the same rule, so a
  customer is told before the save.
- **Read or change another customer's settings through the panel.** The settings table is
  tenant-scoped by a global query filter on `AccountId`, the account for a request is the caller's
  own (a server-established member of the command, never bound from the request), and another
  account's row answers 404, never 403 (rules/security.md item 6).
- **Make a plan change leave a pool above its new bound.** A plan edit or an account's move to another
  plan publishes `AccountPlanChanged`; the Sites handler re-applies each affected account's pools
  with the clamp of the new bounds. If the agent is down the next panel start converges every pool
  (the start-up reconciler passes the same clamped set), so a bound lowered while the agent was
  unreachable is applied at the latest at the next start.

## The audit trail

`ApplyPhpPoolSettings` is listed in `agent/crates/agent/src/audit/mutating_rpcs.rs`, so the agent
emits one line per call (account name, outcome) built only from validated types. The panel journals
`PhpSettingsChanged` with the setting NAMES that changed and never the values' meaning beyond the
figures themselves (they are not secrets).

## What a reviewer must check

- The rpc cannot create a pool (absent file gives `NOT_FOUND` and nothing is written).
- No setting name outside `ALLOWED` survives a mutation of `PhpOverride::parse` (a test names a
  non-listed name and requires the refusal).
- `render_pool` places the `php_admin_*` lines before any customer value; the rpc reuses `render_pool`
  unchanged.
- The clamp in the panel never raises a stored value and never lowers the stored row (the stored value
  is the customer's wish; the pool carries the clamp).
- The panel's catalogue of names (`PhpSettingNames`) and the agent's `ALLOWED` agree; a name added on
  one side and not the other must fail a test (the agent refuses it, so the customer sees an error,
  not a silent drop).

## What the author could not verify

- Behaviour of a reload with a value that `php-fpm -t` accepts but a worker rejects at run time. The
  timezone check is shape-only by design, so a name absent from the host's tzdata passes both checks.
- Behaviour with several pool files being rewritten in quick succession on a host with many
  accounts: the rpc takes the account's wait-free lock (`write_pool`) and the config-tree lock, so a
  busy answer (`ACCOUNT_BUSY`) is possible and the panel treats it as a failure to retry; the plan-change
  fan-out has not been measured against a host with thousands of accounts.

## Amendment 2026-11-09: a size of zero is refused (#480)

**What was wrong.** "The agent refuses a request above its ceilings" did not hold for the value 0.
`PhpOverride::parse` bounded the three size settings (`memory_limit`, `upload_max_filesize`,
`post_max_size`) from above only, and PHP reads `post_max_size = 0` as "no limit", so a buggy or
compromised panel could write a pool with no request-body limit that the plan bound could not clamp.

**What changed.** `OverrideKind::Bytes` carries a `minimum`, set to 1 byte for all three, and the
parse checks it before the maximum exactly as the seconds and count kinds do. `0`, `0K`, `0M`, `0G` and
`00` are refused as malformed for each setting; `1` and `1K` are accepted as the inverse control.
`-1` was already refused (no sign in the grammar).

**Why it is safe now.** No spelling of "unlimited" reaches the pool: negative values have no grammar,
zero has a floor, and the ceiling is the 512 MiB maximum.

**Second review: OUTSTANDING (#331).** A reviewer must check that no other size-like setting was added
to `ALLOWED` without a floor.
