//! Why a candidate Node.js major version was refused.

use thiserror::Error;

/// Reasons [`super::node_major::NodeMajor::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum NodeMajorError {
    /// The candidate was empty.
    #[error("a Node.js version cannot be empty")]
    Empty,

    /// The candidate is not one of the majors the app catalogue offers.
    ///
    /// Closed on purpose: the value names a package, a service unit and a line of a rendered
    /// unit, so anything the agent cannot fully account for is refused, including a number
    /// that looks like a version but is not in the catalogue.
    #[error("`{candidate}` is not a supported Node.js major version")]
    Unsupported {
        /// What was offered.
        candidate: String,
    },
}
