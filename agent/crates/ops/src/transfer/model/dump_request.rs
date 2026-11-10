//! Everything one database transfer needs.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::remote_database_name::RemoteDatabaseName;

use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::model::source_endpoint::SourceEndpoint;

/// One database to dump from a source and load into an existing database here.
///
/// There is no `Clone`, because the credential must not be copied.
#[derive(Debug)]
pub struct DumpRequest {
    /// The registered source.
    pub endpoint: SourceEndpoint,
    /// The key to connect with and the host key to require.
    pub credential: SourceCredential,
    /// The database on the source.
    pub source_database: RemoteDatabaseName,
    /// The existing database on this host, with its owner's prefix.
    pub target_database: DatabaseName,
    /// The most dump bytes to accept.
    pub max_bytes: u64,
}
