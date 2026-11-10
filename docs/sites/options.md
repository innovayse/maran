# Per-site options

A site can carry typed options that change how its web server answers. The panel stores them and
sends them to the agent on every render of the site's vhost, so a re-render for an unrelated reason
(a PHP version switch, a certificate installed, renewed or removed, a site resumed) keeps them. The
agent renders them into the vhost it owns and, for a password, into one credential file.

## The options

| Option | What it does | Limits |
|---|---|---|
| Canonical host | `www` or the bare name answers; the other redirects (301) to it, on both ports, keeping its certificate challenge | The canonical name must be one the site serves |
| Redirect rules | Send a path to another path or an absolute `http(s)` URL, 301 or 302 | At most 25; path `[A-Za-z0-9._~/-]`, at most 255 bytes; never under `/.well-known/acme-challenge` |
| Basic authentication | A password in front of the whole site | At most 10 users; the panel sends a bcrypt hash, the agent never sees a password |
| IP list | Allow only the listed networks, or refuse them | At most 25 networks, canonical `address/prefix` spelling |
| Upload size | `client_max_body_size` for the site | 1 to 1024 MiB; PHP and reverse-proxied sites only |
| Static cache | `expires` and `Cache-Control: public` on css, js, images and fonts | 1 to 365 days; PHP and static sites only |
| Request limit | At most N requests per second from one visitor address, plus a burst; over the limit the answer is 429 | 1 to 1000 requests per second, burst 0 to 1000; all backends |

Every field's zero value means "absent", so a site with none renders exactly the vhost it rendered
before options existed.

## Behaviours worth knowing

- **Certificate renewal always works.** The certificate-challenge location says `auth_basic off;`
  and `allow all;` whenever the site restricts access, so a password or an allow-list never reaches
  the certificate authority.
- **Redirects are answered before access checks.** nginx runs `return` and `rewrite` in its rewrite
  phase, ahead of the access phase, so a redirect is sent even to a client the IP list or the
  password would refuse. A redirect reveals only the target its owner wrote.
- **A prefix redirect keeps the rest of the path.** Both the path and the target end with `/`;
  `/blog/a?x=1` under `/blog/` → `https://new.example/` becomes `https://new.example/a?x=1`.
- **The default upload ceiling is replaced, not added to.** Without the option a PHP or proxied
  site accepts bodies up to 512 MiB (the largest value PHP's `upload_max_filesize` accepts); with it
  the ceiling is the option's value.
- **The request limit is per visitor address.** It is an nginx `limit_req` zone (`rl_<domain>`, one
  mebibyte) keyed on the connecting address, so behind a proxy or CDN every visitor shares the
  proxy's address and one budget. A rate of 0 means no limit; a burst without a rate is refused, not
  read as no limit. The zone is declared at the top of the vhost file (`limit_req_zone` is valid only
  at `http` level) and `limit_req` is written once at server level, so with a certificate it sits in
  the TLS block; the port-80 block only redirects and answers the certificate challenge, which
  therefore is never limited.
- **A suspended site serves the stub only.** The suspension page carries none of the site's options
  and no credential file is written for it; resuming the site writes both again.

## Where things are

- Contract: `proto/agent/v1/sites.proto` (`SiteOptions`, carried by `SiteSpec.options` and
  `CreateSiteRequest.options`).
- Validated types: `agent/crates/agent-core/src/validation/web/` (`redirect_path`, `redirect_target`,
  `basic_auth_user_name`, `bcrypt_hash`, `client_body_size`, `static_cache_days`, `request_rate`; networks reuse
  `source_cidr`).
- Rendering: `agent/crates/templates/templates/nginx/` (`site_access_rules`, `site_option_locations`,
  `site_request_limit`, `redirect_site`, `canonical_redirect_body`, `site_auth_file`) and the goldens beside them.
- Credential files: `/etc/maran/nginx/auth/<domain>.htpasswd`, `root:<web server group>` mode `0750`
  directory (no setgid bit: the agent's unit forbids one), `0644` files, which only the directory's group can reach. Written before the vhost that names it, removed after it, and swept at
  agent start when the vhost is gone. nginx reads the file on every request, so a change needs no
  reload.
- Threat note: `docs/superpowers/notes/2026-11-07-site-options-threat-note.md`.

## In the panel

- **Where they are stored.** In the Sites module: `sites.SiteOptions` (one row per site, keyed by the
  site) and, under it, `SiteRedirects`, `SiteBasicAuthUsers` and `SiteIpRules`. Truth lives in
  PostgreSQL; the vhost and the credential file on the host are renderings of it.
- **Where they are edited.** The site's **Options** tab, which reads and saves
  `GET` / `PUT /api/v1/sites/{id}/options`. The answer carries the bounds (how many rules, the size
  ranges, the shortest password) and every list the selects offer, so the page holds neither.
- **A save replaces the whole set.** Anything not in the request is removed, so repeating a save
  converges to the same state. The panel asks the agent to render the vhost first and stores the row
  only when the agent accepted; if the row cannot be written afterwards, the vhost is rendered again from
  the row as it was.
- **Passwords are write-only.** The panel hashes a password with bcrypt (`$2y$`, cost 12) and sends the
  agent the hash only. Responses list user names and nothing else; the audit entry names the option
  groups that changed and the user names, never a password, a hash or an address. A user already on the
  site who is sent without a password keeps the hash the panel holds. A password is 8 characters
  to 72 bytes, because bcrypt reads no more than 72.
- **A canonical host needs both names.** The site has to answer for its plain name and the `www.`
  form of it (domain or alias) before either can be chosen as the main one.
- **Every re-render carries them.** A site loads its options with itself, so enabling, resuming, switching
  PHP version and the Ssl module's certificate install and removal all hand the agent the stored options.
- **A disabled site is stored but not sent.** Its vhost is the suspension stub, which carries no options;
  they reach the host when the site is enabled again.
- **Options a backend cannot use are refused.** An upload size on a static site and cache headers on a
  reverse-proxied one are answered `SiteOptionNotForBackend` before the agent is asked.
- **A new site starts with no options**, so site creation sends none (`CreateSiteRequest.options` stays
  absent); they are set afterwards from the Options tab.
