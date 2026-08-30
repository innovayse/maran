//! Reading the host's os-release file.

use super::detect_error::DetectError;
use super::distro_info::DistroInfo;
use super::os_release::parse;

/// Absolute path of the file every supported distribution publishes.
const OS_RELEASE_PATH: &str = "/etc/os-release";

/// Detects the host distribution by reading [`OS_RELEASE_PATH`].
///
/// # Errors
///
/// Returns [`DetectError::Unreadable`] when the file cannot be read and
/// [`DetectError::Unsupported`] when the distribution is outside the matrix.
pub fn detect() -> Result<DistroInfo, DetectError> {
    let content = std::fs::read_to_string(OS_RELEASE_PATH).map_err(|_| DetectError::Unreadable)?;
    parse(&content)
}
