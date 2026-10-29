# Threat note: customer vhosts refuse links across owners (issue #84)

`rules/security.md` ("Sensitive change escalation") asks for a threat note and a second reviewer for
privileged surfaces. This change renders a root-delegated nginx configuration for customer sites, so
the note records the decision, the residual gap and what a reviewer must check.

**The second reviewer requirement is OUTSTANDING.** The work was done by an agent session, which
cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it.

## The problem

A customer can create a symbolic link inside their own document root that points at a file owned by
another account, or by root. nginx runs as one user and follows the link, so it serves a file the
customer was never allowed to read. `disable_symlinks if_not_owner;` makes nginx refuse a link whose
owner differs from its target's owner.

## The decision: the rule lives in the rendered vhosts only

Every vhost the agent renders that serves files from a root (static, PHP, proxy with a static
location, TLS variants, and the ACME challenge location of the suspended stub) carries
`disable_symlinks if_not_owner;`.

A host-wide `http`-level directive in `installer/nginx/maran.conf` was considered and DROPPED, and
`installer/nginx/maran.conf` is unchanged by this branch. Its blast radius was the whole nginx,
including hand-written server blocks an operator keeps under `conf.d` (a cross-owner link they rely
on would start failing), it is a duplicate-directive error where `nginx.conf` already sets it at
http level, and it needed an `off` override on the panel vhost. Its only benefit, covering vhosts
not yet re-rendered, was a transitional one.

## Residual gap (stated, not glossed)

A vhost already on disk keeps its old text until something re-renders it (a certificate install or
renewal, a PHP-version change). Until then that site is NOT protected. Upgrading the agent binary
alone does not close the issue for existing sites. The owner decides whether a one-off re-render of
all vhosts on upgrade is wanted; this branch does not add one.

Stubs of suspended sites written before the rule are still recognised as suspended through
`SuspendedSite::render_legacy_config` and `render_legacy_suspended_vhost`. The ops comparison
accepts exactly two byte-exact renders, the current stub and the stub minus the rule line, and
nothing looser, so a suspended customer is not put back on the air by the change.

## What an attacker gains or loses

- Loses, on re-rendered vhosts: reading another account's or root's files through a link placed in
  their own root. The check is per request, so it also holds for a link created later.
- Gains nothing new; the directive only removes a capability. A link whose owner equals its
  target's owner is still followed, which is intended.
- nginx's check is not atomic with the open, so a customer able to swap a link between the two could
  win a race. That is nginx's documented limit and is not fixed here.

## Statuses differ from the issue

The issue expected 403 for every blocked path. Measured, only the ACME location answers 403; the
others answer 404. The tests assert the real statuses and that the body never contains the secret.

## What a reviewer must check

- That the legacy recognition accepts only the two byte-exact renders above: a stub with an extra
  location or a different root must read as not suspended.
- Whether the residual gap for existing vhosts is acceptable or needs a re-render step.

## What the author could not verify

- Behaviour on every nginx version the installer supports; only the polygon's version is measured.
- A PHP-over-TLS witness and an unreadable `index.php` case are not yet measured; phase B adds them.
