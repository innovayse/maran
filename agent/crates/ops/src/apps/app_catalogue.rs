//! The closed catalogue of apps, in the order the panel lists them.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;
use maran_agent_core::validation::web::node_major::{NodeMajor, SUPPORTED_MAJORS};
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::apps::AppsOpError;
use crate::apps::model::app_kind::AppKind;
use crate::php::supported_versions::{SUPPORTED_VERSIONS, ensure_supported};

/// Every app of the catalogue: nginx, apache, then the PHP versions newest first, then the
/// Node.js majors newest first.
///
/// The PHP versions are the PHP area's closed set and the Node.js majors are
/// [`SUPPORTED_MAJORS`]; neither list is written again here, so the catalogue cannot drift from
/// what the rest of the agent accepts. An entry that fails to parse would mean one of those
/// constants was edited wrongly, and that app is then simply not offered.
pub(crate) fn catalogue() -> Vec<ValidatedAppId> {
    let php = SUPPORTED_VERSIONS
        .iter()
        .rev()
        .filter_map(|version| PhpVersion::parse(version).ok())
        .map(ValidatedAppId::Php);
    let node = SUPPORTED_MAJORS
        .iter()
        .rev()
        .filter_map(|major| NodeMajor::parse(major).ok())
        .map(ValidatedAppId::Nodejs);

    [ValidatedAppId::Nginx, ValidatedAppId::Apache]
        .into_iter()
        .chain(php)
        .chain(node)
        .collect()
}

/// What `id` is for.
pub(crate) fn kind_of(id: &ValidatedAppId) -> AppKind {
    if id.is_web_server() {
        AppKind::WebServer
    } else {
        AppKind::Runtime
    }
}

/// Refuses `id` unless it is an app of the catalogue.
///
/// `ValidatedAppId` fixes the SHAPE of an id; a PHP version is additionally checked against the
/// PHP area's supported set, the one place that list is written. Every operation asks this before
/// it derives a package name from the id.
///
/// # Errors
///
/// Returns [`AppsOpError::UnsupportedApp`] for a PHP version outside the supported set.
pub(crate) fn ensure_in_catalogue(id: &ValidatedAppId) -> Result<(), AppsOpError> {
    if let ValidatedAppId::Php(version) = id {
        ensure_supported(version).map_err(|_| AppsOpError::UnsupportedApp { app: id.as_id() })?;
    }
    Ok(())
}
