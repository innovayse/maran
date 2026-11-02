//! The one mapping from a vhost listing onto the wire's readable flag and list.

use maran_ops::sites::AccountSiteSuspension;

use crate::proto::SiteSuspensionFact;

/// Converts the listing into the pair the contract carries on
/// `GetAccountSuspensionStateOk`: `sites_directory_readable` and `sites`.
///
/// The wire keeps a flag beside a repeated field, so an unreadable directory
/// travels as `false` with an empty list; the panel is documented to refuse a
/// suspension conclusion on `false`. In this crate the two cannot disagree,
/// because only a listed directory has a list to send.
#[must_use]
pub fn to_wire_site_listing(listing: AccountSiteSuspension) -> (bool, Vec<SiteSuspensionFact>) {
    match listing {
        AccountSiteSuspension::Unreadable => (false, Vec::new()),
        AccountSiteSuspension::Listed(facts) => (
            true,
            facts
                .into_iter()
                .map(|fact| SiteSuspensionFact {
                    domain: fact.domain,
                    serving_stub: fact.serving_stub,
                })
                .collect(),
        ),
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/to_wire_site_listing_tests.rs"]
mod tests;
