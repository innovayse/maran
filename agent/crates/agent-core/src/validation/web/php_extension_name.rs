//! A PHP extension the panel manages, drawn from a closed catalogue.

use crate::validation::web::php_extension_name_error::PhpExtensionNameError;

/// The catalogue: each managed extension's name, then the name `php-fpm -m`
/// prints for the module that extension loads, alphabetical by the first.
///
/// The two differ for exactly two entries: the MySQL driver extension is
/// reported as `mysqli`, and OPcache as `Zend OPcache`. Every other module is
/// listed under its own name.
static CATALOGUE: [(&str, &str); 13] = [
    ("bcmath", "bcmath"),
    ("curl", "curl"),
    ("gd", "gd"),
    ("gmp", "gmp"),
    ("imagick", "imagick"),
    ("intl", "intl"),
    ("mbstring", "mbstring"),
    ("mysql", "mysqli"),
    ("opcache", "Zend OPcache"),
    ("redis", "redis"),
    ("soap", "soap"),
    ("xml", "xml"),
    ("zip", "zip"),
];

/// A PHP extension from the panel's closed catalogue of thirteen — `gd`,
/// `redis`, `opcache` and so on — checked once at the boundary and then carried
/// as a type.
///
/// This value is turned into an OS package name, becomes the instance of a
/// root-run install unit, and is passed as an argument to the family's
/// extension switch. Construction is the only way to obtain one and it can only
/// yield a catalogue entry, so a `PhpExtensionName` in a signature is a
/// promise that the value is one of thirteen fixed strings: no package name,
/// path, option or control character can arrive through it. The inner text is
/// `&'static str` on purpose — it points into the catalogue, never into the
/// caller's input.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PhpExtensionName(&'static str);

impl PhpExtensionName {
    /// Parses `candidate` as one of the catalogue's names, exactly as written
    /// (lowercase).
    ///
    /// # Errors
    ///
    /// - [`PhpExtensionNameError::Empty`] when `candidate` is empty.
    /// - [`PhpExtensionNameError::ControlCharacter`] for a newline, a carriage
    ///   return or any other control character — checked explicitly and
    ///   before the lookup.
    /// - [`PhpExtensionNameError::NotInCatalogue`] for anything that is not
    ///   one of the thirteen names.
    pub fn parse(candidate: &str) -> Result<Self, PhpExtensionNameError> {
        if candidate.is_empty() {
            return Err(PhpExtensionNameError::Empty);
        }

        if candidate.chars().any(char::is_control) {
            return Err(PhpExtensionNameError::ControlCharacter);
        }

        CATALOGUE
            .iter()
            .find(|(name, _)| *name == candidate)
            .map(|&(name, _)| Self(name))
            .ok_or_else(|| PhpExtensionNameError::NotInCatalogue {
                candidate: candidate.to_owned(),
            })
    }

    /// The catalogue name, as it is written — `mysql`.
    #[must_use]
    pub fn as_str(&self) -> &'static str {
        self.0
    }

    /// The name `php-fpm -m` prints for the module this extension loads —
    /// `mysqli` for `mysql`, `Zend OPcache` for `opcache`, the name itself
    /// for the rest.
    ///
    /// Compare it case-insensitively: the listing is the daemon's own wording,
    /// not the catalogue's.
    #[must_use]
    pub fn loaded_module_name(&self) -> &'static str {
        CATALOGUE
            .iter()
            .find(|(name, _)| *name == self.0)
            .map_or(self.0, |(_, module)| *module)
    }

    /// Every extension in the catalogue, alphabetical.
    pub fn catalogue() -> impl Iterator<Item = Self> {
        CATALOGUE.iter().map(|&(name, _)| Self(name))
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/php_extension_name_tests.rs"]
mod tests;
