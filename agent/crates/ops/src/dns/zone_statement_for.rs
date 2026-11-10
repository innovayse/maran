//! The statement the daemon is given for a zone it loads.

use std::path::Path;

use maran_templates::bind::zone_statement::ZoneStatement;

use crate::dns::dns_error::DnsError;
use crate::dns::model::signing_mode::SigningMode;

/// Builds the `{ type primary; file "..."; ... };` argument of `rndc addzone` and
/// `rndc modzone` for a zone whose file is `zone_file`, in the shape `mode` asks.
///
/// The path of the file is built by the caller from a directory the distribution
/// named and a `ZoneFileName`, and the key directory is the distribution's: no
/// caller string reaches the statement (rules/security.md item 4). For
/// [`SigningMode::Unsigned`] the result is byte-identical to the text this crate
/// built with `format!` before signing existed, which a test restates. `keys` is
/// not written into an unsigned statement.
///
/// # Errors
///
/// Returns [`DnsError::ConfigWrite`] when a path is not valid UTF-8 and
/// [`DnsError::Render`] when the template and its render type have drifted apart.
pub(crate) fn zone_statement_for(
    zone_file: &Path,
    keys: &Path,
    mode: SigningMode,
) -> Result<String, DnsError> {
    let file = text_of(zone_file)?;
    let key_directory = text_of(keys)?;

    let statement = match mode {
        SigningMode::Unsigned => ZoneStatement::unsigned(file),
        SigningMode::Signed => ZoneStatement::signed(file, key_directory),
        SigningMode::Withdrawing => ZoneStatement::withdrawing(file, key_directory),
    };

    statement.statement().map_err(|_| DnsError::Render)
}

/// A path as text, or the error that says it is not.
///
/// # Errors
///
/// Returns [`DnsError::ConfigWrite`] when the path is not valid UTF-8.
fn text_of(path: &Path) -> Result<&str, DnsError> {
    path.to_str().ok_or_else(|| DnsError::ConfigWrite {
        reason: "the zone directory is not valid UTF-8".to_owned(),
    })
}

#[cfg(test)]
#[path = "../tests/dns/zone_statement_for_tests.rs"]
mod tests;
