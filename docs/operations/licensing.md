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

Installing also asks the licence service for the licence's lease straight away (see "How the lease is renewed"),
and the screen says how that went in one sentence under the confirmation: the lease was stored, the service could not
be reached, it refused, and so on. A licence that is installed stays installed whatever that sentence says; the panel
tries again later after a failure.

A licence is not tied to a server unless it names one. To move a bound licence to another server, use the
licence's own transfer in your account with the issuer; the panel does not do it.

### This server's ID

To ask the marketplace for a licence tied to this server, give it the server's ID. The Licence screen shows it in a
"This server" card, with a copy button and a link to the marketplace's claim page that already carries it, and it
shows it whether or not a licence is installed. `maran status` prints it on a `Server ID:` line under the `Licence:`
line. It is the very value a bound licence is compared with (the host's machine id, which is also in
`/etc/machine-id`), so a licence claimed for it verifies here and on no other server. The marketplace accepts 32
hexadecimal digits; an ID of another shape is shown for copying, with no link. When the panel cannot read the ID
(the agent is down or the host has none) the card and the status line say so, and a licence bound to a server is
refused until it can. The ID is shown to administrators only and is never written to a log or the audit journal.
`Licensing__ClaimPageBaseUrl` (default `https://plugins-maran.innovayse.com`) is the website the link opens; the panel
never contacts it, your browser does.

## The states

| State | Meaning | What happens to paid modules |
|---|---|---|
| None installed | The ordinary first-run state | Not available; included modules unaffected |
| Not accepted | The file was edited, is for another product or server, or this build has no licence key | Not available |
| Installed | Verified and in date | Available while the lease holds (below) |
| Expired | The licence's expiry has passed | Marked lapsed and refused; their data is untouched and still reachable by backups |

### Before a licence expires

An installed licence that is still in date but close to its expiry is warned about, so the paid modules do not
lapse by surprise. The panel decides this, never the browser: it compares the expiry with its own clock against
three thresholds, **30, 14 and 3 days** by default. A licence is inside a threshold from the moment the time left
is that many days or fewer (a licence with exactly 30 days left is inside the first; one with 30 days and a second
left is not). Once the expiry has passed there is no warning: the licence is then **Expired** and the screen says so.

While a licence is inside a threshold:

- **The Licence screen** shows an amber warning on the status card with the expiry day, the days left and the
  renewal steps, with a link to claim the licence again for this server. The same facts are in the
  licence-status answer (`expiryWarningDaysLeft`, `expiryWarning`, `expiryRenewalAdvice`; all null otherwise).
- **`maran status`** prints one more line under the `Licence:` line, indented with the others: the expiry day, the
  days left, the steps and, when the server has an ID the marketplace accepts, the claim link.
- **The dashboard** lists "licence close to its expiry" in the needs-attention notice and adds one to the
  navigation figure.
- **The operator is notified**, once for each threshold the licence crosses, through the notifications that
  already exist: a warning entry in the notification feed, the alert webhook event, and a mail to the alert
  recipients when outgoing mail is configured. The mail names the expiry day and the days left and points at the
  Licence screen; it carries no licence id, no server ID and no link, and nothing is sent anywhere new.

To renew, do either of these; both end with the warning gone as soon as the new licence is installed:

1. Claim the licence again for this server on the marketplace (`https://plugins-maran.innovayse.com/claim?server=<id>`;
   the Licence screen's "This server" card has the link with the ID filled in). A free licence claimed on the
   marketplace is valid for one year, and each new claim extends it. Paste the new licence file into the form on the
   Licence screen.
2. Install a new licence file you already have, the same way.

The panel never fetches a refreshed licence itself: it has no code path that replaces `licence.json` other than an
administrator installing a file. After the install the screen, `maran status` and the dashboard stop warning at
once (the install answer is judged against the new expiry), and no further notification is sent for the old one.

**What the notification costs, said plainly.** A background check runs every five minutes (and reads the licence through a
30-second memory), so a threshold is announced within about five and a half minutes of being crossed. What it has announced
(the licence's expiry and the threshold) is written to a small file next to `licence.json`
(`licence-expiry-announced.json`), so a panel restart, an update or a crash loop never announces the same threshold
again. A briefly unreadable licence does not reset it; only a licence that is read and is not within any threshold
(installing a licence further from its expiry) does, and a licence with a different expiry is announced afresh. A failed
delivery (for instance the list of mail addresses could not be read) is retried at the next check and, because the
addresses are read before anything is journalled or published, repeats nothing that was already sent. If the file
cannot be written the alert is still sent and the threshold is announced again after the next restart.

Set other thresholds with `Licensing__ExpiryWarningDays__0`, `__1` and so on (whole days, 1 to 365; see
"Configuration"). Setting them is the only configuration; a value outside the range stops the panel starting, so a
threshold that would never fire cannot go unnoticed.

### The lease

A licence that is not an offline licence is kept alive by a signed lease, valid for 14 days and renewed
daily. If a renewal does not arrive, for example because this server cannot reach the issuer, the last
lease keeps the cover for its whole life; only after it ends do the paid modules lapse. A missing lease
lapses them at once, because there is nothing to measure a grace against; installing an online licence therefore asks
for its lease immediately (below), and the daily renewal covers the case where that request fails.

The screen shows the lease's standing: current, renewal overdue, expired, missing, for another licence or
server, dated in the future, or not trusted. An **offline licence** (the enterprise bundle for a server
that cannot reach the issuer) is covered by its own expiry and needs no lease.

### How the lease is renewed

The panel renews the lease itself. Once a day (about 22 to 24 hours after the last renewal, with a random offset
so many servers do not ask at the same minute) a background task asks the licence service for a fresh lease. The
first attempt is a few minutes after the panel starts, and only if the stored lease is missing or at least 20
hours old, so a restart does not cause a request.

- **Nothing is asked** for an offline licence, when no licence is installed, or when the installed one is not valid.
- **Installing or replacing a licence asks at once.** It is the same renewal pass the daily schedule runs, so it is
  journalled, counted in the same run of failures and alerted like any other, and nothing is asked if the stored lease is
  already recent for that licence. The schedule then waits as that pass's outcome says (about a day after a stored
  lease, 6 to 7 hours after a failure). It is made after the install, so a failed request never fails the install.
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
standing. When the licence is close to its expiry ("Before a licence expires") the next line is the warning with the
renewal steps and the claim link, and a `Server ID:` line follows (the ID to give the marketplace when claiming a
licence). It never prints the licence text, its signature or its id, and it never changes the command's exit status.

## What goes over the network

Verification is offline. The only thing this feature sends is the lease renewal (daily, and once when an administrator installs a licence): one HTTPS request to
`api-marketplace-maran.innovayse.com` containing the licence id, as described above and named in `rules/security.md` item 10.
The panel contacts no other host for it, follows no redirect, and reads at most 16 KiB of the answer. The connection's
source address is visible to the host, as with any HTTPS request. The scheduled request is made even when no administrator is
signed in; its call-free alternative is an **offline licence**, which needs no lease and never contacts the issuer.
The technical contract for the service is `docs/marketplace/api-v1.md`, "Lease renewal".

## Configuration

`Licensing:LicenceFilePath`, `Licensing:LeaseFilePath` and `Licensing:ExpiryAnnouncementFilePath` (environment variables
`Licensing__LicenceFilePath`, `Licensing__LeaseFilePath` and `Licensing__ExpiryAnnouncementFilePath`) exist so a test or a developer can point the
panel at another directory. A server never sets them. See `.env.example`.

`Licensing__LeaseServiceBaseUrl` (default `https://api-marketplace-maran.innovayse.com`) is the one host the renewal contacts: an
absolute `https` address with a host and an optional port, nothing after it. Set it only to use another licence
service that signs leases with the same lease key. A value that is not such an address does not stop the panel; the
renewal logs a warning on each pass and does nothing, so the current lease runs out. `Licensing__LeaseRequestTimeoutSeconds`
(5 to 120, default 30) is how long one request may take. `Licensing__InstallRequestDeadlineSeconds` (5 to 30, default
12) is how long installing a licence waits for its own lease request, including any wait behind a scheduled renewal; keep
it under the reverse proxy's read timeout. When it runs out the install is still accepted and the answer says the
renewal is in progress or the service did not answer, and the schedule tries again. All three are commented at their
defaults in `installer/panel.env.example`.

`Licensing__ClaimPageBaseUrl` (default `https://plugins-maran.innovayse.com`) is the origin of the marketplace
website the Licence screen links to for claiming a licence for this server: an absolute `https` address, a host and an
optional port, nothing after it. A value that is not such an address leaves the screen without the link (the ID is
still shown) and cannot stop the panel starting.

`Licensing__ExpiryWarningDays__0`, `Licensing__ExpiryWarningDays__1`, ... (default `30`, `14`, `3`) are the numbers of
days before the expiry at which the warning begins and the notification is repeated (see "Before a licence
expires"). Each is a whole number from 1 to 365; the list is read in any order and a repeated number counts once. A
value outside the range stops the panel starting. An empty list (no threshold at all) turns the warning off: nothing
is shown, counted or sent; it is different from leaving the setting unset, which means the defaults. They are commented at their defaults in
`installer/panel.env.example`.

## Pinned keys in a release

A shipping build of the panel needs five public keys in `installer/keys/`: `licence-verification.pub`,
`lease-verification.pub`, `marketplace-metadata.pub`, `module-signing.pub` and `release-signing.pub`. They come
from the offline key ceremony and only the public halves are committed. "Shipping" is the MSBuild property
`MaranShippingBuild=true`, which only the release pipeline sets (`maran release build --publish` passes it to
`dotnet publish`). A shipping build stops with `MARAN001` naming the first absent file; a panel built without
one would refuse every licence, lease and catalogue. Every other build, including a plain Release build such as
CI's, the OpenAPI emit or `maran release deploy-local`, is not checked: it embeds a pinned key only when the file
exists (the test keys) and is otherwise unaffected.

A published release also records the five keys it pins. Its signed `manifest.json` carries a `pinned_keys`
object (file name to SHA-256), and the api archive holds the same list as `pinned-keys.txt`. `maran status`
prints a "Pinned keys" section from that file: each key's name and the first twelve hex characters of its
SHA-256, never any key content. A build that does not record the list prints "none recorded".
