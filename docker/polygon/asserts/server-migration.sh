#!/usr/bin/env bash
# The closing check of whole-server migration (issue #197, phase 2), run by the lead on docker: ten accounts on a
# generic Linux source are discovered, mapped, dry-run, transferred three at a time, cut over and verified, and the
# inverse controls the issue asks for are probed. It prints a verdict line and exits 0 only for SERVER-MIGRATION-OK.
#
# What it observes, and what it cannot (rules/testing.md): it drives the panel's HTTP API and reads the target host's
# files and database, so it sees the artefacts the system uses. It cannot see the source's own logs, so "nothing was
# written to the source" is checked by comparing a digest of the source's tree before and after, not by trusting the
# agent. Without docker, a built panel and a built agent it prints SERVER-MIGRATION-UNOBSERVED and exits 2: that is
# not a pass.
#
# Environment (all required): MARAN_API (base URL of the target panel), MARAN_TOKEN (an administrator's bearer
# token), SOURCE_CONTAINER (the running source-linux container), TARGET_PLAN (a plan id on the target),
# SOURCE_ADDRESS (the container's address the target reaches).
set -Eeuo pipefail

for variable in MARAN_API MARAN_TOKEN SOURCE_CONTAINER TARGET_PLAN SOURCE_ADDRESS; do
  if [ -z "${!variable:-}" ]; then
    echo "SERVER-MIGRATION-UNOBSERVED: ${variable} is not set, so nothing was run."
    exit 2
  fi
done
command -v docker >/dev/null 2>&1 || { echo "SERVER-MIGRATION-UNOBSERVED: docker is not installed."; exit 2; }

fail() {
  echo "SERVER-MIGRATION-FAILED: $*" >&2
  exit 1
}

api() {
  local method="$1" path="$2" body="${3:-}"
  if [ -n "${body}" ]; then
    curl -fsS -X "${method}" "${MARAN_API}${path}" -H "Authorization: Bearer ${MARAN_TOKEN}" \
      -H 'Content-Type: application/json' -d "${body}"
  else
    curl -fsS -X "${method}" "${MARAN_API}${path}" -H "Authorization: Bearer ${MARAN_TOKEN}"
  fi
}

source_digest() {
  docker exec "${SOURCE_CONTAINER}" sh -c "find /home /etc/nginx/conf.d -type f -exec sha256sum {} + | sort | sha256sum"
}

before="$(source_digest)"

# 1. Register the source, pin its host key and discover its accounts. The key line and the fingerprint are what
#    the screens show; the harness compares the fingerprint it reads from the container with the one the panel
#    offers, exactly as an administrator would.
server="$(api POST /api/v1/remote-servers "{\"name\":\"polygon-source\",\"host\":\"${SOURCE_ADDRESS}\",\"port\":22,\"sshUser\":\"root\"}")"
server_id="$(jq -r .id <<<"${server}")"
public_key="$(jq -r .publicKey <<<"${server}")"
docker exec "${SOURCE_CONTAINER}" sh -c "mkdir -p /root/.ssh && printf '%s\n' '${public_key}' >> /root/.ssh/authorized_keys && chmod 600 /root/.ssh/authorized_keys"

# Inverse control 1: a wrong fingerprint is refused, and the right one is accepted.
wrong='SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
if api POST "/api/v1/remote-servers/${server_id}/host-key" "{\"fingerprint\":\"${wrong}\"}" >/dev/null 2>&1; then
  fail "a host key was pinned from a fingerprint nobody compared"
fi
real="$(jq -r '.hostKeyCandidates[0].fingerprint' <<<"${server}")"
api POST "/api/v1/remote-servers/${server_id}/host-key" "{\"fingerprint\":\"${real}\"}" >/dev/null
api POST "/api/v1/remote-servers/${server_id}/discover" >/dev/null

# 2. A job of every discovered account, three at a time, mapped to the target plan.
ids="$(for page in 1 2; do api GET "/api/v1/remote-servers/${server_id}/accounts?page=${page}&pageSize=100" | jq -r '.items[].id'; done | sort -u | jq -R . | jq -s .)"
job="$(api POST /api/v1/migration-jobs "{\"serverId\":\"${server_id}\",\"name\":\"polygon\",\"discoveredAccountIds\":${ids},\"concurrency\":3}")"
job_id="$(jq -r .id <<<"${job}")"
[ "$(jq -r .accountCount <<<"${job}")" = "10" ] || fail "the job does not hold the ten accounts"

api PUT "/api/v1/migration-jobs/${job_id}/mapping" "{\"accountIds\":[],\"planId\":\"${TARGET_PLAN}\",\"ownerLanguage\":\"en\"}" >/dev/null
for row in $(api GET "/api/v1/migration-jobs/${job_id}/accounts?pageSize=100" | jq -r '.items[] | "\(.id):\(.sourceAccountName)"'); do
  api PUT "/api/v1/migration-jobs/${job_id}/mapping" \
    "{\"accountIds\":[\"${row%%:*}\"],\"ownerEmail\":\"${row##*:}@polygon.test\",\"primaryDomain\":\"${row##*:}.source.test\"}" >/dev/null
done

# 3. The dry run is read-only: no account of the source's names exists on the target afterwards.
report="$(api POST "/api/v1/migration-jobs/${job_id}/dry-run")"
[ "$(jq -r .canStart <<<"${report}")" = "true" ] || fail "the dry run found conflicts on a clean target: $(jq -c '.findings' <<<"${report}")"
[ "$(api GET '/api/v1/accounts?search=site&pageSize=100' | jq -r .total)" = "0" ] || fail "the dry run created an account"

# 4. Start, wait for every account to be synced, and watch the concurrency: never more than three at once.
api POST "/api/v1/migration-jobs/${job_id}/control" '{"action":"Start"}' >/dev/null
peak=0
for _ in $(seq 1 360); do
  states="$(api GET "/api/v1/migration-jobs/${job_id}" | jq -c .accountStates)"
  running="$(jq -r '(.Running // 0)' <<<"${states}")"
  [ "${running}" -gt "${peak}" ] && peak="${running}"
  [ "$(jq -r '(.Synced // 0)' <<<"${states}")" = "10" ] && break
  sleep 5
done
[ "${peak}" -le 3 ] || fail "the job ran ${peak} accounts at once with a limit of 3"
[ "$(api GET "/api/v1/migration-jobs/${job_id}" | jq -r '.accountStates.Synced // 0')" = "10" ] || fail "not every account reached Synced"

# 5. The delta: delete a file on the source, cut every account over, and see it gone on the target.
docker exec "${SOURCE_CONTAINER}" rm -f /home/site01/public_html/stale.txt
for id in $(api GET "/api/v1/migration-jobs/${job_id}/accounts?pageSize=100" | jq -r '.items[].id'); do
  api POST "/api/v1/migration-jobs/${job_id}/accounts/${id}/control" '{"action":"CutOver"}' >/dev/null
done
for _ in $(seq 1 120); do
  [ "$(api GET "/api/v1/migration-jobs/${job_id}" | jq -r '.accountStates.Completed // 0')" = "10" ] && break
  sleep 5
done
[ "$(api GET "/api/v1/migration-jobs/${job_id}" | jq -r '.accountStates.Completed // 0')" = "10" ] || fail "not every account was cut over"
[ ! -e /home/site01/public_html/stale.txt ] || fail "the final delta did not remove a file the source deleted"
[ -f /home/site01/public_html/index.html ] || fail "the page was not copied"

# 6. Verification report per account, and the data present in the target's database.
first="$(api GET "/api/v1/migration-jobs/${job_id}/accounts?pageSize=1" | jq -r '.items[0].id')"
api GET "/api/v1/migration-jobs/${job_id}/accounts/${first}/report" | jq -e '.filesConverged == true' >/dev/null || fail "the report does not say the files converged"
mysql -N -e "SELECT label FROM site01_shop.items WHERE id = 1" | grep -qx site01 || fail "the database content did not arrive"

# 7. Nothing was written to the source.
# The harness deleted ONE file on purpose (step 5); restore it, and the tree must equal what it was before.
docker exec "${SOURCE_CONTAINER}" sh -c "printf 'removed-before-cut-over\n' > /home/site01/public_html/stale.txt && chown site01:site01 /home/site01/public_html/stale.txt"
[ "${before}" = "$(source_digest)" ] || fail "the source's tree differs from before the migration by more than the one file the harness deleted"

# 8. No source password and no private key is stored anywhere readable: the database holds the key only encrypted,
#    and the planted sentinel check of the unit tests has a positive control there; here the key is searched for
#    in the target's logs and scratch.
if grep -rIl 'BEGIN OPENSSH PRIVATE KEY' /var/log /run/maran /run/maran-host 2>/dev/null | grep -q .; then
  fail "a private key is readable on the target"
fi

echo "SERVER-MIGRATION-OK (10 accounts, peak ${peak} at once, delta applied, source untouched)"
