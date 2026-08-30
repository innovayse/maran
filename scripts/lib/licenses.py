"""Builds THIRD-PARTY-NOTICES.md from dependency metadata already on disk.

Invoked by `maran licenses`, which gathers the three inputs first. Nothing here reaches the
network: the answer must be the one the build actually used, and must be reproducible on a
machine with no internet.

The rule that shapes this file: an unreadable licence is an error, never a placeholder. The
first version wrote "see package" when a package was missing from the local cache, which made
the output depend on the reader's disk — the same file regenerated elsewhere differed, and the
check that compares them could never pass.
"""

import json
import pathlib
import sys
import xml.etree.ElementTree as ET

OWN_CRATES = {"maran-agent", "maran-agent-core", "maran-distro", "maran-ops", "maran-templates"}


def crates(metadata):
    """Every crate the agent links, minus our own, from cargo's own resolution."""
    rows = set()
    for package in metadata["packages"]:
        if package["name"] in OWN_CRATES:
            continue
        rows.add((package["name"], package["version"], package.get("license") or "not declared"))
    return sorted(rows)


def read_nuspec_licence(nuspec):
    """The licence a NuGet package declares, or None when the package is not on disk."""
    if not nuspec.is_file():
        return None
    try:
        tree = ET.parse(nuspec).getroot()
    except ET.ParseError:
        return None

    namespace = {"n": tree.tag.split("}")[0].strip("{")} if "}" in tree.tag else {}
    node = tree.find(".//n:license", namespace) if namespace else tree.find(".//license")
    url = tree.find(".//n:licenseUrl", namespace) if namespace else tree.find(".//licenseUrl")
    if node is not None and node.text:
        return node.text
    if url is not None and url.text:
        return url.text
    return "not declared"


def nuget_packages(root):
    """The packages the projects actually RESOLVED, read from every project.assets.json.

    Not from Directory.Packages.props: that file declares versions, and some are referenced by
    nothing. An unused declaration is never downloaded, so its licence cannot be read — and it
    does not ship either, so it does not belong in a notices file in the first place.
    """
    cache = pathlib.Path.home() / ".nuget" / "packages"
    rows, unresolved = set(), set()

    for assets in (root / "backend").rglob("obj/project.assets.json"):
        try:
            graph = json.loads(assets.read_text())
        except json.JSONDecodeError:
            continue

        for key, library in graph.get("libraries", {}).items():
            if library.get("type") != "package":
                continue
            name, _, version = key.partition("/")
            licence = read_nuspec_licence(cache / name.lower() / version / f"{name.lower()}.nuspec")
            if licence is None:
                unresolved.add(f"{name} {version}")
                continue
            rows.add((name, version, licence))

    if unresolved:
        print("Could not read a licence for:", file=sys.stderr)
        for item in sorted(unresolved):
            print(f"  {item}", file=sys.stderr)
        print("Run `dotnet restore backend/Maran.sln` and try again.", file=sys.stderr)
        sys.exit(1)

    return sorted(rows)


def npm_packages(root, tree):
    """The SPA's runtime dependencies, walked from `npm ls --omit=dev`."""
    rows, unresolved = set(), set()

    def walk(node):
        for name, child in (node.get("dependencies") or {}).items():
            version = child.get("version")
            if version:
                manifest = root / "frontend" / "node_modules" / name / "package.json"
                if not manifest.is_file():
                    unresolved.add(f"{name} {version}")
                else:
                    try:
                        licence = json.loads(manifest.read_text()).get("license")
                    except json.JSONDecodeError:
                        licence = None
                    rows.add((name, version, licence if isinstance(licence, str) else "not declared"))
            walk(child)

    walk(tree)

    if unresolved:
        print("Not installed, so their licences could not be read:", file=sys.stderr)
        for item in sorted(unresolved):
            print(f"  {item}", file=sys.stderr)
        print("Run `npm ci` in frontend/ and try again.", file=sys.stderr)
        sys.exit(1)

    return sorted(rows)


def table(title, note, rows):
    """Prints one ecosystem's table."""
    print(f"## {title}\n")
    print(f"{note}\n")
    print("| Package | Version | Licence |")
    print("|---|---|---|")
    for name, version, licence in rows:
        print(f"| `{name}` | {version} | {licence} |")
    print()


def main():
    """Writes the whole document to standard output."""
    root = pathlib.Path(sys.argv[1])
    cargo = json.loads(pathlib.Path(sys.argv[2]).read_text())
    npm_text = pathlib.Path(sys.argv[3]).read_text().strip()
    npm = json.loads(npm_text) if npm_text else {}

    print("# Third-party notices\n")
    print("Maran is distributed as binaries built from the packages below. Each belongs to its own")
    print("authors and is used under its own licence; this file is the attribution those licences")
    print("require. It says nothing about the licence of Maran itself, which is `LICENSE`.\n")
    print("Generated by `maran licenses` from metadata on disk — cargo's resolution of")
    print("`Cargo.lock`, the `.nuspec` of each restored NuGet package, and the manifest of each")
    print("installed npm module. Do not edit it by hand; run the command.\n")

    table("Rust — the agent", "Resolved by cargo from `agent/Cargo.lock`.", crates(cargo))
    table(
        ".NET — the panel",
        "Resolved by restore; versions are pinned in `backend/Directory.Packages.props`.",
        nuget_packages(root),
    )
    table(
        "npm — the application",
        "Runtime dependencies only; build and test tooling is not distributed.",
        npm_packages(root, npm),
    )


if __name__ == "__main__":
    main()
