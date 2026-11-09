#!/usr/bin/env bash
# update-selftest-notes.sh: the signed release date and release-notes URL that `maran update --check`
# prints - valid values, a manifest that carries neither, hostile values that are validly signed but
# must never reach the terminal, and a field edited after signing.
#
# Sourced by update-selftest.sh after update-selftest-dumps.sh; never executed on its own and it sets
# no shell option. Everything here drives the library copies in the temporary root.

# notes_bundle: <dir> <field line replacement or empty> ... builds a signed manifest copy in DIR from
# the pristine stable one. set_field <dir> <field> <value> rewrites one top-level string field and
# signs again, because a hostile value must arrive inside a manifest whose signature verifies.
set_field() {
  local dir="$1" field="$2" value="$3"
  NOTES_VALUE="$value" awk -v field="\"${field}\"" '
    $0 ~ ("^[[:space:]]*" field "[[:space:]]*:") { print "  " field ": \"" ENVIRON["NOTES_VALUE"] "\","; next }
    { print }
  ' "${dir}/manifest.json" > "${dir}/manifest.json.new"
  mv "${dir}/manifest.json.new" "${dir}/manifest.json"
  resign "$dir" "${keydir}/good.key"
}

# notes_case: a fresh, signed copy of the pristine stable manifest; prints its directory.
notes_case() {
  local dir
  dir="$(new_case_dir)"
  cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$dir/"
  printf '%s' "$dir"
}

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 23: the release date and notes URL - valid, absent, hostile, tampered --"
# ---------------------------------------------------------------------------------------------
fresh_layout
d="$(notes_case)"
expect_accept "a manifest carrying notes_url and released, signed by the baked key" cli_release_verify_manifest "$d"
run_capture cli_release_manifest_field "${d}/manifest.json" notes_url
expect_equal "the notes URL is read out of the manifest" "https://github.com/innovayse/maran/releases/tag/v1.0.0" "$(trim "$RUN_OUT")"
run_capture cli_release_manifest_field "${d}/manifest.json" released
expect_equal "the release date is read out of the manifest" "2026-11-07" "$(trim "$RUN_OUT")"
run_capture cli_release_describe_notes "${d}/manifest.json"
expect_equal "a valid manifest prints its date and URL" \
  "released:2026-11-07releasenotes:https://github.com/innovayse/maran/releases/tag/v1.0.0" "$(trim "$RUN_OUT")"

# Absent: an older manifest has neither field, still verifies once signed, and prints the plain line.
d="$(notes_case)"
sed -i '/"notes_url"/d; /"released"/d' "${d}/manifest.json"
grep -qE '"notes_url"|"released"' "${d}/manifest.json" && selftest_failed "fixture: the fields were not removed, so the absent case proves nothing."
resign "$d" "${keydir}/good.key"
expect_accept "a signed manifest without notes_url and released (an older release)" cli_release_verify_manifest "$d"
run_capture cli_release_describe_notes "${d}/manifest.json"
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "a manifest without the fields failed the report (status ${RUN_STATUS}): ${RUN_OUT}"
case "$RUN_OUT" in *"no release notes published"*) ;; *) selftest_failed "the absent case did not print the no-notes line: ${RUN_OUT}" ;; esac
case "$RUN_OUT" in *warning*) selftest_failed "the absent case warned, but nothing is wrong with an older manifest: ${RUN_OUT}" ;; esac
selftest_pass "a manifest without the fields prints the no-notes line and no warning (expected)"

# Hostile URLs: each is validly signed, so the signature cannot be what stops it.
long_ok="https://example.com/$(repeat_char a $((MARAN_RELEASE_NOTES_URL_MAX - 20)))"
long_bad="https://example.com/$(repeat_char a $((MARAN_RELEASE_NOTES_URL_MAX - 19)))"
[ "${#long_ok}" -eq "$MARAN_RELEASE_NOTES_URL_MAX" ] && [ "${#long_bad}" -eq $((MARAN_RELEASE_NOTES_URL_MAX + 1)) ] \
  || selftest_failed "fixture: the boundary URLs have the wrong length."
hostile=(
  "http://example.com/notes"
  "javascript:alert(1)"
  "ftp://example.com/notes"
  "https://"
  "https://example.com/a b"
  $'https://example.com/a\tb'
  $'https://example.com/\x1b[2J\x1b[Hroot@host:~# '
  $'https://example.com/a\rb'
  $'https://example.com/a\x07b'
  'https://example.com/a\nb'
  'https://example.com/a\\b'
  "https://example.com/<script>"
  "https://exämple.com/notes"
  "https://example.com/a'b"
  'https://example.com/$(id)'
  'https://example.com/a;b'
  " https://example.com/notes"
  "$long_bad"
)
for value in "${hostile[@]}"; do
  d="$(notes_case)"
  set_field "$d" notes_url "$value"
  expect_accept "a hostile notes_url inside a correctly signed manifest (${value:0:24})" cli_release_verify_manifest "$d"
  run_capture cli_release_describe_notes "${d}/manifest.json"
  [ "$RUN_STATUS" -eq 0 ] || selftest_failed "a hostile notes_url ended the report (status ${RUN_STATUS}): ${RUN_OUT}"
  case "$RUN_OUT" in *"warning"*) ;; *) selftest_failed "a hostile notes_url printed no warning: ${RUN_OUT}" ;; esac
  case "$RUN_OUT" in *"release notes: not shown"*) ;; *) selftest_failed "a hostile notes_url was not reported as not shown: ${RUN_OUT}" ;; esac
  # The control characters and the text itself must be absent from the whole output.
  case "$RUN_OUT" in *"$value"*) selftest_failed "the hostile notes_url was printed raw: ${RUN_OUT}" ;; esac
  case "$RUN_OUT" in *$'\x1b'* | *$'\r'* | *$'\a'* | *$'\t'*) selftest_failed "a control character reached the output: ${RUN_OUT}" ;; esac
  selftest_pass "a hostile notes_url is dropped with a warning and never printed (${value:0:24})"
done
for value in "$long_ok" "https://example.com/notes/1.0.0-beta.1?x=1&y=2#top" "https://user@example.com:8443/a_b~c%20d,e+f"; do
  d="$(notes_case)"
  set_field "$d" notes_url "$value"
  run_capture cli_release_describe_notes "${d}/manifest.json"
  case "$RUN_OUT" in *"release notes: ${value}"*) ;; *) selftest_failed "a plain https URL was refused (inverse control): ${RUN_OUT}" ;; esac
  case "$RUN_OUT" in *warning*) selftest_failed "a plain https URL warned (inverse control): ${RUN_OUT}" ;; esac
  selftest_pass "a plain https URL is printed (inverse control, ${#value} bytes)"
done

# Dates: a pattern that matches but is not a day, and text that is not a date at all.
for value in "2026-13-01" "2026-02-31" "2026-00-10" "2026-11-07x" "11/07/2026" "yesterday" "2026-11-07 10:00" $'2026-11-07\x1b[2J'; do
  d="$(notes_case)"
  set_field "$d" released "$value"
  run_capture cli_release_describe_notes "${d}/manifest.json"
  [ "$RUN_STATUS" -eq 0 ] || selftest_failed "a bad release date ended the report (status ${RUN_STATUS}): ${RUN_OUT}"
  case "$RUN_OUT" in *"released: not shown"*"warning"* | *"warning"*"released: not shown"*) ;; *) selftest_failed "a bad release date was not dropped with a warning: ${RUN_OUT}" ;; esac
  case "$RUN_OUT" in *"$value"*) selftest_failed "a bad release date was printed raw: ${RUN_OUT}" ;; esac
  selftest_pass "a bad release date is dropped with a warning (${value:0:12})"
done
d="$(notes_case)"
set_field "$d" released "2028-02-29"
run_capture cli_release_describe_notes "${d}/manifest.json"
case "$RUN_OUT" in *"released: 2028-02-29"*) selftest_pass "a leap day is a real date (inverse control)" ;; *) selftest_failed "a leap day was refused: ${RUN_OUT}" ;; esac

# Tampered after signing: the signature covers both fields.
for field in notes_url released; do
  d="$(notes_case)"
  sed -i "s/\"${field}\": \"\\([^\"]*\\)\"/\"${field}\": \"\\1x\"/" "${d}/manifest.json"
  cmp -s "${serve0}/stable/manifest.json" "${d}/manifest.json" && selftest_failed "fixture: the ${field} tamper changed no byte."
  expect_refuse "a manifest whose ${field} was edited after signing" "signature|verif" cli_release_verify_manifest "$d"
done
