//! The checks a restore's scope passes before `ops` is asked anything.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_ops::backup::RestoreScope;
use maran_ops::sites::SitePaths;

use crate::proto::{AgentError, RestoreScope as WireRestoreScope, restore_scope};
use crate::services::wire::invalid_input::invalid_input;

/// Turns the wire's scope into the closed [`RestoreScope`] the operation takes
/// (issue #160).
///
/// An absent scope, and a scope with no target set, is the whole account: the
/// only meaning a request from before the field existed can have, so an older
/// panel keeps restoring exactly what it always did.
///
/// **The caller never names a path.** A database arrives as a name that must
/// decode to `account` (the same check the allowed list passes, so a neighbour's
/// database cannot be aimed at). A site arrives as a domain, validated as a
/// domain (the shape check without the reserved set, because the site already
/// exists and a name that was creatable when it was made must stay restorable),
/// and the directory it occupies is derived here from the place site creation put
/// it (`SitePaths::document_root_in_home`) — the sole spelling of
/// that location, so a restore replaces exactly the directory the site is served
/// from and cannot be talked into any other.
///
/// # Errors
///
/// The wire error for a database name that does not decode to `account`, a
/// name that is not a domain, or a derived path the
/// agent's relative-path type refuses (which no accepted domain produces).
pub fn validated_scope(
    scope: Option<&WireRestoreScope>,
    account: &AccountName,
) -> Result<RestoreScope, AgentError> {
    match scope.and_then(|scope| scope.target.as_ref()) {
        None => Ok(RestoreScope::Whole),
        Some(restore_scope::Target::DatabaseName(name)) => DatabaseName::decode(account, name)
            .map(RestoreScope::Database)
            .ok_or_else(|| {
                invalid_input(
                    "the database to restore is not a database name this account owns".to_owned(),
                )
            }),
        Some(restore_scope::Target::SiteDomain(domain)) => {
            let domain =
                Domain::parse_existing(domain).map_err(|error| invalid_input(error.to_string()))?;
            let directory = SitePaths::document_root_in_home(&domain);
            let directory = RelativePath::parse(&directory.to_string_lossy())
                .map_err(|error| invalid_input(error.to_string()))?;

            Ok(RestoreScope::Directory(directory))
        }
    }
}

#[cfg(test)]
#[path = "../../tests/services/backup/validated_scope_tests.rs"]
mod tests;
