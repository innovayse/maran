//! The package manager's dry run of a removal: its arguments and the reading of its answer.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;

use crate::removal_simulation::RemovalSimulation;
use crate::removal_simulation_failure::RemovalSimulationFailure;

/// The simulation of a package removal, so the agent can learn what the removal unit's
/// `remove -y` would take with it before it starts the unit (issue #527).
pub trait RemovalFacts: Send + Sync {
    /// The arguments of the dry run of removing `package`, run with
    /// [`PackageFacts::package_query_binary`](crate::PackageFacts::package_query_binary).
    ///
    /// A dry run changes nothing: `apt-get -s remove`, `dnf remove --assumeno` and
    /// `zypper remove --dry-run`. `package` is the adapter's own spelling for a validated app id
    /// and follows `--`, so it can never be read as an option.
    fn removal_simulation_arguments(&self, package: &str) -> Vec<String>;

    /// Reads the run of [`Self::removal_simulation_arguments`]: its exit `status` and `stdout`.
    ///
    /// The one place the exit status and the output grammar of a family's dry run are read.
    ///
    /// # Errors
    ///
    /// Returns a [`RemovalSimulationFailure`] for a status that is not a dry run's, and for
    /// output that holds no removal plan or one the reader does not understand. A failure is
    /// never an empty plan.
    fn read_removal_simulation(
        &self,
        status: i32,
        stdout: &str,
    ) -> Result<RemovalSimulation, RemovalSimulationFailure>;

    /// The arguments of the dry run of removing the module stream `stream` (issue #532), run with
    /// the same program as [`Self::removal_simulation_arguments`] and read by
    /// [`Self::read_removal_simulation`]. `dnf module remove --assumeno -- nodejs:20/common` on
    /// the RHEL family. A family without module streams answers an empty list and is never asked.
    fn nodejs_removal_simulation_arguments(&self, stream: &NodejsModuleStream) -> Vec<String>;
}
