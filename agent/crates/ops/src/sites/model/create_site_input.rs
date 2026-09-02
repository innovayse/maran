//! Everything a site operation needs about the site it acts on.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::sites::model::site_certificate::SiteCertificate;
use crate::sites::model::site_kind::SiteKind;

/// Everything `create_site` needs, already validated.
///
/// Every field is a type that cannot hold an invalid value, so no caller can
/// pass a domain and an account in the wrong order and no operation has to
/// re-parse a string it was handed (rules/rust.md "Validation first": once an
/// `AccountName` or a `Domain` exists, it is valid).
///
/// `enable_site` and `disable_site` take the same input, and deliberately so:
/// disabling re-renders the vhost from the suspended template and enabling
/// re-renders the site's own, so both need to know what the site IS. Keeping
/// the state in the rendered file instead of in a marker beside it means a
/// half-finished operation is completed by running the same command again,
/// which is what makes the panel's retry safe.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CreateSiteInput {
    /// The owning account.
    pub account: AccountName,
    /// The primary domain.
    pub domain: Domain,
    /// Additional hostnames served by the same site.
    pub aliases: Vec<Domain>,
    /// What serves the content.
    pub kind: SiteKind,
    /// The installed certificate, when the site has one.
    ///
    /// `None` renders a plain-HTTP vhost; `Some` renders the redirect plus the
    /// TLS block, both serving the one rendered body.
    pub certificate: Option<SiteCertificate>,
}
