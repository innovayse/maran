# Threat note: `maran deploy-local` (issue #410)

`rules/security.md` requires a threat note, written first, for a change to the installer's privileged
steps and to what an installed host trusts. This note is written BEFORE the code: every statement
below is a requirement the code must meet. Where the code lands and differs, the code is wrong or
this note is amended in the same change.

**Second review: OUTSTANDING — must not merge to `main`.** The work is carried out by agent sessions,
which cannot obtain a second reviewer. The branch MUST NOT merge to `main` until a human has read the
key-refusal and installer-copy parts (rules/security.md "Sensitive change escalation"). Nothing here
discharges it.

## What is added

A developer verb, `maran deploy-local`, that on the developer's workstation builds the tree, signs
the bundle with a TEST key, packages and verifies it, copies the bundle and a COPY of the installer
tree to a development host over `ssh`/`scp`, and runs the installer's offline path there under
`sudo`. Not added: no daemon, no listening port, no rpc, no proto change, no outbound call from the
product, no change to what ships to a customer. It is a workstation tool and never runs on a server.

## What an attacker could do with this surface

1. **Sign something with the production key.** The verb handles a private signing key. If it could
   be pointed at the production key, a mistake or a hostile config file would put a dev build under
   the production signature. Requirement: the production key is refused BEFORE it is opened — by
   path (anything under the production key directory, inside this repository, or named by
   `MARAN_RELEASE_SIGNING_KEY`) and, for any other file, by comparing the DER of its derived public
   half with `installer/keys/release-signing.pub`. An unreadable or non-P-384 key is refused, never
   assumed fine. An exported `MARAN_RELEASE_SIGNING_KEY` cannot substitute, because `cmd_sign`'s `--key`
   argument wins over it (the verb also clears it for the call; the selftest observes the
   precedence, not the clearing). An encrypted key is refused before openssl is run, so the verb
   never makes openssl prompt for a passphrase. The ssh identity gets the same path refusal.
2. **Make a production host trust a test key.** The installer copy shipped to the host carries the
   TEST public key in place of `installer/keys/release-signing.pub`, so the host (and every later
   `maran update` there) trusts the test key. That is the purpose, and it is dangerous on a real
   server. Requirement: the verb states it in its output and docs on every run; the repository file
   is never modified (its digest is compared before and after); the only keys accepted are ones the
   developer holds outside the repository. It cannot know which host is "production": that is the
   developer's responsibility, stated loudly. The first-run host key is never auto-accepted.
3. **Option or command injection through host, user, port, identity.** Values come from arguments or
   a local file. Requirement: strict allow-list validation (a host cannot start with `-`), `--`
   before the destination in every ssh/scp call, commands built as argv arrays and never through
   `sh -c` on the workstation, the remote stage path validated against a fixed pattern before it is
   used in any command.
4. **A password stored or leaked.** Requirement: key authentication with `BatchMode=yes`; the config
   reader is a strict allow-list (`host user port identity key arch`), never sourced, and refuses a
   `password`-like key by name; no password is read from a file, an argument or the environment;
   `--ask-sudo-password` only lets `sudo` itself prompt on the terminal (`ssh -t`). The key file, the
   host and the config are outside the repository and a path inside it is refused.
5. **Leftovers on the host.** Removal is best effort on a normal exit and on INT, TERM and HUP; a
   SIGKILL or a lost connection skips it, and the stage directory is then left in `/tmp`.
   The rest of this point: The stage directory is created with `mktemp -d` by the login user and
   the install itself runs from a `0700` root-owned directory (the offline-tarball directory rule);
   both are removed in a trap unless `--keep-remote` is given. The host chooses nothing: the stage
   directory is created from a fixed template and any other answer is refused, so the cleanup can
   only remove a path of that exact shape. `StrictHostKeyChecking=yes` is set on every call.

## What this cannot establish

A real ssh, sudo and install on a host are not exercised by the selftest, which drives fakes for
`ssh` and `scp` and reads the argv they were given. The remote script is checked for its text and its
argv, not run as root. The first real run on a development host is the owner's.

## Reviewer checklist (OUTSTANDING)

- Read `deploy_refuse_production_key`: is the refusal before any open of the key, and does the
  public-half comparison use DER, not text?
- Read the installer-copy step: can the repository's `installer/keys/release-signing.pub` change?
- Read every ssh/scp call for a place an unvalidated value reaches an argument.
