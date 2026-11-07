# Threat note: the panel's public URL is chosen by the installer and validated at start-up

`rules/security.md` requires a threat note, written first, for a change to the installer's privileged
steps and for a change to what goes into an authentication link. This change touches both: the
installer now decides which address ends up as the base of every invitation and password-reset link
(`Panel__PublicUrl` in `/etc/maran/panel.env`), and it adds that address to the panel's self-signed
certificate.

**The second reviewer requirement is OUTSTANDING.** The work was done by an agent session, which
cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it.

## The problem being solved

Invitation links mailed to an account owner or a reseller, the link an administrator is shown, and
the password-reset links all start with the panel's configured address. The installer filled that
address with the machine's own host name (`https://<hostname>:8443`). On most servers that name is
an internal one (`vps-12345`, `ip-10-0-3-7.ec2.internal`) which resolves nowhere outside the
machine, so every link a customer received was unreachable.

## The decision

- One setting, `Panel__PublicUrl` (section `Panel`, key `PublicUrl`), replaces
  `PasswordReset__PanelUrl`, whose name described one of its two readers.
- The installer fills it from the address the operator actually reaches the panel at, chosen in this
  order: an explicit `MARAN_PANEL_PUBLIC_URL` from the operator (an invalid one fails the install, it
  is never replaced by a guess); this host's fully qualified name, when it is not a local-only suffix
  and the local resolver answers it with a publicly routable address; the server-side address of the
  SSH session the installer runs in; the source address of the default route. The machine's bare
  internal name is never used. The same value is the one `90-finish.sh` prints.
- A re-run (an update included) KEEPS a valid recorded `Panel__PublicUrl`; only an explicit
  `MARAN_PANEL_PUBLIC_URL` replaces it, and an absent or invalid recorded value is detected afresh.
  Re-detecting on every run would silently re-point every credential link of a running panel. The old
  `PasswordReset__PanelUrl` is not carried over (it was derived from the machine's own name, the
  defect being fixed) and is dropped when the installer rewrites the file.
- The panel validates the value strictly at start-up and refuses to start on a malformed one.
- One function (`PanelLink.Compose`) joins the base and the relative path; the two link builders and
  the invitation mail composer all call it, so no handler concatenates a link itself.
- With no value, the panel behaves as before (a root-relative link, or a mail carrying the bare
  token) and logs a warning at start-up saying why links are not clickable.

## What an attacker could do with this surface

- **Aim a credential link at their own server.** The classic route is the `Host` header: a caller
  asks for a reset of somebody else's account with `Host: evil.example` and the panel mails a live
  token under that name. This change never reads the request. The base comes from `panel.env`,
  written by root at install time, and nothing in the request pipeline can write it. Asserted: an
  integration test sends a hostile `Host` header and `X-Forwarded-Host` with an invitation request
  and checks the link still carries the configured base.
- **Inject a second setting through the value.** The value is written into a line-oriented env file
  (rules/security.md item 4). The installer writes it only after it passes a shell check that
  refuses anything but `https://`, letters, digits, dots, hyphens, a colon, square brackets (an IPv6
  literal) and a port; a newline, space, quote, `=`, `;` or `$` ends in "nothing is written", never
  in a second line. The same rule is then applied again by the panel at start-up
  (`PublicUrlRule`): https only, no user information, no path, query or fragment, a DNS name or an IP
  address, port 1-65535, and no control character anywhere (checked on the raw text before it is
  parsed, because `Uri` silently drops some of them). Polygon assertion: planted hostile values must
  record nothing.
- **Make the operator-supplied override a way in.** `MARAN_PANEL_PUBLIC_URL` is read from the
  environment of a process that is already root and already writes `panel.env`. Whoever can set it
  can already edit the file. It is validated like every other candidate.
- **Let the detected address be attacker-chosen.** The SSH session address is the local end of a
  connection the operator made, so a remote party cannot pick it; it is read from the installer's own
  `SSH_CONNECTION` and validated as an address before use (the `/proc` ancestor walk that exists for the
  client address is deliberately NOT extended to it). The name candidate is this machine's own name
  and counts only when the resolver answers it with a publicly routable address, so a name that
  resolves to loopback or a private range is skipped. Whoever controls that DNS record is the
  operator's own DNS, which already decides where the server's name points. A wrong guess costs an
  unreachable link (the old failure), never a credential disclosure to a third party, because every
  candidate is this server's own name or address as the server itself sees it.
- **Use the certificate change.** The self-signed certificate gains the chosen address as a subject
  alternative name (an IP or a DNS entry) next to the host name it had. It is still self-signed,
  still ECDSA P-256, still written only when absent, with the same modes. No private key is created
  anywhere new and nothing is trusted by this change. The value is passed to `openssl` as an argv
  element after the same character check; no shell string is built from it.
- **Phone home.** No. Detection reads local files, `ip route get` (a routing-table lookup that sends
  no packet) and the local resolver for this host's own name only. No outbound connection is
  introduced (rules/security.md item 10), which is also why the installer cannot discover a public
  address behind NAT: it says so and offers the override.
- **A private address is written when nothing better exists.** A server behind NAT, installed from
  the console, ends with a private address in its links. The installer prints a warning naming the
  override. Those links are unreachable from outside, as before, and disclose a private address that
  the operator's own SSH session already knows.

## Why it is safe now

The trust boundary is unchanged: the base of a credential link is operator configuration, root-written
and validated twice, never request data. The new code can fail only toward "link not clickable"
(empty value) or "panel refuses to start" (malformed value); neither discloses a token.

## What a reviewer must check

1. `installer/lib/60-config.sh` `detect_panel_public_url` and its character gate: is there any path
   by which a byte outside the alphabet reaches `panel.env` or `openssl`?
2. `installer/lib/80-nginx.sh` certificate generation: the SAN string is built from a gated value
   only; an existing certificate is still left untouched.
3. That `PublicUrlRule` and the shell gate agree (a value one accepts and the other refuses stops the
   boot, which is the safe direction but a surprising one). The shell gate is deliberately the
   narrower: it also refuses a trailing slash, `localhost`, and an all-numeric host that is not a strict
   dotted quad, which the C# rule accepts; the C# rule must never be narrower than the shell gate.
   Both refuse a colon with no port and any non-ASCII character, and both accept any scheme case.
4. That a re-run keeps an operator's value (`step_config`) and that only a valid one is kept.
5. That no handler composes a link without `PanelLink`.

## What the author could not verify

Nothing was built or run in this pass (code-first, one gate later). Specifically unverified: the
installer functions on a real host and on both distribution families (`ip` is in `iproute2`/`iproute`
on both, but its output format was not exercised here); behaviour under `sudo` where `SSH_CONNECTION`
is dropped by `env_reset` (the detection then falls through to the next candidate, which is the
design, but was not run); the polygon assertion; the unit and integration tests; the interaction with
the Ssl panel-certificate work that reads `Panel__HostName` (unchanged by this change); that
`Uri` accepts `https://host:` and an internationalised host exactly as the C# rule's comments assume
(read from documentation, not executed); that openssl accepts `IP:` entries holding an IPv4-mapped
IPv6 literal on every supported distribution's openssl; `shellcheck` was not installed here, so only
`bash -n` and a direct run of the pure validator functions were done.
