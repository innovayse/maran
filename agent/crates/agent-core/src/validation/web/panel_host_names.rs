//! The names the panel itself is served under, which no tenant may claim.

use crate::validation::web::domain::Domain;
use crate::validation::web::domain_error::DomainError;

/// The separator of the list the installer hands the agent on its command line.
const LIST_SEPARATOR: char = ',';

/// The prefix of the alternative spelling every public name is also reached by.
const WWW_PREFIX: &str = "www.";

/// The host names the panel answers to — the name its own nginx vhost serves and the
/// host of its public address — plus the `www.` form of each.
///
/// [`Domain::parse`] refuses what is reserved on every host. These names are a fact of
/// THIS host, so they cannot be spelled in that function: the installer records them in a
/// root-owned file and the agent reads them at start, which is why the value is a
/// constructed type the services carry rather than a lookup anyone can make. A tenant who
/// holds a vhost for one of them answers the name on 80 and 443, including the ACME
/// HTTP-01 location, and can obtain a publicly trusted certificate for it.
///
/// The failing direction is permissive: [`PanelHostNames::none`], which an unset or empty
/// list yields, refuses nothing here, and the static reserved set stays unconditional.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PanelHostNames(Vec<Domain>);

impl PanelHostNames {
    /// No panel name: nothing is refused here. What a host with no recorded name has.
    #[must_use]
    pub fn none() -> Self {
        Self(Vec::new())
    }

    /// Parses the comma-separated list the installer writes. An empty list is
    /// [`PanelHostNames::none`].
    ///
    /// # Errors
    ///
    /// Returns the [`DomainError`] of the first entry that is not a well-formed host
    /// name, an empty entry (`a,,b`, a trailing comma) included. Refused rather than
    /// skipped: a name that could not be read must not silently stop being reserved.
    pub fn parse_list(list: &str) -> Result<Self, DomainError> {
        if list.is_empty() {
            return Ok(Self::none());
        }

        let names = list
            .split(LIST_SEPARATOR)
            .map(Domain::parse_existing)
            .collect::<Result<Vec<_>, _>>()?;

        Ok(Self(names))
    }

    /// Hands `domain` back when a tenant may claim it.
    ///
    /// # Errors
    ///
    /// Returns [`DomainError::PanelHostName`] when `domain` is one of the names or the
    /// `www.` form of one. The error carries no value, so a message never says which
    /// name matched.
    pub fn check(&self, domain: Domain) -> Result<Domain, DomainError> {
        if self.refuses(domain.as_str()) {
            return Err(DomainError::PanelHostName);
        }

        Ok(domain)
    }

    /// Whether `name` is one of the names or the `www.` form of one.
    ///
    /// `name` must already be lower-case, as every validated name is: the comparison is
    /// exact, so a value that skipped validation is compared as written.
    #[must_use]
    pub fn refuses(&self, name: &str) -> bool {
        self.0.iter().any(|panel| {
            name == panel.as_str()
                || name
                    .strip_prefix(WWW_PREFIX)
                    .is_some_and(|rest| rest == panel.as_str())
        })
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/panel_host_names_tests.rs"]
mod tests;
