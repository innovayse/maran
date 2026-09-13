//! The php.ini settings a customer may change, and the bounds they may change
//! them within.

use crate::php::PhpOpError;
use crate::php::model::override_kind::OverrideKind;

/// A mebibyte, the unit the byte bounds below are written in.
const MEBIBYTE: u64 = 1024 * 1024;

/// The largest byte size any of the three size settings may be given.
///
/// One number for all three because they are set together in practice — an
/// upload larger than `post_max_size` is silently discarded by PHP, and a
/// `memory_limit` below either is an out-of-memory error mid-upload — and
/// three different ceilings would only produce combinations that cannot work.
const MAXIMUM_BYTES: u64 = 512 * MEBIBYTE;

/// The most path components a timezone name may have — `America/Indiana/Knox`
/// is the deepest shape the IANA database uses.
const MAXIMUM_TIMEZONE_COMPONENTS: usize = 3;

/// The PHP settings a customer may change, and the bounds they may change them
/// within.
///
/// A whitelist, not a filter: a name that is not here is REFUSED rather than
/// sanitised or dropped (see [`PhpOverride::parse`]). `disable_functions`,
/// `open_basedir`, `allow_url_fopen` and `cgi.fix_pathinfo` are deliberately
/// absent — they are the pool's own protection, and the template sets them
/// with `php_admin_value`/`php_admin_flag`, which a `php_value` line cannot
/// countermand at any position in the file.
///
/// The agent re-derives this list rather than trusting the panel's copy of it
/// (rules/security.md item 1): the panel is another network peer, and a bug or
/// a compromise there must not become a php.ini the agent writes as root.
const ALLOWED: &[(&str, OverrideKind)] = &[
    (
        "memory_limit",
        OverrideKind::Bytes {
            maximum: MAXIMUM_BYTES,
        },
    ),
    (
        "upload_max_filesize",
        OverrideKind::Bytes {
            maximum: MAXIMUM_BYTES,
        },
    ),
    (
        "post_max_size",
        OverrideKind::Bytes {
            maximum: MAXIMUM_BYTES,
        },
    ),
    // Above five minutes a request is not slow, it is stuck, and a pool's
    // workers are a fixed budget: enough stuck requests and the account's
    // sites stop answering entirely.
    (
        "max_execution_time",
        OverrideKind::Seconds {
            // Not zero: PHP reads zero as "no limit".
            minimum: 1,
            maximum: 300,
        },
    ),
    (
        "max_input_vars",
        OverrideKind::Count {
            minimum: 1,
            maximum: 10_000,
        },
    ),
    ("date.timezone", OverrideKind::Timezone),
];

/// One `php_value[name] = value` line the customer has asked for, checked
/// against the whitelist above.
///
/// Construction is the only way to obtain one, so a `PhpOverride` in a pool's
/// input is a promise that the name is whitelisted, the value is in range, and
/// the value contains nothing that could end the line it is written on.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PhpOverride {
    /// The setting name — borrowed from [`ALLOWED`], never from the caller.
    name: &'static str,
    /// The value, as it will be written.
    value: String,
}

impl PhpOverride {
    /// Checks `name` against the whitelist and `value` against that name's
    /// bound.
    ///
    /// The name stored is the whitelist's own `&'static str` and not the
    /// caller's string. That is not a micro-optimisation: it means the bytes
    /// written into the pool's left-hand side provably came from this file, so
    /// no amount of trailing whitespace, unicode look-alike or embedded
    /// newline in the request can reach the config even if a later check is
    /// loosened.
    ///
    /// # Errors
    ///
    /// - [`PhpOpError::OverrideNotAllowed`] when `name` is not on the
    ///   whitelist. Refused rather than dropped: a silently discarded override
    ///   means a customer sets a value, sees success, and gets behaviour they
    ///   did not ask for.
    /// - [`PhpOpError::OverrideControlCharacter`] when `value` contains a
    ///   newline, a carriage return or any other control character — checked
    ///   first and by name, because it is the injection this type exists to
    ///   stop (rules/security.md item 4): `pool.conf` is line-oriented, and
    ///   one embedded newline turns one setting into a setting plus a
    ///   directive of the customer's choosing.
    /// - [`PhpOpError::OverrideMalformed`] when `value` is not the shape the
    ///   setting takes.
    /// - [`PhpOpError::OverrideOutOfRange`] when it is well-formed but exceeds
    ///   the bound.
    pub fn parse(name: &str, value: &str) -> Result<Self, PhpOpError> {
        let (allowed_name, kind) = ALLOWED
            .iter()
            .find(|(allowed, _)| *allowed == name)
            .ok_or_else(|| PhpOpError::OverrideNotAllowed {
                name: name.to_owned(),
            })?;

        // First, and separately from every shape check below. A shape check
        // that happens to exclude newlines today is a refusal that a later
        // loosening of the grammar removes without anyone noticing.
        if value.chars().any(char::is_control) {
            return Err(PhpOpError::OverrideControlCharacter {
                name: (*allowed_name).to_owned(),
            });
        }

        match *kind {
            OverrideKind::Bytes { maximum } => {
                check_maximum(
                    allowed_name,
                    value,
                    parse_bytes(allowed_name, value)?,
                    maximum,
                )?;
            }
            OverrideKind::Seconds { minimum, maximum }
            | OverrideKind::Count { minimum, maximum } => {
                let actual = parse_number(allowed_name, value)?;
                check_minimum(allowed_name, value, actual, minimum)?;
                check_maximum(allowed_name, value, actual, maximum)?;
            }
            OverrideKind::Timezone => check_timezone(allowed_name, value)?,
        }

        Ok(Self {
            name: allowed_name,
            value: value.to_owned(),
        })
    }

    /// The whitelisted setting name.
    #[must_use]
    pub fn name(&self) -> &'static str {
        self.name
    }

    /// The checked value.
    #[must_use]
    pub fn value(&self) -> &str {
        &self.value
    }
}

/// Parses a php.ini byte size: digits, then an optional `K`, `M` or `G`.
///
/// # Errors
///
/// Returns [`PhpOpError::OverrideMalformed`] for anything else — including
/// `-1`, PHP's spelling of "unlimited", which the grammar admits no sign for.
fn parse_bytes(name: &str, value: &str) -> Result<u64, PhpOpError> {
    let malformed = || PhpOpError::OverrideMalformed {
        name: name.to_owned(),
        value: value.to_owned(),
    };

    let (digits, multiplier) = match value.as_bytes().last() {
        Some(b'K' | b'k') => (&value[..value.len() - 1], 1024),
        Some(b'M' | b'm') => (&value[..value.len() - 1], MEBIBYTE),
        Some(b'G' | b'g') => (&value[..value.len() - 1], 1024 * MEBIBYTE),
        _ => (value, 1),
    };

    let count = parse_number(name, digits)?;

    // A size the caller wrote so large that scaling it overflows is refused,
    // not wrapped: `18446744073709551G` must not become a small number that
    // passes the bound below.
    count.checked_mul(multiplier).ok_or_else(malformed)
}

/// Parses a plain non-negative decimal number.
///
/// # Errors
///
/// Returns [`PhpOpError::OverrideMalformed`] when `value` is empty or is not
/// entirely ASCII digits. Deliberately stricter than `u64::from_str`, which
/// accepts a leading `+`.
fn parse_number(name: &str, value: &str) -> Result<u64, PhpOpError> {
    let malformed = || PhpOpError::OverrideMalformed {
        name: name.to_owned(),
        value: value.to_owned(),
    };

    if value.is_empty() || !value.bytes().all(|byte| byte.is_ascii_digit()) {
        return Err(malformed());
    }

    value.parse().map_err(|_| malformed())
}

/// Refuses `actual` when it exceeds `maximum`.
///
/// # Errors
///
/// Returns [`PhpOpError::OverrideOutOfRange`], carrying the bound so an
/// operator's log says what the ceiling was and not only that one was hit.
fn check_maximum(name: &str, value: &str, actual: u64, maximum: u64) -> Result<(), PhpOpError> {
    if actual <= maximum {
        return Ok(());
    }

    Err(PhpOpError::OverrideOutOfRange {
        name: name.to_owned(),
        value: value.to_owned(),
        maximum,
    })
}

/// Refuses `actual` when it is below `minimum`.
///
/// Reported as malformed rather than out of range, because the value is not a
/// number that is merely too small — it is PHP's spelling of "no limit at
/// all", and telling an operator it "exceeds the maximum of 1" would be
/// actively misleading.
///
/// # Errors
///
/// Returns [`PhpOpError::OverrideMalformed`].
fn check_minimum(name: &str, value: &str, actual: u64, minimum: u64) -> Result<(), PhpOpError> {
    if actual >= minimum {
        return Ok(());
    }

    Err(PhpOpError::OverrideMalformed {
        name: name.to_owned(),
        value: value.to_owned(),
    })
}

/// Checks an IANA timezone name by shape.
///
/// By shape and not against the zoneinfo tree: the agent must give the same
/// answer on a host whose tzdata is a week older than the panel's, and a check
/// that reads the filesystem would also be a check whose result depends on
/// what is mounted. What matters for safety is that the value is one to three
/// components of letters, digits, `_`, `+` and `-` — which excludes `.`, and
/// therefore `..`, and excludes an absolute path.
///
/// # Errors
///
/// Returns [`PhpOpError::OverrideMalformed`] for anything else, including
/// `../../etc/passwd` and an empty component from a leading or doubled `/`.
fn check_timezone(name: &str, value: &str) -> Result<(), PhpOpError> {
    let malformed = || PhpOpError::OverrideMalformed {
        name: name.to_owned(),
        value: value.to_owned(),
    };

    // `split` never yields nothing, so there is no empty case to check: a
    // value with no separator is one component, and the empty string is one
    // empty component, which the loop below refuses.
    let components: Vec<&str> = value.split('/').collect();
    if components.len() > MAXIMUM_TIMEZONE_COMPONENTS {
        return Err(malformed());
    }

    for component in components {
        if component.is_empty() {
            return Err(malformed());
        }
        if !component
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'_' | b'+' | b'-'))
        {
            return Err(malformed());
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../../tests/php/php_override_tests.rs"]
mod tests;
