# Deploying a development build to a test host

`maran deploy-local` builds the tree on your workstation, signs it with a TEST key, packages and
verifies it, copies it to a development host over `ssh`/`scp`, and runs the installer's offline path
there under `sudo`. It is for getting a work-in-progress build onto a machine you use for development
or testing. How a release is cut and signed for customers is in `docs/operations/release-process.md`;
this page is only about the developer shortcut.

## Never use it on a production server

**The host will trust the TEST key afterwards, and not only for this install.** To make the install
verify, the installer copy shipped to the host carries your test public key in place of
`installer/keys/release-signing.pub`. From then on that host also trusts the test key for `maran
update`: anyone holding the test private key can sign an update the host accepts. The verb cannot know
which host is a production one; that judgement is yours. Treat every host you run it against as a
development host from that moment, and reinstall from a real release before it carries anything real.

The repository's own `installer/keys/release-signing.pub` is never modified (the verb compares its
digest before and after the run and fails if it changed), and a real release is unaffected.

## One-time setup

### 1. A test signing key, outside the repository

```
mkdir -p ~/.maran-dev
openssl ecparam -name secp384r1 -genkey -noout -out ~/.maran-dev/test-signing.key
chmod 600 ~/.maran-dev/test-signing.key
```

This is the default `--key`. The key is a P-384 key, the same curve the release pipeline uses, and it is
a different key. Nothing about it goes into the repository: a key path inside the repository is refused.

### 2. Key-only ssh, and sudo on the host

- ssh authentication must work with a key and without a prompt. Every `ssh` and `scp` call runs with
  `StrictHostKeyChecking=yes` and `BatchMode=yes`, with one exception named below. Run
  `ssh <user>@<host>` once by hand first: a host key that is not already known is refused, never
  accepted automatically, and the run ends as DID NOT RUN.
- The login user needs passwordless `sudo`. If you would rather type the sudo password, pass
  `--ask-sudo-password`; `sudo` then prompts on your terminal and the verb never sees the password.
  That is the single call (the one that runs the remote script) made as `ssh -t` without
  `BatchMode=yes`, dropped only so that `sudo` can prompt. It sets `PasswordAuthentication=no` and
  `KbdInteractiveAuthentication=no`, so the ssh login itself still cannot fall back to a password, and
  it keeps `StrictHostKeyChecking=yes`. Every other call keeps `BatchMode=yes`.

### 3. Optional config file

`$HOME/.maran-dev/deploy-local.conf` is read when it exists. It is a plain file of `key=value` lines;
`#` comments and blank lines are allowed. It is never sourced or evaluated, only parsed. The allowed
keys are exactly `host`, `user`, `port`, `identity`, `key` and `arch`; any other key is refused by name.

```
# ~/.maran-dev/deploy-local.conf
host=<development host>
user=<login user>
port=22
identity=~/.ssh/<your key>
```

The file must be a regular file (not a symlink), owned by you, and not writable by group or others. It
never holds a password: a `password`-like key is refused with a message pointing at key
authentication and `--ask-sudo-password`. A config path, key path or identity path that resolves
inside the repository is refused. Keep the file outside the repository and untracked.

Every value is checked by the same rules as the matching option. In `identity` and `key`, a leading
`~/` is expanded to `$HOME/`; a `~user/` form or a bare `~` is refused, so write the absolute path
for those. Values given on the command line are not expanded by the verb (your shell does that). The
config file may set only `host`, `user`, `port`, `identity`, `key` and `arch`; a command-line option
always wins over the file.

## Usage

```
maran deploy-local [[user@]host] [options]
```

Precedence is command line, then config file, then defaults. With no host from any source the verb
fails and says how to give one; there is no default host.

| Argument | Meaning |
|---|---|
| `[user@]host` | the destination, positional |
| `--host H` | the host (letters, digits, `.`, `_` and `-` only, so no `:`; must start with a letter or digit; at most 253 characters) |
| `--user U` | the login user (`[a-z_][a-z0-9_-]*`, at most 32 characters); `$USER` is used only when nothing else names one |
| `--port N` | the ssh port, 1 to 65535; default 22 |
| `--identity PATH` | the ssh private key to use (adds `-i` and `IdentitiesOnly=yes`); must be outside the repository and outside `$HOME/.maran-release` |
| `--key PATH` | the TEST signing key; default `$HOME/.maran-dev/test-signing.key`; must be unencrypted (a passphrase-protected key is refused) and outside the repository |
| `--config PATH` | the config file; default `$HOME/.maran-dev/deploy-local.conf`, read only if it exists; an explicit path that does not exist is a failure |
| `--arch x86_64\|aarch64` | the host's architecture; when omitted the verb asks the host with `uname -m` (`arm64` is read as `aarch64`, anything else fails) |
| `--version V` | forwarded to the build step |
| `--channel stable\|beta` | forwarded to the build step |
| `--bundle DIR` | use an already built bundle directory and skip the build; it is copied and the copy is signed with the test key (below) |
| `--ask-sudo-password` | let `sudo` on the host prompt on your terminal instead of requiring `sudo -n` |
| `--keep-remote` | do not delete the stage directory on the host when the run ends |
| `--dry-run` | validate and print, run nothing (below) |

### What a run does

1. Parses and validates every value, and requires `ssh`, `scp`, `openssl` and `tar`.
2. Refuses the key if it is the production key (below).
3. Finds the host's architecture unless `--arch` was given, then (without `--ask-sudo-password`) proves
   that `sudo -n` works on the host, before anything is built or copied.
4. Builds the bundle (unless `--bundle`, which is copied instead), signs it with the test key, derives
   the test public key, and packages the offline bundle with that key.
5. Copies the installer tree to a temporary directory, replaces `installer/keys/release-signing.pub`
   in the COPY with the test public key, and checks that the copy holds the test key and not the
   repository's.
6. Creates the stage directory on the host with `mktemp -d /tmp/maran-deploy-stage.XXXXXXXXXX`. The
   answer must match that template exactly (the fixed prefix and ten letters or digits); any other
   answer is refused as FAILED and nothing is removed on the host. It then copies the bundle, the
   installer copy and the remote script into the stage directory and runs the remote script under
   `sudo`. The remote script works in a `0700` root-owned directory and runs the installer's
   `--offline-tarball` path.
7. Removes the stage directory and the local temporary directory on a normal exit and on INT, TERM and
   HUP, best effort (see Cleanup).

On success the verb prints a loud warning that the host now trusts the TEST key (also for `maran
update`), then `DEPLOY-LOCAL VERDICT: OK`.

### `--dry-run`

Validates the arguments, refuses the production key, checks the tools are present, and then prints
each command as a line starting with `+ ` exactly as it would run. Nothing is built, signed, packed,
copied or executed, and the host is not contacted. If `--arch` was not given the architecture is
shown as `<arch detected on the host>`. It ends with `DEPLOY-LOCAL VERDICT: OK - dry run, nothing was
executed`. The host and key path appear in this output; key contents never do.

### `--bundle`

`--bundle DIR` skips the build and uses a bundle you already have. The directory is COPIED into the
verb's private temporary directory and the copy is signed there with the test key; the original is
never re-signed or modified. A bundle signed by the release pipeline is therefore not reused as it
was: the test signature on the copy is what the host will verify.

### `--keep-remote`

Leaves the stage directory on the host for inspection. The root-owned working directory used by the
install is removed regardless.

## What is refused

- **The production key.** Refusal is by path first, before the file is opened: a key is refused if it
  is under `$HOME/.maran-release`, inside the repository, or the file named by
  `MARAN_RELEASE_SIGNING_KEY`. Then the file must be a readable regular file, not passphrase-protected
  (an encrypted key is refused), and a P-384 key. Last comes the public-half comparison, which catches
  a copy of the production key kept anywhere else: the key is refused if its public half is the
  repository's `installer/keys/release-signing.pub` (compared as DER), with a message containing
  "production release key". The signing step is called with `MARAN_RELEASE_SIGNING_KEY` cleared, so an
  exported variable can never substitute for `--key`.
- **An ssh identity under `$HOME/.maran-release` or inside the repository.** The production key
  directory is refused for the identity by path, as it is for the signing key.
- **A host that starts with a dash**, or any host, user or port outside the patterns in the table. Every
  `ssh` and `scp` call carries `--` before the destination, and commands are argument arrays, never
  `sh -c` on your workstation.
- **A password anywhere.** There is no password option, no password key in the config file, and none
  is read from the environment.
- **A key, identity or config path inside the repository.**

## Exit codes and verdict lines

The last line on stdout is always one of these.

| Line | Exit | Meaning |
|---|---|---|
| `DEPLOY-LOCAL VERDICT: OK` | 0 | the installer on the host exited 0 (or a dry run finished) |
| `DEPLOY-LOCAL VERDICT: FAILED` | 1 | the arguments, key or build were refused, or the remote installer exited non-zero (its status is in the message) |
| `DEPLOY-LOCAL VERDICT: DID NOT RUN` | 2 | nothing was measured: a required tool is missing, the host is unreachable or ssh authentication failed, or `sudo -n` needs a password |

## Cleanup

A real run (not `--dry-run`) installs a cleanup trap for a normal exit and for INT, TERM and HUP. It
removes the stage directory on the host unless `--keep-remote` is given (it then says it left the
directory), and removes the workstation's temporary directory (test public key, bundle copy, packaged
bundle, installer copy). Cleanup is best effort, not a guarantee: SIGKILL or a lost connection skips
it, and a failed removal is reported without changing the exit status. The directory stays on the host
then, and it is yours to delete. A stage directory the host answered with an unusable path is never
removed.

## What this does NOT verify

- It reads the installer's exit status and nothing else. `UNOBSERVED HERE`: the host's service health
  after the install. Run `maran status` on the host.
- The selftest (`maran deploy-local selftest`) drives fake `ssh` and `scp` and reads the argument
  lists they were given. A real ssh, a real sudo and a real install on a host are not exercised by it,
  and the remote script is checked for its text and arguments, not run as root.
- It cannot tell a development host from a production one.
- The second security review of this verb is OUTSTANDING:
  `docs/superpowers/notes/2026-11-06-deploy-local-threat-note.md` lists what a reviewer must check.
