//! Asking the daemon's own checker whether this build can sign.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::adapter::DistroAdapter;
use maran_templates::bind::dnssec_probe::DnssecProbe;

use crate::dns::dns_host::DnsHost;
use crate::safe_write::model::Validator;

/// The name of the throwaway probe file, in the directory that holds the real
/// configuration.
const PROBE_FILE_NAME: &str = "dnssec-probe.conf";

/// Whether this host's daemon build accepts a `dnssec-policy` statement.
///
/// **Observation, not a version table.** The statement exists from BIND 9.16 and
/// the oldest supported release branch is unverified here, so the answer is asked
/// of the thing that decides: the daemon's own `named-checkconf`, run at the real
/// path over a probe configuration stating the one policy `named.conf` states
/// (`DnssecProbe`, whose policy line a test pins to the real file's). A BIND
/// without the statement refuses the whole file it meets it in, which is why the
/// real configuration cannot state it unconditionally.
///
/// Read-only apart from the throwaway file, which is written through the
/// config-write protocol and removed whatever the verdict.
///
/// **A probe that could not run is `false`.** The checker refusing the policy,
/// the checker not starting, outrunning its deadline, the file not being
/// writable and the probe not rendering are one answer: DNSSEC is hidden. The
/// other reading, `true`, would offer signing on a daemon nobody asked.
#[must_use]
pub fn probe_dnssec_support(host: &dyn DnsHost, distro: &dyn DistroAdapter) -> bool {
    let Ok(probe) = DnssecProbe {}.render_config() else {
        return false;
    };

    let target = Path::new(AgentPaths::DNS_CONFIG_DIRECTORY).join(PROBE_FILE_NAME);
    let Some(target_text) = target.to_str() else {
        return false;
    };

    let checker = distro.dns_checkconf_binary();
    host.probe_config(
        &target,
        &probe,
        &Validator {
            program: &checker,
            arguments: &[target_text],
            class: CallClass::Validate,
        },
    )
    .is_ok()
}

#[cfg(test)]
#[path = "../tests/dns/probe_dnssec_support_tests.rs"]
mod tests;
