//! Why a candidate reverse-proxy upstream was refused.

use thiserror::Error;

/// Reasons [`super::upstream::Upstream::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum UpstreamError {
    /// The candidate was empty.
    #[error("an upstream cannot be empty")]
    Empty,

    /// The candidate contained a newline, carriage return or other control
    /// character, which would end the config line this value is written into.
    #[error("an upstream cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// The candidate was not `host:port`, or the port half was not a number.
    #[error("`{candidate}` is not a valid `host:port` upstream")]
    Malformed {
        /// The rejected candidate, unmodified.
        candidate: String,
    },

    /// The port fell outside 1–65535.
    #[error("an upstream port must be between 1 and 65535")]
    InvalidPort,

    /// The host did not parse as an IP address.
    #[error("`{host}` is not a valid IP address")]
    InvalidHost {
        /// The rejected host.
        host: String,
    },

    /// The host resolved but is neither loopback nor an RFC1918 private
    /// address — a reverse proxy pointing at it would turn the panel into an
    /// open proxy for whoever asked for the site.
    #[error("`{host}` is not a loopback or private address")]
    NotPrivate {
        /// The rejected host.
        host: String,
    },
}
