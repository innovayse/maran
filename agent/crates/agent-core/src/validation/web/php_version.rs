//! A PHP version that is safe to write into a config file and a path.

use crate::validation::web::php_version_error::PhpVersionError;

/// The most digits either component of a real PHP version needs.
///
/// Two, which covers everything from `5.6` to a hypothetical `10.99`. A bound
/// exists at all so that a value naming a filesystem path cannot be arbitrarily
/// long.
const MAX_COMPONENT_DIGITS: usize = 2;

/// A two-component PHP version — `8.3` — checked once at the boundary and then
/// carried as a type so no later caller has to remember to check it again.
///
/// This value does four dangerous things: it is interpolated into the php-fpm
/// socket path, written into an nginx `fastcgi_pass` directive inside a
/// root-owned config, turned into a package name, and turned into a systemd
/// unit name. The templates escape nothing by design — values are VALIDATED,
/// not escaped (rules/rust.md "Validation first") — so a version containing
/// `;`, `}` or a newline would inject directives of the caller's choosing into
/// a configuration `nginx -t` then happily accepts. Construction is the only
/// way to obtain one, so a `PhpVersion` in a signature is a promise that the
/// value has been through [`PhpVersion::parse`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PhpVersion(String);

impl PhpVersion {
    /// Parses `candidate` as `<major>.<minor>`.
    ///
    /// # Errors
    ///
    /// - [`PhpVersionError::Empty`] when `candidate` is empty.
    /// - [`PhpVersionError::ControlCharacter`] for a newline, a carriage
    ///   return or any other control character — checked explicitly and first,
    ///   because it is the injection this type exists to stop.
    /// - [`PhpVersionError::Malformed`] when the candidate is not exactly two
    ///   dot-separated components of ASCII digits.
    /// - [`PhpVersionError::ComponentTooLong`] when either component exceeds
    ///   two digits.
    pub fn parse(candidate: &str) -> Result<Self, PhpVersionError> {
        if candidate.is_empty() {
            return Err(PhpVersionError::Empty);
        }

        if candidate.chars().any(char::is_control) {
            return Err(PhpVersionError::ControlCharacter);
        }

        let malformed = || PhpVersionError::Malformed {
            candidate: candidate.to_owned(),
        };

        let (major, minor) = candidate.split_once('.').ok_or_else(malformed)?;

        for component in [major, minor] {
            if component.is_empty() || !component.chars().all(|c| c.is_ascii_digit()) {
                return Err(malformed());
            }
            if component.len() > MAX_COMPONENT_DIGITS {
                return Err(PhpVersionError::ComponentTooLong);
            }
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated version, as it is written — `8.3`.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/php_version_tests.rs"]
mod tests;
