# Licensing: installing a licence and what the states mean

The panel is source-available. Every module that ships with it is included in every installation, so a
panel with no licence runs all of them. A licence matters for the modules sold separately (add-ons): the
panel refuses a request for such a module unless the installed licence covers it. Nothing else is ever
refused because of a licence: sign-in, the dashboard, backups, restore, the licence screen itself and the
`maran` command line work with no licence, a refused one or a lapsed one.

## Installing a licence

As an administrator open Settings, then Licence, paste the licence file's contents and install it. The
panel checks the licence offline (a signature made by the issuer, the product, the expiry and, for a
licence bound to one server, the server) before it keeps anything; a licence that does not pass changes
nothing, and the screen says which check failed. The licence is stored as `/var/lib/maran/licence.json`,
owned by the service account and not world readable, and is carried by `maran panel-backup` and
`maran panel-restore`.

A licence is not tied to a server unless it names one. To move a bound licence to another server, use the
licence's own transfer in your account with the issuer; the panel does not do it.

## The states

| State | Meaning | What happens to paid modules |
|---|---|---|
| None installed | The ordinary first-run state | Not available; included modules unaffected |
| Not accepted | The file was edited, is for another product or server, or this build has no licence key | Not available |
| Installed | Verified and in date | Available while the lease holds (below) |
| Expired | The licence's expiry has passed | Marked lapsed and refused; their data is untouched and still reachable by backups |

### The lease

A licence that is not an offline licence is kept alive by a signed lease, valid for 14 days and renewed
daily. If a renewal does not arrive, for example because this server cannot reach the issuer, the last
lease keeps the cover for its whole life; only after it ends do the paid modules lapse. A missing lease
lapses them at once, because there is nothing to measure a grace against; the renewal below supplies the first lease
within about an hour of installing an online licence on a panel that had no licence before, and at the latest by the next daily renewal.

The screen shows the lease's standing: current, renewal overdue, expired, missing, for another licence or
server, dated in the future, or not trusted. An **offline licence** (the enterprise bundle for a server
that cannot reach the issuer) is covered by its own expiry and needs no lease.

### How the lease is renewed

The panel renews the lease itself. Once a day (about 22 to 24 hours after the last renewal, with a random offset
so many servers do not ask at the same minute) a background task asks the licence service for a fresh lease. The
first attempt is a few minutes after the panel starts, and only if the stored lease is missing or at least 20
hours old, so a restart does not cause a request.

- **Nothing is asked** for an offline licence, when no licence is installed, or when the installed one is not valid.
  A licence you install on a panel that had none is picked up within about an hour; one that replaces another, by the next
  daily renewal at the latest.
- **What is sent** is the licence id and nothing else: no panel version, no server name or identity, no account, site
  or customer data, no credential. See "What goes over the network".
- **What is accepted.** The answer is stored only if it is a lease signed by the issuer's lease key, for this
  licence (and this server, when the licence names one), issued within the last day, and ending later than the lease
  you already have. Anything else, including a forged, wrong, repeated or oversized answer, changes nothing: the old
  lease is kept and keeps covering paid modules until it ends. The new lease is written in one atomic step to
  `/var/lib/maran/lease.json` (the same ownership and mode as the licence), and takes effect at once.
- **If a renewal fails** (the service cannot be reached, it refuses, or its answer is not usable) the panel logs the kind
  of failure, writes a "Licence lease renewal failed" entry to the audit journal and tries again 6 to 7 hours later.
  After **three failures in a row** it raises one alert to the server's operator (the mail addresses, webhook and
  notification bell you have configured), and one more when a renewal succeeds again. The alert says how many attempts
  failed and why, in one closed sentence. Because a lease lives 14 days, three failures leave most of its life to fix the
  cause, usually a firewall that blocks `api-marketplace-maran.innovayse.com` on port 443.
- **The alert count is kept in memory.** After a panel restart the count starts again, so an outage that spans a
  restart alerts a little later.

The audit journal gets a "Licence lease renewed" entry (the new lease's end date) for each stored lease and a
"Licence lease renewal failed" entry (the kind of failure and how many in a row) for each failed attempt, written as
the panel with no account on the entry. They never carry the licence id, the lease, its signature or the server's
identity.

### What the audit journal records

When a paid module's cover ends, the audit journal gets a "Licensed module lapsed" entry, and when a lapsed
module is covered again, a "Licensed module covered again" one. The panel writes them itself (actor
`maran-licensing`, no account on the entry) when it notices the change, which is within about five and a half
minutes after it happened, and the time on the entry is the time it noticed. The subject names the module
and the two states (`module=mail;from=Covered;to=Lapsed`); after a restart, a module found already lapsed is
recorded once with `from=Unobserved`. The entries never carry the licence id, text, signature or the server's
identity. The refusal of the module does not wait for the entry: it applies from the moment the cover ends.

## From the command line

`maran status` prints a `Licence:` line under "Panel": the state, the tier, the expiry day and the lease's
standing. It never prints the licence text, its signature, its id or the server's identity, and it never
changes the command's exit status.

## What goes over the network

Verification is offline. The only thing this feature sends is the daily lease renewal: one HTTPS request to
`api-marketplace-maran.innovayse.com` containing the licence id, as described above and named in `rules/security.md` item 10.
The panel contacts no other host for it, follows no redirect, and reads at most 16 KiB of the answer. The connection's
source address is visible to the host, as with any HTTPS request. The request is made even when no administrator is
signed in; its call-free alternative is an **offline licence**, which needs no lease and never contacts the issuer.
The technical contract for the service is `docs/marketplace/api-v1.md`, "Lease renewal".

## Configuration

`Licensing:LicenceFilePath` and `Licensing:LeaseFilePath` (environment variables
`Licensing__LicenceFilePath` and `Licensing__LeaseFilePath`) exist so a test or a developer can point the
panel at another directory. A server never sets them. See `.env.example`.

`Licensing__LeaseServiceBaseUrl` (default `https://api-marketplace-maran.innovayse.com`) is the one host the renewal contacts: an
absolute `https` address with a host and an optional port, nothing after it. Set it only to use another licence
service that signs leases with the same lease key. A value that is not such an address does not stop the panel; the
renewal logs a warning on each pass and does nothing, so the current lease runs out. `Licensing__LeaseRequestTimeoutSeconds`
(5 to 120, default 30) is how long one request may take. Both are commented at their defaults in
`installer/panel.env.example`.
