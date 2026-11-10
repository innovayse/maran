//! Tests for [`set_php_extension`].
//!
//! The module listing is the only state: each test scripts what
//! `php-fpm8.3 -m` prints before and after the change, and reads back which
//! programs the operation ran, in which order, with which argv and call class.
//! Every expected package, unit and switch argv is read from the ADAPTER, never
//! spelled out, because the operation must not know a platform literal.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for};

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::{PhpOpError, set_php_extension};

/// A listing with `gd` loaded.
const WITH_GD: &str = "[PHP Modules]\nCore\ngd\n\n[Zend Modules]\n";

/// A listing with `gd` absent.
const WITHOUT_GD: &str = "[PHP Modules]\nCore\n\n[Zend Modules]\n";

/// PHP 8.3, the version every test here switches.
fn version() -> PhpVersion {
    PhpVersion::parse("8.3").unwrap()
}

/// The catalogue name `text`.
fn name(text: &str) -> PhpExtensionName {
    PhpExtensionName::parse(text).unwrap()
}

/// A Debian host with 8.3 whose first listing is `before` and later ones `after`.
fn debian_host(before: &str, after: &str) -> FakePhpHost {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.answer_in_turn("-m", &[(0, before, ""), (0, after, "")]);
    host
}

/// A RHEL host with 8.3 whose first listing is `before` and later ones `after`.
fn rhel_host(before: &str, after: &str) -> FakePhpHost {
    let host = FakePhpHost::with_installed_on(adapter_for(DistroFamily::Rhel), &["8.3"]);
    host.answer_in_turn("-m", &[(0, before, ""), (0, after, "")]);
    host
}

/// A SUSE host with 8.3 whose first listing is `before` and later ones `after`.
fn suse_host(before: &str, after: &str) -> FakePhpHost {
    let host = FakePhpHost::with_installed_on(adapter_for(DistroFamily::Suse), &["8.3"]);
    host.answer_in_turn("-m", &[(0, before, ""), (0, after, "")]);
    host
}

/// Runs the operation and returns its result with the progress it reported.
fn set(
    host: &FakePhpHost,
    adapter: &dyn DistroAdapter,
    extension: &str,
    enabled: bool,
) -> (Result<(), PhpOpError>, Vec<(u32, String)>) {
    let reported = Mutex::new(Vec::new());
    let result = set_php_extension(
        host,
        adapter,
        &version(),
        name(extension),
        enabled,
        |percent, stage| {
            reported.lock().unwrap().push((percent, stage.to_owned()));
        },
    );

    (result, reported.into_inner().unwrap())
}

/// `program` followed by `arguments`, as the fake records an argv.
fn argv(program: &str, arguments: &[&str]) -> Vec<String> {
    let mut command = vec![program.to_owned()];
    command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
    command
}

/// The module-listing command for `adapter`.
fn probe(adapter: &dyn DistroAdapter) -> Vec<String> {
    argv(adapter.php_fpm_binary(&version()).as_str(), &["-m"])
}

/// The install unit start for `extension`, from the adapter's package.
fn install_start(adapter: &dyn DistroAdapter, extension: &str) -> Vec<String> {
    let package = adapter
        .php_extension_package(&version(), name(extension))
        .unwrap();
    argv(
        adapter.service_manager().as_str(),
        &[
            "start",
            "--",
            &format!("maran-php-install@{package}.service"),
        ],
    )
}

/// The switch command for `extension` in the given direction.
fn switch_command(adapter: &dyn DistroAdapter, extension: &str, enabled: bool) -> Vec<String> {
    let switch = adapter
        .php_extension_switch(&version(), name(extension))
        .unwrap();
    let (program, arguments) = if enabled {
        (&switch.enable_program, &switch.enable_arguments)
    } else {
        (&switch.disable_program, &switch.disable_arguments)
    };
    let mut command = vec![program.as_str().to_owned()];
    command.extend(arguments.iter().cloned());
    command
}

/// The `php-fpm -t` validator command.
fn validate(adapter: &dyn DistroAdapter) -> Vec<String> {
    argv(adapter.php_fpm_binary(&version()).as_str(), &["-t"])
}

/// The reload of the version's php-fpm service.
fn reload(adapter: &dyn DistroAdapter) -> Vec<String> {
    argv(
        adapter.service_manager().as_str(),
        &["reload", "--", &adapter.php_fpm_service("8.3")],
    )
}

#[test]
fn an_extension_already_enabled_is_left_alone_with_nothing_but_the_listing_run() {
    let host = debian_host(WITH_GD, WITH_GD);

    let (result, progress) = set(&host, distro(), "gd", true);

    result.unwrap();
    assert_eq!(host.commands(), vec![probe(distro())]);
    assert_eq!(progress.last().unwrap().0, 100);
}

#[test]
fn an_extension_already_disabled_is_left_alone_with_nothing_but_the_listing_run() {
    let host = debian_host(WITHOUT_GD, WITHOUT_GD);

    let (result, _) = set(&host, distro(), "gd", false);

    result.unwrap();
    assert_eq!(host.commands(), vec![probe(distro())]);
}

#[test]
fn enabling_installs_the_derived_package_switches_validates_reloads_and_reads_back() {
    let adapter = distro();
    let host = debian_host(WITHOUT_GD, WITH_GD);

    let (result, progress) = set(&host, adapter, "gd", true);

    result.unwrap();
    assert_eq!(
        host.commands(),
        vec![
            probe(adapter),
            install_start(adapter, "gd"),
            switch_command(adapter, "gd", true),
            validate(adapter),
            reload(adapter),
            probe(adapter),
        ]
    );
    assert_eq!(
        host.classes(),
        vec![
            CallClass::Validate,
            CallClass::PackageInstall,
            CallClass::Mutation,
            CallClass::Validate,
            CallClass::Reload,
            CallClass::Validate,
        ]
    );
    let stages: Vec<_> = progress.iter().map(|(_, stage)| stage.as_str()).collect();
    assert_eq!(
        stages,
        [
            "preparing",
            "install",
            "switch",
            "reload",
            "verify",
            "verify"
        ]
    );
    assert!(progress.windows(2).all(|pair| pair[0].0 <= pair[1].0));
    assert_eq!(progress.last().unwrap().0, 100);
}

#[test]
fn disabling_switches_off_validates_reloads_and_reads_back_without_installing_anything() {
    let adapter = distro();
    let host = debian_host(WITH_GD, WITHOUT_GD);

    let (result, _) = set(&host, adapter, "gd", false);

    result.unwrap();
    assert_eq!(
        host.commands(),
        vec![
            probe(adapter),
            switch_command(adapter, "gd", false),
            validate(adapter),
            reload(adapter),
            probe(adapter),
        ]
    );
}

#[test]
fn enabling_on_the_rhel_family_installs_the_package_and_runs_no_switch() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = rhel_host(WITHOUT_GD, WITH_GD);

    let (result, progress) = set(&host, adapter, "gd", true);

    result.unwrap();
    assert_eq!(
        host.commands(),
        vec![
            probe(adapter),
            install_start(adapter, "gd"),
            validate(adapter),
            reload(adapter),
            probe(adapter),
        ]
    );
    assert!(progress.iter().all(|(_, stage)| stage != "switch"));
}

#[test]
fn disabling_on_the_rhel_family_is_refused_as_unsupported_before_anything_changes() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = rhel_host(WITH_GD, WITH_GD);

    let (result, _) = set(&host, adapter, "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionSwitchUnsupported { ref extension } if extension == "gd"
    ));
    assert_eq!(host.commands(), vec![probe(adapter)]);
}

#[test]
fn enabling_on_the_suse_family_installs_the_package_and_runs_no_switch() {
    // UNVERIFIED: the family is read to ship no `phpdismod`-style switch, so enabling is the
    // package install alone, as on the RHEL family; confirm with `command -v phpdismod` in the
    // pinned Leap image.
    let adapter = adapter_for(DistroFamily::Suse);
    let host = suse_host(WITHOUT_GD, WITH_GD);

    let (result, progress) = set(&host, adapter, "gd", true);

    result.unwrap();
    assert_eq!(
        host.commands(),
        vec![
            probe(adapter),
            install_start(adapter, "gd"),
            validate(adapter),
            reload(adapter),
            probe(adapter),
        ]
    );
    assert!(progress.iter().all(|(_, stage)| stage != "switch"));
}

#[test]
fn disabling_on_the_suse_family_is_refused_as_unsupported_before_anything_changes() {
    let adapter = adapter_for(DistroFamily::Suse);
    let host = suse_host(WITH_GD, WITH_GD);

    let (result, _) = set(&host, adapter, "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionSwitchUnsupported { ref extension } if extension == "gd"
    ));
    assert_eq!(host.commands(), vec![probe(adapter)]);
}

#[test]
fn an_extension_already_off_on_the_suse_family_is_a_no_op_and_not_unsupported() {
    let adapter = adapter_for(DistroFamily::Suse);
    let host = suse_host(WITHOUT_GD, WITHOUT_GD);

    let (result, _) = set(&host, adapter, "gd", false);

    result.unwrap();
    assert_eq!(host.commands(), vec![probe(adapter)]);
}

#[test]
fn disabling_on_the_debian_family_is_supported() {
    // The pair of the RHEL refusal above: the same request on the other family
    // runs, so the refusal is the family's and not the operation's.
    let adapter = adapter_for(DistroFamily::Debian);
    let host = debian_host(WITH_GD, WITHOUT_GD);

    let (result, _) = set(&host, adapter, "gd", false);

    result.unwrap();
}

#[test]
fn an_extension_already_off_on_the_rhel_family_is_a_no_op_and_not_unsupported() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = rhel_host(WITHOUT_GD, WITHOUT_GD);

    let (result, _) = set(&host, adapter, "gd", false);

    result.unwrap();
    assert_eq!(host.commands(), vec![probe(adapter)]);
}

#[test]
fn enabling_an_extension_the_family_has_no_package_for_is_refused_as_not_packaged() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = rhel_host(WITHOUT_GD, WITHOUT_GD);

    let (result, _) = set(&host, adapter, "curl", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionNotPackaged { ref extension, ref version }
            if extension == "curl" && version == "8.3"
    ));
    assert_eq!(host.commands(), vec![probe(adapter)]);
}

#[test]
fn a_refused_validation_after_enabling_runs_the_inverse_switch_and_returns_the_refusal() {
    let adapter = distro();
    let host = debian_host(WITHOUT_GD, WITH_GD);
    host.reject_validation("ERROR: unable to load dynamic library");

    let (result, _) = set(&host, adapter, "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::PoolValidation { ref stderr } if stderr.contains("unable to load")
    ));
    assert_eq!(
        host.commands(),
        vec![
            probe(adapter),
            install_start(adapter, "gd"),
            switch_command(adapter, "gd", true),
            validate(adapter),
            switch_command(adapter, "gd", false),
        ]
    );
}

#[test]
fn a_refused_validation_after_disabling_runs_the_enabling_switch_to_undo_it() {
    let adapter = distro();
    let host = debian_host(WITH_GD, WITHOUT_GD);
    host.reject_validation("refused");

    let (result, _) = set(&host, adapter, "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::PoolValidation { .. }
    ));
    assert_eq!(
        host.commands(),
        vec![
            probe(adapter),
            switch_command(adapter, "gd", false),
            validate(adapter),
            switch_command(adapter, "gd", true),
        ]
    );
}

#[test]
fn a_deadline_on_the_validation_is_a_bound_and_still_undoes_the_switch() {
    let adapter = distro();
    let host = debian_host(WITHOUT_GD, WITH_GD);
    host.time_out_validation();

    let (result, _) = set(&host, adapter, "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::Bound(BoundExceeded::TimedOut { .. })
    ));
    assert_eq!(
        host.commands().last().unwrap(),
        &switch_command(adapter, "gd", false)
    );
}

#[test]
fn a_refused_validation_on_a_family_without_a_switch_has_nothing_to_undo() {
    let adapter = adapter_for(DistroFamily::Rhel);
    let host = rhel_host(WITHOUT_GD, WITH_GD);
    host.reject_validation("refused");

    let (result, _) = set(&host, adapter, "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::PoolValidation { .. }
    ));
    assert_eq!(host.commands().last().unwrap(), &validate(adapter));
}

#[test]
fn a_deadline_on_the_install_unit_is_a_bound_and_not_a_package_manager_failure() {
    let adapter = distro();
    let host = debian_host(WITHOUT_GD, WITH_GD);
    host.time_out("start");

    let (result, _) = set(&host, adapter, "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::Bound(BoundExceeded::TimedOut { .. })
    ));
    assert_eq!(host.commands().len(), 2);
}

#[test]
fn a_failed_install_unit_is_reported_like_a_failed_php_install_and_changes_nothing_else() {
    let adapter = distro();
    let host = debian_host(WITHOUT_GD, WITH_GD);
    host.answer("start", 1, "", "Unit not found");

    let (result, _) = set(&host, adapter, "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::PackageManager { .. }
    ));
    assert!(
        !host
            .commands()
            .contains(&switch_command(adapter, "gd", true))
    );
}

#[test]
fn a_switch_that_exits_non_zero_is_a_failed_switch_and_nothing_is_reloaded() {
    let adapter = distro();
    let host = debian_host(WITH_GD, WITHOUT_GD);
    let first = switch_command(adapter, "gd", false)[1].clone();
    host.answer(&first, 1, "", "ERROR: Module gd ini file doesn't exist");

    let (result, _) = set(&host, adapter, "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionSwitchFailed { ref stderr } if stderr.contains("ini file")
    ));
    assert!(!host.commands().contains(&validate(adapter)));
}

#[test]
fn a_switch_that_cannot_be_started_is_a_failed_switch() {
    let adapter = distro();
    let host = debian_host(WITH_GD, WITHOUT_GD);
    let first = switch_command(adapter, "gd", false)[1].clone();
    host.fail_to_spawn(&first);

    let (result, _) = set(&host, adapter, "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionSwitchFailed { .. }
    ));
}

#[test]
fn a_deadline_on_the_switch_is_a_bound_and_not_a_failed_switch() {
    let adapter = distro();
    let host = debian_host(WITH_GD, WITHOUT_GD);
    let first = switch_command(adapter, "gd", false)[1].clone();
    host.time_out(&first);

    let (result, _) = set(&host, adapter, "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::Bound(BoundExceeded::TimedOut { .. })
    ));
}

#[test]
fn an_enabled_extension_the_daemon_still_does_not_load_is_not_loaded() {
    let host = debian_host(WITHOUT_GD, WITHOUT_GD);

    let (result, _) = set(&host, distro(), "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionNotLoaded { ref extension } if extension == "gd"
    ));
}

#[test]
fn a_disabled_extension_the_daemon_still_loads_is_still_loaded() {
    let host = debian_host(WITH_GD, WITH_GD);

    let (result, _) = set(&host, distro(), "gd", false);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionStillLoaded { ref extension } if extension == "gd"
    ));
}

#[test]
fn a_listing_that_fails_before_anything_is_a_failed_probe_and_nothing_runs() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.answer("-m", 1, "", "broken");

    let (result, _) = set(&host, distro(), "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionProbeFailed { .. }
    ));
    assert_eq!(host.commands().len(), 1);
}

#[test]
fn a_listing_that_fails_after_the_change_is_a_failed_probe_and_not_a_success() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.answer_in_turn("-m", &[(0, WITHOUT_GD, ""), (1, "", "broken after")]);

    let (result, _) = set(&host, distro(), "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::ExtensionProbeFailed { ref stderr } if stderr.contains("broken after")
    ));
}

#[test]
fn a_version_that_is_not_installed_is_refused_before_anything_runs() {
    let host = FakePhpHost::empty();

    let (result, _) = set(&host, distro(), "gd", true);

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::PhpVersionNotInstalled { .. }
    ));
    assert!(host.commands().is_empty());
}

#[test]
fn a_version_outside_the_supported_set_is_refused_before_anything_runs() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    let result = set_php_extension(
        &host,
        distro(),
        &PhpVersion::parse("9.9").unwrap(),
        name("gd"),
        true,
        |_, _| {},
    );

    assert!(matches!(
        result.unwrap_err(),
        PhpOpError::UnsupportedVersion { .. }
    ));
    assert!(host.commands().is_empty());
}

#[test]
fn a_name_outside_the_catalogue_cannot_become_a_parameter_at_all() {
    // The parameter is a `PhpExtensionName`, so the only way to name a package,
    // a path or a flag is through `parse`, and it refuses all of them.
    for hostile in [
        "php8.3-gd",
        "gd --force",
        "gd\nopcache",
        "../../etc/passwd",
        "-gd",
        "",
    ] {
        assert!(PhpExtensionName::parse(hostile).is_err(), "{hostile:?}");
    }
}
