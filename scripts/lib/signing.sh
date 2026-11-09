#!/usr/bin/env bash
# The ONE place this repository spells how a file is signed and verified with the P-384 / SHA-384
# scheme, SOURCED by scripts/lib/release-bundle.sh (the release manifest) and
# scripts/lib/module-package.sh (the module package manifest), so the thing each verifier checks can
# never drift from the thing that was signed. The functions were moved here from release-bundle.sh
# unchanged (rules/security.md item 9): the text below is that file's own comment and code.
#
# Which KEY signs what is the caller's decision and never this file's: the release key, the module
# key and the licence key are three different keys (rules/security.md item 9).
#
# Sourced only: it sets no shell options and runs nothing.

# The release signature: ECDSA P-384 over SHA-384, created and verified with `openssl dgst`.
#
# It was Ed25519 through `openssl pkeyutl -verify -rawin`, and that could not be verified on half
# the supported fleet: `-rawin` arrived in OpenSSL 3.0, and AlmaLinux 8, Rocky 8, Ubuntu 20.04 and
# Debian 11 all ship OpenSSL 1.1.1, whose `pkeyutl` has no such option and whose CLI cannot verify
# Ed25519 at all. Measured on AlmaLinux 8 (issue #52):
#
#     # openssl pkeyutl -verify -rawin -pubin -inkey release-signing.pub ...
#     pkeyutl: Option unknown option -rawin
#
# The installer reported that as "the signature does not match the trusted key" and told the
# operator to suspect a compromised mirror, so the old arrangement did not merely fail there — it
# failed while accusing the download.
#
# ECDSA over `openssl dgst` is verifiable by the OS's own openssl on EVERY supported version, with
# no fallback branch and no second key: a supply-chain check that asks "which signature can I manage
# here?" is only as strong as the weakest answer. P-384 rather than P-256 because the margin is free
# — one digest name differs — and this is the key that decides whether a server runs our code.
#
# Measured on AlmaLinux 8's OpenSSL 1.1.1k, both halves: a good signature answers `Verified OK` and
# a tampered payload answers `Verification Failure`. A verifier that only ever sees a good signature
# proves nothing (rules/testing.md).
readonly MARAN_SIGNATURE_DIGEST="sha384"

# sign_release_file: <private key> <file> <signature out>. The ONE place this repository spells the
# signing invocation, so the thing `verify_release_file` and installer/lib/50-artifacts.sh check can
# never drift from the thing that was signed.
sign_release_file() {
  openssl dgst -"$MARAN_SIGNATURE_DIGEST" -sign "$1" -out "$3" "$2"
}

# verify_release_file: <public key> <file> <signature>. Silent; the caller reports.
verify_release_file() {
  openssl dgst -"$MARAN_SIGNATURE_DIGEST" -verify "$1" -signature "$3" "$2" >/dev/null 2>&1
}
