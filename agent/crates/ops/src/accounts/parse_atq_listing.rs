//! Parsing the `atq` listing into the jobs an account owns.

use crate::accounts::model::at_job::AtJob;

/// Parses the standard output of `atq` (run with the C locale) into jobs.
///
/// Each line is `<id>\t<date...> <queue> <user>`; the date spans several
/// words, so the user is the LAST whitespace-separated token and the id is
/// everything before the first tab. Blank lines are skipped.
///
/// Returns `None` when any non-blank line does not have that shape (id not a
/// run of ASCII digits, no tab, fewer than three tokens after it). A listing
/// that cannot be read whole is never read in part: a skipped line could be
/// the account's own job.
pub(crate) fn parse_atq_listing(listing: &str) -> Option<Vec<AtJob>> {
    let mut jobs = Vec::new();
    for line in listing.lines().filter(|l| !l.trim().is_empty()) {
        let (id, rest) = line.split_once('\t')?;
        if id.is_empty() || !id.bytes().all(|b| b.is_ascii_digit()) {
            return None;
        }
        let tokens: Vec<&str> = rest.split_whitespace().collect();
        if tokens.len() < 3 {
            return None;
        }
        jobs.push(AtJob {
            id: id.to_owned(),
            user: (*tokens.last()?).to_owned(),
        });
    }
    Some(jobs)
}

#[cfg(test)]
#[path = "../tests/accounts/parse_atq_listing_tests.rs"]
mod tests;
