//! One warning of an inventory read.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::source_warning_kind::SourceWarningKind;

/// A kind of warning and, when it concerns one account or one site, its name.
///
/// **The subject can only be a validated name.** The field is private and the
/// three constructors take nothing but an [`AccountName`], a [`Domain`] or
/// nothing, so a byte the source sent can reach a warning only after it has been
/// through the same validator a hand-typed name passes. A warning about a name
/// that did NOT validate therefore has no subject: it says what happened, never
/// what the text was.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SourceWarningRecord {
    /// What the warning is about.
    kind: SourceWarningKind,
    /// The validated account or domain name, or empty when it concerns the read
    /// as a whole or text that did not validate.
    subject: String,
}

impl SourceWarningRecord {
    /// A warning that concerns the read as a whole, or text that did not validate.
    #[must_use]
    pub fn about_nothing(kind: SourceWarningKind) -> Self {
        Self {
            kind,
            subject: String::new(),
        }
    }

    /// A warning about one account.
    #[must_use]
    pub fn about_account(kind: SourceWarningKind, account: &AccountName) -> Self {
        Self {
            kind,
            subject: account.as_str().to_owned(),
        }
    }

    /// A warning about one site.
    #[must_use]
    pub fn about_domain(kind: SourceWarningKind, domain: &Domain) -> Self {
        Self {
            kind,
            subject: domain.as_str().to_owned(),
        }
    }

    /// What the warning is about.
    #[must_use]
    pub fn kind(&self) -> SourceWarningKind {
        self.kind
    }

    /// The validated account or domain name, or an empty string.
    #[must_use]
    pub fn subject(&self) -> &str {
        &self.subject
    }
}

#[cfg(test)]
#[path = "../../../tests/transfer/inventory/model/source_warning_record_tests.rs"]
mod tests;
