# Certificates and the ACME directory

How the panel obtains and renews site certificates, what an operator sets, and what the panel shows
when something goes wrong (issue #161).

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

## Wildcard certificates

A wildcard certificate covers the domain and every name directly under it (`example.com` and
`*.example.com`). It can only be proven over DNS, so the panel proves it through **the name server
this server runs** (the DNS module) and through no other provider: it never calls a third-party DNS
API.

### What has to be true

1. The account's plan includes DNS zones.
2. The name server is switched on (Administration, DNS).
3. The account owns a **zone** on this server that contains the domain (`example.com`, or a parent
   such as `example.com` for `www.example.com`).
4. The domain's name servers, at the registrar, point at this server. The panel does not check this
   itself and makes no DNS lookup of its own: the certificate authority is the judge. If it cannot
   see the validation record the order fails, and the message says what to check.
5. If the domain has a CAA record, it must allow wildcard issuance for the authority
   (`issuewild`). A CAA record that only has an `issue` tag does not.

When any of the first three is missing, the SSL tab of the site does not offer the wildcard choice and
says why in the panel's language, and a request for it is refused with the same reason before anything
is ordered.

### What the panel does

The order names two identifiers, the domain and `*.domain`. For each the panel publishes a TXT record
at `_acme-challenge.<domain>` in the account's zone (time to live 60 seconds, shown in the zone as a
panel-managed record that cannot be edited or deleted by hand), asks the authority to look, waits, and
removes the records again whether the order succeeded, failed or was cancelled. Both records exist
before the authority is asked to look. The zone is locked only while a record is written, never while
the authority validates. A record that outlives a panel crash is swept within the hour.

These challenge records do not count against the zone's record limit. At most two sit under one name.

### Renewal

Renewal repeats the way a certificate was ordered. A wildcard certificate is renewed through DNS
every time; a plain certificate through the site's document root. If the zone was deleted or the
domain's name servers were moved away, the renewal fails and shows its reason like any other renewal
failure. Check this after moving a domain's DNS.

### Rolling back

The choice is stored in a new column, `Certificates."IsWildcard"` (additive). If an update is rolled
back to a release that does not know it, that release renews a wildcard certificate as a **single-name**
certificate, and a name that relied on the wildcard loses its certificate at the next renewal. Re-issue
the wildcard after updating again.

## What is not done

- **DNS providers other than this server's own name server.** Wildcards are proven only through the
  zones this panel serves. Another provider would be a new outbound call under rules/security.md
  item 10 and needs its own decision.
