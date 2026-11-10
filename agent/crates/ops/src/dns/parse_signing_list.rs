//! Reading which key has finished signing a zone out of `rndc signing -list`.

use crate::dns::is_printable_token::is_printable_token;
use crate::dns::model::key_publication::KeyPublication;

/// The words that open a line reporting a finished signing.
const DONE_PREFIX: &str = "Done signing with key ";

/// The words that open a line reporting a signing still running.
const IN_PROGRESS_PREFIX: &str = "Signing with key ";

/// The line the daemon prints when it holds no signing record at all.
const NONE_LINE: &str = "No signing records found";

/// What `output`, the text of `rndc signing -list <zone>`, says about the key
/// that signed the zone.
///
/// - exactly one distinct key with a `Done signing with key <tag>/<algorithm>`
///   line is [`KeyPublication::Published`];
/// - no such line, and either the daemon's `No signing records found` or a
///   signing still in progress, is [`KeyPublication::NotPublished`];
/// - two different finished keys, a tag that is not a number in range, a token
///   that is not printable, or any line this function does not recognise is
///   [`KeyPublication::NotObserved`]: a reading that cannot be trusted is not a
///   statement about the zone.
///
/// Every line is checked before the answer is given, so one line it does not
/// understand spoils the whole reading rather than being skipped. Nothing in
/// `output` is echoed or returned.
#[must_use]
pub(crate) fn parse_signing_list(output: &str) -> KeyPublication {
    let mut finished: Option<u16> = None;
    let mut in_progress = false;
    let mut none_found = false;

    for line in output
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
    {
        if line == NONE_LINE {
            none_found = true;
        } else if let Some(rest) = line.strip_prefix(DONE_PREFIX) {
            let Some(tag) = key_tag_of(rest) else {
                return KeyPublication::NotObserved;
            };
            match finished {
                Some(earlier) if earlier != tag => return KeyPublication::NotObserved,
                _ => finished = Some(tag),
            }
        } else if let Some(rest) = line.strip_prefix(IN_PROGRESS_PREFIX) {
            if key_tag_of(rest).is_none() {
                return KeyPublication::NotObserved;
            }
            in_progress = true;
        } else {
            return KeyPublication::NotObserved;
        }
    }

    match finished {
        Some(tag) => KeyPublication::Published(tag),
        None if in_progress || none_found => KeyPublication::NotPublished,
        None => KeyPublication::NotObserved,
    }
}

/// The key tag in `<tag>/<algorithm>`, or `None` when the word is not that.
///
/// Both parts must be printable tokens and the tag must be decimal digits that fit
/// a key tag.
fn key_tag_of(word: &str) -> Option<u16> {
    let (tag, algorithm) = word.trim().split_once('/')?;
    if !is_printable_token(tag) || !is_printable_token(algorithm) {
        return None;
    }
    if !tag.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }

    tag.parse().ok()
}

#[cfg(test)]
#[path = "../tests/dns/parse_signing_list_tests.rs"]
mod tests;
