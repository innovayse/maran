//! What a site serves and records, rendered once and used by both of its
//! server blocks.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the part of a site's configuration that says what it actually
/// serves and what it records: its logs, its `root`, its `index`, the rule for
/// following symbolic links and its `location` blocks.
///
/// A file-serving shape also writes `disable_symlinks if_not_owner;` beside
/// its `root`, at server level, so every location in the block inherits it.
/// Every account's home is readable by the web server's group and a customer
/// can make a link to any path, so without it a link in one site's root served
/// another account's files, or the host's. With it nginx opens the requested
/// path one component at a time from `/`, and a component that is a link is
/// followed only when the link and the object it resolves to have the same
/// owner, so a customer's links to their own files keep working.
///
/// It carries no `from=` on purpose. nginx opens a `from=` prefix with a plain
/// `open()` that follows links and checks nothing, and the customer owns the
/// directories above the document root (`/home/<account>` and its `sites/`).
/// With `from=$document_root` a customer could rename their root aside, put a
/// link to another account's root in its place, and have that tree served
/// with no check at all. Checking from `/` instead of from the root costs no
/// working site anything over `from=`, because every caller renders the
/// canonical root `resolve_in_home` reports, which held no link when it was
/// rendered: a link on that path was put there afterwards, and is checked like
/// any other.
///
/// The directive itself does cost something, with or without `from=`, and it
/// is not the walk. With it on, nginx answers the existence tests of
/// `try_files` and `index` by OPENING the candidate for reading as the web
/// server's user, where with it off a `stat()` was enough. So the web server
/// now needs read permission, not only search permission, on a script it
/// hands to php-fpm and on a directory it is asked for. A PHP file the web
/// server cannot read — `0600`, or `0640` in the account's own group, which
/// php-fpm running as the account executed before — now reads as missing
/// (404) to `try_files`, and as forbidden (403) when the `index` test meets it
/// on a request for a directory such as `/`; a directory it can enter but not
/// list reads as missing to the `$uri/` test. A file nginx serves itself
/// needed read permission already, so a static file is unaffected. Like the
/// answers below, this is read from how nginx opens files, not measured.
///
/// It lives here and not in the site templates so both server blocks carry it
/// from the one rendering. A proxied shape opens no file and writes no `root`,
/// so it gets neither. The ACME challenge location the PHP, static and proxied
/// site templates write sets its own `root` and carries its own copy of the
/// directive, because with a certificate the port-80 block holds that location
/// and a redirect, and none of this body. The suspended stub's challenge
/// location carries the rule too; the text earlier agents rendered for it
/// stays recognisable through
/// [`super::suspended_site::SuspendedSite::render_legacy_config`].
///
/// A render reaches a site only when the agent next writes that site's vhost.
/// A vhost an earlier agent wrote carries no such line, and nothing in this
/// crate rewrites it: it gains the rule at its next render as an enabled site
/// (creating or enabling it, a PHP-version switch, a certificate install,
/// renewal or removal), which may be long after the upgrade and is triggered
/// by something unrelated to it. Until then whether that vhost refuses a
/// cross-owner link depends on the nginx tree around it, an http-level
/// `disable_symlinks` it would inherit, and not on this file. The
/// read-permission change above arrives at the same moment as the rule does,
/// so a site that breaks on it breaks then, not at the upgrade.
///
/// What a refused link answers depends on the location that meets it:
/// `try_files` reads a refusal as a missing file, so a static site answers 404
/// and a PHP site falls through to `index.php` (and a refused `.php` link
/// stops at `try_files $uri =404` before `fastcgi_pass`); a request for a
/// directory whose index file is a refused link reaches the `index` test,
/// which answers 403; in the challenge location, which has no `try_files`, the
/// static handler answers 403. `try_files` also logs each refusal at `crit` in
/// the site's error log, which the customer can read and can fill at will.
/// Those answers follow from how nginx handles an open error in each place;
/// they are not a record of a measurement.
///
/// What the rule does not stop, so it is not read as more than it is. It
/// governs symbolic links only. A hard link is the file itself and passes, so
/// a hard link a customer makes to a world-readable host file on the same
/// filesystem as `/home` is served; what refuses making that link is the
/// kernel's `fs.protected_hardlinks`, not this directive. (Another account's
/// file cannot be hard-linked at all: the customer cannot search that
/// account's home to name it.) A link owned by another account and pointing
/// at that account's own file is followed, which is that account publishing
/// its own data. And a customer can still learn whether a path in another
/// home exists: the challenge location answers 404 for a missing target and
/// 403 for a refused one, and the error log tells the two apart in every
/// location. This too is read from source, not measured.
///
/// This exists because a site with a certificate has TWO server blocks — the
/// port-80 one and [`super::ssl_block::SslBlock`] — and both must serve the
/// same rules. Assembling them separately is how a TLS site quietly ends up
/// answering differently from its plain-HTTP twin: a rule added to one is not
/// a compile error in the other, and nothing on the way to production compares
/// them. Here there is one rendered string, and the caller puts the same bytes
/// in both places, so the two cannot disagree.
///
/// The variant is chosen by which optional field is set, not by a flag the
/// caller could contradict:
///
/// | `upstream` | `fpm_socket` | shape                    |
/// | ---------- | ------------ | ------------------------ |
/// | `Some`     | ignored      | reverse proxy            |
/// | `None`     | `Some`       | php-fpm                  |
/// | `None`     | `None`       | static files             |
///
/// Every field is a value the caller has already validated —
/// `agent-core`'s `Domain`, `Upstream` and `resolve_in_home` — because a
/// template escapes nothing (rules/security.md).
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// nginx directives corrupts them silently — an apostrophe in a comment came
// out as `&#x27;`, and a body embedded in two blocks was escaped twice. Values
// reaching a template are VALIDATED, never escaped (rules/security.md §4,
// rules/rust.md "Validation first"): `Domain`, `Upstream` and
// `resolve_in_home` are what make them safe to write, and an escaper here
// would only hide a value that had not been through them.
#[template(path = "nginx/site_body.conf.j2", escape = "none")]
pub struct SiteBody<'a> {
    /// Absolute path of the access log.
    pub access_log: &'a str,
    /// Absolute path of the error log.
    pub error_log: &'a str,
    /// Absolute document root under the account's home. Written as `root` for
    /// a file-serving site, and unused by a proxied one, which serves no file
    /// of its own from either block.
    pub document_root: &'a str,
    /// Absolute path of the php-fpm pool's unix socket, for a PHP site.
    pub fpm_socket: Option<&'a str>,
    /// The validated `host:port` a proxied site forwards to.
    pub upstream: Option<&'a str>,
}

impl SiteBody<'_> {
    /// Renders the whole body — the logs, and for a file-serving shape the
    /// `root`, the `index` and the rule for following symbolic links, then the
    /// locations — without a trailing newline.
    ///
    /// The caller embeds the result in both server blocks verbatim; it is not
    /// re-indented, so the rules carry the four-space indentation a `server`
    /// block expects.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render()
            .map(|text| text.trim_end().to_owned())
            .map_err(RenderError::Askama)
    }
}

#[cfg(test)]
#[path = "../tests/nginx/site_body_tests.rs"]
mod tests;
