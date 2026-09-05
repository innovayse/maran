//! The real nftables the firewall suite loads its rulesets into.
//!
//! Everything `ops::firewall` does is a claim about what a kernel will make of
//! a file, and the central one — that `nft -f` is ADDITIVE, so a re-applied
//! ruleset must delete its own table first — is invisible to a fake host by
//! construction: a fake replays whatever the code hands it. That is not a
//! hypothetical. The first design of this area passed every fake-host test
//! while leaving a denied port open.
//!
//! The container has its own network namespace, so every table this fixture
//! loads and deletes is the container's own and no rule reaches the host that
//! is running the tests.
//!
//! It needs `--cap-add NET_ADMIN` (or `--privileged`). Without it `nft` cannot
//! initialise its cache at all, and [`PolygonFirewall::start`] says so and
//! fails rather than letting a suite report green against a kernel it never
//! reached.

use std::path::Path;
use std::process::{Command, Output};

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::{DistroAdapter, adapter_for, detect};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The address family both of the agent's tables live in.
const TABLE_FAMILY: &str = "inet";

/// The table the agent's rules live in.
pub const RULES_TABLE: &str = "maran";

/// The table runtime bans live in.
pub const BANS_TABLE: &str = "maran_bans";

/// A clean nftables state on the polygon host, restored when the test ends.
pub struct PolygonFirewall {
    /// Where `nft` lives on this family.
    distro: &'static dyn DistroAdapter,
}

impl PolygonFirewall {
    /// Refuses to go on unless this process is root inside a polygon image
    /// whose kernel will actually take a table.
    ///
    /// The second half is the part worth having. `nft` in a container without
    /// `CAP_NET_ADMIN` fails with `cache initialization failed: Operation not
    /// permitted` on every invocation — so a suite started wrongly would see
    /// every apply fail and could report that as a code defect. This says which
    /// it is, once, before any test runs.
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent, the process is not root, or
    /// the kernel will not accept a table.
    pub fn start() -> Self {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "these tests load real nftables rulesets into the kernel and must run \
             only inside a polygon container: {POLYGON_MARKER} is not set. \
             See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "loading a ruleset requires root"
        );

        let distro = adapter_for(
            detect()
                .expect("a polygon image is a supported host")
                .family,
        );
        let fixture = Self { distro };

        let probed = fixture.nft(&["list", "ruleset"]);
        assert!(
            probed.status.success(),
            "nft must be able to reach the kernel. A container needs \
             --cap-add NET_ADMIN (or --privileged) for this; without it every \
             apply fails and the failure reads like a code defect. nft said: {}",
            String::from_utf8_lossy(&probed.stderr)
        );

        fixture.reset();
        fixture
    }

    /// Runs `nft` with `arguments` and returns everything it said.
    pub fn nft(&self, arguments: &[&str]) -> Output {
        Command::new(self.distro.nft_binary())
            .args(arguments)
            .output()
            .expect("the polygon image installs nft")
    }

    /// The kernel's own listing of one of the agent's tables.
    ///
    /// Asked of the KERNEL and not of the rendered file: the file says what was
    /// meant, and this says what is loaded. Every ordering claim in this area is
    /// about the second.
    ///
    /// # Panics
    ///
    /// Panics when the table is not loaded, which is a failure of whatever was
    /// supposed to have applied it.
    pub fn listing(&self, table: &str) -> String {
        let listed = self.nft(&["list", "table", TABLE_FAMILY, table]);
        assert!(
            listed.status.success(),
            "table {TABLE_FAMILY} {table} must be loaded: {}",
            String::from_utf8_lossy(&listed.stderr)
        );

        String::from_utf8_lossy(&listed.stdout).into_owned()
    }

    /// How many lines of `listing` contain `needle`.
    ///
    /// The measurement the review's M3 reported in: a removed rule that is
    /// still live counts 1 where it should count 0, and a duplicated ruleset
    /// counts 2 where it should count 1.
    #[must_use]
    pub fn count(listing: &str, needle: &str) -> usize {
        listing.lines().filter(|line| line.contains(needle)).count()
    }

    /// Takes both tables out of the kernel and both files off the disk.
    ///
    /// Called before the first test and again when the fixture is dropped, so
    /// no test starts from another's state and none is left behind — the two
    /// files are at fixed, agent-owned paths, so they are shared mutable state
    /// between every test in this suite (rules/testing.md).
    pub fn reset(&self) {
        for table in [RULES_TABLE, BANS_TABLE] {
            // Ignored: "no such table" is the ordinary case, and it is the
            // state this function wants.
            let _ = self.nft(&["delete", "table", TABLE_FAMILY, table]);
        }

        for path in [
            AgentPaths::nftables_ruleset_path(),
            AgentPaths::nftables_bans_path(),
        ] {
            remove(path);
        }
    }
}

impl Drop for PolygonFirewall {
    /// Restores the container's firewall, whether the test passed or panicked.
    fn drop(&mut self) {
        self.reset();
    }
}

/// Removes `path`, reporting rather than failing.
///
/// A panic in `drop` during another panic aborts the process and hides the real
/// failure, so nothing here may assert.
fn remove(path: &Path) {
    if let Err(error) = std::fs::remove_file(path)
        && error.kind() != std::io::ErrorKind::NotFound
    {
        eprintln!(
            "the polygon ruleset {} could not be removed: {error}",
            path.display()
        );
    }
}
