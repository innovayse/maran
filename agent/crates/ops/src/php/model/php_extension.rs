//! One catalogue PHP extension as a PHP version's php-fpm sees it.

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;

use crate::php::model::extension_availability::ExtensionAvailability;

/// The state of one extension of the closed catalogue for one PHP version.
///
/// Returned by `list_php_extensions`, one entry per catalogue name, whether or
/// not the extension is installed.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PhpExtension {
    /// The catalogue name. A value of this type is always one of the thirteen,
    /// so nothing downstream re-checks it.
    pub name: PhpExtensionName,
    /// Whether the version's php-fpm has the extension loaded, read from its
    /// own module listing (`php-fpm<version> -m`) and never from a file.
    pub enabled: bool,
    /// What the host's family can do with it.
    pub availability: ExtensionAvailability,
}
