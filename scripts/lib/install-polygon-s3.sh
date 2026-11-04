#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# The S3-compatible object store the panel-recovery stage uploads to. It is a STAND-IN
# (docker/polygon/stand-ins/s3-stand-in.py) running in a container of its own, so the server being
# backed up uploads across a real network hop, over TLS, to a host name, and the object it leaves
# behind survives that server being stopped.
#
# What the stand-in is and is not is written at the top of the script itself. In short: it
# recomputes AWS Signature Version 4, so wrong credentials and a wrong signature are refused, and
# it serves one bucket path-style; it is not a storage provider and proves nothing about any
# particular one.
#
# The certificate is made by this harness and installed into the SERVER'S SYSTEM TRUST STORE, never
# passed to the product: the upload code has no switch to skip verification and no CA option, so the
# only way for it to accept this certificate is the way a real one would be accepted.

REC_S3_HOST="s3.polygon.test"
REC_S3_BUCKET="maran-polygon"
REC_S3_REGION="us-east-1"
REC_S3_PREFIX="panel"
# Where the stand-in keeps its objects inside its container, and the directory a given upload lands in.
REC_S3_STORE="/var/lib/polygon-s3"
REC_S3_OBJECT_DIR="${REC_S3_STORE}/${REC_S3_BUCKET}/${REC_S3_PREFIX}"
REC_S3_REQUEST_LOG="/var/log/polygon-s3-requests.log"
# The credential files the configuration names. In /etc/maran and NOT under /root: the service runs
# with ProtectHome=yes, so a file under /root or /home is invisible to it and the job would refuse
# with "is not a regular file" (the unit's own comment says so).
REC_S3_KEY_ID_FILE="/etc/maran/polygon-s3-key-id"
REC_S3_SECRET_FILE="/etc/maran/polygon-s3-secret"
REC_S3_KEY_ID=""
REC_S3_SECRET=""
REC_S3_CONTAINER=""

# rec_curl_supports_sigv4: 0 when the curl in the current container can sign requests itself
# (--aws-sigv4, curl 7.75 and later). AlmaLinux and Rocky 8 ship 7.61, where the upload cannot run
# at all; the stage then says so instead of scoring a feature the system cannot have.
rec_curl_supports_sigv4() {
  local version major rest minor
  version="$(docker exec "$container" curl --version 2>/dev/null | head -1 | awk '{print $2}' | tr -d '\r')"
  major="${version%%.*}"
  rest="${version#*.}"
  minor="${rest%%.*}"
  case "$major$minor" in ''|*[!0-9]*) return 1 ;; esac
  [ "$major" -gt 7 ] || { [ "$major" -eq 7 ] && [ "$minor" -ge 75 ]; }
}

# rec_s3_start: brings up the stand-in in its own container from the polygon image, and gives the
# server under test the name it will be told to upload to. Returns 1 with a note when it cannot, so
# the stage can say "UNOBSERVED HERE" for the upload rather than fail on a missing stand-in.
#
# The container runs `sleep` and not the image's systemd: it needs python and a network, not an
# init, and a booted systemd would be a second thing that could fail before the stand-in did.
# python3 is installed into it at run time because the polygon image deliberately has no python
# (docker/README.md); that is the stand-in's own dependency, installed into the stand-in's own
# container, never into a server under test.
rec_s3_start() {
  local name="$1" ip pkg_command
  REC_S3_CONTAINER="$name"
  REC_S3_KEY_ID="POLY$(openssl rand -hex 8 | tr 'a-f' 'A-F')"
  REC_S3_SECRET="$(openssl rand -hex 20)"

  docker rm -f "$name" >/dev/null 2>&1 || true
  docker run -d --name "$name" --platform "$platform" --entrypoint sleep "$image" infinity >/dev/null \
    || { note "panel-recovery: the S3 stand-in container did not start"; return 1; }
  extra_containers+=("$name")

  case "$family" in
    debian) pkg_command='export DEBIAN_FRONTEND=noninteractive; apt-get update -qq && apt-get install -y -qq --no-install-recommends python3' ;;
    *) pkg_command='dnf install -y -q python3' ;;
  esac
  docker exec "$name" bash -c "$pkg_command" >"${REC_WORK}/s3-install.log" 2>&1 \
    || { note "panel-recovery: python3 could not be installed into the S3 stand-in container (see ${REC_WORK}/s3-install.log)"; return 1; }

  openssl req -x509 -newkey rsa:2048 -nodes -days 2 -keyout "${REC_WORK}/s3.key" -out "${REC_WORK}/s3.crt" \
    -subj "/CN=${REC_S3_HOST}" -addext "subjectAltName=DNS:${REC_S3_HOST}" >/dev/null 2>&1 \
    || { note "panel-recovery: could not make the stand-in's certificate"; return 1; }
  docker exec "$name" mkdir -p /opt/polygon-s3 "$REC_S3_STORE"
  # Copies into a container this function just made; a failure of any is a harness fault, not a finding.
  docker cp "${root}/docker/polygon/stand-ins/s3-stand-in.py" "${name}:/opt/polygon-s3/s3-stand-in.py" >/dev/null
  docker cp "${REC_WORK}/s3.crt" "${name}:/opt/polygon-s3/s3.crt" >/dev/null
  docker cp "${REC_WORK}/s3.key" "${name}:/opt/polygon-s3/s3.key" >/dev/null
  printf '%s\n' "$REC_S3_KEY_ID" | rec_put_file "$name" 0600 /opt/polygon-s3/key-id
  printf '%s\n' "$REC_S3_SECRET" | rec_put_file "$name" 0600 /opt/polygon-s3/secret
  docker exec -d "$name" python3 /opt/polygon-s3/s3-stand-in.py \
    --root "$REC_S3_STORE" --bucket "$REC_S3_BUCKET" --region "$REC_S3_REGION" \
    --access-key-id-file /opt/polygon-s3/key-id --secret-file /opt/polygon-s3/secret \
    --cert /opt/polygon-s3/s3.crt --key /opt/polygon-s3/s3.key --port 443 --log "$REC_S3_REQUEST_LOG"

  ip="$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$name" | tr -d '\r')"
  [ -n "$ip" ] || { note "panel-recovery: the S3 stand-in container has no address"; return 1; }
  # The name the server is told to upload to. /etc/hosts is a bind mount in a container and cannot
  # be replaced, only appended to, which is what is wanted.
  docker exec "$container" bash -c 'printf "%s %s\n" "$1" "$2" >> /etc/hosts' _ "$ip" "$REC_S3_HOST" \
    || { note "panel-recovery: could not add ${REC_S3_HOST} to the server's /etc/hosts"; return 1; }
  return 0
}

# rec_s3_trust: puts the stand-in's certificate into the current container's system trust store, the
# way an administrator installs a private CA, so that the product's unmodified curl accepts it.
rec_s3_trust() {
  case "$family" in
    debian)
      docker cp "${REC_WORK}/s3.crt" "${container}:/usr/local/share/ca-certificates/polygon-s3.crt" >/dev/null
      docker exec "$container" update-ca-certificates >/dev/null 2>&1 || true
      ;;
    *)
      docker cp "${REC_WORK}/s3.crt" "${container}:/etc/pki/ca-trust/source/anchors/polygon-s3.crt" >/dev/null
      docker exec "$container" update-ca-trust extract >/dev/null 2>&1 || true
      ;;
  esac
}

# rec_s3_reachable: 0 once the current container can complete a TLS handshake with the stand-in and
# get an HTTP answer (any: a plain GET is refused with 405). Waits up to ~20 seconds for the
# interpreter to start. Run after rec_s3_trust, this is also the positive control for the trust
# store: without the certificate installed the same request fails at the handshake with 000.
rec_s3_reachable() {
  local code
  for _ in $(seq 1 20); do
    code="$(docker exec "$container" curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
      "https://${REC_S3_HOST}/" 2>/dev/null | tr -d '\r' || true)"
    case "$code" in ''|000) sleep 1 ;; *) return 0 ;; esac
  done
  return 1
}

# rec_s3_objects: the names of the objects the stand-in holds under the panel's prefix, sorted.
rec_s3_objects() {
  { docker exec "$REC_S3_CONTAINER" find "$REC_S3_OBJECT_DIR" -maxdepth 1 -type f -name '*.tar.gpg' \
      -printf '%f\n' 2>/dev/null | tr -d '\r' || true; } | sort
}

# rec_s3_requests: how many lines of the stand-in's request log match the extended regular
# expression $1 (for example `^PUT .* 403$`).
rec_s3_requests() {
  { docker exec "$REC_S3_CONTAINER" cat "$REC_S3_REQUEST_LOG" 2>/dev/null | tr -d '\r' \
      | grep -cE "$1" || true; }
}

# rec_s3_write_credentials: writes the two root-only credential files the panel's configuration
# names, in the current container. Passing other values than the stand-in holds is how the stage
# makes it refuse an upload.
#   $1 = access key id, $2 = secret key.
rec_s3_write_credentials() {
  printf '%s\n' "$1" | rec_put_file "$container" 0600 "$REC_S3_KEY_ID_FILE"
  printf '%s\n' "$2" | rec_put_file "$container" 0600 "$REC_S3_SECRET_FILE"
}
