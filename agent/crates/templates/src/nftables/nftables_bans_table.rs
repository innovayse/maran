//! The table that holds runtime bans: `table inet maran_bans`.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the table brute-force bans are added to at runtime.
///
/// The text is constant — it declares two timeout sets and a chain, and every
/// banned address is added to a set later, as an element. It is a render type
/// with a template and a golden all the same, so the same review artifact
/// covers it: what the firewall is told is read in a golden diff, never
/// inferred from a string literal in Rust.
///
/// It is a SECOND table rather than a section of
/// [`NftablesRuleset`](crate::nftables::nftables_ruleset::NftablesRuleset)
/// because that one is replaced wholesale on every apply — `delete table`
/// would take every live ban with it. Keeping bans here means the rules table
/// can be re-rendered as often as the operator edits it without touching them.
///
/// The chain hooks at priority -5, so it runs BEFORE the rules chain, and it
/// therefore carries its own `iif "lo" accept` ahead of the set drops:
/// ordering between tables is decided by hook priority alone, so a banned
/// address that also matched loopback would otherwise be dropped here before
/// the rules chain's loopback exemption was ever reached — severing the
/// panel's own web-server-to-application hop.
///
/// Re-applying this file ERASES the elements in its sets, so it is applied
/// only when the table is absent; that decision belongs to `ops::firewall`,
/// not to a render type.
#[derive(Template)]
// A config file is not a document: HTML-escaping is meaningless in a grammar
// `nft` parses, and this template interpolates nothing at all.
#[template(path = "nftables/bans_table.nft.j2", escape = "none")]
pub struct NftablesBansTable {}

impl NftablesBansTable {
    /// Renders the bans table file.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template file is missing or unreadable at build
    /// time.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
