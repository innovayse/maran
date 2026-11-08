# Threat note: per-site nginx options written by the root agent (issue #151)

`rules/security.md` requires a threat note, written first, for a change whose input reaches a
configuration file the root agent writes. This change lets a site carry typed options: redirect
rules, a canonical host, HTTP basic authentication, an IP allow or deny list, an upload size
ceiling and static-asset cache headers. They are rendered into the site's nginx vhost and, for
basic authentication, into a credential file. It is written before any code of the branch.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it
and signed off; it merges to `dev` only. Recorded together with the other outstanding reviews
under #331.

## The surfaces this change adds

1. **A new message on the existing contract, no new rpc**: `SiteOptions` in
   `proto/agent/v1/sites.proto`, carried by `SiteSpec.options` (every re-rendering rpc already
   carries a `SiteSpec`) and by `CreateSiteRequest.options`. The zero value means "no options",
   so an old caller is read as a site with none.
2. **Text in the vhost**: `location` blocks with `return` / `rewrite`, `auth_basic` and
   `auth_basic_user_file`, `allow` / `deny`, `client_max_body_size`, `expires`, and for a canonical
   host extra `server` blocks.
3. **A new root-written file per site**: `/etc/maran/nginx/auth/<domain>.htpasswd`, holding
   `user:bcrypt-hash` lines, in a directory the agent creates `root:<web server group> 0750`,
   setgid, so the files inside it are readable by the web server's workers and by nobody else.
4. **A new `SiteHost` capability** (write, remove and list those files), implemented only by
   `ProcessSiteHost` and the test fakes.

## What an attacker could do, and why it is safe

**Goal: inject an nginx directive.** Every value reaches the template as a validated type from
`agent-core::validation::web` or a closed enum, and none of the types can hold `;`, `{`, `}`,
`"`, `'`, `$`, a backslash, whitespace or a control character:

- redirect path: `/` then `[A-Za-z0-9._~/-]`, no `.` or `..` segment, bounded length;
- redirect target: a site-relative path, or an absolute `http` / `https` URL whose host is a
  validated `Domain` and whose remainder uses an unreserved-plus-URL-punctuation set that excludes
  every character above; control characters are refused first and by name;
- user name: `[A-Za-z0-9._-]{1,32}` (no `:`);
- password hash: exactly the bcrypt shape (`$2a$`, `$2b$` or `$2y$`, cost 10 to 14, 53 characters
  of the bcrypt alphabet);
- networks: the existing `SourceCidr` (an `IpAddr` and a prefix, rendered back by the type);
- numbers: `ClientBodySize` 1 to 1024 MiB, `StaticCacheDays` 1 to 365;
- file extensions of the cache rule and the realm text are literals in the template.

They are validated, not escaped (rules/security.md item 4). Counts are capped (25 redirects, 10
users, 25 networks) and duplicates are refused, because a duplicate `location` is an nginx error
that would take the whole reload down.

**Goal: take certificate issuance down.** The ACME challenge location carries `auth_basic off;`
and `allow all;` whenever the site restricts access, so a site-wide password or allow-list does not
inherit into it. No redirect may name a path under `/.well-known/acme-challenge`: an exact-match
redirect would outrank that prefix location. A canonical-host redirect is a `server` block of its
own that keeps the challenge location, not a server-level `if` (which runs before location
selection and would redirect challenges). A render test builds every combination of options and
asserts the challenge location is byte-identical to the one the unrestricted site has.

**Goal: read another account's files through the options.** No option names a filesystem path the
customer chose. The credential file path is derived by the agent from the validated domain
(`AgentPaths::site_auth_file`); `root`, `alias` and `include` appear in no option. There is no
free-form snippet in this change.

**Goal: open redirect abuse.** A redirect target is only what the site owner wrote and can only
be `http`, `https` or a site-relative path; a scheme such as `javascript:` or `//host` is refused by
the target type. A prefix redirect keeps only the remainder of the requested path, built by nginx.

**Goal: steal or forge credentials.** The panel hashes (bcrypt) and sends only the hash; the agent
checks its shape and never sees a password. The hash is a `SecretString`: `Debug` and `Display`
print `«redacted»`, so a request struct in a log line cannot leak it, and the audit slot has no entry
point for it. MD5 (`$apr1$`), SHA-1 (`{SHA}`) and plain crypt are refused by the shape check
(rules/security.md item 9). The file is written through `safe_write` (temporary file at its final
mode beside the target, fsync, rename, `nginx -t` as the validator) with no reload, because nginx
re-reads the user file on every request. It is written BEFORE the vhost that names it, removed AFTER
the vhost is removed, and a start-up pass removes any file whose vhost is gone.

**Goal: lock the owner out of their own site.** A refused write leaves the previous vhost and
file. An IP list that excludes the owner is the owner's choice; the panel shows it plainly.

**Suspended sites.** The suspended stub carries no options, asserted by a test: a suspended site
serves the stub and nothing of the customer's configuration.

**Reverse-proxy and static sites.** `auth_basic` and the IP rules sit at server level before the
proxy `location`, so the upstream never sees an unauthenticated request. Options that mean nothing
for a kind are refused rather than ignored: the upload size for a static site (it accepts no body),
the cache rule for a reverse-proxied one (its responses come from the upstream).

**Redirects and access control.** `return` and `rewrite` run in nginx's rewrite phase, before the
access phase, so a redirect is answered even for a client the IP list or the password would
refuse. A redirect reveals only the target its owner wrote. Documented for customers.

## What a reviewer must check

- `agent/crates/templates/templates/nginx/site_access_rules.conf.j2`,
  `site_option_locations.conf.j2` and `redirect_site.conf.j2`, and their goldens under
  `agent/crates/templates/tests/golden/nginx/`: the text for each option group, and that a site
  with no options renders byte-identical to before this change.
- `agent/crates/agent-core/src/validation/web/` (`redirect_path`, `redirect_target`,
  `basic_auth_user_name`, `bcrypt_hash`, `client_body_size`, `static_cache_days`): that no accepted
  value contains a character listed above, and that every refusal has a test.
- `agent/crates/ops/src/sites/render_vhost.rs`: that the options reach BOTH server blocks of a site
  with a certificate, and that the certificate install and removal paths (`ops/src/ssl`) re-render
  with the options they were handed.
- `ProcessSiteHost::write_auth_file`: the directory mode and group, the file mode, that the file is
  written before the vhost in create, switch, converge and enable, and removed after it in delete.

## What the author could not verify

- That crypt(3) bcrypt (`$2y$`) is accepted by `auth_basic_user_file` in the nginx build of every
  supported family (Ubuntu 22.04 and 24.04, Debian 12 and 13, AlmaLinux and Rocky 9 and 10). Both
  families link libxcrypt, which supports bcrypt, but this must be measured on each family
  (rules/architecture.md). If a family fails, the fallback is SHA-512 crypt (`$6$`) in the shape
  check.
- That `return 301 /relative` and `rewrite ... permanent` with a relative replacement produce the
  absolute `Location` the author expects on every nginx version the families ship (read from
  nginx's header filter, not measured).
- That the setgid bit on the credential directory yields group ownership of files created inside
  it on every filesystem the host may use for `/etc` (it is a POSIX rule; not measured here).
- The agent's build and test suites were not run by the author of this note.
