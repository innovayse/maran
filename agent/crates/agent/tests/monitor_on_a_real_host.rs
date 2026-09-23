//! Host and service readings taken from the real kernel of each supported
//! family, which is the only place two of `ops::monitor`'s decisions are
//! exercised at all.
//!
//! The area's unit tests parse committed captures of `/proc`, and those pin the
//! FORMAT. They cannot pin two other things, and this suite is the only place
//! either is reached:
//!
//! - **The four `/proc` paths, and the available-versus-free choice.** The paths
//!   are string literals no structure rule inspects: a reading taken from the
//!   real kernel fails outright if one is wrong, so the metrics call succeeding
//!   is what closes them. And `filesystem_usage` measures used space as
//!   `f_blocks - f_bavail` — AVAILABLE, not free — so that the figure counts
//!   only what a hosting account could actually write; the root reserve is 5%
//!   of the filesystem that no customer will ever get. That argument lives in a
//!   doc comment, and the assertion here is what holds it up.
//!
//!   What this suite does NOT settle, said plainly because an earlier version
//!   of this comment claimed the opposite: the `f_frsize`-over-`f_bsize` choice
//!   is unobservable here. Both fields are 4096 on both polygon roots, so no
//!   assertion taken on these images can tell them apart.
//! - **A real per-family parse.** Only `/proc/net/dev` is namespaced, so the
//!   container's captures of `meminfo`, `stat` and `loadavg` are the HOST
//!   kernel's whatever image they were taken in — Docker cannot give alma9 its
//!   own kernel. So the committed fixtures are one kernel's format twice, and
//!   the only per-family signal available anywhere is that the parse succeeds
//!   against whatever each image's userland and kernel actually emit here.
//!
//! The service statuses are read through the polygon's `systemctl` stand-in,
//! which is a MODEL of an init system and is documented as one: a container has
//! none. What that model can settle is the agent's own classification — that a
//! unit the manager calls inactive is reported Stopped, that one waiting behind
//! a listening socket is reported Unknown and never Stopped, and that the two
//! are distinguishable. What it cannot settle is that systemd's real vocabulary
//! is these words; the committed `systemctl show` captures in `ops::monitor`
//! are what stand behind that.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;

use std::path::{Path, PathBuf};
use std::process::Command;

use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::monitor::{
    MachineIdentity, PrimaryInterface, ProcessMonitorHost, ServiceState, ServiceStatus,
    SftpJailStatus, get_accounts_disk_usage, get_host_metrics, get_server_fingerprint_inputs,
    get_service_statuses, get_sftp_jail_status,
};

use polygon_account::PolygonAccount;

/// Where the polygon's `systemctl` stand-in keeps one file per unit.
///
/// It MUST match `STATE_DIRECTORY` in `docker/polygon/systemctl-stand-in.sh`.
/// Written out here rather than read from the script, because a test that
/// derived the path from the thing under test could not notice the two coming
/// apart — and a suite whose overrides land nowhere would report every unit in
/// its default state and assert that happily.
const UNIT_STATE_DIRECTORY: &str = "/run/polygon-units";

/// Position of the web server in `DistroAdapter::managed_units`' fixed order.
const WEB_SERVER: usize = 0;

/// Position of the OpenSSH server in that same order.
const SSH: usize = 3;

/// How much a figure may differ from `df`'s before the two are answering
/// different questions.
///
/// One percent. They should agree exactly — both read `statvfs` on the same
/// filesystem — so the tolerance is for a write landing between the two
/// readings and nothing else. In particular it is far tighter than the reserve
/// that separates `f_bfree` from `f_bavail`, which is 5% of the filesystem by
/// default: that is the difference the used-bytes assertion below exists to
/// catch, and a tolerance that absorbed it would catch nothing.
const DF_TOLERANCE: f64 = 0.01;

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// Every managed unit's status, in the adapter's fixed order.
///
/// # Panics
///
/// Panics when the service manager cannot be reached, which in the polygon
/// means the stand-in is not installed where the adapter says it is.
fn statuses() -> Vec<ServiceStatus> {
    get_service_statuses(&ProcessMonitorHost::new(), polygon_distro())
        .unwrap_or_else(|error| panic!("the service manager must answer in the polygon: {error}"))
}

/// Asks the stand-in to change a unit's state, the way the agent never does.
///
/// The agent has no operation that starts or stops a managed unit — this area
/// is read-only by design — so the state is changed from OUTSIDE it, which is
/// what makes the reading a reading rather than an echo.
///
/// # Panics
///
/// Panics when the stand-in refuses.
fn ask_manager(verb: &str, unit: &str) {
    let answered = Command::new(polygon_distro().service_manager())
        .args([verb, unit])
        .output()
        .expect("the polygon image installs a service manager");
    assert!(
        answered.status.success(),
        "the stand-in must accept `{verb} {unit}`: {}",
        String::from_utf8_lossy(&answered.stderr)
    );
}

/// The path of one of a unit's state files in the stand-in's directory.
fn unit_state(unit: &str, suffix: &str) -> PathBuf {
    Path::new(UNIT_STATE_DIRECTORY).join(format!("{unit}{suffix}"))
}

#[test]
#[ignore = "reads this host's real kernel statistics: polygon only"]
fn every_metric_parses_against_this_familys_own_kernel_and_lands_in_its_range() {
    PolygonAccount::require_polygon();

    // That this call SUCCEEDS is itself the assertion that closes the four
    // `/proc` path literals: a wrong path is not a wrong number, it is a read
    // that fails, and the area refuses to report an unreadable statistic as
    // zero. Nothing else in the project reads those four paths for real.
    let metrics = get_host_metrics(&ProcessMonitorHost::new())
        .unwrap_or_else(|error| panic!("this host's statistics must be readable: {error}"));

    assert!(
        (0.0..=100.0).contains(&metrics.cpu_percent),
        "a utilisation percentage across all cores must land in 0..=100, got {}",
        metrics.cpu_percent
    );

    // Memory and disk must be non-zero: a host with no memory and no disk is
    // not a host, so a zero here is a parse that silently found nothing rather
    // than a machine in an unusual state.
    assert!(metrics.memory.total_bytes > 0, "a host has memory");
    assert!(metrics.memory.used_bytes > 0, "some of it is in use");
    assert!(
        metrics.memory.used_bytes <= metrics.memory.total_bytes,
        "used memory cannot exceed installed memory: {:?}",
        metrics.memory
    );

    assert!(metrics.root_filesystem.total_bytes > 0, "a host has a disk");
    assert!(metrics.root_filesystem.used_bytes > 0, "some of it is used");
    assert!(
        metrics.root_filesystem.used_bytes <= metrics.root_filesystem.total_bytes,
        "used disk cannot exceed the filesystem: {:?}",
        metrics.root_filesystem
    );

    // Load is allowed to be zero on a quiet host, so what is asserted is that
    // three finite non-negative numbers came back rather than a NaN from a
    // field read at the wrong offset.
    for (name, value) in [
        ("1m", metrics.load.one_minute),
        ("5m", metrics.load.five_minutes),
        ("15m", metrics.load.fifteen_minutes),
    ] {
        assert!(
            value.is_finite() && value >= 0.0,
            "the {name} load average must be a non-negative number, got {value}"
        );
    }

    // The counters are summed over the physical interfaces with loopback
    // skipped. A container gets its own network namespace with one interface in
    // it, and bringing that interface up costs a few packets — so zero here
    // means the sum found no interface at all, not a quiet one.
    assert!(
        metrics.network.received_bytes + metrics.network.transmitted_bytes > 0,
        "the interface counters must have found an interface; a container \
         started with --network none has none to find: {:?}",
        metrics.network
    );
}

#[test]
#[ignore = "compares this host's statvfs answer against df's: polygon only"]
fn the_root_filesystem_is_measured_against_what_an_account_can_actually_write() {
    PolygonAccount::require_polygon();

    // `usage_of` reports `used_bytes` as `f_blocks - f_bavail`, deliberately
    // NOT `df`'s own `Used` column (`f_blocks - f_bfree`). The two differ by
    // the root reserve — 5% of the filesystem by default — and the doc comment
    // argues at length for the available-based figure: the reserve is room no
    // hosting account will ever get, so a gauge built on `f_bfree` tells an
    // operator they have space their customers cannot use.
    //
    // That argument had no test. The obvious one — comparing `total_bytes`
    // against `df --output=size` — cannot supply it, and this file used to
    // claim it did: `total_bytes` contains neither `f_bfree` nor `f_bavail`, so
    // swapping them leaves it untouched. Measured: the `f_bavail -> f_bfree`
    // mutant left all five monitor tests green.
    //
    // So the comparison is against `df`'s AVAILABLE column instead. `size -
    // avail` is `f_blocks - f_bavail` computed by a separate implementation of
    // the same statvfs question, which is what makes it evidence rather than a
    // restatement — and it is exactly the quantity the mutant moves, by the
    // whole reserve.
    let reported = get_host_metrics(&ProcessMonitorHost::new())
        .expect("this host's statistics must be readable")
        .root_filesystem;

    let df = Command::new("df")
        .args(["-B1", "--output=size,avail", "/"])
        .output()
        .expect("the polygon image installs df");
    assert!(df.status.success(), "df must answer about /");
    let printed = String::from_utf8_lossy(&df.stdout);
    let figures: Vec<u64> = printed
        .lines()
        .nth(1)
        .map(|line| {
            line.split_whitespace()
                .filter_map(|word| word.parse().ok())
                .collect()
        })
        .unwrap_or_default();
    let [size, available] = figures.as_slice() else {
        panic!("df must print a size and an available figure in bytes, printed: {printed:?}");
    };

    // The assertion the reserve makes load-bearing.
    let expected_used = size.saturating_sub(*available);
    let used_difference = reported.used_bytes.abs_diff(expected_used) as f64 / expected_used as f64;
    assert!(
        used_difference <= DF_TOLERANCE,
        "used bytes must be measured against what an account can write — \
         df says size {size} minus available {available} = {expected_used}, the \
         agent reports {}, off by {:.2}%. A gap of about the root reserve is \
         the signature of f_bfree where f_bavail belongs.",
        reported.used_bytes,
        used_difference * 100.0
    );

    // The total, which does equal df's Size. Kept because it is true and cheap,
    // and labelled with what it CANNOT distinguish so nobody reads more into it
    // later: `f_frsize` against `f_bsize` is invisible on both polygon roots,
    // where the two fields are both 4096. Nothing here closes that choice, and
    // saying so is better than a comment implying otherwise.
    let total_difference = reported.total_bytes.abs_diff(*size) as f64 / *size as f64;
    assert!(
        total_difference <= DF_TOLERANCE,
        "the reported total ({}) must match df's size ({size}); off by {:.2}%",
        reported.total_bytes,
        total_difference * 100.0
    );
}

#[test]
#[ignore = "changes a unit's state through the polygon stand-in: polygon only"]
fn a_unit_the_manager_calls_inactive_is_reported_stopped_and_a_running_one_running() {
    PolygonAccount::require_polygon();
    let unit = polygon_distro().nginx_service();

    // Before: the manager calls it active, and the agent says Running.
    ask_manager("start", unit);
    let running = &statuses()[WEB_SERVER];
    assert_eq!(running.unit, unit);
    assert_eq!(
        running.state,
        ServiceState::Running,
        "a unit the manager calls active must be reported Running: {running:?}"
    );

    // After: the SAME unit, the same call, a different answer. The stopped half
    // is the half that could not be written before the stand-in kept unit state
    // — it answered nothing at all, so every unit classified as Unknown and no
    // invocation of it could produce a Stopped.
    ask_manager("stop", unit);
    let stopped = &statuses()[WEB_SERVER];
    assert_eq!(
        stopped.state,
        ServiceState::Stopped,
        "a unit the manager calls inactive, with no socket behind it, must be \
         reported Stopped: {stopped:?}"
    );
    assert!(
        stopped.detail.contains("inactive"),
        "the detail must carry the manager's own word for it: {}",
        stopped.detail
    );

    // Restored, so the next test starts from the state this one found.
    ask_manager("start", unit);
    assert_eq!(statuses()[WEB_SERVER].state, ServiceState::Running);
}

#[test]
#[ignore = "models a socket-activated service through the polygon stand-in: polygon only"]
fn a_service_waiting_behind_a_listening_socket_is_unknown_and_never_stopped() {
    PolygonAccount::require_polygon();
    let service = polygon_distro().ssh_service();
    let socket = format!("{service}.socket");

    // The situation, exactly as the Debian family really presents it: the
    // ENABLED unit is the socket, and the service it fronts is inactive from
    // boot until the first connection — on a host whose SSH is listening and
    // completely healthy. Calling that "stopped" invents an SSH outage on every
    // freshly booted host of that family, and the panel's alerting mails an
    // operator about each one.
    std::fs::create_dir_all(UNIT_STATE_DIRECTORY).expect("the state directory must be writable");
    std::fs::write(unit_state(service, ".triggeredby"), format!("{socket}\n"))
        .expect("the stand-in's override must be writable");
    ask_manager("stop", service);
    ask_manager("start", &socket);

    let waiting = &statuses()[SSH];
    assert_eq!(waiting.unit, service);
    assert_eq!(
        waiting.state,
        ServiceState::Unknown,
        "a service waiting behind a listening socket is not an outage: {waiting:?}"
    );
    assert_ne!(
        waiting.state,
        ServiceState::Stopped,
        "and it must never be reported as one"
    );
    assert!(
        waiting.detail.contains(&socket),
        "the detail must name the socket that is listening for it: {}",
        waiting.detail
    );

    // And the pair that makes the assertion above mean something: the SAME
    // inactive service, with nothing listening on its behalf, IS stopped.
    // Without this, `Unknown` could simply be what this area answers for every
    // inactive unit, and the test above would be asserting nothing.
    ask_manager("stop", &socket);
    let down = &statuses()[SSH];
    assert_eq!(
        down.state,
        ServiceState::Stopped,
        "an inactive service with no listening socket behind it is stopped: {down:?}"
    );

    // Restored, including the override file, so the next test starts from the
    // state this one found.
    let _ = std::fs::remove_file(unit_state(service, ".triggeredby"));
    ask_manager("start", service);
    ask_manager("start", &socket);
}

#[test]
#[ignore = "creates a real account and measures its real home: polygon only"]
fn each_hosting_account_is_reported_with_what_it_occupies_and_nothing_else_is() {
    let account = PolygonAccount::create("polymonitorone");

    // A file of a size the test chose, so the reported figure is compared
    // against something rather than merely being non-zero.
    let planted = account.home().join("occupies.bin");
    let bytes = vec![0_u8; 128 * 1024];
    std::fs::write(&planted, &bytes).expect("the account's home must be writable");
    // Owned by the account, like anything a customer would have put there —
    // the walk must count what the ACCOUNT occupies, not only what root left.
    std::os::unix::fs::chown(
        &planted,
        Some(account.ids().uid()),
        Some(account.ids().gid()),
    )
    .expect("the planted file must belong to the account");

    let usage = get_accounts_disk_usage(&ProcessMonitorHost::new(), polygon_distro())
        .expect("the password database must be readable");

    let reported = usage
        .iter()
        .find(|row| row.account == *account.name())
        .unwrap_or_else(|| {
            panic!(
                "the account must be reported: {:?}",
                usage
                    .iter()
                    .map(|row| row.account.as_str())
                    .collect::<Vec<_>>()
            )
        });
    assert!(
        reported.used_bytes >= bytes.len() as u64,
        "the reported usage ({}) must include the {} bytes planted in the home",
        reported.used_bytes,
        bytes.len()
    );

    // And the set is HOSTING accounts, not every row of the password database.
    // A report that included root or the web server user would have the panel
    // showing quotas for identities no customer owns.
    for name in ["root", polygon_distro().web_server_user()] {
        assert!(
            !usage.iter().any(|row| row.account.as_str() == name),
            "{name} is not a hosting account and must not be reported"
        );
    }
}

/// The polygon image's real `/etc/ssh/sshd_config` is exactly what
/// `installer/lib/86-sftp.sh` wrote when the image was built (see the module
/// doc on `sftp_on_a_real_host.rs`), so this is the one place the check reads
/// a file this project did not compose for the test itself.
#[test]
#[ignore = "reads this host's real /etc/ssh/sshd_config: polygon only"]
fn the_installer_written_block_is_intact_on_a_real_host() {
    PolygonAccount::require_polygon();

    let status = get_sftp_jail_status(&ProcessMonitorHost::new(), polygon_distro())
        .unwrap_or_else(|error| panic!("the real sshd_config must be readable: {error}"));

    assert_eq!(
        status,
        SftpJailStatus::Intact,
        "the installer's own block, freshly written when this image was built, must read as intact"
    );
}

/// **The failing direction, on the real file the daemon this host runs would
/// itself read.** Every other case for this logic is exercised in
/// `ops::monitor::model::sftp_jail_status`'s unit tests against text the test
/// composed; this is the one place the REMOVAL is done to the file a real
/// `sshd` on a real supported family actually has, which is the closest this
/// project's test suites come to reproducing the README's defect end to end.
///
/// The file is restored with a plain `cp`, never a rename, so a second run of
/// this suite in the same container starts from the installer's own content
/// rather than from whatever the previous run left as a temporary path.
#[test]
#[ignore = "rewrites this host's real /etc/ssh/sshd_config: polygon only"]
fn removing_the_block_by_hand_is_noticed_and_restoring_it_clears_the_finding() {
    PolygonAccount::require_polygon();

    let path = polygon_distro().sshd_config_path();
    let original =
        std::fs::read_to_string(path).expect("the real sshd_config must be readable to back it up");

    let intact = get_sftp_jail_status(&ProcessMonitorHost::new(), polygon_distro())
        .expect("the real sshd_config must be readable");
    assert_eq!(
        intact,
        SftpJailStatus::Intact,
        "the suite must start from the installer's own intact block"
    );

    // The exact defect the README describes: the WHOLE block gone, by hand,
    // with nothing else in the file touched. Removed as one contiguous span
    // from its own begin marker line to its own end marker line — a partial
    // removal that left the indented directives outside any `Match` would
    // turn them into GLOBAL settings and break every other login on this
    // container, which would not be testing this check any more.
    let begin_at = original
        .find("# BEGIN Maran SFTP")
        .expect("the installer's begin marker must be in the image's real sshd_config");
    let end_at = original[begin_at..]
        .find("# END Maran SFTP")
        .map(|offset| begin_at + offset)
        .expect("the installer's end marker must be in the image's real sshd_config");
    let end_of_line = original[end_at..]
        .find('\n')
        .map_or(original.len(), |offset| end_at + offset + 1);
    let without_block = format!("{}{}", &original[..begin_at], &original[end_of_line..]);
    std::fs::write(path, &without_block).expect("the config must be writable as root");

    let drifted = get_sftp_jail_status(&ProcessMonitorHost::new(), polygon_distro());
    // Restore FIRST: a failed assertion below must not leave this container's
    // sshd_config broken for the next test in the binary.
    std::fs::write(path, &original).expect("the original config must be restorable");

    let SftpJailStatus::Drifted { missing } = drifted.expect("the config remains readable") else {
        panic!("removing the block by hand must be reported as drifted, not intact");
    };
    assert!(
        !missing.is_empty(),
        "a drifted finding must name at least one missing thing"
    );

    let restored = get_sftp_jail_status(&ProcessMonitorHost::new(), polygon_distro())
        .expect("the restored config must be readable");
    assert_eq!(
        restored,
        SftpJailStatus::Intact,
        "restoring the installer's exact content must clear the finding"
    );
}

/// The fingerprint inputs are read from this host's own files, not from a capture.
///
/// The unit tests parse committed captures, which pin the FORMAT and cannot pin two other things
/// this suite is the only place to reach. First, the machine-id path comes from the distro adapter
/// and is a string no structure rule inspects: a read against the real filesystem fails outright if
/// it is wrong on this family. Second, `/proc/net/route` is the live routing table, so the
/// lowest-metric tie-break runs against whatever the kernel actually has rather than a fixture
/// somebody chose.
///
/// **Both values are asserted as PRESENT here, and that is deliberate.** A polygon image has a
/// machine-id and a default route, so absence would mean the read failed rather than that the host
/// genuinely lacks them — which is exactly the confusion `MachineIdentity::NotAvailable` exists to
/// prevent, and cannot be demonstrated on an image that has both.
///
/// Both arms are real on the polygon and BOTH are checked against the file the adapter names: the
/// ubuntu24 image carries a machine-id and the alma9 image does not, which is why an earlier
/// version of this test — one that simply declared "a polygon image has a machine-id" — failed on
/// half the matrix while asserting nothing about the code. What it asserts now is the agreement
/// between the file and the answer, in both directions: an id reported while the file holds none,
/// or none reported while the file holds one, is the read having gone somewhere else.
#[test]
#[ignore = "reads this host's real machine-id and routing table: polygon only"]
fn the_fingerprint_inputs_are_read_from_this_hosts_own_files() {
    PolygonAccount::require_polygon();

    let host = ProcessMonitorHost;
    let distro = polygon_distro();
    // What the answer is CHECKED AGAINST: the file the adapter names, observed here rather than
    // assumed. An earlier version of this test asserted that a polygon image simply has a
    // machine-id, and that was false on the alma9 image while true on ubuntu24 — so the suite
    // failed on a fact about the image instead of a fact about the code, and the comment above it
    // already said as much. Reading the path lets absence be a legitimate answer AND still catches
    // the defect this test exists for: a read that went somewhere else.
    let machine_id_file = std::fs::read_to_string(distro.machine_id_path()).ok();
    let file_holds_an_id = machine_id_file
        .as_deref()
        .map(str::trim)
        .is_some_and(|contents| !contents.is_empty());

    let inputs = get_server_fingerprint_inputs(&host, distro)
        .expect("this host's machine-id and routing table are readable");

    match inputs.machine_id {
        MachineIdentity::Present(ref id) => {
            assert!(
                file_holds_an_id,
                "a machine-id was reported while {} holds none — the read found some other file",
                distro.machine_id_path()
            );
            // Asserted as a VALUE and not merely as non-empty: systemd writes 32 lowercase hex
            // digits, so a path that happened to read some other file would be caught here rather
            // than passing as "something was read".
            assert_eq!(id.len(), 32, "machine-id is 32 hex digits, read `{id}`");
            assert!(
                id.chars()
                    .all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase()),
                "machine-id is lowercase hex, read `{id}`"
            );
        }
        MachineIdentity::NotAvailable => {
            // Absence is a real state, not a failure: a container image often carries no
            // /etc/machine-id at all. What would be a failure is absence reported while the file
            // is sitting there, which is what this asserts.
            assert!(
                !file_holds_an_id,
                "{} holds a machine-id and the agent reported none — the read went to the wrong path",
                distro.machine_id_path()
            );
        }
        // The type is #[non_exhaustive] so a third state can be added without breaking callers;
        // this arm fails rather than passing over one, because a state nobody asserted on is a
        // state this suite would otherwise report as fine.
        other => panic!("unhandled machine-id state: {other:?}"),
    }

    match inputs.primary_interface {
        PrimaryInterface::Present(ref name) => {
            assert!(!name.is_empty(), "an interface name is never empty");
        }
        PrimaryInterface::NotAvailable => {
            panic!("a polygon container has a default route, so absence means the parse failed")
        }
        other => panic!("unhandled primary-interface state: {other:?}"),
    }
}
