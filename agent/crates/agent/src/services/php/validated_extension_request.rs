//! The checks a `SetPhpExtension` request passes before `ops` is asked anything.

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the version and the extension name an extension request carries.
///
/// The API validated both already. This is the agent's own check
/// (rules/security.md item 1): the version becomes half a package name and a
/// socket path, and the extension becomes the instance of a root-run install
/// unit and an argument of the family's switch, so it must be one of the
/// catalogue's names and nothing a caller wrote.
///
/// # Errors
///
/// Returns the wire error (INVALID_INPUT) for a version that is not two
/// components, and for an extension that is empty, carries a control character
/// or is not in the catalogue.
pub fn validated_extension_request(
    version: &str,
    extension: &str,
) -> Result<(PhpVersion, PhpExtensionName), AgentError> {
    let version = PhpVersion::parse(version).map_err(|error| invalid_input(error.to_string()))?;
    let extension =
        PhpExtensionName::parse(extension).map_err(|error| invalid_input(error.to_string()))?;

    Ok((version, extension))
}

#[cfg(test)]
#[path = "../../tests/services/php/validated_extension_request_tests.rs"]
mod tests;
