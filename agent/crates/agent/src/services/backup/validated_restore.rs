//! Everything a restore rpc carries, once the agent has checked it.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::{AgentError, RestoreBackupRequest};
use crate::services::backup::validated_destination::validated_destination;
use crate::services::wire::invalid_input::invalid_input;

/// The number of characters a hex-encoded SHA-256 digest has.
const DIGEST_LENGTH: usize = 64;

/// A restore request the agent has revalidated, field by field.
///
/// A bundle and not four parameters threaded through the handler, for the
/// reason the service anatomy gives (rules/rust.md): the handler stays the
/// three steps, and every check that could have been inlined has a name and a
/// test. It is built only by [`ValidatedRestore::from_request`], so holding one
/// is proof that each field was checked.
///
/// It deliberately does NOT flatten into `ops::restore_backup`'s parameter
/// list. That operation takes named parameters precisely so an empty
/// `allowed_databases` or a blank `expected_sha256` cannot be a field somebody
/// forgot to fill; this type is the checking step, not a request object handed
/// down whole.
pub struct ValidatedRestore {
    /// The local root the artifact rests under — always the agent's own.
    pub root: LocalBackupRoot,

    /// The backup to restore from.
    pub backup_id: BackupId,

    /// The digest the panel recorded for the artifact, lowercase hex.
    pub expected_sha256: String,

    /// The databases the panel still knows this account owns.
    pub allowed_databases: Vec<DatabaseName>,
}

impl ValidatedRestore {
    /// Revalidates `request` against `account`.
    ///
    /// `account` is passed in already validated rather than re-parsed here,
    /// because the database names are checked AGAINST it: a name is accepted
    /// only if it decodes to this account, so a request cannot name a
    /// neighbour's database and have it dropped and reloaded on the way past.
    ///
    /// The digest is required and is required to be lowercase hex of the right
    /// length. An empty value is refused rather than read as "do not check":
    /// the comparison it feeds is the one thing standing between a restore and
    /// bytes somebody else chose, and a check that can be turned off by leaving
    /// a field blank is a check that will be.
    ///
    /// # Errors
    ///
    /// The wire error for a destination the agent will not accept (including a
    /// remote one), an id that is not a backup id, a digest that is not 64
    /// lowercase hex characters, or a database name that does not decode to
    /// `account`.
    pub fn from_request(
        request: &RestoreBackupRequest,
        account: &AccountName,
    ) -> Result<Self, AgentError> {
        let root = validated_destination(request.destination.as_ref())?;
        let backup_id = BackupId::parse(&request.backup_id)
            .map_err(|error| invalid_input(error.to_string()))?;
        let expected_sha256 = validated_digest(&request.expected_sha256)?;

        let mut allowed_databases = Vec::with_capacity(request.allowed_databases.len());
        for name in &request.allowed_databases {
            let database = DatabaseName::decode(account, name).ok_or_else(|| {
                invalid_input(
                    "an allowed database is not a database name this account owns".to_owned(),
                )
            })?;
            allowed_databases.push(database);
        }

        Ok(Self {
            root,
            backup_id,
            expected_sha256,
            allowed_databases,
        })
    }
}

/// Checks that `candidate` is a hex-encoded SHA-256 digest, lowercase.
///
/// Lowercase specifically, and not a case-insensitive compare: the agent writes
/// the digest lowercase, `ops::restore_backup` compares the two as strings, and
/// accepting `AB…` here would produce a refusal at the comparison that reads
/// like a corrupted artifact. One spelling, checked where the value enters.
///
/// # Errors
///
/// The wire error for a digest of the wrong length or holding anything outside
/// `0-9a-f`.
fn validated_digest(candidate: &str) -> Result<String, AgentError> {
    if candidate.len() != DIGEST_LENGTH {
        return Err(invalid_input(format!(
            "the expected checksum is not {DIGEST_LENGTH} characters"
        )));
    }
    if !candidate
        .bytes()
        .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
    {
        return Err(invalid_input(
            "the expected checksum is not lowercase hexadecimal".to_owned(),
        ));
    }

    Ok(candidate.to_owned())
}

#[cfg(test)]
#[path = "../../tests/services/backup/validated_restore_tests.rs"]
mod tests;
