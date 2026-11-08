# Threat note: a machine-readable catalogue of the operator's commands, and `maran help --json` (issue #155)

`rules/security.md` asks for a threat note for a change to the installer's privileged steps. This
change adds one data file to the installer package, installs it with the CLI's libraries, and lets
the root-only `maran` wrapper print it. It adds no verb that acts, no daemon, port, outbound call,
shell string or caller-supplied argument. The note was written before the code.

**The second reviewer requirement is OUTSTANDING (#331).** The work is carried out by an agent
session, which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has
read it and signed off. The reviewer reads "What a reviewer must check".

## What is added

1. `installer/cli/commands.json`: the catalogue (verbs, their flags, the installer scripts' flags).
   Static data, no executable content.
2. `installer/lib/66-cli.sh` installs it to `/usr/local/maran/cli/commands.json`, 0644 root:root, in
   the directory the step already creates 0755 root:root, in the same pass and under the same
   refusal-before-writing check as the libraries.
3. `maran help --json` prints that file's bytes with `cat` and exits. No parsing on the server, no
   `jq`, and no value from the caller reaches a command: the only argument compared is the literal
   string `--json`.
4. `scripts/lib/operator-commands.sh` and `.py` (developer toolbox, CI only) compare the catalogue
   with the dispatcher, the flag parsers and the docs. They never run on a server and execute
   nothing they read.

## What an attacker could do with each, and why it is safe

- **Replace the catalogue on a server.** Needs root, who can already replace the wrapper. A forged
  catalogue changes what `help --json` prints and nothing the CLI does: no verb reads it to decide
  anything, and dispatch stays the fixed `case` list.
- **Make `help --json` run something.** The branch is `[ "${1:-}" = "--json" ]` and then
  `cat -- "$MARAN_CLI_CATALOGUE"` with a constant path. No caller text is expanded. Any other second
  word keeps today's behaviour (the usage text).
- **Learn something secret.** The catalogue holds verb and flag names and one-line summaries, which
  `maran help` already prints. No path, key, host or value.
- **The installer step.** One more `install -m 0644 -o root -g root` line copies a file from the
  package under the same `-f` check as every library; a missing catalogue is a refusal before any
  write, exactly as for a missing library. The step's `/usr/local/sbin` checks are untouched.

## What a reviewer must check

1. The diff of `installer/cli/maran` adds only the `--json` branch, its path constant and the usage
   and header lines, and changes no other dispatch arm.
2. `66-cli.sh` copies the file with exact mode and owner, and the missing-file refusal comes before
   the first write.
3. `help --json` output is byte-identical to the shipped file (the polygon `cmp`).
4. No code reads the installed catalogue to decide behaviour:
   `/usr/bin/grep -rn "commands.json" installer scripts docker` should list only 66-cli.sh, the
   wrapper's print branch, the checker, the polygon assertion and the CI workflow.

## What the author could not verify

Behaviour on the AlmaLinux and Debian families is verified only by the polygon lane
(`maran help --json` on both images), not by the author. The catalogue's completeness is what the CI
gate `maran operator-commands` measures; the gate prints its own blind spots (flags a library accepts
through a variable or a computed case label, commands quoted outside `docs/operations`, behaviour on
a host).
