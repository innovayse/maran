# SFTP public-key login (issue #135) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This
change makes root write a file that OpenSSH reads to decide who may log in. Written before the code,
per that rule; the branch may land on `dev` but must not merge to `main` until a second reviewer has
read this note and checked the items under "What a reviewer must check".

## The surface

A new rpc, `SftpService.SetSftpAuthorizedKeys(account_username, sftp_username, public_keys)`,
served by the agent as root. It REPLACES the whole key set of one SFTP login: the panel's record is
the truth (rules/architecture.md "Truth lives in PostgreSQL"), so the rpc converges the host to the
list it is handed, and an empty list removes the file. It writes, through `ops::safe_write`:

```
/etc/ssh/maran-sftp-keys/<login>             root:root 0644, one `restrict <algorithm> <base64>` line per key
/etc/ssh/maran-sftp-keys/<login>.suspended   the same file while the account is suspended
```

The directory is created by the installer (step 86) as root:root 0711, and the installer's `Match
Group maran-sftp` block gains one line, `AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u`, so the
setting applies to SFTP logins and to nothing else on the host.

This slice ships the contract and the agent only. No panel module calls the rpc yet, so on a
released panel it is inert (rules/architecture.md "Agent": new operations stay inert until a C#
module drives them). The panel half is listed at the end, and it re-opens this note.

## What an attacker could try, and why it does not work

1. **Inject sshd options through a key — `command="…"`, `from=`, `permitopen=`, `environment=`.**
   A key reaches the file only as an `SshPublicKey` (`agent-core::validation::system`), which is
   constructed from `<algorithm> <base64> [comment]` and nothing else: the first field must be one
   of an allow-listed set of algorithm names, so a line that starts with an option is refused as an
   unsupported algorithm rather than parsed. The comment is dropped, not escaped. The agent writes
   the line itself, as `restrict <algorithm> <base64>`, so the only option on any line is one the
   agent chose and the one that turns everything off.
2. **Inject a second line — a newline, a carriage return, a NUL inside a key.** Any control
   character anywhere in the input is refused before it is split (rules/security.md item 4: the
   value is validated, not escaped). The base64 field is then DECODED and parsed as the SSH wire
   format, so it is a key and not merely key-shaped text.
3. **Upload a weak or mismatched key.** The decoded blob's own algorithm name must equal the
   declared one. RSA moduli under 2048 bits are refused, and so is anything over 16384; Ed25519
   keys must be 32 bytes, ECDSA points must be the uncompressed length of their named curve, and
   nothing may trail the last field. DSA (`ssh-dss`) is not accepted at all.
4. **Exhaust the host through the key set.** At most 32 keys per login and 8 KiB per key, refused
   (not truncated) at both the agent's service layer and in the operation.
5. **Write a key onto somebody else's login.** The login is addressed as `<account>_<name>` from a
   validated `AccountName` and `SftpUserName`, the account must be a managed hosting account, and
   the login must be one this account holds — its passwd home is exactly this account's jail, the
   check `SetSftpPassword` already uses — all under the account's never-waiting lock. A login the
   account does not hold is `NOT_FOUND` and nothing is written.
6. **Plant or swap the file from inside the jail, or from a tenant's php-fpm pool.** The file is
   outside every jail and every home, in a root-owned directory no customer uid can write; the
   chroot is unchanged (`ChrootDirectory %h` still names the root-owned jail). sshd's own
   `StrictModes` check (on by default) refuses a key file whose path is writable by anyone but root.
7. **Keep logging in while suspended.** This is the one that would have been a real hole. A
   suspension locks the login's password with `usermod --lock`, and with `UsePAM yes` — the default
   on every supported family — sshd does NOT consult the shadow lock for public-key logins. So a
   key file left in place would be a working credential on a suspended account.
   `SetAccountLoginsLocked` therefore renames `<login>` to `<login>.suspended` when it locks and
   back when it unlocks; sshd reads `%u`, so the suspended name is never read. A key set written
   while the login is locked is written under the suspended name, decided from the same shadow
   field the password path classifies (`StoredPassword`), so changing keys cannot lift a suspension
   any more than changing the password can.
8. **Revoke a key and keep the session it opened.** Revocation stops NEW logins only. A session
   already authenticated with the revoked key stays open, as it does for a changed password: the
   login shares the account's uid, so there is no per-login process set to end. Stated, not
   solved; suspension still ends every session through the existing cull.

## Accepted residual

- The key file is world-readable (0644) because sshd reads it with the LOGIN's uid
  (`temporarily_use_uid` in OpenSSH's key-file reader), and the login's uid is the account's. The
  directory is 0711, so it cannot be listed, but a local process that guesses a login name can read
  that login's public keys. They are public keys with their comments removed; nothing in them is a
  credential.

## What a reviewer must check

- That no path from the wire reaches the file without `SshPublicKey::parse`, and that its tests
  cover every refusal above (options, control characters, algorithm mismatch, weak RSA, trailing
  bytes, oversize).
- That `set_account_logins_locked` renames BOTH directions and that a key write on a locked login
  lands under the suspended name.
- That the `Match Group` block, as rendered by `installer/lib/86-sftp.sh`, still passes `sshd -t`
  on both families, and that `AuthorizedKeysFile` inside a `Match` block is honoured by the
  distribution's OpenSSH (it is a documented Match keyword since OpenSSH 5.x).

## What the author could not verify here

- The real-host polygon suite (`sftp_on_a_real_host.rs`) has a key-login case written for this
  change; it needs the polygon images and root, and was not run in this session.
- The `UsePAM` behaviour in item 7 is read from OpenSSH's source (`auth.c`, `allowed_user`) and the
  distributions' default configs, not measured on a host in this session.

## Not in this slice

The panel module that stores keys per login, its endpoints, the SPA screen, the website
documentation for customers, and the call that re-applies keys after a login is recreated. When
that lands, this note gains the panel half: tenant scoping of the key rows (IDOR 404), the audit
entry, and the validator mirroring `SshPublicKey`.
