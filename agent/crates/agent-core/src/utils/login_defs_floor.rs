//! Reads one id floor (`UID_MIN`, `GID_MIN`) out of `login.defs`, for every place that needs it.

use crate::utils::login_defs_floor_reading::LoginDefsFloorReading;

/// The files that may define a floor, most authoritative first: `/etc` overrides the vendor copy under
/// `/usr/etc` that a distribution with a usr-merged layout ships its defaults in. A file a family does
/// not ship is simply absent and skipped.
pub const LOGIN_DEFS_PATHS: [&str; 2] = ["/etc/login.defs", "/usr/etc/login.defs"];

/// The floor to assume when no file sets one, which is the value `login.defs(5)` documents and every
/// distribution in the support matrix ships. Wrong only in the visible direction: a host whose real floor
/// is lower would see some accounts refused, never a system account accepted.
pub const DEFAULT_ID_FLOOR: u32 = 1000;

/// Reads the floor named `key` from the text of one `login.defs`.
///
/// Comment lines, other keys and a `SYS_`-prefixed relative are skipped: the key must be the FIRST field of
/// its line. When the key appears more than once the LAST line decides, which is how `useradd` reads the
/// file (a later definition replaces an earlier one), so every caller agrees with the tool that allocates
/// the ids. A value that is not a positive number is [`LoginDefsFloorReading::Unusable`], not skipped: a
/// zero or a typo in the deciding line must not be answered with an older line the host no longer means.
///
/// This is the one reader of the format; the firewall's output chain and the account module's id checks
/// both build on it and differ only in what they do with [`LoginDefsFloorReading::Unset`] and
/// [`LoginDefsFloorReading::Unusable`].
#[must_use]
pub fn login_defs_floor(text: &str, key: &str) -> LoginDefsFloorReading {
    let mut reading = LoginDefsFloorReading::Unset;

    for line in text.lines() {
        // `split_whitespace` skips leading whitespace, splits on the tabs the file uses and treats the
        // `\r` of a file edited on Windows as whitespace.
        let mut fields = line.split_whitespace();
        if fields.next() != Some(key) {
            continue;
        }

        reading = match fields.next().map(str::parse::<u32>) {
            Some(Ok(value)) if value > 0 => LoginDefsFloorReading::Set(value),
            _ => LoginDefsFloorReading::Unusable,
        };
    }

    reading
}

#[cfg(test)]
#[path = "../tests/utils/login_defs_floor_tests.rs"]
mod tests;
