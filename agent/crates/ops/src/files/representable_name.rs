//! Which directory entry names can be sent to the panel and addressed later.

use std::ffi::OsStr;

/// Returns `name` as text when the panel can be told it, and `None` otherwise.
///
/// A name is representable when it is valid UTF-8 and holds no control
/// character. Anything else cannot be carried by the contract's strings and
/// could not be named in a later request (`BrowsePath` refuses a control
/// character), so a search counts it as skipped instead of reporting a name the
/// panel could never use.
#[must_use]
pub(crate) fn representable_name(name: &OsStr) -> Option<&str> {
    let text = name.to_str()?;
    if text.chars().any(char::is_control) {
        return None;
    }

    Some(text)
}

#[cfg(test)]
#[path = "../tests/files/representable_name_tests.rs"]
mod tests;
