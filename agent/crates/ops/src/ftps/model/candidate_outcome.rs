//! What happened when the daemon was pointed at a candidate configuration.

/// The result of running vsftpd against a candidate file in the foreground for a
/// bounded time.
///
/// vsftpd has no `-t`: there is no mode in which it reads a configuration, says
/// whether it likes it, and exits zero. The only program that can judge this
/// file is the daemon itself, and the only way it answers is by living or dying —
/// so the outcome of the check is which of those it did, and this type is that
/// answer rather than an exit status.
///
/// The classification lives in the operation
/// ([`validate_candidate_config`](crate::ftps::validate_candidate_config)) and
/// not behind the host seam, so a test can drive both answers without a daemon.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CandidateOutcome {
    /// The daemon was still running when the deadline expired, and was killed.
    ///
    /// The accepted case. In standalone mode a vsftpd that has read the file,
    /// bound its socket and is serving is a vsftpd that had no objection.
    StillRunning,
    /// The daemon exited before the deadline, refusing the configuration.
    ///
    /// The refused case, whatever the status: measured on both families, a good
    /// configuration in the wrong mode also exits 2, which is why the check runs
    /// the daemon in standalone mode and reads only "did it stay up".
    Exited {
        /// Everything the daemon printed, which on the Debian family is nothing
        /// at all for every refusal it has (measured 2026-09-08).
        output: String,
    },
}
