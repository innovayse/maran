//! Reading a zone's signing mode out of `rndc showzone`.

use crate::dns::is_printable_token::is_printable_token;
use crate::dns::model::signing_mode::SigningMode;

/// The policy name `named.conf` states and the signed statement names.
const SIGNED_POLICY: &str = "maran-csk";

/// The daemon's built-in policy that keeps a zone served with its key while the
/// DS record is withdrawn.
const WITHDRAWING_POLICY: &str = "insecure";

/// The keyword that introduces a policy in a zone statement.
const POLICY_KEYWORD: &str = "dnssec-policy";

/// The signing mode the daemon's statement for a zone shows, or `None` when the
/// text is not a statement this function understands.
///
/// `output` is what `rndc showzone` printed: `zone "<name>" { type primary; file
/// "..."; ... };`. It is split into whitespace-separated tokens and every token
/// must pass [`is_printable_token`], or the whole text is not understood. The
/// text must begin with `zone` (so an error message is not read as a statement).
/// The mode is then decided by the token after `dnssec-policy`, stripped of its
/// quotes and semicolon:
///
/// - `maran-csk` is [`SigningMode::Signed`] and `insecure` is
///   [`SigningMode::Withdrawing`];
/// - no `dnssec-policy` at all is [`SigningMode::Unsigned`];
/// - any other policy name, or a keyword with nothing after it, is `None`: a
///   zone under a policy this agent did not write is not one it can describe.
///
/// Nothing in `output` is echoed or returned: only the closed set of modes
/// leaves.
#[must_use]
pub(crate) fn parse_showzone_mode(output: &str) -> Option<SigningMode> {
    let tokens: Vec<&str> = output.split_whitespace().collect();
    if tokens.first() != Some(&"zone") || !tokens.iter().all(|token| is_printable_token(token)) {
        return None;
    }

    let Some(position) = tokens.iter().position(|token| *token == POLICY_KEYWORD) else {
        return Some(SigningMode::Unsigned);
    };

    let policy = tokens
        .get(position + 1)?
        .trim_end_matches(';')
        .trim_matches('"');
    match policy {
        SIGNED_POLICY => Some(SigningMode::Signed),
        WITHDRAWING_POLICY => Some(SigningMode::Withdrawing),
        _ => None,
    }
}

#[cfg(test)]
#[path = "../tests/dns/parse_showzone_mode_tests.rs"]
mod tests;
