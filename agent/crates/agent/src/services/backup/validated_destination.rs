//! Turning the wire's destination into the one place this agent can write.

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::web::s3_bucket::S3Bucket;
use maran_agent_core::validation::web::s3_object_prefix::S3ObjectPrefix;
use maran_agent_core::validation::web::s3_region::S3Region;

use crate::proto::{AgentError, BackupDestination, BackupDestinationKind};
use crate::services::backup::remote_destination_refused::remote_destination_refused;
use crate::services::wire::invalid_input::invalid_input;

/// The scheme an S3 endpoint must carry.
///
/// Checked here as well as at `S3ObjectStoreHost`'s construction, because this
/// agent never reaches that construction: a destination refused as unreachable
/// must still be refused as insecure first, or the day the remote arm lands is
/// the day an `http://` endpoint stops being noticed.
const REQUIRED_SCHEME: &str = "https://";

/// Revalidates the destination an rpc carries and answers the local root the
/// operation will use.
///
/// The API validated this already. The agent validates it again because the
/// agent is root and the API is not (rules/security.md item 1), and because
/// every field here ends up either in a path this process writes to or in a
/// signed request it makes.
///
/// # What this returns, and why it is not the caller's path
///
/// A [`LocalBackupRoot`] — always the agent's own default,
/// `/var/backups/maran`. `BackupDestination::path` is refused when it is not
/// empty for a local destination rather than being joined onto that root, and
/// the reason is that `ListBackups` carries no destination at all: a per-call
/// subdirectory would produce artifacts that no listing, and therefore no
/// retention, could ever see. A field the agent would ignore is a field a
/// caller believes in, so it is refused rather than dropped.
///
/// # Why the S3 arm is validated and then refused
///
/// In that order deliberately. A malformed bucket, region or prefix is
/// [`ErrorCode::InvalidInput`](crate::proto::ErrorCode::InvalidInput) whether or
/// not this agent could store to it, and an operator typing a destination
/// wants to hear about the typo. Only a destination that is otherwise valid
/// reaches [`remote_destination_refused`], which says the honest thing: the
/// request is fine and this build has nowhere to put it.
///
/// The two credential fields are read into [`SecretString`] and dropped with
/// this function. Nothing here logs them, nothing writes them, and the type
/// prints `«redacted»` from both `Debug` and `Display`, so a future `?err` on a
/// value that carries one cannot leak it.
///
/// # Errors
///
/// - The wire error for a missing destination message, an unknown or
///   unspecified kind, a local destination carrying S3 fields or a path, or an
///   S3 destination whose bucket, region, prefix, endpoint or credentials the
///   agent will not accept.
/// - [`remote_destination_refused`]'s error for a well formed S3 destination.
pub fn validated_destination(
    destination: Option<&BackupDestination>,
) -> Result<LocalBackupRoot, AgentError> {
    let destination = destination
        .ok_or_else(|| invalid_input("the request carries no destination".to_owned()))?;

    match BackupDestinationKind::try_from(destination.kind) {
        Ok(BackupDestinationKind::Local) => validated_local(destination),
        Ok(BackupDestinationKind::S3) => {
            validated_s3(destination)?;
            Err(remote_destination_refused())
        }
        Ok(BackupDestinationKind::Unspecified) | Err(_) => Err(invalid_input(
            "the destination names no storage kind this agent knows".to_owned(),
        )),
    }
}

/// Checks a local destination and answers the root it resolves to.
///
/// Every S3 field must be at its proto3 default. A destination that names a
/// bucket AND a local kind is a caller that has confused itself, and answering
/// it with a successful local backup is the failure mode this whole file
/// exists to prevent — one field at a time.
///
/// # Errors
///
/// The wire error naming the field that is not empty.
fn validated_local(destination: &BackupDestination) -> Result<LocalBackupRoot, AgentError> {
    if !destination.path.is_empty() {
        return Err(invalid_input(
            "a local destination carries no path: the agent's backup root is its own".to_owned(),
        ));
    }
    if !destination.s3_bucket.is_empty()
        || !destination.s3_region.is_empty()
        || !destination.s3_endpoint.is_empty()
        || !destination.s3_access_key_id.is_empty()
        || !destination.s3_secret_access_key.is_empty()
        || destination.s3_path_style
    {
        return Err(invalid_input(
            "a local destination carries no S3 fields".to_owned(),
        ));
    }

    Ok(LocalBackupRoot::default())
}

/// Checks every field of an S3 destination, so that a typo is reported as a
/// typo before the destination is refused as unreachable.
///
/// # Errors
///
/// The wire error for a bucket, region, object prefix, endpoint or credential
/// pair the agent will not accept.
fn validated_s3(destination: &BackupDestination) -> Result<(), AgentError> {
    S3Bucket::parse(&destination.s3_bucket).map_err(|error| invalid_input(error.to_string()))?;
    S3Region::parse(&destination.s3_region).map_err(|error| invalid_input(error.to_string()))?;
    S3ObjectPrefix::parse(&destination.path).map_err(|error| invalid_input(error.to_string()))?;

    if !destination.s3_endpoint.is_empty()
        && !destination
            .s3_endpoint
            .to_ascii_lowercase()
            .starts_with(REQUIRED_SCHEME)
    {
        return Err(invalid_input(
            "the destination's endpoint is not https".to_owned(),
        ));
    }

    // Held as secrets for the length of this check and dropped with it. The
    // emptiness of a credential is the only thing asked about it here: the
    // provider is the authority on whether it is the right one, and this agent
    // never reaches the provider.
    let access_key_id = SecretString::new(destination.s3_access_key_id.clone());
    let secret_access_key = SecretString::new(destination.s3_secret_access_key.clone());
    if access_key_id.expose().is_empty() || secret_access_key.expose().is_empty() {
        return Err(invalid_input(
            "an S3 destination carries both credential fields".to_owned(),
        ));
    }

    Ok(())
}

#[cfg(test)]
#[path = "../../tests/services/backup/validated_destination_tests.rs"]
mod tests;
