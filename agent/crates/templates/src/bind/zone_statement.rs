//! The zone statement `rndc addzone` and `rndc modzone` take as their last argument.

use askama::Template;

use crate::render_error::RenderError;

/// The braces-and-all statement that tells the daemon where a zone lives and
/// whether it signs it: `{ type primary; file "..."; };`, with the signing
/// clauses of a signed or withdrawing zone.
///
/// Three shapes, each a constructor and nothing else, so the signing clauses
/// cannot be half stated (a key directory without a policy, a policy without
/// inline signing):
///
/// - [`Self::unsigned`]: `{ type primary; file "<path>"; };`, byte-identical to
///   the statement the agent built with `format!` before this type existed;
/// - [`Self::signed`]: the file path is the SIGNED zone directory's, plus
///   `key-directory`, `dnssec-policy "maran-csk"` (the policy `named.conf`
///   states) and `inline-signing yes`;
/// - [`Self::withdrawing`]: the same, with the built-in `"insecure"` policy,
///   which keeps the daemon serving the zone with the same key while the DS
///   record is removed at the registrar.
///
/// **What the caller owes.** The crate validates nothing (it depends on no
/// validating crate), so every string here is built by the caller from validated
/// parts: the file path from a directory the distro adapter named and a
/// `ZoneFileName`, the key directory from the adapter's. They are absolute paths
/// whose alphabet cannot contain a quote, a backslash, a `;`, a brace or a
/// control character (rules/security.md item 4); the template does not escape
/// them and a caller that passes anything else has broken the contract, not the
/// template.
///
/// **The trailing newline.** [`Self::render_config`] ends with a newline like
/// every render in this crate (`every_golden_ends_with_a_newline` reads the
/// goldens, which are this text). The statement a process receives as an argv
/// element must be the old bytes exactly, so [`Self::statement`] is that render
/// with its one final newline removed. The golden pins the first and a test pins
/// that the second equals the previous `format!` output.
#[derive(Template)]
// A statement is not a document: HTML-escaping a path would corrupt it silently.
// Every value reaching it is built from validated parts (see above).
#[template(path = "bind/zone_statement.conf.j2", escape = "none")]
pub struct ZoneStatement<'a> {
    /// The zone file's absolute path.
    zone_file: &'a str,
    /// The key directory; `Some` exactly when the zone is signed or withdrawing.
    key_directory: Option<&'a str>,
    /// Whether the policy is the built-in `insecure` one. Meaningful only with a
    /// key directory.
    withdrawing: bool,
}

impl<'a> ZoneStatement<'a> {
    /// A zone that is not signed.
    #[must_use]
    pub fn unsigned(zone_file: &'a str) -> Self {
        Self {
            zone_file,
            key_directory: None,
            withdrawing: false,
        }
    }

    /// A zone signed inline with the `maran-csk` policy.
    #[must_use]
    pub fn signed(zone_file: &'a str, key_directory: &'a str) -> Self {
        Self {
            zone_file,
            key_directory: Some(key_directory),
            withdrawing: false,
        }
    }

    /// A zone whose signing is being withdrawn: still signed inline, under the
    /// built-in `insecure` policy.
    #[must_use]
    pub fn withdrawing(zone_file: &'a str, key_directory: &'a str) -> Self {
        Self {
            zone_file,
            key_directory: Some(key_directory),
            withdrawing: true,
        }
    }

    /// Renders the statement with its final newline, the form the golden holds.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }

    /// The statement as the argument of `rndc addzone` or `rndc modzone`: the
    /// render without its final newline.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn statement(&self) -> Result<String, RenderError> {
        let mut rendered = self.render_config()?;

        if rendered.ends_with('\n') {
            rendered.pop();
        }

        Ok(rendered)
    }
}
