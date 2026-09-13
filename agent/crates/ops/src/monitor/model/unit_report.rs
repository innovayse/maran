//! What the service manager says about one unit, as properties.

/// The property separator `systemctl show` writes: `Key=Value`, one per line.
const PROPERTY_SEPARATOR: char = '=';

/// The property naming whether the unit file was found and loaded.
const LOAD_STATE: &str = "LoadState";

/// The property naming whether the unit is up.
const ACTIVE_STATE: &str = "ActiveState";

/// The property naming what the unit is doing within its active state.
const SUB_STATE: &str = "SubState";

/// The property listing the units that can start this one on demand.
const TRIGGERED_BY: &str = "TriggeredBy";

/// The suffix of a socket unit's name.
const SOCKET_SUFFIX: &str = ".socket";

/// The four properties this area asks the service manager for, in one answer.
///
/// `systemctl show` is asked rather than `is-active`, and the reason is the
/// whole design of this area: `is-active` answers one word about the service
/// and cannot say that a SOCKET is holding the listening descriptor on its
/// behalf. The unit's own `TriggeredBy` is the property that says so, and it
/// arrives in the same call rather than being guessed at from the unit's name.
///
/// `show` is also the subcommand that answers about a unit that does not exist
/// instead of refusing: it exits zero and reports `LoadState=not-found`, which
/// is what lets this area tell "not installed on this host" from "the service
/// manager could not be reached" — two situations `is-active`'s non-zero exit
/// runs together.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnitReport {
    /// `LoadState`: `loaded`, `not-found`, `masked`, `error`.
    pub load_state: String,
    /// `ActiveState`: `active`, `inactive`, `failed`, `activating`,
    /// `deactivating`, `reloading`.
    pub active_state: String,
    /// `SubState`: the unit-type-specific word behind the active state —
    /// `running`, `dead`, `listening`, `exited`.
    pub sub_state: String,
    /// `TriggeredBy`: the units that start this one on demand, space
    /// separated, and empty for a unit nothing triggers.
    pub triggered_by: String,
}

impl UnitReport {
    /// Reads `systemctl show`'s `Key=Value` lines.
    ///
    /// Infallible on purpose. A property the service manager did not print
    /// comes back empty, and an empty `ActiveState` is classified as "not
    /// known" further up rather than as an outage — the only direction a
    /// missing answer is allowed to move a monitor. Failing here instead would
    /// turn a systemd release that renames a property into an alert storm.
    ///
    /// A value containing `=` survives: the line is split at the FIRST
    /// separator only, because the key never contains one and the value may.
    #[must_use]
    pub fn parse(shown: &str) -> Self {
        let mut report = Self {
            load_state: String::new(),
            active_state: String::new(),
            sub_state: String::new(),
            triggered_by: String::new(),
        };

        for line in shown.lines() {
            let Some((key, value)) = line.split_once(PROPERTY_SEPARATOR) else {
                continue;
            };

            match key.trim() {
                LOAD_STATE => report.load_state = value.trim().to_owned(),
                ACTIVE_STATE => report.active_state = value.trim().to_owned(),
                SUB_STATE => report.sub_state = value.trim().to_owned(),
                TRIGGERED_BY => report.triggered_by = value.trim().to_owned(),
                _ => {}
            }
        }

        report
    }

    /// The socket units that can start this one on demand.
    ///
    /// `TriggeredBy` lists every unit that can trigger this one, which for a
    /// timer-started service is a `.timer`. Only a socket answers the question
    /// this area asks — "is something listening on this service's behalf right
    /// now" — so the list is narrowed to sockets here rather than by the
    /// caller, and a service triggered only by a timer correctly reports no
    /// socket at all.
    #[must_use]
    pub fn triggering_sockets(&self) -> Vec<&str> {
        self.triggered_by
            .split_whitespace()
            .filter(|unit| unit.ends_with(SOCKET_SUFFIX))
            .collect()
    }
}

#[cfg(test)]
#[path = "../../tests/monitor/unit_report_tests.rs"]
mod tests;
