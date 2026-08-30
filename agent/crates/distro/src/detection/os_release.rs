//! Parsing os-release content into a supported-distribution decision.

use super::detect_error::DetectError;
use super::distro_info::DistroInfo;
use crate::family::DistroFamily;

/// Parses os-release content.
///
/// Split from [`super::detect::detect`] so the classification can be exercised
/// against fixture text for distributions this machine will never be.
///
/// Only `ID` and `VERSION_ID` are read. The format also allows quoting, which
/// distributions apply inconsistently — Ubuntu quotes `VERSION_ID` but not `ID`,
/// AlmaLinux quotes both — so surrounding quotes are stripped from either.
///
/// # Errors
///
/// Returns [`DetectError::Unsupported`] when `ID` names a distribution the panel
/// does not support, including when the field is absent and the id is empty.
pub fn parse(content: &str) -> Result<DistroInfo, DetectError> {
    let mut id = String::new();
    let mut version_id = String::new();

    for line in content.lines() {
        if let Some(value) = line.strip_prefix("ID=") {
            id = unquote(value);
        } else if let Some(value) = line.strip_prefix("VERSION_ID=") {
            version_id = unquote(value);
        }
    }

    let family = match id.as_str() {
        "ubuntu" | "debian" => DistroFamily::Debian,
        "almalinux" | "rocky" => DistroFamily::Rhel,
        _ => return Err(DetectError::Unsupported { id }),
    };

    Ok(DistroInfo {
        id,
        family,
        version_id,
    })
}

/// Strips surrounding whitespace and double quotes from an os-release value.
///
/// Trims every leading and trailing quote rather than exactly one pair: a value
/// wrapped twice is malformed either way, and the distribution ids this feeds
/// are compared against a fixed list that no amount of quoting can widen.
fn unquote(value: &str) -> String {
    value.trim().trim_matches('"').to_owned()
}

#[cfg(test)]
#[path = "../tests/detection/os_release_tests.rs"]
mod tests;
