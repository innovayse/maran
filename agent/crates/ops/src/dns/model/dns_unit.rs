//! The systemd commands this area runs against its own unit.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// The argument vectors for the DNS unit. Constants, so no value from a request
/// can reach a command line: the unit name is `AgentPaths::DNS_UNIT` and every
/// vector ends its options before it.
pub struct DnsUnit;

impl DnsUnit {
    /// Restarts the unit, which is how a changed configuration is loaded.
    pub const RESTART: [&'static str; 3] = ["restart", END_OF_OPTIONS, AgentPaths::DNS_UNIT];

    /// Asks whether the unit is active; the answer is the exit status.
    pub const IS_ACTIVE: [&'static str; 3] = ["is-active", END_OF_OPTIONS, AgentPaths::DNS_UNIT];

    /// Asks whether the unit starts at boot; the answer is the exit status.
    pub const IS_ENABLED: [&'static str; 3] = ["is-enabled", END_OF_OPTIONS, AgentPaths::DNS_UNIT];

    /// Puts the unit in the boot sequence without starting it.
    pub const ENABLE: [&'static str; 3] = ["enable", END_OF_OPTIONS, AgentPaths::DNS_UNIT];

    /// Stops the unit and takes it out of the boot sequence in one command.
    pub const DISABLE_NOW: [&'static str; 4] =
        ["disable", "--now", END_OF_OPTIONS, AgentPaths::DNS_UNIT];

    /// Stops the unit and nothing else.
    pub const STOP: [&'static str; 3] = ["stop", END_OF_OPTIONS, AgentPaths::DNS_UNIT];
}
