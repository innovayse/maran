//! The most files one session of an account may hold open at once.

use super::open_files_limit_error::OpenFilesLimitError;

/// The largest limit accepted: `1_048_576` (2^20), the default of the kernel's
/// `fs.nr_open` ceiling on the supported distributions. A `nofile` above
/// `fs.nr_open` is refused by `pam_limits` when the session starts, so the
/// figure is capped where the kernel's own default ceiling is.
pub const MAXIMUM: u32 = 1_048_576;

/// The most files one session of an account may hold open at once (`nofile`).
///
/// It becomes the `nofile` value of the account's `pam_limits` drop-in, applied
/// to the account's next PAM session (an SSH or SFTP login, `su`). It does not
/// reach php-fpm workers or a cron job outside a PAM session.
///
/// The inner value is private and [`OpenFilesLimit::parse`] is the only
/// constructor, so holding one is proof the figure is in `1..=MAXIMUM`. Zero is
/// refused rather than read as "no limit": an absent wire field decodes to
/// zero, and a `nofile` of `0` would give the account a session that can open
/// nothing. "No limit" is a separate, typed answer (the accounts area's
/// `ResourceCeiling::Unlimited`), and it removes the drop-in file.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct OpenFilesLimit(u32);

impl OpenFilesLimit {
    /// Validates `candidate` as an open-files limit and wraps it.
    ///
    /// # Errors
    ///
    /// - [`OpenFilesLimitError::Zero`] when `candidate` is `0`, which is what
    ///   an absent field decodes to and which would leave a session unable to
    ///   open a file.
    /// - [`OpenFilesLimitError::TooLarge`] when `candidate` is above
    ///   [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, OpenFilesLimitError> {
        if candidate == 0 {
            return Err(OpenFilesLimitError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(OpenFilesLimitError::TooLarge {
                value: candidate,
                maximum: MAXIMUM,
            });
        }

        Ok(Self(candidate))
    }

    /// The validated figure.
    #[must_use]
    pub fn value(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/open_files_limit_tests.rs"]
mod tests;
