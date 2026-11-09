# The operator's commands, as data

The root-only `maran` command on a server and the three installer scripts (`install.sh`,
`uninstall.sh`, `get.sh`) are what an operator types. Their verbs and flags are listed in one
machine-readable file, so a page that quotes them can be checked against the product instead of
against someone's memory (issue #155).

## Reading the list

```
sudo maran help --json
```

prints the installed copy of `installer/cli/commands.json`, byte for byte, and exits. The same file
is in the installer package and in the repository. The command only prints it: nothing in `maran`
reads the file to decide what to do, and dispatch stays a fixed list of verbs.

This is the server's command, not the developer toolbox of the same name (`maran test`,
`maran structure`, which exist only in a checkout after `source scripts/dev`). The toolbox is not in
the list.

## The format

Schema 1. All lists are sorted; the file ends with a newline.

| Field | Meaning |
|---|---|
| `schema` | The format version, `1`. A reader that does not know it must stop. |
| `wrapper` | The command name, `maran`. |
| `commands[].verb` | A verb the wrapper routes. |
| `commands[].summary` | One sentence for a page that wants to display something. |
| `commands[].positional` | Positional arguments, as written in the usage text. |
| `commands[].subcommands` | Words the verb takes instead of flags, such as `enable`, `disable`, `status`. |
| `commands[].flags` | Every flag the verb parses. |
| `aliases` | Other spellings of a verb, such as `--help` and `-h` for `help`. |
| `installer` | The flags of each installer script, by file name. |

A flag that takes a value is listed once, by its name. The file says nothing about which flags can
be combined; the usage text and `docs/operations/panel-update.md` do.

## Keeping the list true

`maran operator-commands` (developer toolbox, `scripts/lib/operator-commands.sh`) fails CI when the
list and the sources disagree, in both directions:

- the wrapper's dispatcher, the usage text and the list name the same verbs;
- every flag a verb's source parses is in the list, and every listed flag is parsed there and named by
  the usage text;
- the same for each installer script's argument loop;
- every `maran <verb> --flag` quoted in code in `docs/operations` names a listed verb and flag. A
  verb of the developer toolbox is accepted only when it is not written as the root command
  (`sudo maran ...`).

So a verb or flag added to the code without a row in the list fails, and so does a page that quotes
one that was removed. `maran operator-commands selftest` proves the check against planted breakage
and against an unchanged copy that it must accept. The check prints what it cannot see: flags a
library accepts through a variable or a computed case label, commands quoted outside
`docs/operations`, and behaviour on a host.

A new verb or flag therefore takes four edits in one change: the code, the usage text in
`installer/cli/maran`, the row in `installer/cli/commands.json`, and the page that explains it.
