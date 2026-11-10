//! The id of an app of the closed app catalogue (`docs/apps/README.md`).

use crate::validation::apps::app_id_error::AppIdError;
use crate::validation::web::node_major::NodeMajor;
use crate::validation::web::php_version::PhpVersion;

/// The id of the nginx app.
const NGINX_ID: &str = "nginx";

/// The id of the Apache app.
const APACHE_ID: &str = "apache";

/// What a PHP app id starts with; the rest is a two-component version.
const PHP_PREFIX: &str = "php-";

/// What a Node.js app id starts with; the rest is a major version.
const NODEJS_PREFIX: &str = "nodejs-";

/// The longest id of the catalogue, with room to spare: `nodejs-24` is the longest
/// today. A bound exists at all so a value naming a package cannot be arbitrarily long.
const MAX_LENGTH: usize = 16;

/// An app id parsed against the closed catalogue and then carried as a type, so no
/// later caller has to remember to check it again.
///
/// Nothing the panel sends reaches a command line, a package name or a unit instance
/// except through this type: the id decides which package the root install unit is
/// started for, so a spelling outside the catalogue is refused here rather than handed to
/// the package manager (rules/security.md item 12). The catalogue is four kinds of id:
/// `nginx`, `apache`, `php-<version>` and `nodejs-<major>`.
///
/// `Php` carries a [`PhpVersion`], which fixes the SHAPE of the version. Whether that
/// version is one the panel runs (7.4 to 8.4) is the closed set of the PHP area, kept in
/// exactly one place; the apps operations ask it before they use the id, so the list is
/// not written twice.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ValidatedAppId {
    /// The nginx web server.
    Nginx,
    /// The Apache web server.
    Apache,
    /// A PHP version's FPM runtime.
    Php(PhpVersion),
    /// A Node.js major version's runtime.
    Nodejs(NodeMajor),
}

impl ValidatedAppId {
    /// Parses `candidate` against the catalogue's spellings.
    ///
    /// # Errors
    ///
    /// - [`AppIdError::Empty`] when `candidate` is empty.
    /// - [`AppIdError::TooLong`] when it is longer than any id of the catalogue.
    /// - [`AppIdError::ControlCharacter`] for a newline, a carriage return or any other
    ///   control character, checked explicitly and before the lookup.
    /// - [`AppIdError::Unknown`] for anything else, including a PHP version or a Node.js
    ///   major that does not parse.
    pub fn parse(candidate: &str) -> Result<Self, AppIdError> {
        if candidate.is_empty() {
            return Err(AppIdError::Empty);
        }
        if candidate.chars().any(char::is_control) {
            return Err(AppIdError::ControlCharacter);
        }
        if candidate.len() > MAX_LENGTH {
            return Err(AppIdError::TooLong { max: MAX_LENGTH });
        }

        let unknown = || AppIdError::Unknown {
            candidate: candidate.to_owned(),
        };

        if candidate == NGINX_ID {
            return Ok(Self::Nginx);
        }
        if candidate == APACHE_ID {
            return Ok(Self::Apache);
        }
        if let Some(version) = candidate.strip_prefix(PHP_PREFIX) {
            return PhpVersion::parse(version)
                .map(Self::Php)
                .map_err(|_| unknown());
        }
        if let Some(major) = candidate.strip_prefix(NODEJS_PREFIX) {
            return NodeMajor::parse(major)
                .map(Self::Nodejs)
                .map_err(|_| unknown());
        }

        Err(unknown())
    }

    /// The id as the catalogue spells it, and as the panel sent it.
    #[must_use]
    pub fn as_id(&self) -> String {
        match self {
            Self::Nginx => NGINX_ID.to_owned(),
            Self::Apache => APACHE_ID.to_owned(),
            Self::Php(version) => format!("{PHP_PREFIX}{}", version.as_str()),
            Self::Nodejs(major) => format!("{NODEJS_PREFIX}{}", major.as_str()),
        }
    }

    /// Whether the app is a site web server, of which a host has at most one.
    #[must_use]
    pub fn is_web_server(&self) -> bool {
        matches!(self, Self::Nginx | Self::Apache)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/apps/validated_app_id_tests.rs"]
mod tests;
