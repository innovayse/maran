# DNS server (authoritative)

The panel can run its own authoritative name server (BIND 9) for the zones its customers edit
(issue #178). This page covers the daemon on the host; zones, records, DNSSEC and zone transfer
arrive in later phases and are documented when they ship.

## What the installer does, and does not do

`installer/lib/89-dns.sh` installs BIND 9 and **leaves it switched off**:

- The distribution's own unit (`named.service`, and on Debian and Ubuntu the alias
  `bind9.service`) is **masked before the package is installed**. On Debian and Ubuntu the
  package's post-install script would otherwise start the daemon under the distribution's
  configuration, which recurses for the local network, for as long as the install runs.
- Maran's own unit, `maran-dns.service`, is installed disabled and stopped. It runs
  `/usr/sbin/named` in the foreground against `/etc/maran/bind/named.conf`, which the agent
  renders. The distribution's configuration is never read.
- No port is opened, in the firewall or anywhere else. No socket is bound until an administrator
  turns DNS on.

| Path | Owner and mode | Holds |
|---|---|---|
| `/etc/maran/bind/` | `root:<daemon group>` 0750 | the configuration the agent renders, the control key |
| `/etc/maran/bind/rndc.key` | `root:<daemon group>` 0640 | the control channel's shared secret, generated once with `openssl` |
| `/etc/maran/bind/dns.env` | `root:root` 0644 | the user the daemon drops to (one variable the unit expands) |
| `/var/lib/bind/maran/` (Debian family) or `/var/named/maran/` (RHEL family) | `root:<daemon group>` 0770 | the zone list the daemon keeps itself |
| `<state directory>/zones/` | `root:<daemon group>` 0750 | zone files; the agent writes them, the daemon only reads them |

The daemon's user and group are `bind` on the Debian family and `named` on the RHEL family. On the
RHEL family release 9 the installer asks for the `bind9.18` packages so that every supported host
runs BIND 9.18 or newer.

## Turning it on and off

`EnableDns` renders the configuration, has BIND's own `named-checkconf` read it at its real path,
restarts the unit, waits for the daemon to answer on its control channel and puts the unit in the
boot sequence. A configuration the checker refuses, or a daemon that never answers, restores the
previous configuration and returns an error. Repeating it on a healthy daemon changes nothing.

`DisableDns` stops the unit and removes it from the boot sequence. Zones and their files stay.

**Port 53 is not opened or closed by either, and the daemon listens on every address.** Allow TCP and
UDP 53 in the firewall as a separate step, the same way FTPS leaves its ports to the administrator (see
`docs/operations/firewall.md`), and remove the rules when you disable DNS. On a host with no firewall in
force the port is reachable the moment DNS is turned on.

The DNS server card on the nameservers page shows whether the panel firewall lets TCP and UDP 53
through. With the server on and a protocol missing it offers "Open port 53"; with the server off and a
rule still there it offers "Close port 53" (only for rules naming exactly port 53; a broader range is
narrowed on the Firewall page). The card reads the panel's own rules, so it cannot tell a host with no
firewall in force from one that blocks the port: "not open" there means no rule of the panel matches.

The status the panel shows is observed on the host: whether the unit is active, whether the daemon
answers its control channel, whether port 53 accepts a connection, and whether the live
configuration still refuses recursion and rate-limits responses. A configuration that cannot be read
is shown as not observed, never as compliant.

## Zones: made with a site, and kept in step with the database

When a site is created, its primary domain gets a zone automatically if the name server is switched on
with its settings stated, the account's plan allows zones (`MaxDnsZones`) and the account has not used
them, and the account does not already own that name or a zone above it. A site below an existing zone
(`blog.example.com` beside the account's `example.com`) is not given a zone of its own, because that
would take the subtree out of the parent zone. The zone starts empty apart from the SOA and the name
servers; a customer can also create it by hand from the DNS screen. Nothing is created for a site on a
server that does not publish DNS, and the panel records no failed attempt for it.

**Deleting a site does not delete its zone.** Mail and other records may live in it. A zone goes with its
account, or when the customer or an administrator deletes it.

The database is the truth and the zone files are derived from it. Every time the panel starts it compares
what the name server serves with the zone rows: a zone whose served serial differs from its row is
published again from the row (a served serial ahead of the row is moved past, so it still only
increases), and a served zone that no row owns is removed. It does nothing while DNS is switched off. A
pass that leaves a zone unreconciled is retried up to five times, thirty seconds apart, and then
abandoned with an error line in the panel log.

## What the configuration enforces

Authoritative only: recursion is refused, there is no cache and no forwarder, a name the server is
not authoritative for is answered REFUSED. Response rate limiting is on, answers are minimal, the
version, hostname and server id are not disclosed, and dynamic updates and zone transfers are off.
The control channel listens on `127.0.0.1` port 953 only and is authenticated by the key above.

Rate limiting and minimal answers reduce reflection amplification; they do not remove it. An
authoritative server still answers queries for the names it serves, and a large TXT or DNSKEY answer
is larger than the query that asked for it.

## Zone templates and DNSSEC (issue #452)

- **Templates** are administrator-only. A template's records may use `{apex}`, `{ipv4}`, `{ipv6}`, `{ns1}`, `{ns2}` and `{hostmaster}` and nothing else; a record whose variable is empty in the DNS settings (no IPv6 address, no second name server) is skipped when a zone is created. The default template seeds zones created with a site.
- **DNSSEC** needs a name server build that accepts `dnssec-policy`; `EnableDns` probes this with the daemon's own checker and the panel hides the feature when the answer is no. Signed zones live in `<state>/signed-zones` and keys in `<state>/keys` (both `root:<daemon group>` 0770, created by the installer). There is one key per zone and no automatic rollover.
- **Publishing:** copy the DS record shown on the zone's DNSSEC tab to the registrar.
- **Withdrawing:** remove the DS record at the registrar FIRST. The panel switches the zone to the built-in `insecure` policy, and the final step is refused for 48 hours after it began, for administrators too. Removing signing while the DS is still published makes every validating resolver refuse the domain.

Threat note: `docs/superpowers/notes/2026-11-09-dns-templates-and-dnssec-threat-note.md` (second review outstanding, #331).

## Troubleshooting

- **`EnableDns` says the checker refused the configuration.** The message carries what
  `named-checkconf` printed. The previous configuration is back in place and the daemon was not
  restarted onto the refused one.
- **The unit starts and the daemon never answers.** Read the journal for `maran-dns.service`. On
  Ubuntu look for `apparmor="DENIED"` lines naming `/usr/sbin/named`; on AlmaLinux and Rocky look for
  `avc: denied` lines (`ausearch -m avc`). The installer writes an AppArmor local override and an
  SELinux file context for the state directory, but no named has been run under either in this
  repository's tests yet: that is the most likely first-run failure.
- **A binary is reported missing at install time.** The installer requires `named`,
  `named-checkconf`, `named-checkzone` and `rndc` at fixed absolute paths and stops by name when one
  is absent. The paths were taken from the packages' documented file lists and not measured; correct
  `dns_binaries_for_family` in `installer/lib/89-dns.sh` and the matching functions in
  `agent/crates/distro/src/debian/debian_services.rs` and `rhel/rhel_services.rs` together.
- **Config changes that skip the agent.** The agent restarts the unit to apply a configuration; the
  daemon drops to an unprivileged user after it starts and cannot re-read `/etc/maran/bind` on a
  `rndc reload`, because `/etc/maran` is created `root:<service group>` 0750 by the installer's step 60, which that user is not in. Do not edit
  `named.conf` by hand: the next `EnableDns` replaces it whole.

## Removing it

The uninstaller (`installer/uninstall.sh`) stops and removes `maran-dns.service`, `/etc/maran/bind`, the AppArmor block this
installer wrote and the masks on the distribution's units. It leaves the BIND package and the state
directory with its zone files in place: those may be zones a registrar still delegates to this host.

Threat note: `docs/superpowers/notes/2026-11-07-dns-module-threat-note.md`. The second-reviewer
requirement it records is outstanding.
