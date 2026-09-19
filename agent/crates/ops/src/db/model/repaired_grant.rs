//! One grant the repair rewrote from a pattern into a literal name.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;

/// A grant whose stored database pattern was replaced by the escaped form.
///
/// The two names are validated types because a row only reaches this struct
/// after both decoded as names this agent could itself have created — which is
/// the whole of the repair's predicate.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RepairedGrant {
    /// The database the grant was always meant to name, and now names alone.
    pub database: DatabaseName,

    /// The user the grant belongs to.
    pub user: DbUserName,

    /// Other databases on this server that the OLD pattern also matched.
    ///
    /// **The one piece of evidence this operation can produce**, and it is
    /// evidence of exposure rather than of use: a name in this list is a
    /// database that the repaired credential could have read and written for as
    /// long as the unescaped row stood. An empty list means the wildcard reached
    /// nothing else *at the moment of the repair* — it does not mean it never
    /// did, because a matching database may have been created and dropped in
    /// between, and a grant is matched at connection time.
    ///
    /// Nothing here says whether the reach was USED. That question is answered
    /// by a query log, which is off on a default install.
    pub also_matched: Vec<String>,
}
