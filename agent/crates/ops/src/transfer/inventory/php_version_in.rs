//! Finding a PHP version in a path, a handler line or a directory name.

/// The `major.minor` PHP version that `text` names, if it names one.
///
/// Three spellings are read: the whole text being a version (`8.2`); the first
/// isolated `major.minor` after the word `php` (`/run/php/php8.2-fpm.sock`,
/// `php-fpm8.2`, `SetHandler "proxy:unix:/run/php/php8.1-fpm.sock|fcgi://x"`);
/// and the compact form directly after `php` (`php82`, `/etc/opt/remi/php74`,
/// read as `8.2` and `7.4`). Numbers inside a longer dotted number (`10.0.0.5`,
/// `8.2.1`) are not versions. The text is untrusted and may be anything: the
/// answer is a short string of digits and one dot, built from the digits, never
/// the text.
#[must_use]
pub fn php_version_in(text: &str) -> Option<String> {
    let trimmed = text.trim();
    if version_end(trimmed.as_bytes(), 0) == Some(trimmed.len()) {
        return Some(trimmed.to_owned());
    }

    let lowered = text.to_ascii_lowercase();
    let bytes = lowered.as_bytes();
    let after_php = lowered.find("php")? + 3;
    let mut index = after_php;
    while index < bytes.len() {
        let starts_number = bytes.get(index).is_some_and(u8::is_ascii_digit)
            && index
                .checked_sub(1)
                .and_then(|before| bytes.get(before))
                .is_none_or(|byte| !byte.is_ascii_digit() && *byte != b'.');
        if starts_number {
            if let Some(end) = version_end(bytes, index) {
                return lowered.get(index..end).map(str::to_owned);
            }
            if let Some(version) = compact_version(bytes, index) {
                return Some(version);
            }
        }
        index += 1;
    }
    None
}

/// The end of a `major.minor` number that starts at `start`, with one or two
/// digits on each side of the dot and no further `.digit` after it.
fn version_end(bytes: &[u8], start: usize) -> Option<usize> {
    let major_end = digits_end(bytes, start);
    if !(1..=2).contains(&(major_end - start)) || bytes.get(major_end) != Some(&b'.') {
        return None;
    }
    let minor_start = major_end + 1;
    let minor_end = digits_end(bytes, minor_start);
    if !(1..=2).contains(&(minor_end - minor_start)) {
        return None;
    }
    let continues = bytes.get(minor_end) == Some(&b'.')
        && bytes.get(minor_end + 1).is_some_and(u8::is_ascii_digit);
    if continues { None } else { Some(minor_end) }
}

/// The compact `php82` form: the number that starts at `start` is two or three
/// digits, directly follows the letters `php`, and is not part of a dotted
/// number. The first digit is the major version and the rest the minor.
fn compact_version(bytes: &[u8], start: usize) -> Option<String> {
    let preceded_by_php =
        start.checked_sub(3).and_then(|from| bytes.get(from..start)) == Some(b"php".as_slice());
    let end = digits_end(bytes, start);
    let length = end - start;
    if !preceded_by_php || !(2..=3).contains(&length) || bytes.get(end) == Some(&b'.') {
        return None;
    }
    let digits = std::str::from_utf8(bytes.get(start..end)?).ok()?;
    let (major, minor) = digits.split_at(1);
    Some(format!("{major}.{minor}"))
}

/// The index just past the run of ASCII digits that starts at `start`.
fn digits_end(bytes: &[u8], start: usize) -> usize {
    let mut end = start;
    while bytes.get(end).is_some_and(u8::is_ascii_digit) {
        end += 1;
    }
    end
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/php_version_in_tests.rs"]
mod tests;
