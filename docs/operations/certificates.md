# Certificates and the ACME directory

How the panel obtains and renews site certificates, what an operator sets, and what the panel shows
when something goes wrong (issue #161, partial).

## The ACME directory URL

The authority the panel talks to is set by one variable in `panel.env`, `Acme__DirectoryUrl`.

- **Form.** An absolute `https` URL with a host. `http`, user information (`user:pass@`), a fragment
  and whitespace are refused, and the panel does not start until the value is fixed. A change takes
  effect after the panel restarts.
- **Production and staging.** The installed default is the Let's Encrypt production directory,
  `https://acme-v02.api.letsencrypt.org/directory`. The staging directory,
  `https://acme-staging-v02.api.letsencrypt.org/directory`, issues certificates browsers do not trust;
  use it only to test a server's own issuance path, because it has far more generous rate limits.
  Development defaults to staging.
- **A private certificate authority.** Any ACME authority is allowed, including one on a private
  address. Its root certificate must be in the operating system's trust store, or every call to it
  fails the TLS handshake and no certificate is issued or renewed. Install the root with the
  distribution's own tool (`update-ca-certificates` on the Debian family, `update-ca-trust` on the
  RHEL family) and restart the panel.
- **One account per directory.** The panel registers an ACME account with the authority the first
  time it is needed and keeps one per directory URL. Switching the URL registers a new account with
  the new authority; certificates already issued by the old one keep working until they are renewed,
  and the renewal is then made through the account of the directory that is configured now.

### Where the panel shows it

Administrators see the configured directory, as a kind (`production`, `staging` or `custom`) and its
host, in the header of the Certificates page. The kind is decided only by an exact match with the two
Let's Encrypt URLs above; every other URL is `custom`. It is read only: the value is changed in
`panel.env`, never from the interface. Customers do not see it.

## A renewal that fails

A renewal pass runs daily. When a certificate cannot be renewed, its card on the Certificates page
shows:

- the **reason**, as one sentence in the panel's language (a failure the panel has no sentence for
  reads as a generic "renewal failed" sentence, never as raw tool output);
- the **count** of consecutive failed attempts;
- the **time of the last attempt**.

None of this is shown for a certificate that is not failing, and a successful renewal clears all
three. The card also keeps showing the expiry date and the days left.

## The expiry alert

When a certificate has 14 days or fewer left (or has already expired), the panel mails the operator
once:

- **Once per certificate until it is renewed.** The panel records that the alert was sent, so the
  daily pass does not repeat it. A successful renewal clears the record, so the next certificate's
  approach to expiry is announced again.
- **Recipient.** The operator alert address, sent through the panel's outgoing-mail settings, the
  same path the monitoring and backup alerts take (see [backup alerts](../backups/alerts.md)). The
  mail names the domain, the days left and the last failure sentence, if any.
- **No mail settings.** When no outgoing mail is configured the mail is not sent and the skip is
  written to the audit journal. The alert is still recorded as raised, so it is not repeated once
  mail is configured; the journal entry is where the operator learns it was missed.

## The panel's own certificate from the console

The administrator orders the panel's own certificate from the interface. When the interface cannot
be used, the same operation runs from the server's shell:

```
sudo maran issue-panel-certificate
```

It takes no argument: the host name is the one in `panel.env` (`Panel__HostName`), and a panel with no
usable name (empty, the installer's placeholder, `localhost`) is refused before anything is ordered.
It orders from the configured authority, installs the pair the interface is served with and records
it, so the daily sweep renews it from then on. Running it again renews the certificate now. A failure
at any step leaves the pair the panel was serving as it was, and the exit status says which kind it
was: 3 for a refusal (no host name), 69 when the agent or the authority could not be reached, 1 for a
failure. The run is written to the audit journal as `CertificateIssued`, with the operator named by
`SUDO_USER` recorded as an unverified claim.

## What is not done

- **DNS-01 and wildcard certificates.** The panel validates domains over HTTP only. Wildcards need
  DNS-01, which needs a hook into a DNS provider. No DNS module exists and the choice of provider,
  and the outbound DNS API call it implies, is an owner decision (rules/security.md item 10), so
  this is deferred and #161 stays open.
