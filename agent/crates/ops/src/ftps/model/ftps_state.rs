//! What the host is actually doing about FTPS, as opposed to what it was asked
//! to do.

use crate::ftps::model::listen_mode::ListenMode;
use crate::ssl::CertificateState;

/// The observed state of this host's FTPS daemon.
///
/// Every field here is the answer to a question asked of the MACHINE — the
/// service manager, a TCP connection, the bytes of the file the daemon reads —
/// and never a restatement of what the panel asked for. A status that reported
/// the panel's own intent is the defect this repository has found most often
/// (rules/testing.md, "A check must be able to observe what it reports on"), and
/// for this feature it is worse than usual: a daemon serving FTPS with forced
/// TLS switched off answers every liveness question perfectly.
///
/// # Why four of the fields are optional
///
/// Because "the host did not answer that" is a real state and the alternative is
/// to invent a value for it. There is no live configuration on a host where FTPS
/// has never been enabled, so there is no listening mode and no passive range to
/// report; and [`Self::certificate`] is `None` when the caller asked no question
/// about a hostname. A zero port or a defaulted [`ListenMode`] in those places
/// would read to every consumer as an observation, which is exactly the lie this
/// type exists to avoid.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FtpsState {
    /// Whether the service manager reports the FTPS unit active.
    ///
    /// One of two liveness questions, and on its own not the interesting one:
    /// the unit is `Type=simple`, so it counts as started the moment the process
    /// has been forked.
    pub running: bool,
    /// Whether a TCP connection to the control port was greeted with `220`.
    ///
    /// The question the configuration file cannot answer, and the one that
    /// catches a certificate whose key does not match or a port something else
    /// already holds — both of which leave a unit the service manager is happy
    /// with.
    pub control_port_answered: bool,
    /// What is installed for the hostname the caller asked about.
    ///
    /// `None` when no hostname was supplied, which is the case for a plain
    /// "is FTPS up on this host" reading. Never a fabricated absent state: the
    /// paths in a [`CertificateState`] are derived from a domain, and there is
    /// no domain here to derive them from.
    pub certificate: Option<CertificateState>,
    /// Whether the LIVE configuration still forces TLS on both the login and
    /// the data connection.
    ///
    /// `Some(true)` only when the last occurrence of BOTH `force_local_logins_ssl`
    /// and `force_local_data_ssl` is `YES`; `None` when the file does not carry
    /// both keys at all.
    ///
    /// This field exists because of a specific, measured failure: those two keys
    /// are the ones a single appended line can switch off, on a file that still
    /// parses and a daemon that still starts, and neither validation layer of an
    /// enable can see it — a daemon with forced TLS disabled is active and greets
    /// on its control port perfectly. Reading them back from the file the daemon
    /// serves, at their LAST occurrence, is the only thing in the agent that can.
    /// Without it the panel would report a feature whose entire promise is forced
    /// TLS as healthy while passwords crossed the wire in the clear.
    pub forced_tls: Option<bool>,
    /// Lowest port of the passive range the LIVE configuration carries.
    ///
    /// Read back from the file the daemon serves, taking the LAST occurrence of
    /// the key — see [`get_ftps_status`](crate::ftps::get_ftps_status). `None` when the
    /// live file does not carry it.
    pub passive_port_min: Option<u16>,
    /// Highest port of the passive range the LIVE configuration carries.
    pub passive_port_max: Option<u16>,
    /// The listening mode the host is in.
    ///
    /// Reported by [`enable_ftps`](crate::ftps::enable_ftps) as the mode it
    /// probed and rendered, and by
    /// [`get_ftps_status`](crate::ftps::get_ftps_status) as the mode the LIVE
    /// file carries. `None` when that file carries neither of the two pairs the
    /// template writes, which — since the agent is the only writer of it — means
    /// either that FTPS has never been enabled here or that the file is not one
    /// this agent produced.
    pub listen_mode: Option<ListenMode>,
}
