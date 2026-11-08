//! Reading the serial out of `rndc zonestatus`.

/// Returns the serial the `serial:` line of an `rndc zonestatus` answer states,
/// or `None` when there is no such line or it is not a number.
///
/// Reads one line of one tool's output, so it refuses rather than guesses: a
/// missing or malformed line is `None`, never a default serial. The first
/// `serial:` line wins; the answer has exactly one.
#[must_use]
pub(crate) fn parse_zone_serial(status_output: &str) -> Option<u32> {
    status_output.lines().find_map(|line| {
        line.trim_start()
            .strip_prefix("serial:")
            .and_then(|rest| rest.trim().parse::<u32>().ok())
    })
}

#[cfg(test)]
#[path = "../tests/dns/parse_zone_serial_tests.rs"]
mod tests;
