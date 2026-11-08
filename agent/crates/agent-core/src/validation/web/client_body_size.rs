//! The largest request body a site's web server accepts, in MiB.

use super::client_body_size_error::ClientBodySizeError;

/// The largest ceiling a site may set, in MiB.
///
/// One gibibyte: a larger value is an upload nobody should send through a
/// shared web server, and bounding it keeps the figure far from any overflow
/// when it is written as `<n>m`.
pub const MAXIMUM_MEBIBYTES: u32 = 1024;

/// A validated `client_max_body_size`, in MiB, in `1..=1024`.
///
/// The inner value is private and the only constructor is
/// [`ClientBodySize::parse`], so holding one is proof that validation happened.
/// It is written into the vhost as `<n>m` by the template, which is why it is a
/// number and never the caller's text: nginx reads a size with a unit suffix,
/// and a caller-supplied string would be a directive argument of the caller's
/// choosing (rules/security.md item 4).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ClientBodySize(u32);

impl ClientBodySize {
    /// Validates `candidate` as a ceiling in MiB and wraps it.
    ///
    /// # Errors
    ///
    /// - [`ClientBodySizeError::Zero`] when `candidate` is `0`.
    /// - [`ClientBodySizeError::TooLarge`] when it is above [`MAXIMUM_MEBIBYTES`].
    pub fn parse(candidate: u32) -> Result<Self, ClientBodySizeError> {
        if candidate == 0 {
            return Err(ClientBodySizeError::Zero);
        }
        if candidate > MAXIMUM_MEBIBYTES {
            return Err(ClientBodySizeError::TooLarge {
                value: candidate,
                maximum: MAXIMUM_MEBIBYTES,
            });
        }

        Ok(Self(candidate))
    }

    /// The ceiling in MiB.
    #[must_use]
    pub fn mebibytes(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/client_body_size_tests.rs"]
mod tests;
