#!/usr/bin/env bash
# Enforces rules/proto.md on `proto/agent/v1/`: every file compiles, and the contract has only
# grown since the committed baseline. Used by CI (cross job).
#
# Two things are checked, in this order:
#
#   1. Compilation — protoc resolves every import, type and `reserved` clause. A number reused
#      after an explicit `reserved` fails here, in protoc itself.
#   2. The additive law — the compiled descriptor set is rendered as a sorted, human-readable
#      inventory (one line per message, enum, service, rpc, field, enum value and reserved
#      clause) and compared with `proto/agent/v1/contract-baseline.txt`. Removals, renames,
#      renumberings, retypings and number reuse are refused by name. Additions pass silently:
#      new fields and new rpcs are always allowed within v1, and a gate that charged them a
#      ceremony would be routed around.
#
# Why an inventory and not the raw `.pb`: a binary baseline is unreviewable, so regenerating it
# to bury a break would show up in a pull request as an opaque blob change. A text baseline
# makes the same attempt read as "- field … 3 domain" in the diff, in front of a reviewer.
#
# The check NEVER writes the baseline. Recording an accepted change is a separate, deliberate
# command — `maran proto --accept` — that a developer types and CI never runs; refreshing the
# baseline from inside the check would enforce nothing at all.
set -euo pipefail
cd "$(dirname "$0")/../.."

accept=0
case "${1:-}" in
  --accept) accept=1 ;;
  '') ;;
  *) echo "usage: maran proto [--accept]" >&2; exit 2 ;;
esac

baseline="proto/agent/v1/contract-baseline.txt"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

protoc --proto_path=proto --descriptor_set_out="$out/all.pb" proto/agent/v1/*.proto

# protoc's own include directory holds descriptor.proto, which is what teaches `--decode` the
# shape of a FileDescriptorSet. It sits next to the binary in a user-local toolchain and under
# /usr/include when protoc comes from a distro package (CI); both are tried.
protoc_bin="$(command -v protoc)"
descriptor_include="$(cd "$(dirname "$protoc_bin")/../include" 2>/dev/null && pwd || true)"
if [ ! -r "${descriptor_include:-/nonexistent}/google/protobuf/descriptor.proto" ]; then
  descriptor_include="/usr/include"
fi
if [ ! -r "$descriptor_include/google/protobuf/descriptor.proto" ]; then
  echo "proto: cannot find google/protobuf/descriptor.proto next to protoc or in /usr/include" >&2
  exit 1
fi

protoc --proto_path="$descriptor_include" \
  --decode=google.protobuf.FileDescriptorSet google/protobuf/descriptor.proto \
  < "$out/all.pb" > "$out/all.txt"

python3 - "$out/all.txt" > "$out/inventory.txt" <<'PYINVENTORY'
"""Renders a protobuf FileDescriptorSet, in protoc's text form, as a sorted contract inventory.

One line per declared thing, so a reviewer reads a diff of the contract rather than of a blob.
"""
import sys

def parse(lines):
    """Parses protoc's text format into nested dicts; repeated keys become lists."""
    stack = [{}]
    for raw in lines:
        line = raw.strip()
        if not line:
            continue
        if line == '}':
            stack.pop()
            continue
        if line.endswith('{'):
            key = line[:-1].strip().rstrip(':').strip()
            child = {}
            stack[-1].setdefault(key, []).append(child)
            stack.append(child)
            continue
        key, _, value = line.partition(':')
        value = value.strip()
        if value.startswith('"') and value.endswith('"'):
            value = value[1:-1]
        stack[-1].setdefault(key.strip(), []).append(value)
    return stack[0]

def one(node, key, default=''):
    """Returns the single value of a field that is not repeated in practice."""
    values = node.get(key)
    return values[0] if values else default

OUT = []

def field_line(owner, field, oneof_names):
    """Renders a field: its number, name and every part of its wire shape."""
    index = one(field, 'oneof_index', '')
    oneof = oneof_names[int(index)] if index != '' else '-'
    OUT.append('field %s %s %s %s %s type_name=%s oneof=%s' % (
        owner, one(field, 'number'), one(field, 'name'), one(field, 'type'),
        one(field, 'label'), one(field, 'type_name', '-'), oneof))

def walk_enum(prefix, enum):
    """Renders an enum, its values and its reserved clauses."""
    owner = prefix + '.' + one(enum, 'name')
    OUT.append('enum %s' % owner)
    for value in enum.get('value', []):
        OUT.append('enum-value %s %s %s' % (owner, one(value, 'number'), one(value, 'name')))
    for name in enum.get('reserved_name', []):
        OUT.append('reserved-name %s %s' % (owner, name))
    for span in enum.get('reserved_range', []):
        for number in range(int(one(span, 'start')), int(one(span, 'end')) + 1):
            OUT.append('reserved-number %s %d' % (owner, number))

def walk_message(prefix, message):
    """Renders a message, its fields, reserved clauses and every nested declaration."""
    owner = prefix + '.' + one(message, 'name')
    OUT.append('message %s' % owner)
    oneof_names = [one(decl, 'name') for decl in message.get('oneof_decl', [])]
    for field in message.get('field', []):
        field_line(owner, field, oneof_names)
    for name in message.get('reserved_name', []):
        OUT.append('reserved-name %s %s' % (owner, name))
    for span in message.get('reserved_range', []):
        for number in range(int(one(span, 'start')), int(one(span, 'end'))):
            OUT.append('reserved-number %s %d' % (owner, number))
    for nested in message.get('nested_type', []):
        walk_message(owner, nested)
    for nested in message.get('enum_type', []):
        walk_enum(owner, nested)

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    root = parse(handle)

for descriptor in root.get('file', []):
    package = one(descriptor, 'package')
    for message in descriptor.get('message_type', []):
        walk_message(package, message)
    for enum in descriptor.get('enum_type', []):
        walk_enum(package, enum)
    for service in descriptor.get('service', []):
        owner = package + '.' + one(service, 'name')
        OUT.append('service %s' % owner)
        for method in service.get('method', []):
            OUT.append('rpc %s %s in=%s out=%s client_streaming=%s server_streaming=%s' % (
                owner, one(method, 'name'), one(method, 'input_type'), one(method, 'output_type'),
                one(method, 'client_streaming', 'false'), one(method, 'server_streaming', 'false')))

for line in sorted(set(OUT)):
    print(line)
PYINVENTORY

if [ "$accept" -eq 1 ]; then
  cp "$out/inventory.txt" "$baseline"
  echo "PROTO-BASELINE-WRITTEN $baseline"
  echo "review the diff of that file: every removed or changed line is a breaking change (rules/proto.md)"
  exit 0
fi

if [ ! -r "$baseline" ]; then
  echo "proto: no baseline at $baseline — create it once with: maran proto --accept" >&2
  exit 1
fi

python3 - "$baseline" "$out/inventory.txt" <<'PYCOMPARE'
"""Compares the current contract inventory with the baseline under rules/proto.md's additive law.

Breaking (refused): a removed or renamed message, enum, service, rpc, field or enum value; a
changed field number, type, label or oneof membership; a number whose owner changed; a field
that took a number or name marked reserved. Additive (allowed): everything new.
"""
import sys

def load(path):
    """Reads an inventory file into a list of token lists, ignoring blank lines."""
    with open(path, 'r', encoding='utf-8') as handle:
        return [line.split() for line in handle.read().splitlines() if line.strip()]

def index(rows, kind):
    """Returns the rows of one kind, keyed by owner."""
    result = {}
    for row in rows:
        if row[0] == kind:
            result.setdefault(row[1], []).append(row[2:])
    return result

old, new = load(sys.argv[1]), load(sys.argv[2])
breaks = []

# Declarations: a name that existed must still exist. A rename is a removal plus an addition,
# and reads here as the removal it is.
for kind, label in (('message', 'message'), ('enum', 'enum'), ('service', 'service')):
    gone = {r[1] for r in old if r[0] == kind} - {r[1] for r in new if r[0] == kind}
    for name in sorted(gone):
        breaks.append('%s removed or renamed: %s' % (label, name))

# Fields and enum values, by name and by number, per owner.
for kind, number_at, name_at in (('field', 0, 1), ('enum-value', 0, 1)):
    old_by, new_by = index(old, kind), index(new, kind)
    for owner, rows in sorted(old_by.items()):
        current = new_by.get(owner, [])
        by_name = {row[name_at]: row for row in current}
        by_number = {row[number_at]: row for row in current}
        for row in rows:
            name, number = row[name_at], row[number_at]
            if name not in by_name:
                breaks.append('%s removed or renamed: %s.%s (number %s)' % (kind, owner, name, number))
            elif by_name[name][number_at] != number:
                breaks.append('%s renumbered: %s.%s was %s, now %s'
                              % (kind, owner, name, number, by_name[name][number_at]))
            elif by_name[name] != row:
                breaks.append('%s shape changed: %s.%s\n    was: %s\n    now: %s'
                              % (kind, owner, name, ' '.join(row), ' '.join(by_name[name])))
            if number in by_number and by_number[number][name_at] != name:
                breaks.append('%s number reused: %s number %s held %s, now holds %s'
                              % (kind, owner, number, name, by_number[number][name_at]))

# Reserved numbers and names, from the baseline and from the current contract alike: a field may
# never occupy either. protoc refuses this within one file; the baseline catches the case where
# the `reserved` clause itself was deleted in the same change.
reserved_numbers, reserved_names = {}, {}
for rows in (old, new):
    for row in rows:
        if row[0] == 'reserved-number':
            reserved_numbers.setdefault(row[1], set()).add(row[2])
        elif row[0] == 'reserved-name':
            reserved_names.setdefault(row[1], set()).add(row[2])
for row in new:
    if row[0] not in ('field', 'enum-value'):
        continue
    owner, number, name = row[1], row[2], row[3]
    if number in reserved_numbers.get(owner, ()):
        breaks.append('reserved number reused: %s number %s is reserved but held by %s'
                      % (owner, number, name))
    if name in reserved_names.get(owner, ()):
        breaks.append('reserved name reused: %s.%s is reserved' % (owner, name))

# Rpcs: by name, with the whole signature compared.
old_rpcs = {(r[1], r[2]): r for r in old if r[0] == 'rpc'}
new_rpcs = {(r[1], r[2]): r for r in new if r[0] == 'rpc'}
for key, row in sorted(old_rpcs.items()):
    if key not in new_rpcs:
        breaks.append('rpc removed or renamed: %s.%s' % key)
    elif new_rpcs[key] != row:
        breaks.append('rpc signature changed: %s.%s\n    was: %s\n    now: %s'
                      % (key[0], key[1], ' '.join(row), ' '.join(new_rpcs[key])))

if breaks:
    print('PROTO-BREAKING-CHANGE — rules/proto.md allows additive changes only within v1:',
          file=sys.stderr)
    for item in breaks:
        print('  - ' + item, file=sys.stderr)
    print('\nA change of this shape belongs in proto/agent/v2/, not in v1. If the baseline is what',
          file=sys.stderr)
    print('is wrong, fix the baseline in its own reviewed commit: maran proto --accept',
          file=sys.stderr)
    sys.exit(1)

added = len([r for r in new if r not in old])
if added:
    print('PROTO-ADDITIVE %d new line(s); record them with: maran proto --accept' % added)
PYCOMPARE

echo "PROTO-OK"
