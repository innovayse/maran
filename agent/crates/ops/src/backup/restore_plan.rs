//! What a restore's verification decided.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::model::manifest_database::ManifestDatabase;

/// What verification decided: which databases to replace, and from which
/// manifest entry.
///
/// Built only by `prepare_restore`, which is the only thing that has checked any of
/// it. A value of this type means every refusal in steps 1 to 5 has already
/// been made — which is what lets `perform` be read as "and now the
/// irreversible half", with no verification mixed into it.
pub(super) struct RestorePlan {
    /// Every database to replace, paired with the manifest entry that
    /// describes its dump, in the manifest's own order.
    pub(super) databases: Vec<(DatabaseName, ManifestDatabase)>,
}
