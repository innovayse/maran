//! One redirect rule of a site, with its parts checked against each other.

use maran_agent_core::validation::web::redirect_path::RedirectPath;
use maran_agent_core::validation::web::redirect_target::RedirectTarget;

use crate::sites::model::redirect_match::RedirectMatch;
use crate::sites::model::redirect_status::RedirectStatus;
use crate::sites::model::site_options_error::SiteOptionsError;

/// A redirect rule: a path, how it is matched, where it sends the visitor and
/// with which status.
///
/// The path and the target are validated types; what this type adds is the
/// check no single one of them can make. A prefix rule appends the rest of the
/// request path to the target, so both must end with `/` (the `/blog/x` of
/// `/blog/` becomes `https://new.example/x`, not `https://new.examplex`).
/// Fields are private and the only constructor is [`RedirectRule::new`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RedirectRule {
    /// The request path the rule applies to.
    path: RedirectPath,
    /// How the path is matched.
    matching: RedirectMatch,
    /// Where the rule sends the visitor.
    target: RedirectTarget,
    /// Which redirect it answers with.
    status: RedirectStatus,
}

impl RedirectRule {
    /// Builds a rule from validated parts.
    ///
    /// # Errors
    ///
    /// - [`SiteOptionsError::PrefixPathNotDirectory`] when a prefix rule's path
    ///   does not end with `/`.
    /// - [`SiteOptionsError::PrefixTargetNotDirectory`] when a prefix rule's
    ///   target does not end with `/`.
    pub fn new(
        path: RedirectPath,
        matching: RedirectMatch,
        target: RedirectTarget,
        status: RedirectStatus,
    ) -> Result<Self, SiteOptionsError> {
        if matching == RedirectMatch::Prefix {
            if !path.as_str().ends_with('/') {
                return Err(SiteOptionsError::PrefixPathNotDirectory);
            }
            if !target.as_str().ends_with('/') {
                return Err(SiteOptionsError::PrefixTargetNotDirectory);
            }
        }

        Ok(Self {
            path,
            matching,
            target,
            status,
        })
    }

    /// The request path the rule applies to.
    #[must_use]
    pub fn path(&self) -> &RedirectPath {
        &self.path
    }

    /// How the path is matched.
    #[must_use]
    pub fn matching(&self) -> RedirectMatch {
        self.matching
    }

    /// Where the rule sends the visitor.
    #[must_use]
    pub fn target(&self) -> &RedirectTarget {
        &self.target
    }

    /// Which redirect the rule answers with.
    #[must_use]
    pub fn status(&self) -> RedirectStatus {
        self.status
    }
}

#[cfg(test)]
#[path = "../../tests/sites/model/redirect_rule_tests.rs"]
mod tests;
