//! Reads the answer of the home-filesystem probe.

use crate::transfer::model::source_disk::SourceDisk;

/// The room `text` describes: one line `<fragment size> <total blocks>
/// <available blocks>`, all decimal (the output of `stat -f -c '%S %b %a'`).
///
/// The figures are multiplied by the fragment size with overflow checked. A
/// line that is not exactly three decimal numbers, has a zero fragment size or
/// total, or claims more free bytes than the filesystem holds is `None`: it is
/// not a reading, and a source must not be able to feed the report an
/// impossible figure.
#[must_use]
pub fn parse_home_filesystem(text: &str) -> Option<SourceDisk> {
    let mut fields = text.split_whitespace();
    let fragment: u64 = fields.next()?.parse().ok()?;
    let blocks: u64 = fields.next()?.parse().ok()?;
    let available: u64 = fields.next()?.parse().ok()?;

    if fields.next().is_some() || fragment == 0 || blocks == 0 {
        return None;
    }

    let total_bytes = blocks.checked_mul(fragment)?;
    let free_bytes = available.checked_mul(fragment)?;

    if free_bytes > total_bytes {
        return None;
    }

    Some(SourceDisk {
        free_bytes,
        total_bytes,
    })
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_home_filesystem_tests.rs"]
mod tests;
