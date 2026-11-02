//! The one reading of an observed FTPS daemon that four responses are built
//! from.

use maran_ops::ftps::{FtpsState, ListenMode};

use crate::proto::{DisableFtpsOk, EnableFtpsOk, GetFtpsStatusOk, ReloadFtpsTlsOk, TlsEnforcement};

/// The ten facts every FTPS response carries, computed once from one
/// [`FtpsState`].
///
/// # Why this type exists
///
/// `EnableFtpsOk`, `DisableFtpsOk`, `GetFtpsStatusOk` and `ReloadFtpsTlsOk` are
/// the same ten fields under four names, because all four rpcs answer with
/// what the daemon is doing and a panel that has just enabled or disabled should
/// not have to make a second call to find out. prost generates four DISTINCT
/// structs for them, so no single function can return all four and there is no
/// compile-time coupling to be had that way. What there is instead is this: the
/// reading — which of the state's optional values are present, what an absent
/// one means, and how a listen mode becomes a boolean — happens exactly once,
/// here, and the four constructions below are field copies with no decision left
/// in them.
///
/// # What an absent value means, decided once
///
/// [`FtpsState`] carries four of its facts as `Option`, and each `None` means a
/// different thing:
///
/// - **No certificate**: the caller did not name a hostname, so nothing was
///   asked. It reports as absent-and-not-self-signed with an empty path, which
///   a caller that named no hostname must not read as "there is no material".
/// - **No passive range**: there is no live configuration on this host yet, so
///   there is no range in force. Zero, which is not a port number.
/// - **No listen mode**: the same, and it reports as `ipv4_only = false` — the
///   dual-stack default, which is what the next enable will write.
/// - **No forced-TLS answer**: the agent could not resolve the two
///   `force_local_*_ssl` keys, which is what no live configuration looks like.
///   It reports as [`TlsEnforcement::NotObserved`], a state of its own beside
///   "enforced" and "not enforced". The deprecated `forced_tls` bool, still
///   written for a panel that reads it, folds it into `false` — an unknown
///   must never read as "encryption is enforced".
pub struct FtpsStatusFields {
    /// True when the service manager reports the FTPS unit active.
    running: bool,
    /// True when a LOCAL connect to the control port returned a `220` greeting.
    control_port_answered: bool,
    /// True when certificate material exists for the hostname that was asked
    /// about.
    certificate_present: bool,
    /// True when that material is the agent's own self-signed placeholder.
    certificate_is_self_signed: bool,
    /// Where the material lives, or would have to be placed.
    certificate_path: String,
    /// Lowest port of the passive range the LIVE configuration carries.
    passive_port_min: u32,
    /// Highest port of that range.
    passive_port_max: u32,
    /// True when the live configuration is the IPv4-only fallback.
    ipv4_only: bool,
    /// DEPRECATED mirror of `tls_enforcement`: true only for `Enforced`.
    forced_tls: bool,
    /// Whether the live configuration forces TLS on both channels, with "not
    /// observed" a state of its own.
    tls_enforcement: TlsEnforcement,
}

impl FtpsStatusFields {
    /// Reads `state` into the ten values the contract carries.
    #[must_use]
    pub fn from_state(state: FtpsState) -> Self {
        let (certificate_present, certificate_is_self_signed, certificate_path) = state
            .certificate
            .map_or((false, false, String::new()), |certificate| {
                (
                    certificate.installation.is_present(),
                    certificate.installation.is_self_signed_placeholder(),
                    certificate.certificate_path,
                )
            });

        Self {
            running: state.running,
            control_port_answered: state.control_port_answered,
            certificate_present,
            certificate_is_self_signed,
            certificate_path,
            passive_port_min: u32::from(state.passive_port_min.unwrap_or_default()),
            passive_port_max: u32::from(state.passive_port_max.unwrap_or_default()),
            ipv4_only: state.listen_mode == Some(ListenMode::Ipv4Only),
            // The deprecated bool keeps its old reading, an unresolvable
            // configuration answering "this agent cannot say TLS is forced"
            // rather than certifying it; the enum beside it says which of
            // "off" and "unknown" that false was.
            forced_tls: state.forced_tls.unwrap_or_default(),
            tls_enforcement: match state.forced_tls {
                Some(true) => TlsEnforcement::Enforced,
                Some(false) => TlsEnforcement::NotEnforced,
                None => TlsEnforcement::NotObserved,
            },
        }
    }

    /// The observation as `EnableFtps` answers with it.
    #[must_use]
    pub fn into_enable_ok(self) -> EnableFtpsOk {
        EnableFtpsOk {
            running: self.running,
            control_port_answered: self.control_port_answered,
            certificate_present: self.certificate_present,
            certificate_is_self_signed: self.certificate_is_self_signed,
            certificate_path: self.certificate_path,
            passive_port_min: self.passive_port_min,
            passive_port_max: self.passive_port_max,
            ipv4_only: self.ipv4_only,
            forced_tls: self.forced_tls,
            tls_enforcement: self.tls_enforcement as i32,
        }
    }

    /// The observation as `DisableFtps` answers with it.
    #[must_use]
    pub fn into_disable_ok(self) -> DisableFtpsOk {
        DisableFtpsOk {
            running: self.running,
            control_port_answered: self.control_port_answered,
            certificate_present: self.certificate_present,
            certificate_is_self_signed: self.certificate_is_self_signed,
            certificate_path: self.certificate_path,
            passive_port_min: self.passive_port_min,
            passive_port_max: self.passive_port_max,
            ipv4_only: self.ipv4_only,
            forced_tls: self.forced_tls,
            tls_enforcement: self.tls_enforcement as i32,
        }
    }

    /// The observation as `GetFtpsStatus` answers with it.
    #[must_use]
    pub fn into_status_ok(self) -> GetFtpsStatusOk {
        GetFtpsStatusOk {
            running: self.running,
            control_port_answered: self.control_port_answered,
            certificate_present: self.certificate_present,
            certificate_is_self_signed: self.certificate_is_self_signed,
            certificate_path: self.certificate_path,
            passive_port_min: self.passive_port_min,
            passive_port_max: self.passive_port_max,
            ipv4_only: self.ipv4_only,
            forced_tls: self.forced_tls,
            tls_enforcement: self.tls_enforcement as i32,
        }
    }

    /// The observation as `ReloadFtpsTls` answers with it.
    #[must_use]
    pub fn into_reload_ok(self) -> ReloadFtpsTlsOk {
        ReloadFtpsTlsOk {
            running: self.running,
            control_port_answered: self.control_port_answered,
            certificate_present: self.certificate_present,
            certificate_is_self_signed: self.certificate_is_self_signed,
            certificate_path: self.certificate_path,
            passive_port_min: self.passive_port_min,
            passive_port_max: self.passive_port_max,
            ipv4_only: self.ipv4_only,
            forced_tls: self.forced_tls,
            tls_enforcement: self.tls_enforcement as i32,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/services/ftps/ftps_status_fields_tests.rs"]
mod tests;
