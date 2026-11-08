# Threat note: an administrator sets the owner's first password, and grants a login shell, at account creation (issue #194)

**Surface:** (a) Identity's sign-in: a new "first password was chosen by someone else, must be replaced" state and the gate that enforces it. (b) The agent's account operations (`ops::accounts`) and a new PAM limits drop-in: a customer system user may now be created with a real login shell, with an inode limit, and with an open-files limit written under `/etc/security/limits.d/` by root.

**Status: written BEFORE the change.** **Second reviewer: OUTSTANDING (#331).** The branch MUST NOT merge to `main` until a human has read "What a reviewer must check". The owner is told at hand-off that this surface carries an outstanding review.

## What is added

1. `CreateAccountCommand` gains an owner-login mode (invitation, the default, or initial password), `InitialPassword`, per-account inode / process / open-files overrides, `ShellAccess` and `BackupEnabled`. The audit entry lists the options and never the password.
2. Identity: `User.PasswordChangeRequired` (new column, expand-only), `AccessToken.RequiresPasswordChange` and a claim, an authorization requirement refusing every endpoint but change-password and sign-out while it is set, cleared by `User.ChangePassword`.
3. Agent: `CreateAccountRequest` gains `inode_limit` and `login_shell`; `UnsuspendAccountRequest` gains `login_shell`; new rpc `SetAccountOpenFilesLimit` writing `/etc/security/limits.d/maran-<account>.conf` through `ops::safe_write`; the distro adapter gains `bash_shell()`.
4. Not added: no daemon, port, outbound call, rpc that runs caller text, change to `fork_as_account`. No IP field, no reseller flag, no automatic certificate (account creation makes no site).

## The initial password (the administrator knows a credential)

- **Who learns it:** the administrator who typed or generated it, the browser's memory, and the owner once told out of band. That is the honest cost of the "host without mail" use case and the reason the invitation stays the default.
- **Never persisted readable, never logged:** it travels `SPA -> POST /api/v1/accounts -> CreateAccountCommand -> AccountCreated -> AccountCreatedHandler` in memory only. `AccountCreated` is invoked locally and is never durable (a message carrying a secret is never durable, rules/csharp.md). The audit journal records the MODE, never the value. It is hashed with Argon2id before the first `SaveChanges`.
- **Strength is checked on the backend**, once, on the Identity side, against the same policy every other password path uses. A missing password with mode "password", and a password with mode "invitation", are both refused: no silent fall-through to a passwordless login.
- **Forced change:** the new login has `PasswordChangeRequired = true`. The gate is an authorization requirement, so a token can do nothing but change its password. The failure to refuse is the one `RequiresTwoFactorSetup` already guards: a body saying "you are free" over a token the handlers refuse. Refresh must not launder the flag.
- **No reset-by-admin path is added.** The Admin role is never created here; the handler always makes a Customer.

## A login shell for a customer

- Today every account is `nologin`. `bash` turns the account's user into a possible interactive login with the account's uid and slice ceilings. Default is **off**.
- **Containment:** `LoginShell` is a closed enum (`Nologin`, `Bash`) mapped by `DistroAdapter` to an absolute path; no caller text reaches `useradd --shell`. The choice is stored on the account row and passed to the agent on every create and unsuspend, because the agent is stateless and unsuspend re-applies a shell (it used to reset it to nologin, silently revoking a granted shell).
- **Suspension still locks it:** suspend keeps setting nologin plus the password lock.
- **Blast radius:** the process ceiling (`TasksMax`) and the memory / CPU slice ceilings bound a fork bomb. A reseller may only LOWER a limit below its plan; only an administrator may set any value inside the agent's validated range.

## The limits drop-in (root writes under `/etc/security/limits.d/`)

- File name `maran-<account>.conf` is built from a validated `AccountName` only; content is a template with one validated integer (range 1..=1048576), written through `safe_write`, removed on account deletion before `userdel`. No newline or control character can reach the line.
- `pam_limits` applies to PAM sessions that load it; it does NOT reach php-fpm workers or cron jobs started outside a PAM session. The documentation says exactly that.
- Inode quota is one more field on the existing `setquota` call; `0` means unlimited.

## What a reviewer must check

1. The password never appears in a log, an audit row, a Wolverine envelope table, an OpenAPI example or an exception message (probe with a PLANTED password and a positive control).
2. No endpoint skips the password-change gate.
3. Unsuspend restores the shell; suspend locks it; on both families.
4. The limits file is removed on deletion and cannot be written for a name that is not an existing hosting account.
5. A reseller cannot raise any limit above its plan.

## What the author could not verify

sshd behaviour for an unchrooted account user per family; presence of `pam_limits` in the sshd stack per family; that both families accept the same `limits.d` `nofile` syntax; the exact `setquota` inode semantics on XFS project quota hosts.
