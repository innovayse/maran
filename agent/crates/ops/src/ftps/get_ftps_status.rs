//! What this host is actually doing about FTPS — asked of the host, never of the
//! panel's own record.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::adapter::DistroAdapter;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::ftps_state::FtpsState;
use crate::ftps::model::ftps_unit::FtpsUnit;
use crate::ftps::model::listen_mode::ListenMode;

/// The control port the daemon serves, and the one the template writes.
///
/// Explicit FTPS on 21 (`AUTH TLS`, RFC 4217) rather than implicit FTPS on 990:
/// one port to open, and the port an operator already understands. Not
/// configurable, so it is a constant here and not a field of anything.
const CONTROL_PORT: u16 = 21;

/// The greeting a healthy FTP control connection opens with.
const GREETING: &str = "220";

/// Reads the FTPS daemon's state off the host: the service manager's answer, the
/// control port's answer, and the configuration file the daemon actually serves.
///
/// Read-only. It starts nothing, stops nothing and writes nothing, so it is safe
/// to call on any host at any time — including one where FTPS has never been
/// enabled, which answers with a state full of `None` rather than an error.
///
/// # Every one of these is a question asked of the machine
///
/// That is the whole point of the function, and it is worth being explicit about
/// because the alternative is the defect this repository keeps finding: a status
/// that restates what the panel decided agrees with the panel whatever the host
/// is doing, and a green light produced that way is worse than none. So:
/// `running` is the service manager's own `is-active`; `control_port_answered` is
/// a TCP connection and a greeting; the listening mode and the passive range come
/// out of the bytes on disk that the daemon reads; and `certificate` — when a
/// hostname is supplied — is three file names in the certificate store.
///
/// # The last occurrence of a key, never the first
///
/// vsftpd's parser takes the **LAST** occurrence of a key, measured with controls
/// on both families on 2026-09-09. So this read does too, and the distinction is
/// not academic. The agent writes this file whole and is its only writer, which is
/// what makes reading its own decision back a sound question at all; but where
/// the file and the daemon COULD disagree — because something appended to it —
/// a read that took the first occurrence would report the pair the agent
/// rendered while the daemon obeyed the pair appended after it. That is a check
/// that agrees with the panel's own decision no matter what the host is doing.
///
/// The same rule is what makes an appended `force_local_logins_ssl=NO` visible:
/// it is the one line that can switch off the forced TLS this feature exists to
/// enforce, on a file that still parses and a daemon that still starts, so
/// neither validation layer of an enable can see it. What sees it is a read that
/// answers with the last occurrence — and a live file carrying either listen pair
/// twice, or a key the template writes once appearing more than once, is by
/// construction not a file this agent wrote.
///
/// # Errors
///
/// Returns [`FtpsError::ConfigUnreadable`] when the configuration file exists and
/// cannot be read, or when certificate material exists and cannot be read.
/// Returns [`FtpsError::SpawnFailed`] when the service manager cannot be started
/// at all — a host whose `systemctl` is missing cannot be reported on, and
/// answering `running: false` there would be an I/O failure wearing the shape of
/// an observation.
pub fn get_ftps_status(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    hostname: Option<&Domain>,
) -> Result<FtpsState, FtpsError> {
    let active = host.run(distro.service_manager(), &FtpsUnit::IS_ACTIVE)?;
    let greeting = host.control_port_greeting(CONTROL_PORT);
    let live = host.read_config(Path::new(AgentPaths::VSFTPD_CONFIG_PATH))?;

    let certificate = match hostname {
        Some(domain) => Some(host.certificate_state(domain)?),
        None => None,
    };

    Ok(FtpsState {
        running: active.status == 0,
        control_port_answered: greeting.is_some_and(|line| line.starts_with(GREETING)),
        certificate,
        forced_tls: live.as_deref().and_then(forced_tls_of),
        passive_port_min: live
            .as_deref()
            .and_then(|config| last_value(config, "pasv_min_port"))
            .and_then(|value| value.parse().ok()),
        passive_port_max: live
            .as_deref()
            .and_then(|config| last_value(config, "pasv_max_port"))
            .and_then(|value| value.parse().ok()),
        listen_mode: live.as_deref().and_then(listen_mode_of),
    })
}

/// The value of `key`'s LAST occurrence in `config`, or `None` when the key is
/// not there.
///
/// Last and not first, because that is what vsftpd's own parser does — see the
/// section on the function above. Comment lines are skipped, and a line without
/// an `=` is not a setting.
fn last_value<'a>(config: &'a str, key: &str) -> Option<&'a str> {
    // Reversed and then the FIRST match, which is the last occurrence in the
    // file — the one vsftpd obeys.
    config
        .lines()
        .rev()
        .map(str::trim)
        .filter(|line| !line.starts_with('#'))
        .filter_map(|line| line.split_once('='))
        .find(|(name, _)| name.trim() == key)
        .map(|(_, value)| value.trim())
}

/// Whether `config` still forces TLS on both the login and the data connection,
/// or `None` when it does not carry both keys.
///
/// The last occurrence of each, like every other read here, and here the rule is
/// not a detail: an appended `force_local_logins_ssl=NO` is precisely the line
/// this answer exists to catch, and a read that took the first occurrence would
/// answer `Some(true)` for it — agreeing with what the agent rendered while the
/// daemon obeyed the line below it.
fn forced_tls_of(config: &str) -> Option<bool> {
    let logins = last_value(config, "force_local_logins_ssl")?;
    let data = last_value(config, "force_local_data_ssl")?;
    Some(logins == "YES" && data == "YES")
}

/// Which of the template's two listening pairs `config` carries, or `None` when
/// it carries neither.
///
/// The pairs are exact and mutually exclusive, and they are pinned byte-for-byte
/// by the template goldens. `None` is the honest answer for a file that says
/// something else: FTPS has never been enabled here, or this is not a file this
/// agent wrote.
fn listen_mode_of(config: &str) -> Option<ListenMode> {
    match (
        last_value(config, "listen"),
        last_value(config, "listen_ipv6"),
    ) {
        (Some("NO"), Some("YES")) => Some(ListenMode::DualStack),
        (Some("YES"), Some("NO")) => Some(ListenMode::Ipv4Only),
        _ => None,
    }
}

#[cfg(test)]
#[path = "../tests/ftps/get_ftps_status_tests.rs"]
mod tests;
