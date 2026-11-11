#!/usr/bin/env python3
"""Drop the CodeQL results that sit in generated C# before they are uploaded (issue #523).

WHY THIS EXISTS

CodeQL applies `paths-ignore` only to interpreted languages. A C# database is built from the
compiler's own inputs, so the protobuf and gRPC sources of Maran.Agent.Client and the RegexGenerator
output, which the build writes under an `obj/` directory, are analysed whatever the configuration
says. Generated code is never edited (rules/proto.md), so an alert in it can only be dismissed,
never fixed, and the noise hides a real new finding in a pull request's checks.

The analysis therefore runs with its upload switched off, this script removes every result whose
primary location is below an `obj/` directory, and the filtered file is uploaded afterwards.

USAGE
  codeql-sarif-filter.py filter <sarif-file-or-directory>   rewrite the SARIF files in place
  codeql-sarif-filter.py selftest                           prove it drops AND keeps

A path counts as generated when one of its DIRECTORY segments is exactly `obj`; `src/objects/X.cs`,
`obj.cs` and `objective/X.cs` are not. Only the primary location decides, because a result is
reported where its fault is.

The OUTPUT is the verdict: `CODEQL-SARIF VERDICT: OK` or `... FAILED`. A filter that was handed no
SARIF file at all refuses with `DID NOT RUN`, because it compared nothing.
"""
import json
import os
import sys
import tempfile

GENERATED_SEGMENT = "obj"


def is_generated(uri):
    """True when a directory segment of the artifact URI is exactly `obj`."""
    segments = uri.replace("\\", "/").split("/")
    return GENERATED_SEGMENT in segments[:-1]


def primary_uri(result):
    """The artifact URI of a result's first location, or None when it has none."""
    for location in result.get("locations") or []:
        uri = (location.get("physicalLocation") or {}).get("artifactLocation", {}).get("uri")
        if uri:
            return uri
        break
    return None


def filter_document(document):
    """Remove generated results in place; return how many were dropped."""
    dropped = 0
    for run in document.get("runs") or []:
        results = run.get("results") or []
        kept = [r for r in results if not (primary_uri(r) and is_generated(primary_uri(r)))]
        dropped += len(results) - len(kept)
        run["results"] = kept
    return dropped


def sarif_files(target):
    if os.path.isdir(target):
        return sorted(
            os.path.join(target, name) for name in os.listdir(target) if name.endswith(".sarif")
        )
    return [target] if os.path.isfile(target) else []


def run_filter(target):
    files = sarif_files(target)
    if not files:
        print("CODEQL-SARIF VERDICT: DID NOT RUN (no .sarif file under %s)" % target)
        return 1
    for path in files:
        with open(path, encoding="utf-8") as handle:
            document = json.load(handle)
        dropped = filter_document(document)
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(document, handle)
        print("%s: dropped %d generated result(s)" % (path, dropped))
    print("CODEQL-SARIF VERDICT: OK")
    return 0


def sample_result(rule, uri):
    return {
        "ruleId": rule,
        "message": {"text": "sample"},
        "locations": [{"physicalLocation": {"artifactLocation": {"uri": uri}}}],
    }


def selftest():
    failures = []

    def expect(name, condition):
        if not condition:
            failures.append(name)

    dropped_uris = [
        "backend/src/Maran.Agent.Client/obj/Release/net9.0/Grpc/Agent.cs",
        "backend/src/Maran.Modules/obj/Debug/net9.0/generated/RegexGenerator.g.cs",
        "obj/Foo.cs",
        "backend\\src\\X\\obj\\Foo.cs",
    ]
    kept_uris = [
        "backend/src/Maran.Modules/Sites/Services/SiteService.cs",
        "backend/src/objects/Foo.cs",
        "backend/src/objective/Foo.cs",
        "backend/src/Maran.Modules/obj.cs",
        "backend/tests/Maran.Tests/ObjTests.cs",
    ]
    document = {
        "runs": [
            {
                "results": [sample_result("cs/x", u) for u in dropped_uris + kept_uris]
                + [{"ruleId": "cs/no-location", "message": {"text": "sample"}}]
            }
        ]
    }
    dropped = filter_document(document)
    survivors = [primary_uri(r) for r in document["runs"][0]["results"]]
    expect("every generated path is dropped", dropped == len(dropped_uris))
    for uri in kept_uris:
        expect("kept (inverse control): " + uri, uri in survivors)
    expect("a result without a location is kept", None in survivors)
    for uri in dropped_uris:
        expect("really gone: " + uri, uri not in survivors)

    clean = {"runs": [{"results": [sample_result("cs/x", u) for u in kept_uris]}]}
    expect("a document with no generated path loses nothing", filter_document(clean) == 0)

    with tempfile.TemporaryDirectory() as folder:
        path = os.path.join(folder, "csharp.sarif")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"runs": [{"results": [sample_result("cs/x", dropped_uris[0])]}]}, handle)
        expect("the command filters a directory", run_filter(folder) == 0)
        with open(path, encoding="utf-8") as handle:
            expect("the file on disk was rewritten", json.load(handle)["runs"][0]["results"] == [])
        expect("an empty directory refuses", run_filter(os.path.join(folder, "nothing")) == 1)

    for name in failures:
        print("FAIL: " + name)
    print("CODEQL-SARIF VERDICT: " + ("FAILED" if failures else "OK"))
    return 1 if failures else 0


def main(argv):
    if len(argv) == 2 and argv[0] == "filter":
        return run_filter(argv[1])
    if len(argv) == 1 and argv[0] == "selftest":
        return selftest()
    print(__doc__.split("USAGE")[1].split("A path counts")[0], file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
