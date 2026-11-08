//! A directory flag's value, required to be absolute.

use std::path::PathBuf;

use super::options_error::OptionsError;

/// Reads the value of a flag that names a directory.
///
/// # Errors
///
/// Returns [`OptionsError::RelativePath`] when `value` is not an absolute path. A relative one
/// means "relative to wherever the process was started", and the directories named by the fleet
/// flags decide where a private key is written and read.
pub(super) fn absolute_directory(flag: &'static str, value: &str) -> Result<PathBuf, OptionsError> {
    let path = PathBuf::from(value);

    if path.is_absolute() {
        Ok(path)
    } else {
        Err(OptionsError::RelativePath {
            flag,
            value: value.to_owned(),
        })
    }
}

#[cfg(test)]
#[path = "../tests/config/absolute_directory_tests.rs"]
mod tests;
