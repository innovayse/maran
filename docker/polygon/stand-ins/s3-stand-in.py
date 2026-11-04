#!/usr/bin/env python3
"""s3-stand-in.py: a minimal S3-compatible object store that verifies AWS Signature Version 4.

Used by scripts/lib/install-polygon-recovery.sh, which starts it in a container of its own so the
panel backup's upload (installer/cli/lib/panel-backup-upload.sh) has something to talk to over
HTTPS. It is a STAND-IN: it makes an upload test possible by pretending to be a storage provider,
and it is deliberately the opposite of an assertion. What it does and does not pretend:

  - It serves exactly one bucket, path-style (PUT or HEAD /<bucket>/<key>), over TLS with the
    certificate it is given. It stores a PUT body under <root>/<bucket>/<key>, written to a
    temporary name and renamed, so a HEAD never sees half an object.
  - It RECOMPUTES the request signature from the access key and secret it was started with, using
    the headers the request itself lists in SignedHeaders and the payload hash the request carries
    in x-amz-content-sha256, and answers 403 InvalidAccessKeyId or SignatureDoesNotMatch when they
    do not agree. A stand-in that accepted any Authorization header would pass an upload that sent
    the wrong credentials; this one cannot.
  - It does NOT check the clock skew of x-amz-date, the payload against its stated hash when the
    request says UNSIGNED-PAYLOAD, multipart uploads, ACLs, versioning, listing or any other
    request. GET, DELETE and POST are refused with 405: the panel backup never deletes remote
    objects, and a stand-in that quietly honoured a DELETE would hide a retention that reached it.
  - Whether a particular provider accepts the same request is not something this file can say.

Standard library only, and no syntax newer than Python 3.6, because the oldest supported system
(AlmaLinux 8) ships 3.6.

Every request is appended to --log as one line, `METHOD /path STATUS`, and nothing else: no header,
no credential, no signature. The stage reads it to count what the panel actually sent.
"""

import argparse
import hashlib
import hmac
import os
import re
import ssl
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

AUTHORIZATION_PATTERN = re.compile(
    r"^AWS4-HMAC-SHA256 Credential=(?P<key>[^/,\s]+)/(?P<date>\d{8})/(?P<region>[^/,\s]+)/"
    r"(?P<service>[^/,\s]+)/aws4_request, SignedHeaders=(?P<signed>[a-z0-9;_-]+), "
    r"Signature=(?P<signature>[0-9a-f]{64})$"
)


def sign(secret, date, region, service, string_to_sign):
    """Derives the SigV4 signing key and signs the string to sign with it."""
    key = ("AWS4" + secret).encode("utf-8")
    for part in (date, region, service, "aws4_request"):
        key = hmac.new(key, part.encode("utf-8"), hashlib.sha256).digest()
    return hmac.new(key, string_to_sign.encode("utf-8"), hashlib.sha256).hexdigest()


def uri_encode(path):
    """Percent-encodes a request path the way S3's canonical request wants it (slashes kept)."""
    safe = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~/"
    return "".join(c if c in safe else "%%%02X" % b for c in path for b in c.encode("utf-8"))


class Handler(BaseHTTPRequestHandler):
    """Handles one connection's requests. Configuration lives on the class, set by main()."""

    protocol_version = "HTTP/1.1"
    settings = None

    def log_message(self, format, *args):  # noqa: A002 - signature fixed by the base class
        """Silences the base class's access log: the stage's own --log is the only record."""

    def record(self, status):
        """Appends `METHOD /path STATUS` to the request log."""
        with open(self.settings.log, "a") as handle:
            handle.write("%s %s %d\n" % (self.command, self.path.split("?", 1)[0], status))

    def reply(self, status, code=None):
        """Sends a status line with an S3-shaped error body when `code` is given, then logs it."""
        body = b""
        if code is not None and self.command != "HEAD":
            body = ("<Error><Code>%s</Code></Error>" % code).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)
        self.record(status)

    def authenticate(self):
        """Returns None when the request's signature verifies, else the S3 error code to answer."""
        match = AUTHORIZATION_PATTERN.match(self.headers.get("Authorization", ""))
        if match is None:
            return "AccessDenied"
        if match.group("key") != self.settings.access_key_id:
            return "InvalidAccessKeyId"
        if match.group("region") != self.settings.region or match.group("service") != "s3":
            return "SignatureDoesNotMatch"
        signed = match.group("signed").split(";")
        if "host" not in signed:
            return "SignatureDoesNotMatch"
        stamp = self.headers.get("x-amz-date", "")
        if not stamp.startswith(match.group("date")):
            return "SignatureDoesNotMatch"
        path, _, query = self.path.partition("?")
        canonical_headers = ""
        for name in signed:
            value = self.headers.get(name)
            if value is None:
                return "SignatureDoesNotMatch"
            canonical_headers += "%s:%s\n" % (name, " ".join(value.split()))
        payload_hash = self.headers.get("x-amz-content-sha256", "")
        canonical_request = "\n".join([
            self.command, uri_encode(path), query, canonical_headers, match.group("signed"), payload_hash,
        ])
        scope = "%s/%s/s3/aws4_request" % (match.group("date"), match.group("region"))
        string_to_sign = "\n".join([
            "AWS4-HMAC-SHA256", stamp, scope, hashlib.sha256(canonical_request.encode("utf-8")).hexdigest(),
        ])
        expected = sign(self.settings.secret, match.group("date"), match.group("region"), "s3", string_to_sign)
        if not hmac.compare_digest(expected, match.group("signature")):
            return "SignatureDoesNotMatch"
        return None

    def locate(self):
        """Maps the request path onto a file under the bucket, or None when it is not ours."""
        path = self.path.split("?", 1)[0]
        prefix = "/" + self.settings.bucket + "/"
        if not path.startswith(prefix) or ".." in path.split("/"):
            return None
        key = path[len(prefix):]
        if not key or key.endswith("/"):
            return None
        return os.path.join(self.settings.root, self.settings.bucket, key)

    def do_PUT(self):  # noqa: N802 - name fixed by the base class
        """Stores the request body as the object, once the signature has verified."""
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        failure = self.authenticate()
        if failure is not None:
            return self.reply(403, failure)
        target = self.locate()
        if target is None:
            return self.reply(404, "NoSuchBucket")
        os.makedirs(os.path.dirname(target), exist_ok=True)
        temporary = target + ".partial"
        with open(temporary, "wb") as handle:
            handle.write(body)
        os.rename(temporary, target)
        self.send_response(200)
        self.send_header("ETag", '"%s"' % hashlib.md5(body).hexdigest())
        self.send_header("Content-Length", "0")
        self.end_headers()
        self.record(200)

    def do_HEAD(self):  # noqa: N802 - name fixed by the base class
        """Reports the stored object's size, once the signature has verified."""
        failure = self.authenticate()
        if failure is not None:
            return self.reply(403, failure)
        target = self.locate()
        if target is None or not os.path.isfile(target):
            return self.reply(404, "NoSuchKey")
        self.send_response(200)
        self.send_header("Content-Length", str(os.path.getsize(target)))
        self.end_headers()
        self.record(200)

    def do_GET(self):  # noqa: N802 - name fixed by the base class
        """Refused: the panel backup never reads, lists or downloads."""
        self.reply(405, "MethodNotAllowed")

    do_DELETE = do_GET
    do_POST = do_GET


def main():
    """Parses the arguments and serves until killed. The secret comes from a file, never argv."""
    parser = argparse.ArgumentParser(description="S3-compatible stand-in (see the module docstring).")
    parser.add_argument("--root", required=True)
    parser.add_argument("--bucket", required=True)
    parser.add_argument("--region", required=True)
    parser.add_argument("--access-key-id-file", required=True)
    parser.add_argument("--secret-file", required=True)
    parser.add_argument("--cert", required=True)
    parser.add_argument("--key", required=True)
    parser.add_argument("--port", type=int, default=443)
    parser.add_argument("--log", required=True)
    parser.add_argument("--plain", action="store_true", help="serve HTTP, for a local test of this file")
    settings = parser.parse_args()
    with open(settings.access_key_id_file) as handle:
        settings.access_key_id = handle.readline().strip()
    with open(settings.secret_file) as handle:
        settings.secret = handle.readline().strip()
    Handler.settings = settings
    server = HTTPServer(("0.0.0.0", settings.port), Handler)
    if not settings.plain:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(settings.cert, settings.key)
        server.socket = context.wrap_socket(server.socket, server_side=True)
    sys.stderr.write("s3 stand-in listening on %d\n" % settings.port)
    server.serve_forever()


if __name__ == "__main__":
    main()
