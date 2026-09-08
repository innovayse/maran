//! The `BackupService` handler, its error mapping, and the units its rpcs
//! would otherwise inline.
//!
//! The service is the boundary where a destination stops being a wire message
//! and becomes a place on this machine — which is why the refusal of a remote
//! destination lives here and not in `ops`. `ops::backup`'s four operations
//! take a `LocalBackupRoot` and nothing else, so an S3 destination cannot even
//! be named down there and a refusal written there would be code no call site
//! could reach. Here it is reachable, and it is the only thing standing
//! between a bucket an operator configured and a backup quietly written to the
//! local disk instead.

pub mod backup_service;
pub mod backup_status;
pub mod channel_progress_sink;
pub mod channel_restore_sink;
pub mod db_host_catalog;
pub mod home_group;
pub mod remote_destination_refused;
pub mod to_backup_info;
pub mod validated_destination;
pub mod validated_restore;
