#!/usr/bin/env bash
# demo-selftest.sh: `maran demo selftest` - the product's installer refuses a demo bundle.
#
# Sourced by demo-bundle.sh, which supplies demo_ok, demo_failed, require_tool, write_demo_manifest,
# demo_verify_bundle and the constant MARAN_DEMO_SIGNATURE_DIGEST; never executed on its own and it sets
# no shell option.
#
# What is proved, each half with its inverse control (rules/testing.md: a refusing gate needs one):
#
#   1. A demo bundle signed with a throwaway key is REFUSED by the product's verification primitive
#      against installer/keys/release-signing.pub - the key the installer and `maran update` trust.
#   2. The SAME bundle is ACCEPTED against the throwaway key's public half (the control: the bundle
#      is well formed, so the refusal in 1 is about the key and not about a malformed fixture).
#   3. A manifest tampered after signing is REFUSED even against the right key (the control that the
#      acceptance in 2 is not a verifier that accepts everything).
#   4. The committed demo public key is not the product's key (cmp), so the two can never be confused.
#
# UNOBSERVED HERE: the product's own verifier (cli_release_verify_manifest) demands a root-owned 0700 work
# directory and cannot run as the developer. What this runs is the primitive it calls, with the same
# digest and the same key file, which is the only thing the refusal depends on. It also cannot see the
# release workflow, so it does not prove a published product release never contains the demo assembly.
cmd_selftest() {
  require_tool openssl "creates a throwaway key and verifies signatures"
  local work product_key demo_key
  work="$(mktemp -d)"
  product_key="$root/installer/keys/release-signing.pub"
  demo_key="$root/demo/keys/demo-release-signing.pub"
  [ -f "$product_key" ] || demo_failed "the product public key is missing at ${product_key}"

  echo "-- fixture: a demo bundle signed with a throwaway key --"
  mkdir -p "${work}/bundle/api" "${work}/bundle/frontend"
  echo "stand-in api" > "${work}/bundle/api/Maran.Host.Demo"
  echo "stand-in spa" > "${work}/bundle/frontend/index.html"
  ( cd "${work}/bundle/api" && tar -czf ../api.tar.gz . )
  ( cd "${work}/bundle/frontend" && tar -czf ../frontend.tar.gz . )
  write_demo_manifest "${work}/bundle" "0.0.0-demo.selftest"
  openssl ecparam -name secp384r1 -genkey -noout -out "${work}/throwaway.key" 2>/dev/null
  openssl ec -in "${work}/throwaway.key" -pubout -out "${work}/throwaway.pub" 2>/dev/null
  openssl dgst -"$MARAN_DEMO_SIGNATURE_DIGEST" -sign "${work}/throwaway.key" \
    -out "${work}/bundle/manifest.json.sig" "${work}/bundle/manifest.json"

  echo "-- case 1: the product key refuses the demo bundle --"
  if demo_verify_bundle "${work}/bundle" "$product_key" 2>/dev/null; then
    rm -rf -- "$work"
    demo_failed "the PRODUCT key accepted a demo bundle: an installer would install the demonstration"
  fi
  echo "refused, as required"

  echo "-- case 2 (control): the same bundle verifies against the key that signed it --"
  demo_verify_bundle "${work}/bundle" "${work}/throwaway.pub" \
    || { rm -rf -- "$work"; demo_failed "the control bundle did not verify against its own key: the fixture is broken, case 1 proved nothing"; }
  echo "accepted, as required"

  echo "-- case 3 (control): a manifest edited after signing is refused even against the right key --"
  cp -r "${work}/bundle" "${work}/tampered"
  sed -i 's/"flavour": "demo"/"flavour": "stable"/' "${work}/tampered/manifest.json"
  if demo_verify_bundle "${work}/tampered" "${work}/throwaway.pub" 2>/dev/null; then
    rm -rf -- "$work"
    demo_failed "a tampered manifest verified: the verification accepts everything"
  fi
  echo "refused, as required"

  echo "-- case 4: the committed demo key is not the product key --"
  if cmp -s "$product_key" "$demo_key"; then
    rm -rf -- "$work"
    demo_failed "demo/keys/demo-release-signing.pub equals the product key"
  fi
  echo "different, as required"

  rm -rf -- "$work"
  demo_ok "the product key refuses a demo bundle; controls accepted and refused as required"
  echo "UNOBSERVED HERE: the product verifier's own entry point (root-only) and the release workflow's contents."
}
