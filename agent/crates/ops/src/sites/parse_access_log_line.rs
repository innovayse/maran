//! Reading the five facts a traffic summary needs out of one access-log line.

use crate::sites::model::parsed_access_line::ParsedAccessLine;
use crate::sites::model::traffic_date::TrafficDate;
use crate::sites::traffic_budget::{MAXIMUM_LINE_BYTES, MAXIMUM_PATH_BYTES};

/// The month abbreviations nginx writes in `$time_local`, in calendar order.
const MONTHS: [&[u8]; 12] = [
    b"Jan", b"Feb", b"Mar", b"Apr", b"May", b"Jun", b"Jul", b"Aug", b"Sep", b"Oct", b"Nov", b"Dec",
];

/// What replaces a control character in a kept path.
const CONTROL_REPLACEMENT: char = '_';

/// Reads one line of nginx's default `combined` format:
///
/// ```text
/// $remote_addr - $remote_user [$time_local] "$request" $status $body_bytes_sent "$http_referer" "$http_user_agent"
/// ```
///
/// Returns `None` for anything that is not that shape, and the caller counts it
/// as unparsed. **A hand-written single pass over bytes, with no regular
/// expression**: every step either consumes a delimiter it can see or gives up,
/// so there is nothing to backtrack into and a hostile line costs time
/// proportional to its length, which [`MAXIMUM_LINE_BYTES`] bounds again here
/// even though the reader already refuses longer ones.
///
/// The visitor's address (the first field), the remote user, the referer and the
/// user agent are skipped, never copied: they are the parts of the line a
/// stranger chose or that identify one, and no output field carries them. nginx
/// writes `"`, `\` and every byte outside printable ASCII as `\xNN` inside a
/// quoted field, so the first `"` after the request opens its end; the parser
/// relies on that for nothing but finding the field, and refuses a line that
/// lacks it.
///
/// The request target is cut at the first `?` BEFORE it is kept, because the
/// query string is where tokens and personal data live.
#[must_use]
pub(crate) fn parse_access_log_line(line: &[u8]) -> Option<ParsedAccessLine> {
    if line.len() > MAXIMUM_LINE_BYTES {
        return None;
    }

    // $remote_addr: skipped to the first space.
    let after_address = line.iter().position(|byte| *byte == b' ')?;
    let rest = &line[after_address + 1..];

    // `- $remote_user ` is skipped up to the bracket that opens $time_local.
    let open = rest.iter().position(|byte| *byte == b'[')?;
    let rest = &rest[open + 1..];
    let close = rest.iter().position(|byte| *byte == b']')?;
    let date = parse_time_local(&rest[..close])?;

    // `] "<request>" <status> <bytes> ...`
    let rest = rest[close + 1..].strip_prefix(b" \"")?;
    let end_of_request = rest.iter().position(|byte| *byte == b'"')?;
    let request = &rest[..end_of_request];
    let rest = rest[end_of_request + 1..].strip_prefix(b" ")?;

    let (status, rest) = parse_status(rest)?;
    let body_bytes = parse_body_bytes(rest)?;

    Some(ParsedAccessLine {
        date,
        status,
        body_bytes,
        path: path_of(request),
    })
}

/// The UTC day of nginx's `$time_local`: `07/Nov/2026:10:12:13 +0300`.
fn parse_time_local(field: &[u8]) -> Option<TrafficDate> {
    let space = field.iter().position(|byte| *byte == b' ')?;
    let (stamp, zone) = (&field[..space], &field[space + 1..]);

    // dd/Mon/yyyy:hh:mm:ss
    if stamp.len() != 20
        || stamp[2] != b'/'
        || stamp[6] != b'/'
        || stamp[11] != b':'
        || stamp[14] != b':'
        || stamp[17] != b':'
    {
        return None;
    }

    let day = number(&stamp[0..2])?;
    let month = MONTHS.iter().position(|name| *name == &stamp[3..6])?;
    let year = number(&stamp[7..11])?;
    let hour = number(&stamp[12..14])?;
    let minute = number(&stamp[15..17])?;
    let second = number(&stamp[18..20])?;
    if hour > 23 || minute > 59 || second > 60 {
        return None;
    }

    TrafficDate::from_local(
        i64::from(year),
        u32::try_from(month + 1).ok()?,
        day,
        (hour * 3600 + minute * 60 + second).min(86_399),
        parse_zone(zone)?,
    )
}

/// A `+hhmm` or `-hhmm` zone as seconds east of UTC.
fn parse_zone(zone: &[u8]) -> Option<i64> {
    if zone.len() != 5 {
        return None;
    }

    let sign = match zone[0] {
        b'+' => 1_i64,
        b'-' => -1_i64,
        _ => return None,
    };
    let hours = number(&zone[1..3])?;
    let minutes = number(&zone[3..5])?;
    if hours > 23 || minutes > 59 {
        return None;
    }

    Some(sign * i64::from(hours * 3600 + minutes * 60))
}

/// The three-digit status at the start of `rest`, and what follows its space.
fn parse_status(rest: &[u8]) -> Option<(u16, &[u8])> {
    if rest.len() < 4 || rest[3] != b' ' {
        return None;
    }

    let status = u16::try_from(number(&rest[..3])?).ok()?;
    if !(100..=599).contains(&status) {
        return None;
    }

    Some((status, &rest[4..]))
}

/// `$body_bytes_sent`: digits up to the next space, or `-`.
fn parse_body_bytes(rest: &[u8]) -> Option<u64> {
    let end = rest
        .iter()
        .position(|byte| *byte == b' ')
        .unwrap_or(rest.len());
    let field = &rest[..end];

    if field == b"-" {
        return Some(0);
    }
    if field.is_empty() || field.len() > 19 || !field.iter().all(u8::is_ascii_digit) {
        return None;
    }

    field.iter().try_fold(0_u64, |total, digit| {
        total.checked_mul(10)?.checked_add(u64::from(digit - b'0'))
    })
}

/// A run of ASCII digits as a number; `None` for an empty run or any other byte.
fn number(digits: &[u8]) -> Option<u32> {
    if digits.is_empty() || !digits.iter().all(u8::is_ascii_digit) {
        return None;
    }

    digits.iter().try_fold(0_u32, |total, digit| {
        total.checked_mul(10)?.checked_add(u32::from(digit - b'0'))
    })
}

/// The path of the `METHOD target PROTOCOL` request, query string removed.
///
/// `None` unless the target is origin-form (starts with `/`) and fits the cap.
/// Bytes that are not valid UTF-8 are replaced and control characters become
/// [`CONTROL_REPLACEMENT`], so what is kept is plain text a browser can render.
fn path_of(request: &[u8]) -> Option<String> {
    let mut tokens = request.split(|byte| *byte == b' ');
    let _method = tokens.next()?;
    let target = tokens.next()?;

    let path = match target.iter().position(|byte| *byte == b'?') {
        Some(question) => &target[..question],
        None => target,
    };
    if path.first() != Some(&b'/') || path.len() > MAXIMUM_PATH_BYTES {
        return None;
    }

    Some(
        String::from_utf8_lossy(path)
            .chars()
            .map(|character| {
                if character.is_control() {
                    CONTROL_REPLACEMENT
                } else {
                    character
                }
            })
            .collect(),
    )
}

#[cfg(test)]
#[path = "../tests/sites/parse_access_log_line_tests.rs"]
mod tests;
