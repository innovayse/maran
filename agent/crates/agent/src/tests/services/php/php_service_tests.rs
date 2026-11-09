//! The `InstallPhpVersion` rpc, driven the way the server drives it.
//!
//! These go through `PhpServiceImpl` itself, on a real multi-threaded runtime,
//! because the rpc wraps a blocking operation in `spawn_blocking` and turns its
//! progress callback into stream messages — the part nothing else exercises.
//! The operation underneath has its own suite in `maran_ops`; what is pinned
//! here is the stream's shape: the stages, exactly one terminal message, and
//! the wire code a failure travels as.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{ConfigHost, SafeWriteError};
use tokio_stream::StreamExt;
use tonic::Request;

use crate::proto::php_service_server::PhpService;
use crate::proto::{
    ApplyPhpPoolSettingsRequest, ErrorCode, InstallPhpVersionRequest,
    ListInstallablePhpVersionsRequest, PhpSetting, apply_php_pool_settings_response,
    install_php_version_response, list_installable_php_versions_response,
};
use crate::services::php::php_service::PhpServiceImpl;

/// The adapter every test here runs against.
fn adapter() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// A host with no PHP installed whose `start` either installs the version or
/// fails, and which records every argv it is handed.
///
/// Every method the install rpc never reaches says so rather than pretending to
/// work.
struct InstallHost {
    /// Whether `systemctl start` succeeds and leaves the pool directory behind.
    start_succeeds: bool,
    /// The pool directories that exist.
    directories: Mutex<BTreeSet<PathBuf>>,
    /// The configuration files an install wrote (the OPcache drop-in), by path.
    written: Mutex<BTreeMap<PathBuf, String>>,
    /// Every argv handed to `run`, shared with the test that owns the host's
    /// other half because the service takes the host by value.
    calls: Arc<Mutex<Vec<Vec<String>>>>,
}

impl InstallHost {
    /// A host on which starting the install unit works.
    fn installing(calls: &Arc<Mutex<Vec<Vec<String>>>>) -> Self {
        Self::with(true, calls)
    }

    /// A host on which starting the install unit fails.
    fn failing(calls: &Arc<Mutex<Vec<Vec<String>>>>) -> Self {
        Self::with(false, calls)
    }

    /// A host with nothing installed.
    fn with(start_succeeds: bool, calls: &Arc<Mutex<Vec<Vec<String>>>>) -> Self {
        Self {
            start_succeeds,
            directories: Mutex::new(BTreeSet::new()),
            written: Mutex::new(BTreeMap::new()),
            calls: Arc::clone(calls),
        }
    }
}

impl ConfigHost for InstallHost {
    /// Records the argv, and answers as the service manager would.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_string()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        if arguments.first() == Some(&"start") {
            if !self.start_succeeds {
                return Ok(CommandOutcome {
                    status: 1,
                    stdout: String::new(),
                    stderr: "Job for maran-php-install@php8.3-fpm.service failed".to_owned(),
                });
            }
            self.directories
                .lock()
                .unwrap()
                .insert(PathBuf::from(adapter().php_fpm_pool_directory("8.3")));
        }

        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

impl PhpHost for InstallHost {
    /// Answers from the directories a successful start created.
    fn directory_exists(&self, path: &Path) -> bool {
        self.directories.lock().unwrap().contains(path)
    }

    /// Answers only from what the install itself wrote; no pool file exists to read.
    fn read_config(&self, path: &Path) -> Option<String> {
        assert!(
            !path.starts_with(adapter().php_fpm_pool_directory("8.3")),
            "an install must not read a pool file"
        );
        self.written.lock().unwrap().get(path).cloned()
    }

    /// Unreachable: installing creates no directory itself.
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        panic!("an install must not create a directory")
    }

    /// Unreachable: installing creates nothing as an account.
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not create a directory as an account")
    }

    /// Keeps the OPcache drop-in the install ends with; any other write is a defect.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        assert!(
            target.ends_with("99-maran-opcache.ini"),
            "an install must write only the OPcache drop-in"
        );
        self.written
            .lock()
            .unwrap()
            .insert(target.to_path_buf(), contents.to_owned());
        Ok(())
    }

    /// Unreachable: installing removes no configuration.
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not remove a file")
    }

    /// Unreachable: installing reloads no pool.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an install must not reload a pool")
    }
}

/// Drives the rpc for `version` and collects every message of its stream.
async fn collect(host: InstallHost, version: &str) -> Vec<install_php_version_response::Result> {
    let service = PhpServiceImpl::new(host, adapter());
    let mut stream = service
        .install_php_version(Request::new(InstallPhpVersionRequest {
            version: version.to_owned(),
        }))
        .await
        .unwrap()
        .into_inner();

    let mut messages = Vec::new();
    while let Some(message) = stream.next().await {
        messages.push(message.unwrap().result.expect("a result is always set"));
    }

    messages
}

#[tokio::test(flavor = "multi_thread")]
async fn an_install_streams_four_stages_and_ends_with_exactly_one_ok() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let messages = collect(InstallHost::installing(&calls), "8.3").await;

    assert_eq!(messages.len(), 5, "four stages and one terminal message");
    let stages: Vec<(u32, String)> = messages[..4]
        .iter()
        .map(|message| match message {
            install_php_version_response::Result::Progress(progress) => {
                (progress.percent, progress.stage.clone())
            }
            other => panic!("expected a progress message, got {other:?}"),
        })
        .collect();
    assert_eq!(
        stages,
        vec![
            (10, "preparing".to_owned()),
            (30, "download".to_owned()),
            (80, "install".to_owned()),
            (100, "enable".to_owned()),
        ]
    );
    match &messages[4] {
        install_php_version_response::Result::Ok(ok) => assert_eq!(ok.version, "8.3"),
        other => panic!("expected the terminal Ok, got {other:?}"),
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn a_failed_install_unit_ends_the_stream_with_one_system_failure_carrying_its_report() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let messages = collect(InstallHost::failing(&calls), "8.3").await;

    let errors: Vec<_> = messages
        .iter()
        .filter_map(|message| match message {
            install_php_version_response::Result::Error(error) => Some(error),
            _ => None,
        })
        .collect();
    assert_eq!(errors.len(), 1, "exactly one terminal message");
    assert!(matches!(
        messages.last().unwrap(),
        install_php_version_response::Result::Error(_)
    ));
    assert_eq!(errors[0].code, ErrorCode::SystemFailure as i32);
    assert!(
        errors[0]
            .tool_output
            .contains("maran-php-install@php8.3-fpm.service")
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_malformed_version_ends_the_stream_with_invalid_input_and_starts_nothing() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let messages = collect(InstallHost::installing(&calls), "8").await;

    assert_eq!(messages.len(), 1);
    match &messages[0] {
        install_php_version_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert!(calls.lock().unwrap().is_empty());
}

#[tokio::test(flavor = "multi_thread")]
async fn the_installable_list_names_every_supported_version_and_flags_the_installed_one() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let host = InstallHost::installing(&calls);
    host.directories
        .lock()
        .unwrap()
        .insert(PathBuf::from(adapter().php_fpm_pool_directory("8.3")));
    let service = PhpServiceImpl::new(host, adapter());

    let response = service
        .list_installable_php_versions(Request::new(ListInstallablePhpVersionsRequest {}))
        .await
        .unwrap()
        .into_inner();

    let Some(list_installable_php_versions_response::Result::Ok(ok)) = response.result else {
        panic!("expected the ok payload");
    };
    let versions: Vec<&str> = ok
        .versions
        .iter()
        .map(|item| item.version.as_str())
        .collect();
    assert_eq!(versions, vec!["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"]);
    let installed: Vec<&str> = ok
        .versions
        .iter()
        .filter(|item| item.installed)
        .map(|item| item.version.as_str())
        .collect();
    assert_eq!(installed, vec!["8.3"]);
    assert!(calls.lock().unwrap().is_empty(), "listing runs no command");
}

// The two extension rpcs, driven through the same service on a host that
// answers the module listing and the family's switch.

use crate::proto::{
    ListPhpExtensionsRequest, PhpExtensionAvailability, SetPhpExtensionRequest,
    list_php_extensions_response, set_php_extension_response,
};

/// What `php-fpm -m` prints on a host where `gd` is `gd_loaded` and the
/// remaining loaded modules are fixed: the section headers, a module whose name
/// holds a space, and a blank line, all of which the reading must skip or keep
/// whole.
fn module_listing(gd_loaded: bool) -> String {
    let gd = if gd_loaded { "gd\n" } else { "" };
    format!("[PHP Modules]\nCore\n{gd}mbstring\n\n[Zend Modules]\nZend OPcache\n")
}

/// A host with PHP 8.3 installed whose module listing follows the family's
/// switch: `phpdismod` removes `gd` from the listing and `phpenmod` adds it.
struct ExtensionHost {
    /// The family whose pool directory spelling marks 8.3 as installed.
    distro: &'static dyn DistroAdapter,
    /// The exit status `php-fpm -m` answers with.
    probe_status: i32,
    /// Whether `gd` is in the module listing now.
    gd_loaded: Mutex<bool>,
    /// Every argv handed to `run`, program first, shared with the test.
    calls: Arc<Mutex<Vec<Vec<String>>>>,
    /// How many times php-fpm was validated and reloaded.
    reloads: Arc<Mutex<usize>>,
}

impl ExtensionHost {
    /// A host on `distro` with `gd` loaded or not.
    fn new(
        distro: &'static dyn DistroAdapter,
        gd_loaded: bool,
        probe_status: i32,
        calls: &Arc<Mutex<Vec<Vec<String>>>>,
    ) -> Self {
        Self {
            distro,
            probe_status,
            gd_loaded: Mutex::new(gd_loaded),
            calls: Arc::clone(calls),
            reloads: Arc::new(Mutex::new(0)),
        }
    }
}

impl ConfigHost for ExtensionHost {
    /// Records the argv and answers the listing or the switch.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let program = program.to_string();
        let mut command = vec![program.clone()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        let mut stdout = String::new();
        let mut stderr = String::new();
        let mut status = 0;
        if arguments.first() == Some(&"-m") {
            status = self.probe_status;
            if status == 0 {
                stdout = module_listing(*self.gd_loaded.lock().unwrap());
            } else {
                stderr = "php-fpm: cannot load".to_owned();
            }
        } else if program.ends_with("phpdismod") {
            *self.gd_loaded.lock().unwrap() = false;
        } else if program.ends_with("phpenmod") {
            *self.gd_loaded.lock().unwrap() = true;
        }

        Ok(CommandOutcome {
            status,
            stdout,
            stderr,
        })
    }
}

impl PhpHost for ExtensionHost {
    /// Version 8.3 is installed on the host's family.
    fn directory_exists(&self, path: &Path) -> bool {
        path == Path::new(&self.distro.php_fpm_pool_directory("8.3"))
    }

    /// Unreachable: an extension change reads no pool.
    fn read_config(&self, _path: &Path) -> Option<String> {
        panic!("an extension change must not read a pool file")
    }

    /// Unreachable: an extension change creates no directory.
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        panic!("an extension change must not create a directory")
    }

    /// Unreachable: an extension change creates nothing as an account.
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        panic!("an extension change must not create a directory as an account")
    }

    /// Unreachable: an extension change writes no file itself.
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an extension change must not write a file")
    }

    /// Unreachable: an extension change removes no file.
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an extension change must not remove a file")
    }

    /// Counts the check-and-reload and accepts it.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        *self.reloads.lock().unwrap() += 1;
        Ok(())
    }
}

/// Lists version `version`'s extensions through the service.
async fn list_extensions(
    host: ExtensionHost,
    version: &str,
) -> list_php_extensions_response::Result {
    PhpServiceImpl::new(host, host_distro())
        .list_php_extensions(Request::new(ListPhpExtensionsRequest {
            version: version.to_owned(),
        }))
        .await
        .unwrap()
        .into_inner()
        .result
        .expect("a result is always set")
}

/// The adapter the extension tests' services are built with: the Debian one, as
/// every other service test here.
fn host_distro() -> &'static dyn DistroAdapter {
    adapter()
}

/// Drives `SetPhpExtension` and collects every message of its stream.
async fn collect_extension(
    host: ExtensionHost,
    version: &str,
    extension: &str,
    enabled: bool,
) -> Vec<set_php_extension_response::Result> {
    let distro = host.distro;
    let service = PhpServiceImpl::new(host, distro);
    let mut stream = service
        .set_php_extension(Request::new(SetPhpExtensionRequest {
            version: version.to_owned(),
            extension: extension.to_owned(),
            enabled,
        }))
        .await
        .unwrap()
        .into_inner();

    let mut messages = Vec::new();
    while let Some(message) = stream.next().await {
        messages.push(message.unwrap().result.expect("a result is always set"));
    }

    messages
}

#[tokio::test(flavor = "multi_thread")]
async fn the_extension_list_names_the_whole_catalogue_alphabetically_with_what_php_fpm_loads() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let result = list_extensions(ExtensionHost::new(adapter(), true, 0, &calls), "8.3").await;

    let list_php_extensions_response::Result::Ok(ok) = result else {
        panic!("expected the ok payload");
    };
    let names: Vec<&str> = ok
        .extensions
        .iter()
        .map(|item| item.name.as_str())
        .collect();
    assert_eq!(
        names,
        vec![
            "bcmath", "curl", "gd", "gmp", "imagick", "intl", "mbstring", "mysql", "opcache",
            "redis", "soap", "xml", "zip"
        ]
    );
    let enabled: Vec<&str> = ok
        .extensions
        .iter()
        .filter(|item| item.enabled)
        .map(|item| item.name.as_str())
        .collect();
    assert_eq!(enabled, vec!["gd", "mbstring", "opcache"]);
    assert!(
        ok.extensions
            .iter()
            .all(|item| item.availability == PhpExtensionAvailability::Available as i32),
        "the Debian family has a package and a switch for every entry"
    );
    assert_eq!(
        calls.lock().unwrap().len(),
        1,
        "one module listing and nothing else"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_malformed_version_in_the_extension_list_is_invalid_input_and_runs_nothing() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let result = list_extensions(ExtensionHost::new(adapter(), true, 0, &calls), "8").await;

    match result {
        list_php_extensions_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert!(calls.lock().unwrap().is_empty());
}

#[tokio::test(flavor = "multi_thread")]
async fn an_unreadable_module_listing_is_a_system_failure_and_never_an_empty_list() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    let result = list_extensions(ExtensionHost::new(adapter(), true, 1, &calls), "8.3").await;

    match result {
        list_php_extensions_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::SystemFailure as i32);
            assert_eq!(error.tool_output, "php-fpm: cannot load");
        }
        other => panic!("expected an Error, got {other:?}"),
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn switching_an_extension_off_streams_progress_and_ends_with_exactly_one_ok() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let host = ExtensionHost::new(adapter(), true, 0, &calls);
    let reloads = Arc::clone(&host.reloads);

    let messages = collect_extension(host, "8.3", "gd", false).await;

    let (terminal, progress) = messages.split_last().expect("a stream is never empty");
    match terminal {
        set_php_extension_response::Result::Ok(ok) => {
            assert_eq!(ok.version, "8.3");
            assert_eq!(ok.extension, "gd");
            assert!(!ok.enabled);
        }
        other => panic!("expected the terminal Ok, got {other:?}"),
    }
    assert!(
        progress
            .iter()
            .all(|message| matches!(message, set_php_extension_response::Result::Progress(_))),
        "everything before the terminal message is progress"
    );
    let switched: Vec<Vec<String>> = calls
        .lock()
        .unwrap()
        .iter()
        .filter(|command| command[0].ends_with("phpdismod"))
        .cloned()
        .collect();
    assert_eq!(switched.len(), 1, "the family's switch ran once");
    assert_eq!(&switched[0][1..], ["-v", "8.3", "-s", "fpm", "gd"]);
    assert_eq!(
        *reloads.lock().unwrap(),
        1,
        "php-fpm was checked and reloaded once"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn an_extension_already_in_the_requested_state_succeeds_with_only_the_listing_run() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let host = ExtensionHost::new(adapter(), true, 0, &calls);
    let reloads = Arc::clone(&host.reloads);

    let messages = collect_extension(host, "8.3", "gd", true).await;

    assert!(matches!(
        messages.last().unwrap(),
        set_php_extension_response::Result::Ok(_)
    ));
    assert_eq!(
        calls.lock().unwrap().len(),
        1,
        "only the module listing ran"
    );
    assert_eq!(*reloads.lock().unwrap(), 0);
}

#[tokio::test(flavor = "multi_thread")]
async fn a_name_outside_the_catalogue_ends_the_stream_with_invalid_input_and_runs_nothing() {
    let calls = Arc::new(Mutex::new(Vec::new()));

    for hostile in ["php8.3-gd", "gd\n", "../etc/passwd", ""] {
        let messages = collect_extension(
            ExtensionHost::new(adapter(), true, 0, &calls),
            "8.3",
            hostile,
            true,
        )
        .await;

        assert_eq!(messages.len(), 1, "{hostile:?}");
        match &messages[0] {
            set_php_extension_response::Result::Error(error) => {
                assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{hostile:?}");
            }
            other => panic!("expected an Error for {hostile:?}, got {other:?}"),
        }
    }
    assert!(calls.lock().unwrap().is_empty());
}

#[tokio::test(flavor = "multi_thread")]
async fn switching_off_where_the_family_has_no_switch_is_invalid_input_and_changes_nothing() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let rhel = adapter_for(DistroFamily::Rhel);
    let host = ExtensionHost::new(rhel, true, 0, &calls);
    let reloads = Arc::clone(&host.reloads);

    let messages = collect_extension(host, "8.3", "gd", false).await;

    match messages.last().unwrap() {
        set_php_extension_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert_eq!(
        calls.lock().unwrap().len(),
        1,
        "only the module listing ran"
    );
    assert_eq!(*reloads.lock().unwrap(), 0);
}

/// The pool files a test shares with its host, path to content.
type PoolFiles = Arc<Mutex<BTreeMap<PathBuf, String>>>;

/// A count a test shares with its host.
type SharedCount = Arc<Mutex<usize>>;

/// A host with PHP 8.3 installed whose pool files live in a map the test
/// shares, so what the service wrote can be read back after the service took
/// the host by value.
struct PoolHost {
    /// The pool files, path to content.
    files: PoolFiles,
    /// How many pool files were written, validated and reloaded.
    writes: SharedCount,
    /// How many times a pool file was read.
    reads: SharedCount,
}

impl PoolHost {
    /// A host with no pool files, and the handles the test reads them with.
    fn empty() -> (Self, PoolFiles, SharedCount, SharedCount) {
        let files = Arc::new(Mutex::new(BTreeMap::new()));
        let writes = Arc::new(Mutex::new(0));
        let reads = Arc::new(Mutex::new(0));
        let host = Self {
            files: Arc::clone(&files),
            writes: Arc::clone(&writes),
            reads: Arc::clone(&reads),
        };
        (host, files, writes, reads)
    }
}

impl ConfigHost for PoolHost {
    /// Nothing is spawned by a pool rewrite whose write is the host's own.
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

impl PhpHost for PoolHost {
    /// Version 8.3 is installed.
    fn directory_exists(&self, path: &Path) -> bool {
        path == Path::new(&adapter().php_fpm_pool_directory("8.3"))
    }

    /// Answers from the shared map and counts the read.
    fn read_config(&self, path: &Path) -> Option<String> {
        *self.reads.lock().unwrap() += 1;
        self.files.lock().unwrap().get(path).cloned()
    }

    /// Accepts the directory.
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        Ok(())
    }

    /// Accepts the directories.
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        Ok(())
    }

    /// Stores the content and counts the write.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        *self.writes.lock().unwrap() += 1;
        self.files
            .lock()
            .unwrap()
            .insert(target.to_path_buf(), contents.to_owned());
        Ok(())
    }

    /// Unreachable: a settings rewrite removes no pool.
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("a settings rewrite must not remove a file")
    }

    /// Unreachable: the write protocol reloads, not this seam.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("a settings rewrite must not reload outside the write protocol")
    }
}

/// Where the Debian adapter keeps `account`'s 8.3 pool.
fn pool_file(account: &str) -> PathBuf {
    PathBuf::from(adapter().php_fpm_pool_directory("8.3")).join(format!("{account}.conf"))
}

/// Drives `ApplyPhpPoolSettings` for `account` at 8.3 with the given settings.
async fn apply(
    host: PoolHost,
    account: &str,
    max_children: u32,
    settings: &[(&str, &str)],
) -> apply_php_pool_settings_response::Result {
    PhpServiceImpl::new(host, adapter())
        .apply_php_pool_settings(Request::new(ApplyPhpPoolSettingsRequest {
            account_username: account.to_owned(),
            version: "8.3".to_owned(),
            max_children,
            overrides: settings
                .iter()
                .map(|(name, value)| PhpSetting {
                    name: (*name).to_owned(),
                    value: (*value).to_owned(),
                })
                .collect(),
        }))
        .await
        .unwrap()
        .into_inner()
        .result
        .expect("a result is always set")
}

#[tokio::test(flavor = "multi_thread")]
async fn applying_settings_to_an_account_with_no_pool_is_not_found_and_writes_nothing() {
    let (host, files, writes, _reads) = PoolHost::empty();

    let result = apply(host, "svcnopool", 10, &[("memory_limit", "256M")]).await;

    match result {
        apply_php_pool_settings_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::NotFound as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert_eq!(*writes.lock().unwrap(), 0);
    assert!(files.lock().unwrap().is_empty());
}

#[tokio::test(flavor = "multi_thread")]
async fn applying_settings_to_an_existing_pool_rewrites_it_once_and_a_repeat_writes_nothing() {
    let (host, files, writes, _reads) = PoolHost::empty();
    files.lock().unwrap().insert(
        pool_file("svcrewrite"),
        "; a pool from an earlier render\n".to_owned(),
    );

    let result = apply(host, "svcrewrite", 10, &[("memory_limit", "256M")]).await;

    assert!(
        matches!(result, apply_php_pool_settings_response::Result::Ok(_)),
        "{result:?}"
    );
    assert_eq!(*writes.lock().unwrap(), 1);
    let written = files
        .lock()
        .unwrap()
        .get(&pool_file("svcrewrite"))
        .cloned()
        .unwrap();
    assert!(
        written.contains("php_value[memory_limit] = 256M"),
        "{written}"
    );

    // The same request against the file it just wrote: equal bytes, no write.
    let (second, second_files, second_writes, _) = PoolHost::empty();
    second_files
        .lock()
        .unwrap()
        .insert(pool_file("svcrewrite"), written);
    let repeat = apply(second, "svcrewrite", 10, &[("memory_limit", "256M")]).await;
    assert!(
        matches!(repeat, apply_php_pool_settings_response::Result::Ok(_)),
        "{repeat:?}"
    );
    assert_eq!(*second_writes.lock().unwrap(), 0);
}

#[tokio::test(flavor = "multi_thread")]
async fn a_hostile_setting_is_invalid_input_and_the_pool_is_never_read() {
    let (host, files, writes, reads) = PoolHost::empty();
    files
        .lock()
        .unwrap()
        .insert(pool_file("svchostile"), "; untouched\n".to_owned());

    let result = apply(host, "svchostile", 10, &[("open_basedir", "/")]).await;

    match result {
        apply_php_pool_settings_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert_eq!(*reads.lock().unwrap(), 0);
    assert_eq!(*writes.lock().unwrap(), 0);
    assert_eq!(
        files.lock().unwrap().get(&pool_file("svchostile")).unwrap(),
        "; untouched\n"
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn a_malformed_account_name_is_invalid_input_and_touches_nothing() {
    let (host, _files, writes, reads) = PoolHost::empty();

    let result = apply(host, "../etc", 10, &[]).await;

    match result {
        apply_php_pool_settings_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an Error, got {other:?}"),
    }
    assert_eq!(*reads.lock().unwrap(), 0);
    assert_eq!(*writes.lock().unwrap(), 0);
}
