"""Checks the operator command catalogue against the sources and the docs (issue #155).

Run through scripts/lib/operator-commands.sh, which owns the verdict line, the toolchain refusal and
the selftest. This file is the reader: it takes a repository root and prints one line per problem.

The catalogue (installer/cli/commands.json) is DECLARED, not scraped, and this check proves it in
both directions against what the root-only `maran` wrapper and the three installer scripts really
parse:

  a. the dispatcher's case labels == the catalogue's verbs and aliases == the usage text's verbs;
  b. per verb, every flag the implementing source parses is catalogued, every catalogued flag is in
     that source's text, and every catalogued flag and subcommand is named by the usage text;
  c. the same for install.sh, uninstall.sh and get.sh;
  d. every `maran <verb> [--flag]` quoted in code in the scanned docs names a catalogued verb and
     flag (a developer-toolbox verb is accepted only where it is not written as the root command).

Exit status: 0 nothing wrong, 1 at least one problem, 2 the check could not read its input.
"""

import json
import re
import sys
from pathlib import Path

CATALOGUE = "installer/cli/commands.json"
WRAPPER = "installer/cli/maran"
LIBRARY_DIR = "installer/cli/lib"
TOOLBOX = "scripts/maran"
INSTALLER_SCRIPTS = ("install.sh", "uninstall.sh", "get.sh")

# Folders of documents whose quoted commands are held to the catalogue. Widening this list is a
# decision (the other docs quote the developer toolbox far more often than the operator's command).
DOC_DIRS = ("docs/operations",)

# Documents known to quote an operator command. The scan finding NONE in one of them means it went
# blind (a changed fence style, a renamed folder), not that the document is clean.
DOCS_KNOWN_TO_QUOTE = (
    "docs/operations/certificates.md",
    "docs/operations/operator-commands.md",
    "docs/operations/panel-disaster-recovery.md",
    "docs/operations/panel-update.md",
)

FLAG = re.compile(r"(?<![\w-])(--?[A-Za-z][A-Za-z0-9-]*)")
# A case arm: one or more `|`-separated words, then `)`.
ARM = re.compile(r"^\s*([A-Za-z-][\w-]*(?:\|-{0,2}[A-Za-z][\w-]*)*)\)")
COMPARED = re.compile(r"""=\s*["'](--?[A-Za-z][A-Za-z0-9-]*)["']""")
SUDO_FORM = re.compile(r"(?:sudo\s+|/usr/local/sbin/)$")
VERB_TOKEN = re.compile(r"(?<![\w./-])maran\s+([a-z][a-z-]*)")
SCRIPT_TOKEN = re.compile(r"(?<![\w-])(install\.sh|uninstall\.sh|get\.sh)\b")


def read(root, rel):
    """Returns a file's text, or exits 2 naming the file that was missing."""
    path = Path(root, rel)
    try:
        return path.read_text(encoding="utf-8")
    except OSError as error:
        print(f"COULD NOT READ {rel}: {error.strerror}")
        sys.exit(2)


def arm_words(line):
    """Returns the words of a case arm on this line, or an empty list when it is not one."""
    found = ARM.match(line)
    return found.group(1).split("|") if found else []


def source_flags(text):
    """Returns the flags a shell source parses: case-arm words and `= "--x"` comparisons."""
    flags = set()
    for line in text.splitlines():
        flags.update(word for word in arm_words(line) if word.startswith("-"))
        flags.update(COMPARED.findall(line))
    return flags


def dispatcher(text):
    """Returns {label: function} for the wrapper's `main` case; the function is None for an inline arm."""
    start = text.index("main() {")
    labels, current = {}, None
    for line in text[start:].splitlines():
        words = arm_words(line)
        if words and not line.startswith("      "):
            current = words
            body = line.split(")", 1)[1]
            for word in words:
                labels[word] = body
        elif current and line.startswith("      "):
            for word in current:
                labels[word] += " " + line
        elif line.strip() == "esac":
            break
    return {label: (re.search(r"\b(cli_[a-z_]+)", body) or [None, None])[1] for label, body in labels.items()}


def usage_sections(text):
    """Returns {verb: text of its usage lines} from the wrapper's `cli_usage` heredoc."""
    block = text.split("<<'USAGE'", 1)[1].split("\nUSAGE", 1)[0]
    sections, verb = {}, None
    for line in block.splitlines():
        found = re.match(r"^  ([a-z][a-z-]*)\b", line)
        if found:
            verb = found.group(1)
        if verb:
            sections[verb] = sections.get(verb, "") + line + "\n"
    return sections


def defining_source(function, wrapper_text, root):
    """Returns (relative path, text) of the file defining a shell function, or None."""
    pattern = re.compile(rf"^{re.escape(function)}\(\) \{{", re.MULTILINE)
    if pattern.search(wrapper_text):
        return WRAPPER, wrapper_text
    for path in sorted(Path(root, LIBRARY_DIR).glob("*.sh")):
        text = path.read_text(encoding="utf-8")
        if pattern.search(text):
            return f"{LIBRARY_DIR}/{path.name}", text
    return None


def check_verbs(root, catalogue, problems, counts):
    """Checks (a) and (b): the dispatcher, the catalogue and the usage text agree, and so do the flags."""
    wrapper_text = read(root, WRAPPER)
    labels = dispatcher(wrapper_text)
    usage = usage_sections(wrapper_text)
    verbs = {entry["verb"]: entry for entry in catalogue["commands"]}
    aliases = {alias for names in catalogue.get("aliases", {}).values() for alias in names}
    declared = set(verbs) | aliases

    for label in sorted(set(labels) - declared):
        problems.append(f"the dispatcher in {WRAPPER} routes '{label}', which {CATALOGUE} does not declare")
    for label in sorted(declared - set(labels)):
        problems.append(f"{CATALOGUE} declares '{label}', which the dispatcher in {WRAPPER} does not route")
    for verb in sorted(set(verbs) - set(usage)):
        problems.append(f"the usage text in {WRAPPER} does not list the catalogued verb '{verb}'")
    for verb in sorted(set(usage) - set(verbs)):
        problems.append(f"the usage text in {WRAPPER} lists '{verb}', which {CATALOGUE} does not declare")
    counts["verbs"] = len(labels)
    counts["usage verbs"] = len(usage)

    for verb, entry in sorted(verbs.items()):
        function = labels.get(verb)
        defined = defining_source(function, wrapper_text, root) if function else None
        if defined is None:
            if verb in labels:
                problems.append(f"'{verb}' is routed to {function or 'no function'}, which no source defines")
            continue
        path, text = defined
        parsed, catalogued = source_flags(text) - aliases, set(entry["flags"])
        for flag in sorted(parsed - catalogued):
            problems.append(f"{path} parses {flag} for '{verb}', which {CATALOGUE} does not list")
        for flag in sorted(catalogued - parsed):
            problems.append(f"{CATALOGUE} lists {flag} for '{verb}', which {path} never parses")
        said = set(FLAG.findall(usage.get(verb, "")))
        for flag in sorted(catalogued - said):
            problems.append(f"the usage text for '{verb}' does not mention the catalogued flag {flag}")
        for flag in sorted(said - catalogued):
            problems.append(f"the usage text for '{verb}' mentions {flag}, which {CATALOGUE} does not list")
        arms = {word for line in text.splitlines() for word in arm_words(line)}
        for sub in entry["subcommands"]:
            if sub not in arms:
                problems.append(f"{CATALOGUE} lists subcommand '{sub}' of '{verb}', which {path} has no case arm for")
            if sub not in usage.get(verb, ""):
                problems.append(f"the usage text for '{verb}' does not mention the catalogued subcommand '{sub}'")
        counts["flags"] += len(catalogued)
    return verbs


def check_installer(root, catalogue, problems, counts):
    """Checks (c): the flag list of each installer script equals what its argument loop parses."""
    declared = catalogue["installer"]
    for script in sorted(set(declared) | set(INSTALLER_SCRIPTS)):
        if script not in declared or script not in INSTALLER_SCRIPTS:
            problems.append(f"{CATALOGUE} 'installer' lists '{script}', which this check does not know how to read")
            continue
        text = read(root, f"installer/{script}")
        parsed, catalogued = source_flags(text), set(declared[script])
        for flag in sorted(parsed - catalogued):
            problems.append(f"installer/{script} parses {flag}, which {CATALOGUE} does not list")
        for flag in sorted(catalogued - parsed):
            problems.append(f"{CATALOGUE} lists {flag} for {script}, which installer/{script} never parses")
        counts["installer flags"] += len(catalogued)


def toolbox_verbs(root):
    """Returns the verbs of the developer toolbox's own `maran` (scripts/maran), a different command."""
    verbs = set()
    for line in read(root, TOOLBOX).splitlines():
        found = re.match(r"^  ([a-z][a-z-]*)\)", line)
        if found:
            verbs.add(found.group(1))
    return verbs


def code_fragments(text):
    """Yields every fenced line (continuations joined) and every inline code span of a document."""
    fenced, held = False, ""
    for line in text.splitlines():
        if line.lstrip().startswith("```"):
            fenced, held = not fenced, ""
            continue
        if fenced:
            held += line.rstrip("\\").strip() + " "
            if not line.rstrip().endswith("\\"):
                yield held
                held = ""
        else:
            yield from re.findall(r"`([^`]+)`", line)


def check_docs(root, catalogue, verbs, problems, counts):
    """Checks (d) and its vacuity guard: quoted operator commands exist, and the scan is not blind."""
    own = toolbox_verbs(root)
    quoted = {}
    for folder in DOC_DIRS:
        for path in sorted(Path(root, folder).glob("*.md")):
            rel = path.relative_to(root).as_posix()
            quoted[rel] = 0
            for fragment in code_fragments(path.read_text(encoding="utf-8")):
                quoted[rel] += scan_fragment(fragment, rel, verbs, own, catalogue, problems)
    for rel in DOCS_KNOWN_TO_QUOTE:
        if quoted.get(rel, 0) == 0:
            problems.append(f"the scan found no quoted command in {rel}, which is known to quote one: the scan went blind")
    counts["docs scanned"] = len(quoted)
    counts["quoted commands"] = sum(quoted.values())


def scan_fragment(fragment, rel, verbs, own, catalogue, problems):
    """Checks one code fragment; returns the number of operator commands it quoted."""
    quoted = 0
    matches = list(VERB_TOKEN.finditer(fragment))
    for index, found in enumerate(matches):
        verb = found.group(1)
        root_form = bool(SUDO_FORM.search(fragment[: found.start()]))
        if verb in verbs:
            quoted += 1
        elif verb in own and not root_form:
            continue
        else:
            problems.append(f"{rel} quotes 'maran {verb}', which is no operator verb in {CATALOGUE}")
            continue
        end = matches[index + 1].start() if index + 1 < len(matches) else len(fragment)
        for flag in FLAG.findall(fragment[found.end():end]):
            if flag not in verbs[verb]["flags"]:
                problems.append(f"{rel} quotes 'maran {verb} {flag}', and {CATALOGUE} lists no such flag")
    for found in SCRIPT_TOKEN.finditer(fragment):
        script = found.group(1)
        quoted += 1
        for flag in FLAG.findall(fragment[found.end():]):
            if flag not in catalogue["installer"][script]:
                problems.append(f"{rel} quotes '{script} {flag}', and {CATALOGUE} lists no such flag")
    return quoted


def main(root):
    """Runs every check against a repository root; prints problems then a count line; returns the status."""
    try:
        catalogue = json.loads(read(root, CATALOGUE))
    except ValueError as error:
        print(f"COULD NOT PARSE {CATALOGUE}: {error}")
        return 2
    problems = []
    counts = {"verbs": 0, "usage verbs": 0, "flags": 0, "installer flags": 0, "docs scanned": 0, "quoted commands": 0}
    verbs = check_verbs(root, catalogue, problems, counts)
    check_installer(root, catalogue, problems, counts)
    check_docs(root, catalogue, verbs, problems, counts)
    # The vacuity guard on the axis that can go blind: the dispatcher parse must find at least as many
    # verbs as the usage text lists, or the parse read nothing and every "agrees" above is empty.
    if counts["verbs"] < counts["usage verbs"] or counts["verbs"] == 0:
        problems.append("the dispatcher parse found fewer verbs than the usage text lists: the parse went blind")
    for problem in problems:
        print(f"PROBLEM: {problem}")
    print("COMPARED: " + ", ".join(f"{count} {name}" for name, count in counts.items()))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
