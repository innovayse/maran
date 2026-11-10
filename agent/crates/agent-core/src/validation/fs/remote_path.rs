//! A path that names something on a remote server, safe to place in a fixed
//! remote command.

use super::remote_path_error::RemotePathError;

/// The longest path Linux accepts, in bytes.
const MAX_LENGTH: usize = 4096;

/// A path on a remote server an administrator registered as a migration source,
/// fit to be the one variable part of a constant remote command.
///
/// The grammar is a hole's grammar, not a filesystem's: absolute, every
/// character from `[A-Za-z0-9._/@:+-]`, no component beginning with `-`, no
/// `..` component, no empty component. Nothing in it is a quote, a space, a
/// glob, a `$`, a `~`, a backslash or a control character, so inserted between
/// the fixed words of a command it cannot end a word, start an option, open an
/// expansion or begin another command, in whatever shell the remote account
/// happens to use. It is therefore VALIDATED, never escaped: a path the grammar
/// refuses is a warning about that entry and the entry is skipped.
///
/// It is never used as a path on THIS host. The text came from a remote server's
/// own files (a virtual host's document root, a home directory from its password
/// database), is only ever passed back to that server as an argument, and the
/// agent never opens it locally.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct RemotePath(String);

impl RemotePath {
    /// Parses `candidate` as a remote path.
    ///
    /// A trailing `/` and `.` components are accepted: neither can change what a
    /// word of a command means.
    ///
    /// # Errors
    ///
    /// - [`RemotePathError::Empty`] when `candidate` is empty.
    /// - [`RemotePathError::TooLong`] beyond 4096 bytes.
    /// - [`RemotePathError::NotAbsolute`] unless it starts with `/`.
    /// - [`RemotePathError::IllegalCharacter`] for any character outside
    ///   `[A-Za-z0-9._/@:+-]`.
    /// - [`RemotePathError::EmptyComponent`] for `//` anywhere.
    /// - [`RemotePathError::ParentComponent`] for a `..` component.
    /// - [`RemotePathError::LeadingDash`] for a component starting with `-`.
    pub fn parse(candidate: &str) -> Result<Self, RemotePathError> {
        if candidate.is_empty() {
            return Err(RemotePathError::Empty);
        }
        if candidate.len() > MAX_LENGTH {
            return Err(RemotePathError::TooLong { limit: MAX_LENGTH });
        }
        let below_root = candidate
            .strip_prefix('/')
            .ok_or(RemotePathError::NotAbsolute)?;
        if !candidate.bytes().all(is_allowed_byte) {
            return Err(RemotePathError::IllegalCharacter);
        }

        // Skip the empty piece before the leading slash; a trailing slash leaves
        // one empty piece at the end, which is not an empty COMPONENT.
        let mut components = below_root.split('/').peekable();
        while let Some(component) = components.next() {
            let is_trailing_slash = component.is_empty() && components.peek().is_none();
            if component.is_empty() && !is_trailing_slash {
                return Err(RemotePathError::EmptyComponent);
            }
            if component == ".." {
                return Err(RemotePathError::ParentComponent);
            }
            if component.starts_with('-') {
                return Err(RemotePathError::LeadingDash);
            }
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated path text.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// Whether this path is `ancestor` or lies below it, compared component by
    /// component: `/home/ab` is not inside `/home/a`, which a prefix comparison
    /// of the text would say.
    #[must_use]
    pub fn is_inside(&self, ancestor: &Self) -> bool {
        let mut own = self.0.split('/').filter(|component| !component.is_empty());
        ancestor
            .0
            .split('/')
            .filter(|component| !component.is_empty())
            .all(|expected| own.next() == Some(expected))
    }
}

/// Whether `byte` is in the allow-list `[A-Za-z0-9._/@:+-]`.
fn is_allowed_byte(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'/' | b'@' | b':' | b'+' | b'-')
}

#[cfg(test)]
#[path = "../../tests/validation/fs/remote_path_tests.rs"]
mod tests;
