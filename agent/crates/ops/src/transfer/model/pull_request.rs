//! Everything one pull of an account's files needs.

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::bandwidth_limit::BandwidthLimit;
use maran_agent_core::validation::system::name::AccountName;

use crate::transfer::model::pull_phase::PullPhase;
use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::model::source_endpoint::SourceEndpoint;

/// One pull: where from, with what key, into which account's home, in which
/// phase and how fast.
///
/// Every field is a validated type. There is no `Clone` and no `serde`
/// implementation, because the credential must not be copied.
#[derive(Debug)]
pub struct PullRequest {
    /// The registered source.
    pub endpoint: SourceEndpoint,
    /// The key to connect with and the host key to require.
    pub credential: SourceCredential,
    /// The account on THIS host whose home receives the files.
    pub account: AccountName,
    /// The account's home on the source.
    pub source_home: RemotePath,
    /// Which pass this is.
    pub phase: PullPhase,
    /// The most to read per second, or `None` for no limit.
    pub bandwidth: Option<BandwidthLimit>,
    /// How many bytes the caller expects to receive, or `0` when unknown; only
    /// the free-space check reads it.
    pub expected_bytes: u64,
}
