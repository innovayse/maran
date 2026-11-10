//! Which `rndc` command makes the daemon serve a zone's new file.

use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_reading::ZoneReading;

/// How the daemon is told about a zone it was just given a file for.
///
/// [`Self::Add`] when it does not know the zone, [`Self::Modify`] when it knows
/// the zone and the statement it holds differs from the one now wanted, and
/// [`Self::Reload`] when only the file changed. `modzone` carries the statement,
/// `reload` does not, and a reload cannot move a zone between the plain and the
/// signed directory or switch its policy — which is why the choice is made from
/// the mode the daemon reports and not from what the panel last sent.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum ZoneLoad {
    /// `rndc addzone`: a zone the daemon does not know.
    Add,
    /// `rndc modzone`: a known zone whose statement changes.
    Modify,
    /// `rndc reload <zone>`: a known zone whose statement is what it was.
    Reload,
}

impl ZoneLoad {
    /// Picks the command from what the daemon says about the zone.
    ///
    /// `observed` is the mode the daemon's own configuration for the zone shows
    /// (`None` when it could not be read), `wanted` the mode being installed. A
    /// mode that could not be read is a statement that may differ, so it takes
    /// the command that always works on a known zone: `modzone` states the whole
    /// statement, where `reload` would silently keep a stale one.
    #[must_use]
    pub(crate) fn choose(
        reading: &ZoneReading,
        observed: Option<SigningMode>,
        wanted: SigningMode,
    ) -> Self {
        match (reading, observed) {
            (ZoneReading::NotServed, _) => Self::Add,
            (ZoneReading::Served { .. }, Some(mode)) if mode == wanted => Self::Reload,
            (ZoneReading::Served { .. }, _) => Self::Modify,
        }
    }

    /// The `rndc` command words: the verb, the zone and, for the two that carry
    /// one, the statement.
    #[must_use]
    pub(crate) fn command<'a>(self, zone: &'a str, statement: &'a str) -> Vec<&'a str> {
        match self {
            Self::Add => vec!["addzone", zone, statement],
            Self::Modify => vec!["modzone", zone, statement],
            Self::Reload => vec!["reload", zone],
        }
    }
}

#[cfg(test)]
#[path = "../tests/dns/zone_load_tests.rs"]
mod tests;
