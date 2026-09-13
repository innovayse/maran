//! Which site an operation acts on, when that is all it needs to know.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

/// The account and the domain that name one site.
///
/// Separate from [`super::create_site_input::CreateSiteInput`] because two
/// operations do not re-render anything and therefore have no business being
/// told what the site serves. `delete_site` removes the vhost from the agent's
/// own include directory, which is named from these two alone.
///
/// The split is not tidiness. Handed a `CreateSiteInput`, a caller with only
/// the identity has to INVENT the rest — a `Static` kind, no aliases, no
/// certificate — and those invented facts are usually false. They are harmless
/// only for exactly as long as the operation happens to ignore them, and the
/// day a deletion learns to take the site's certificate material with it, the
/// invented `certificate: None` is a lie it acts on. A type that cannot carry
/// the fields cannot carry wrong ones.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SiteIdentity {
    /// The owning account.
    pub account: AccountName,
    /// The primary domain.
    pub domain: Domain,
}
