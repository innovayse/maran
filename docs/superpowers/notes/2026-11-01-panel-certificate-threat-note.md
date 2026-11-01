# Threat note: a trusted certificate for the panel's own host name (issue #132)

`rules/security.md` requires a threat note, written first, for a change that adds agent rpcs and
writes TLS keys as root. This change lets an administrator order a certificate for the panel's own
host name over ACME HTTP-01, has the root agent place the challenge file and then the issued key
and certificate where the panel's own nginx vhost reads them, and renews it daily. It is written
before any code of the branch.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it
and signed off. The owner is told at hand-off that this surface carries an outstanding review.

## The surfaces this change adds

1. **Three new rpcs on `SslService`** (`proto/agent/v1/ssl.proto`):
   - `InstallPanelCertificate(certificate_pem, private_key_pem)` — replaces the pair the panel
     vhost names (`/etc/maran/tls/panel.crt`, `/etc/maran/tls/panel.key`), validates with
     `nginx -t`, reloads, and restores the previous pair on any refusal.
   - `WritePanelChallenge(token, key_authorization)` — writes one file, named by the token, into
     the agent-owned directory the panel's port-80 block serves
     `/.well-known/acme-challenge/` from (`/var/lib/maran-acme`).
   - `RemovePanelChallenge(token)` — removes that file.
2. **A port-80 server block for the panel's host name** in `installer/nginx/maran.conf`, answering
   only `/.well-known/acme-challenge/` from that directory and 404 for everything else.
3. **An HTTP endpoint, administrators only**: `POST /api/v1/panel-certificate` (order and install)
   and `GET /api/v1/panel-certificate` (what is recorded), plus a daily renewal sweep.

## What an attacker could do with each, and why it is safe

**Who can reach the rpcs.** Only `maran-api` (peer-credential check on the agent's socket,
unchanged). Inside the panel, only the Ssl module, which already declares the Ssl agent capability.
The endpoint is `AuthorizationPolicies.AdminOnly`: a customer cannot order, install or read it.
The host name is never taken from a request: it is the operator's own `Panel__HostName`
(`panel.env`, written by the installer), read through a new Sdk window that only the Host
implements.

**Path injection through the token.** The token arrives from a remote party (the CA) and becomes a
file name written as root. It is validated twice: the C# writer refuses anything but base64url
(existing `AcmeChallengeWriter.IsBase64Url`), and the agent constructs a validated `AcmeToken`
(`agent-core/src/validation/web/acme_token.rs`) — non-empty, at most 128 characters, base64url
only — before any path is built. A dot, a slash or a NUL cannot be in it, so the joined path cannot
leave the directory. The directory itself is root-owned `0755`, created by the installer and by no
customer-reachable path, so there is no customer-owned component to swap for a symlink; the file is
written to a temporary name created with `O_EXCL` in that directory and renamed over the target, so
a pre-existing link at the final name is replaced, never followed.

**Content injection through the key authorization.** The file's content is served to the CA as
`text/plain`. It is validated as a `AcmeKeyAuthorization`: the token, one dot, and a base64url
thumbprint, nothing else, at most 256 characters. No newline or control character can be written.

**Disk exhaustion.** The challenge write is a write on behalf of the panel: it is refused below the
free-space floor (#148) exactly as `WriteFile` refuses a customer challenge, with the same
`ERROR_CODE_BELOW_FREE_SPACE_FLOOR`, before anything is created. Nothing bypasses the floor; the
issuance then fails with the floor's own code and the panel keeps its current certificate. The
certificate install is not floor-gated, as the site install is not: it replaces two small files
through the config-write protocol, which rolls back on a write failure.

**Replacing the panel's TLS key with something else.** `InstallPanelCertificate` takes material
from `maran-api` only, checks that the key matches the certificate before anything is written
(`key_matches_certificate`, unchanged), and writes both files as ONE change of `safe_write`
(`write_config_set`), so nginx never sees a mismatched pair. A pair nginx refuses is rolled back and
the panel keeps serving the previous certificate. The key lands `0600` root, with the mode set
before the rename; the directory is re-moded `0700` root (it was `0750 root:maran`; nothing in the
API reads it — checked with `grep -rn "etc/maran/tls" backend frontend`, which matches nothing). A
compromised `maran-api` could install a certificate of its choosing for the panel's own vhost — but
a compromised `maran-api` already IS the panel, so this grants nothing it did not have.

**Fallback.** Issuance that fails at any step writes nothing to `/etc/maran/tls`: the order happens
in C# first, the material is only sent after the CA issued it. The installer's self-signed pair
stays in place, and that is the fallback the issue asks for. A renewal that fails leaves the
current (still valid) certificate and is retried on the next daily sweep.

**The port-80 block.** It is a new `server` in the panel's own vhost, not a new listening port:
nginx already listens on 80 for every site, and the firewall decides whether 80 is reachable
exactly as it does for a site's own HTTP-01. It names only the panel's host name, so it answers no
other name; #96 reserves that name from tenants, so no tenant vhost can conflict with it. It serves
only the challenge directory (`alias`, `default_type text/plain`) and returns 404 for everything
else, logging through the same `maran_no_query` format. On a host whose name is the installer's
`localhost` fallback the block answers only `Host: localhost`.

**Secrets in logs.** The issued private key travels from the ACME client to the agent client and
into the request message only, as for a site certificate. `IssuedCertificate.ToString` prints no
material; the agent's `CertificateMaterial` `Debug` prints `<redacted>`; tool output for the key is
never captured (`run_with_private_key`).

## What a reviewer must check

- `acme_token.rs` / `acme_key_authorization.rs`: that no character outside base64url (and the one
  dot) can be constructed, and that the length bounds hold.
- `write_panel_challenge.rs`: floor first, then the write; and `ProcessSslHost`'s challenge write
  uses `O_EXCL` temp + rename in the fixed directory.
- `install_panel_certificate.rs`: key match before write, one `write_config_set`, no vhost rewrite.
- `installer/nginx/maran.conf`: the port-80 block answers nothing but the challenge directory.

## What the author could not verify

- A real issuance against a public CA, and a renewal, in the polygon: the polygon has no public
  DNS name. The issue's closing criterion (issuance, renewal, fallback observed in the polygon) is
  therefore NOT met by this branch, and the pull request says "Part of #132".
- That the port-80 block parses on nginx 1.20.1 and 1.24.0 was not run here; it uses only
  directives older than both.
