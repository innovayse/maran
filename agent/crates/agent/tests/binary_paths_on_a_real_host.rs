//! Every absolute binary path the distro adapters declare, checked against the
//! filesystem of a real host of that family.
//!
//! The adapters' `*_binary()` answers are `AdapterBinary` values wrapping path
//! literals. The distro
//! crate's own adapter tests compare them to a second copy of the same strings
//! written into a table whose doc comment calls itself "a record of what was
//! verified on a real Debian host" — but that comparison never touches a
//! filesystem, so it cannot notice a path that is wrong, and it cannot notice a
//! tool the host does not have. It caught nothing when `/usr/bin/quota` existed
//! on neither polygon image, because a string equals a string either way.
//!
//! This suite is the assertion that CAN fail. It asks the adapter the same
//! question the agent asks it — `adapter_for(detect()?.family)`, the production path —
//! and then stats what comes back. A path moved to another directory, a tool
//! dropped from the image, or a family that puts its tools somewhere else all
//! fail here by name, on each family in turn, because the answer is taken from
//! the adapter rather than restated beside it.
//!
//! What it does NOT settle: that each program does what the agent expects of
//! it. `/usr/sbin/setquota` on these images is `docker/polygon/stand-ins/setquota-stand-in.sh`,
//! which accepts everything and does nothing — so what is proven for that one
//! entry is the PATH, not the behaviour, and the stand-in's own comment says so.
//! `/usr/bin/quota` is the real tool from the family's `quota` package.
//!
//! The second half of the suite closes the one claim about those programs that
//! only a real host can settle: that each one the agent hands `--` ahead of an
//! operand really reads it as the end of its options (`END_OF_OPTIONS`'s doc
//! comment states that as a precondition, "known to honour it on both
//! distribution families"). The probes name an account, a path or a table that
//! does not exist, so none of them changes anything; each is judged by whether the
//! program complained about an OPTION, and a control with an option that does not
//! exist proves the judgement can see such a complaint at all.
//!
//! UNOBSERVED HERE: `systemctl` and `setquota`. On the polygon images both are
//! stand-ins (`docker/polygon/stand-ins/`), shell scripts that model the tool, so
//! a probe against them would measure the script's argument loop and not the
//! program's. Their `--` is closed by the suites that drive the stand-ins and by
//! the committed captures of the real tools, not by this one.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;
use std::path::Path;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{AdapterBinary, DistroAdapter, adapter_for, detect};

/// The PHP version the polygon images ship.
const POLYGON_PHP_VERSION: &str = "8.3";

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// Refuses to go on unless this process is inside a polygon image.
///
/// A panic and not a quiet `return`: this suite is `#[ignore]`d, so the only way
/// to reach it is to ask for it by name, and a skip would then report as a pass
/// — which is the exact failure this suite exists to end (rules/testing.md: "no
/// tests found" is a failure, never a pass).
///
/// # Panics
///
/// Panics when the polygon marker is absent.
fn require_polygon() {
    let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
    assert!(
        !marker.is_empty(),
        "this suite checks the binary paths of a real host of a supported \
         family, and must run inside a polygon container: {POLYGON_MARKER} is \
         not set. See docker/README.md."
    );
}

/// The adapter for the family this container actually is.
///
/// Detection rather than a hard-coded adapter, so the suite exercises the same
/// pairing the agent makes at startup: an image whose `/etc/os-release` and
/// whose tool layout disagreed would be a real defect and is caught here.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// Each declared path, paired with the accessor that produced it.
///
/// The name is carried alongside so a failure says WHICH accessor is wrong
/// rather than only which path is missing; a bare list of strings would leave a
/// reader grepping for the literal.
///
/// Every accessor that names a program the agent starts is listed, the
/// service manager, the version-built php-fpm validator and `quotaon` included:
/// these are the values an `AdapterBinary` carries, and the type is what every
/// process seam in `ops` accepts, so a path wrong here is a program the agent
/// cannot start. Two accessors answer plain text and are listed all the same,
/// because the agent writes or hands on the path they name: `sh_binary` (the
/// shell a crontab line names) and `package_manager` (the program the php
/// install unit runs).
///
/// # Panics
///
/// Panics when [`POLYGON_PHP_VERSION`] is not a valid PHP version.
fn declared_binaries(distro: &'static dyn DistroAdapter) -> Vec<(&'static str, String)> {
    let php_version = PhpVersion::parse(POLYGON_PHP_VERSION).expect("a valid PHP version");

    vec![
        ("nginx_binary", distro.nginx_binary().to_string()),
        ("service_manager", distro.service_manager().to_string()),
        (
            "php_fpm_binary",
            distro.php_fpm_binary(&php_version).to_string(),
        ),
        ("openssl_binary", distro.openssl_binary().to_string()),
        (
            "mysql_client_binary",
            distro.mysql_client_binary().to_string(),
        ),
        ("useradd_binary", distro.useradd_binary().to_string()),
        ("userdel_binary", distro.userdel_binary().to_string()),
        ("usermod_binary", distro.usermod_binary().to_string()),
        ("pkill_binary", distro.pkill_binary().to_string()),
        ("pgrep_binary", distro.pgrep_binary().to_string()),
        ("setquota_binary", distro.setquota_binary().to_string()),
        ("quota_binary", distro.quota_binary().to_string()),
        ("quotaon_binary", distro.quotaon_binary().to_string()),
        ("id_binary", distro.id_binary().to_string()),
        ("chmod_binary", distro.chmod_binary().to_string()),
        ("chgrp_binary", distro.chgrp_binary().to_string()),
        ("chpasswd_binary", distro.chpasswd_binary().to_string()),
        ("passwd_binary", distro.passwd_binary().to_string()),
        ("getent_binary", distro.getent_binary().to_string()),
        ("crontab_binary", distro.crontab_binary().to_string()),
        ("sh_binary", distro.sh_binary().to_owned()),
        ("package_manager", distro.package_manager().to_owned()),
        ("nft_binary", distro.nft_binary().to_string()),
        ("tar_binary", distro.tar_binary().to_string()),
        ("gzip_binary", distro.gzip_binary().to_string()),
        (
            "database_dump_binary",
            distro.database_dump_binary().to_string(),
        ),
        ("vsftpd_binary", distro.vsftpd_binary().to_string()),
    ]
}

/// Every path a distro adapter declares names an executable file on this host.
///
/// The agent spawns these by absolute path and nothing else: a path that names
/// nothing is a request that fails at exec time, on a customer's server, in
/// whichever feature reaches it first.
#[test]
#[ignore = "requires a polygon container"]
fn every_declared_binary_path_names_an_executable_file() {
    require_polygon();
    let distro = polygon_distro();

    for (accessor, path) in declared_binaries(distro) {
        let metadata = std::fs::metadata(Path::new(&path)).unwrap_or_else(|error| {
            panic!(
                "DistroAdapter::{accessor}() declares {path}, and this host has \
                 nothing there ({error}). The agent execs that exact path: \
                 either the declaration is wrong for this family, or the \
                 polygon image is missing the package that installs it."
            )
        });
        assert!(
            metadata.is_file(),
            "DistroAdapter::{accessor}() declares {path}, which exists but is \
             not a regular file. The agent execs it."
        );
        assert!(
            metadata.permissions().mode() & 0o111 != 0,
            "DistroAdapter::{accessor}() declares {path}, which exists but has \
             no execute bit. The agent execs it."
        );
    }
}

/// A name no account on the polygon has, for the probes that must fail on the
/// operand and nothing else.
const ABSENT_ACCOUNT: &str = "maranabsent";

/// A path nothing is installed at.
const ABSENT_PATH: &str = "/maran-absent-path";

/// A table and a set the firewall does not have.
const ABSENT_TABLE: &str = "maran_absent_table";

/// A set name inside [`ABSENT_TABLE`].
const ABSENT_SET: &str = "maran_absent_set";

/// A member that is in no archive this suite makes.
const ABSENT_MEMBER: &str = "maran-absent-member";

/// An account name the account tools refuse for its colon, before they change
/// anything: the way to probe `useradd` with an operand and still create nobody.
const REFUSED_ACCOUNT_NAME: &str = "maran:absent";

/// An option no program in the suite has.
const BOGUS_OPTION: &str = "--maran-no-such-option";

/// The phrases the programs use to refuse an option, lower-cased.
///
/// Each is some family's wording for the same complaint: `getopt_long` says
/// "unrecognized option", shadow-utils and util-linux say "invalid option", and
/// some tools say "unknown option" or "illegal option". A program that is handed
/// `--` and reads it as an option says one of these; one that reads it as the end
/// of its options does not.
const OPTION_REFUSALS: [&str; 5] = [
    "unrecognized option",
    "unrecognised option",
    "invalid option",
    "unknown option",
    "illegal option",
];

/// One way the agent puts `--` between a program's options and its operands: the
/// program, what precedes the marker, and what follows it.
struct GuardedShape {
    /// The name of the call in the agent that has this shape, for a failure message.
    label: &'static str,
    /// The program, taken from the adapter exactly as the agent takes it.
    program: AdapterBinary,
    /// The arguments the agent places before the marker.
    options: Vec<String>,
    /// The arguments the agent places after the marker.
    operands: Vec<String>,
}

impl GuardedShape {
    /// Builds a shape from borrowed text.
    fn new(
        label: &'static str,
        program: AdapterBinary,
        options: &[&str],
        operands: &[&str],
    ) -> Self {
        Self {
            label,
            program,
            options: options
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
            operands: operands
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        }
    }

    /// The argument vector the agent sends: options, the marker, operands.
    fn guarded(&self) -> Vec<String> {
        let mut arguments = self.options.clone();
        arguments.push(END_OF_OPTIONS.to_owned());
        arguments.extend(self.operands.iter().cloned());
        arguments
    }

    /// The control vector: the same call with an option the program does not have,
    /// placed FIRST, before the shape's own options.
    ///
    /// First and not where the marker is, because option parsing runs left to right
    /// and a program may refuse an earlier option's argument before it ever reaches
    /// the bogus one: `crontab -u <absent account>` says the user is unknown and
    /// stops, which is not a refusal of an option and would make this control fail
    /// for a reason that has nothing to do with the program's wording.
    fn with_a_bogus_option(&self) -> Vec<String> {
        let mut arguments = vec![BOGUS_OPTION.to_owned()];
        arguments.extend(self.options.iter().cloned());
        arguments.push(END_OF_OPTIONS.to_owned());
        arguments.extend(self.operands.iter().cloned());
        arguments
    }
}

/// Every `--` placement the agent uses against a program this suite can observe.
///
/// The shapes are copied from the call sites in `ops` (accounts, sftp, ftps,
/// firewall, backup, cron, monitor), with the operand replaced by one that does not
/// exist, so the real program fails on the operand. Where a shape's options matter
/// to whether `--` is honoured (`tar`'s `-C` before the marker, `nft`'s `--json`
/// before it) they are kept.
///
/// # Panics
///
/// Panics when `archive` cannot be put in an argument.
fn guarded_shapes(distro: &'static dyn DistroAdapter, archive: &Path) -> Vec<GuardedShape> {
    let archive = archive.to_str().expect("a UTF-8 archive path");
    let directory = std::env::temp_dir();
    let directory = directory.to_str().expect("a UTF-8 temporary directory");
    let nologin = distro.nologin_shell();

    vec![
        GuardedShape::new("id", distro.id_binary(), &["-u"], &[ABSENT_ACCOUNT]),
        GuardedShape::new(
            "getent shadow",
            distro.getent_binary(),
            &[],
            &["shadow", ABSENT_ACCOUNT],
        ),
        GuardedShape::new(
            "useradd",
            distro.useradd_binary(),
            &["--user-group"],
            &[REFUSED_ACCOUNT_NAME],
        ),
        GuardedShape::new(
            "userdel",
            distro.userdel_binary(),
            &["--remove"],
            &[ABSENT_ACCOUNT],
        ),
        GuardedShape::new(
            "usermod",
            distro.usermod_binary(),
            &["--shell", nologin],
            &[ABSENT_ACCOUNT],
        ),
        GuardedShape::new(
            "passwd lock",
            distro.passwd_binary(),
            &["--lock"],
            &[ABSENT_ACCOUNT],
        ),
        GuardedShape::new(
            "chgrp",
            distro.chgrp_binary(),
            &["--no-dereference"],
            &["root", ABSENT_PATH],
        ),
        GuardedShape::new("chmod", distro.chmod_binary(), &[], &["0750", ABSENT_PATH]),
        GuardedShape::new(
            "quota",
            distro.quota_binary(),
            &["-u", "-w"],
            &[ABSENT_ACCOUNT],
        ),
        GuardedShape::new("quotaon", distro.quotaon_binary(), &["-p"], &[ABSENT_PATH]),
        GuardedShape::new(
            "crontab install",
            distro.crontab_binary(),
            &["-u", ABSENT_ACCOUNT],
            &[ABSENT_PATH],
        ),
        GuardedShape::new(
            "nft list a table",
            distro.nft_binary(),
            &[],
            &["list", "table", "inet", ABSENT_TABLE],
        ),
        GuardedShape::new(
            "nft list a set as json",
            distro.nft_binary(),
            &["--json"],
            &["list", "set", "inet", ABSENT_TABLE, ABSENT_SET],
        ),
        GuardedShape::new(
            "nft add an element",
            distro.nft_binary(),
            &[],
            &[
                "add",
                "element",
                "inet",
                ABSENT_TABLE,
                ABSENT_SET,
                "{",
                "192.0.2.1",
                "}",
            ],
        ),
        GuardedShape::new(
            "nft delete an element",
            distro.nft_binary(),
            &[],
            &[
                "delete",
                "element",
                "inet",
                ABSENT_TABLE,
                ABSENT_SET,
                "{",
                "192.0.2.1",
                "}",
            ],
        ),
        GuardedShape::new(
            "tar member after the change of directory",
            distro.tar_binary(),
            &["--list", "--file", archive, "--directory", directory],
            &[ABSENT_MEMBER],
        ),
    ]
}

/// Runs `program` with `arguments` the way the agent runs everything, bounded,
/// and answers what it printed and returned.
///
/// # Panics
///
/// Panics when the program cannot be started or is stopped by its bound: a probe
/// that did not run is not a program that honoured the marker.
fn run_probe(label: &str, program: &AdapterBinary, arguments: &[String]) -> CommandOutcome {
    let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();

    spawn_argv(program.as_path(), &borrowed, CallClass::Query).unwrap_or_else(|error| {
        panic!("the probe `{label}` ({program} {arguments:?}) did not run to the end: {error:?}")
    })
}

/// Whether the program's own words say it refused an option.
fn refused_an_option(outcome: &CommandOutcome) -> bool {
    let said = format!("{}\n{}", outcome.stdout, outcome.stderr).to_lowercase();

    OPTION_REFUSALS.iter().any(|phrase| said.contains(phrase))
}

/// Makes an archive with no member in it, with the family's own `tar`.
///
/// # Panics
///
/// Panics when `tar` cannot be run or refuses.
fn empty_archive(distro: &'static dyn DistroAdapter, directory: &Path) -> std::path::PathBuf {
    let archive = directory.join("empty.tar");
    let made = std::process::Command::new(distro.tar_binary())
        .arg("--create")
        .arg("--file")
        .arg(&archive)
        .arg("--files-from")
        .arg("/dev/null")
        .output()
        .expect("tar can be started");
    assert!(
        made.status.success(),
        "tar could not make the fixture archive: {}",
        String::from_utf8_lossy(&made.stderr)
    );

    archive
}

/// Every program the agent puts `--` in front of an operand for reads it as the end
/// of its options, on this family.
///
/// The marker is only a guard where the program honours it; a program that does
/// not turns the guard into an error that fails every call on every server of
/// that family. `END_OF_OPTIONS`'s contract is "only for programs known to honour
/// it on both families", and this is where that is known. The inverse control
/// is the next test: the same probes with an option the program does not have
/// MUST be refused in the program's words, otherwise "no complaint" here would
/// only mean the phrase list never matched that program.
#[test]
#[ignore = "requires a polygon container"]
fn every_program_the_agent_guards_with_an_end_of_options_marker_reads_it_as_the_end_of_options() {
    require_polygon();
    let distro = polygon_distro();
    let directory = tempfile::tempdir().expect("a temporary directory");
    let archive = empty_archive(distro, directory.path());

    for shape in guarded_shapes(distro, &archive) {
        let outcome = run_probe(shape.label, &shape.program, &shape.guarded());

        assert!(
            !refused_an_option(&outcome),
            "`{}` ({}) read the end-of-options marker as an option of its own on this \
             family. Arguments: {:?}. It said: {} {}",
            shape.label,
            shape.program,
            shape.guarded(),
            outcome.stdout,
            outcome.stderr
        );
    }
}

/// The control for the test above: the same probes, with an option the program does
/// not have, are refused in the program's own words.
///
/// Without it, a program whose refusal is worded in a way [`OPTION_REFUSALS`] does
/// not list would pass the test above whatever it did with the marker. A shape that
/// fails here is a program whose wording has to be added to the list before its
/// result above means anything.
#[test]
#[ignore = "requires a polygon container"]
fn the_option_refusal_check_sees_every_program_it_judges() {
    require_polygon();
    let distro = polygon_distro();
    let directory = tempfile::tempdir().expect("a temporary directory");
    let archive = empty_archive(distro, directory.path());

    for shape in guarded_shapes(distro, &archive) {
        let outcome = run_probe(shape.label, &shape.program, &shape.with_a_bogus_option());

        assert!(
            refused_an_option(&outcome),
            "`{}` ({}) was given {BOGUS_OPTION} and did not say it refused an option, so \
             the check cannot tell it apart from a program that honours the marker. \
             Arguments: {:?}. It said: {} {}",
            shape.label,
            shape.program,
            shape.with_a_bogus_option(),
            outcome.stdout,
            outcome.stderr
        );
    }
}

/// The probes create no account: the one they name stays absent, and the lookup
/// that says so can still find an account that exists.
///
/// The `useradd` probe is the one that could: its name is refused for its colon,
/// and this is the check that the refusal came before any change rather than
/// after. A probe that quietly made something would make the next run of the
/// polygon differ from this one. The positive control is the lookup of `root`:
/// without it, "absent" would only mean the lookup finds nobody.
#[test]
#[ignore = "requires a polygon container"]
fn the_end_of_options_probes_leave_no_account_behind() {
    require_polygon();
    let distro = polygon_distro();
    let directory = tempfile::tempdir().expect("a temporary directory");
    let archive = empty_archive(distro, directory.path());

    for shape in guarded_shapes(distro, &archive) {
        run_probe(shape.label, &shape.program, &shape.guarded());
    }

    let lookup = |name: &str| {
        run_probe(
            "getent passwd",
            &distro.getent_binary(),
            &["passwd".to_owned(), name.to_owned()],
        )
    };
    assert_eq!(
        lookup("root").status,
        0,
        "the lookup must find an account that exists, or its answer below means nothing"
    );
    assert_eq!(
        lookup(ABSENT_ACCOUNT).status,
        2,
        "the probes must not have created {ABSENT_ACCOUNT}"
    );
}
