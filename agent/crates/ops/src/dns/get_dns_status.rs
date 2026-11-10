//! What this host is actually doing about DNS — asked of the host, never of the
//! panel's own record.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::adapter::DistroAdapter;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::dns::model::dns_policy_observation::DnsPolicyObservation;
use crate::dns::model::dns_state::DnsState;
use crate::dns::model::dns_unit::DnsUnit;
use crate::dns::probe_dnssec_support::probe_dnssec_support;
use crate::dns::rndc_arguments::rndc_arguments;

/// The DNS port the daemon serves on. A protocol constant, not a platform fact.
pub(crate) const DNS_PORT: u16 = 53;

/// Observes the DNS daemon: the unit, the control channel, port 53 and the two
/// policy statements of the live configuration.
///
/// Read-only and idempotent, apart from the throwaway probe file that asks the
/// daemon's checker whether this build can sign (`probe_dnssec_support`, written
/// and removed each time, issue #452). The control channel is asked only while the unit is
/// active: a stopped unit cannot answer, and a spawn that can only fail is noise.
///
/// The configuration is read from the file the daemon is started against and its
/// statements are resolved the way the daemon resolves them — the LAST
/// occurrence of a statement wins — so a status that reports the policy reports
/// the bytes the daemon obeys and not the rendering the panel intended
/// (rules/rust.md "Reading a live config back").
///
/// # Errors
///
/// Returns [`DnsError::SpawnFailed`] when the service manager or `rndc` cannot be
/// started, [`DnsError::Bound`] when either outruns its deadline, and
/// [`DnsError::ConfigUnreadable`] when the live configuration exists and cannot
/// be read.
pub fn get_dns_status(
    host: &dyn DnsHost,
    distro: &dyn DistroAdapter,
) -> Result<DnsState, DnsError> {
    let service_manager = distro.service_manager();
    let active = host.run(&service_manager, &DnsUnit::IS_ACTIVE, CallClass::Query)?;
    let enabled = host.run(&service_manager, &DnsUnit::IS_ENABLED, CallClass::Query)?;
    let running = active.status == 0;

    let control_channel_answered = if running {
        let port = AgentPaths::DNS_CONTROL_PORT.to_string();
        let arguments = rndc_arguments(&port, &["status"]);
        let answer = host.run(&distro.dns_control_binary(), &arguments, CallClass::Query)?;
        answer.status == 0
    } else {
        false
    };

    let live = host.read_config(Path::new(AgentPaths::DNS_CONFIG_PATH))?;

    Ok(DnsState {
        running,
        control_channel_answered,
        port_53_answering: host.tcp_port_accepts(DNS_PORT),
        recursion_refused: recursion_refused_in(live.as_deref()),
        response_rate_limiting: rate_limiting_in(live.as_deref()),
        enabled_at_boot: enabled.status == 0,
        dnssec_supported: probe_dnssec_support(host, distro),
    })
}

/// Whether `config` refuses recursion, by its LAST `recursion` statement.
///
/// A readable configuration with no such statement is `NotInForce`, not
/// `NotObserved`: the daemon's own default for an absent `recursion` is to
/// recurse, so silence is the unsafe answer.
fn recursion_refused_in(config: Option<&str>) -> DnsPolicyObservation {
    let Some(config) = config else {
        return DnsPolicyObservation::NotObserved;
    };
    match last_statement_value(config, "recursion") {
        Some("no") => DnsPolicyObservation::InForce,
        _ => DnsPolicyObservation::NotInForce,
    }
}

/// Whether `config` carries a `rate-limit` block. Silence is `NotInForce`: the
/// daemon rate-limits nothing unless told to.
fn rate_limiting_in(config: Option<&str>) -> DnsPolicyObservation {
    let Some(config) = config else {
        return DnsPolicyObservation::NotObserved;
    };
    let present = config
        .lines()
        .map(str::trim)
        .filter(|line| !line.starts_with("//") && !line.starts_with('#'))
        .any(|line| line.starts_with("rate-limit") && line.contains('{'));
    if present {
        DnsPolicyObservation::InForce
    } else {
        DnsPolicyObservation::NotInForce
    }
}

/// The value of the last one-line `key value;` statement named `key`.
///
/// Reversed and then the FIRST match, which is the last occurrence in the file —
/// the one the daemon obeys. Comment lines are skipped, and the trailing `;` is
/// not part of the value.
fn last_statement_value<'a>(config: &'a str, key: &str) -> Option<&'a str> {
    config
        .lines()
        .rev()
        .map(str::trim)
        .filter(|line| !line.starts_with("//") && !line.starts_with('#'))
        .filter_map(|line| line.strip_suffix(';'))
        .filter_map(|statement| statement.split_once(char::is_whitespace))
        .find(|(name, _)| *name == key)
        .map(|(_, value)| value.trim())
}

#[cfg(test)]
#[path = "../tests/dns/get_dns_status_tests.rs"]
mod tests;
