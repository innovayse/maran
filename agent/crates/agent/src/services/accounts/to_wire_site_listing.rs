//! The one mapping from a vhost listing onto the wire's readable flag and list.

use maran_ops::sites::{AccountSiteSuspension, SiteServing};

use crate::proto::{SiteServing as WireSiteServing, SiteSuspensionFact};

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
                    // The deprecated bool keeps its refusing direction: only an
                    // observed stub is true, so "could not be compared" still
                    // reads as not stubbed to a panel that has not moved to
                    // `serving`.
                    serving_stub: fact.serving == SiteServing::Stub,
                    serving: to_wire_site_serving(fact.serving) as i32,
                })
                .collect(),
        ),
    }
}

/// The wire spelling of what a vhost was observed to serve.
fn to_wire_site_serving(serving: SiteServing) -> WireSiteServing {
    match serving {
        SiteServing::Stub => WireSiteServing::Stub,
        SiteServing::NotStub => WireSiteServing::NotStub,
        SiteServing::Unobservable => WireSiteServing::Unobservable,
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/to_wire_site_listing_tests.rs"]
mod tests;
