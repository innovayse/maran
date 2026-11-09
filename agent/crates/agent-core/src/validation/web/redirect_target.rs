//! Where a redirect rule sends the visitor.

use super::domain::Domain;
use super::redirect_target_error::RedirectTargetError;

/// The longest redirect target accepted, in bytes.
pub const MAXIMUM_LENGTH: usize = 2048;

/// A validated redirect target: a site-relative path (`/new/`) or an absolute
/// `http://` / `https://` URL.
///
/// The target is written into a `return` or `rewrite` line. Its alphabet is
/// ASCII letters and digits plus `- . _ ~ / : % @ + = & ? ,` — none of `;`,
/// `{`, `}`, quotes, `$`, backslash, `#` or whitespace — so it cannot end the
/// line, open a block or smuggle in an nginx variable (rules/security.md
/// item 4). It is validated, not escaped.
///
/// What it refuses beyond characters, and why:
///
/// - **Any scheme but http and https**, so `javascript:` or `data:` can never
///   be a `Location`.
/// - **`//host`**, which takes the request's scheme and so can lead a secure
///   site to an insecure one.
/// - **User information** (`https://trusted.example@evil.example/`), the shape
///   of a look-alike link.
/// - **A host that is not a valid [`Domain`]**, which also leaves out IP
///   literals and the reserved names (`localhost`, `*.internal`, ...): a
///   customer's redirect has no business pointing at the host itself.
///
/// The inner value is private and the only constructor is
/// [`RedirectTarget::parse`].
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct RedirectTarget(String);

impl RedirectTarget {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`RedirectTargetError::Empty`] when `candidate` is empty.
    /// - [`RedirectTargetError::ControlCharacter`] when it holds a control
    ///   character; checked before everything else.
    /// - [`RedirectTargetError::TooLong`] beyond [`MAXIMUM_LENGTH`] bytes.
    /// - [`RedirectTargetError::ForbiddenCharacter`] for a character outside the
    ///   allowed set.
    /// - [`RedirectTargetError::ProtocolRelative`] for `//...`.
    /// - [`RedirectTargetError::UnsupportedScheme`] when it is neither a `/`
    ///   path nor an `http://` / `https://` URL.
    /// - [`RedirectTargetError::UserInformation`] when the URL's authority holds
    ///   an `@`.
    /// - [`RedirectTargetError::InvalidHost`] when the URL's host is not a valid
    ///   [`Domain`].
    /// - [`RedirectTargetError::InvalidPort`] when the URL's port is not a
    ///   number in `1..=65535`.
    pub fn parse(candidate: &str) -> Result<Self, RedirectTargetError> {
        if candidate.is_empty() {
            return Err(RedirectTargetError::Empty);
        }
        if candidate.chars().any(char::is_control) {
            return Err(RedirectTargetError::ControlCharacter);
        }
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(RedirectTargetError::TooLong {
                length: candidate.len(),
                maximum: MAXIMUM_LENGTH,
            });
        }
        if !candidate.bytes().all(|byte| {
            byte.is_ascii_alphanumeric()
                || matches!(
                    byte,
                    b'-' | b'.'
                        | b'_'
                        | b'~'
                        | b'/'
                        | b':'
                        | b'%'
                        | b'@'
                        | b'+'
                        | b'='
                        | b'&'
                        | b'?'
                        | b','
                )
        }) {
            return Err(RedirectTargetError::ForbiddenCharacter);
        }

        if candidate.starts_with("//") {
            return Err(RedirectTargetError::ProtocolRelative);
        }
        if candidate.starts_with('/') {
            return Ok(Self(candidate.to_owned()));
        }

        let rest = candidate
            .strip_prefix("https://")
            .or_else(|| candidate.strip_prefix("http://"))
            .ok_or(RedirectTargetError::UnsupportedScheme)?;
        check_authority(rest)?;

        Ok(Self(candidate.to_owned()))
    }

    /// The validated target.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

/// Checks the authority of a URL — everything between `://` and the first `/`
/// or `?` — as `host` or `host:port`.
///
/// # Errors
///
/// As [`RedirectTarget::parse`] for user information, host and port.
fn check_authority(after_scheme: &str) -> Result<(), RedirectTargetError> {
    let authority = after_scheme
        .split(['/', '?'])
        .next()
        .unwrap_or(after_scheme);
    if authority.contains('@') {
        return Err(RedirectTargetError::UserInformation);
    }

    let (host, port) = match authority.split_once(':') {
        Some((host, port)) => (host, Some(port)),
        None => (authority, None),
    };
    Domain::parse(host)?;

    if let Some(port) = port {
        let in_range = !port.is_empty()
            && port.len() <= 5
            && port.bytes().all(|byte| byte.is_ascii_digit())
            && port
                .parse::<u32>()
                .is_ok_and(|value| (1..=65_535).contains(&value));
        if !in_range {
            return Err(RedirectTargetError::InvalidPort);
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../../tests/validation/web/redirect_target_tests.rs"]
mod tests;
