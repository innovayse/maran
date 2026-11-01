//! Moving PHP sites from the old pool-socket directory to `/run/maran-php`,
//! against a real php-fpm and a real nginx (issue #125).
//!
//! A site written before the socket directory changed keeps a pool listening on
//! `/run/maran/php` and a vhost naming it until something rewrites both. The
//! same-version `UpdateSitePhpVersion` is that something, and three claims hold
//! it up, each of which only a real server can answer:
//!
//! 1. a running php-fpm master, told to reload by the same `systemctl reload`
//!    the agent issues, REBINDS a pool's listener to the path its rewritten
//!    configuration names, and lets the old socket go (measurement, not
//!    assumption: the whole design rests on the reload binding a new path);
//! 2. a site whose pool and vhost both name the old directory, and answers 502,
//!    is served again after one same-version update, its files are the ones a
//!    fresh render writes, and a second update touches and reloads nothing;
//! 3. the inverse control: a site already on the new directory stays served and
//!    the same update writes nothing, so claim 2's "writes nothing" is not a
//!    property of a call that never does anything.
//!
//! What this suite CANNOT see, stated rather than implied: the old directory is
//! created by the test (the agent's own unit no longer names it), the old layout
//! is broken by nothing but the directory's own mode (`/run/maran` is root-owned
//! and closed to the web user, so nginx cannot reach a socket inside it), and
//! the stand-in `systemctl` turns a php-fpm reload into SIGUSR2 to the master —
//! it does not model systemd's `ExecReload=`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/in_process_agent.rs"]
mod in_process_agent;
// This suite uses only part of the shared fixture; the other suites use the rest of
// it. The allow is on the module rather than on the fixture, so a fixture item no
// suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_config_file.rs"]
mod polygon_config_file;
// This suite uses only part of the shared fixture; the other suites use the rest of
// it. The allow is on the module rather than on the fixture, so a fixture item no
// suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_php.rs"]
mod polygon_php;
#[path = "fixtures/polygon_php_master.rs"]
mod polygon_php_master;
#[path = "fixtures/polygon_web.rs"]
mod polygon_web;

use std::path::{Path, PathBuf};
use std::time::Duration;

use maran_agent::proto::sites_service_client::SitesServiceClient;
use maran_agent::proto::{
    SiteBackendType, SiteSpec, UpdateSitePhpVersionRequest, update_site_php_version_response,
};
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::files::{ProcessFilesHost, WriteFileInput};
use maran_ops::php::{PoolInput, PoolPaths, ProcessPhpHost, write_pool};
use maran_ops::sites::{
    CreateSiteInput, ProcessSiteHost, SiteKind, SitePaths, SitesOpError, create_site,
};

use in_process_agent::InProcessAgent;
use polygon_account::PolygonAccount;
use polygon_config_file::PolygonConfigFile;
use polygon_php::PolygonPhp;
use polygon_php_master::PolygonPhpMaster;
use polygon_web::PolygonWeb;

/// The socket directory this release writes, spelled again here and not imported:
/// a test that took its expectation from the code under test would pass if that
/// code changed the directory and this assertion changed with it.
const NEW_DIRECTORY: &str = "/run/maran-php";

/// The directory earlier releases wrote, which the sites under test still name.
const OLD_DIRECTORY: &str = "/run/maran/php";

/// The worker budget every pool here is written with, by the site's creation and
/// by the update alike, so a converged pool is byte-equal to the first render.
const POLYGON_WORKERS: u32 = 5;

/// What the site's `index.php` prints, and so what a served response contains.
const SERVED_BODY: &str = "maran-converged-ok";

/// How long a socket is given to start or stop accepting after a reload.
const SOCKET_BUDGET: Duration = Duration::from_secs(20);

/// Where the stand-in service manager records the reloads it was asked for.
const UNIT_STATE_DIRECTORY: &str = "/run/polygon-units";

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

/// The pool input every write in this suite uses for `account`.
fn pool_input(account: &PolygonAccount) -> PoolInput {
    PoolInput {
        account: account.name().clone(),
        version: PolygonPhp::version(),
        max_children: POLYGON_WORKERS,
        overrides: Vec::new(),
    }
}

/// Takes the old socket directory away again when dropped.
///
/// Only the leaf is removed: `/run/maran` may be the agent's own directory.
struct OldSocketDirectory(PathBuf);

impl OldSocketDirectory {
    /// Creates the old directory, as an earlier release's installer left it.
    fn create() -> Self {
        std::fs::create_dir_all(OLD_DIRECTORY).expect("the old directory must be creatable");
        Self(PathBuf::from(OLD_DIRECTORY))
    }
}

impl Drop for OldSocketDirectory {
    /// Removes the directory, reporting rather than panicking.
    fn drop(&mut self) {
        if let Err(error) = std::fs::remove_dir_all(&self.0) {
            eprintln!(
                "the old socket directory {:?} could not be removed: {error}",
                self.0
            );
        }
    }
}

/// Forgets the reloads the stand-in has recorded, for nginx and for php-fpm.
///
/// The marker is one file per unit whose presence is the whole answer, so the
/// question "was this service reloaded since here" needs it cleared first.
fn forget_reloads() {
    for unit in reloadable_units() {
        let _ = std::fs::remove_file(reload_marker(&unit));
    }
}

/// The two units a convergence can reload: the web server and this version's php-fpm.
fn reloadable_units() -> [String; 2] {
    [
        polygon_distro().nginx_service().to_owned(),
        polygon_distro().php_fpm_service(PolygonPhp::VERSION),
    ]
}

/// The file the stand-in writes when `unit` is asked to reload.
fn reload_marker(unit: &str) -> PathBuf {
    Path::new(UNIT_STATE_DIRECTORY).join(format!("{unit}.reloaded"))
}

/// The units among [`reloadable_units`] that were asked to reload since the last
/// [`forget_reloads`].
fn reloaded_units() -> Vec<String> {
    reloadable_units()
        .into_iter()
        .filter(|unit| reload_marker(unit).exists())
        .collect()
}

/// A PHP site the agent created with its real hosts, serving a fixed page.
///
/// The bytes of both files are kept as the agent first wrote them: they are the
/// "fresh render" a later convergence must reproduce, taken from the writer
/// itself and not re-derived by the test.
struct ServedSite {
    /// The domain the site answers for.
    domain: Domain,
    /// The pool file the site's creation wrote.
    pool_path: PathBuf,
    /// The vhost file the site's creation wrote.
    vhost_path: PathBuf,
    /// The pool file exactly as the creation wrote it.
    fresh_pool: String,
    /// The vhost file exactly as the creation wrote it.
    fresh_vhost: String,
    /// The site's account, named in the update request.
    account_name: String,
    /// Removes the pool file when the test ends.
    _pool: PolygonConfigFile,
    /// Removes the vhost file when the test ends.
    _vhost: PolygonConfigFile,
}

impl ServedSite {
    /// Creates the site for `account` at `domain` and gives it a page that prints
    /// [`SERVED_BODY`].
    ///
    /// # Panics
    ///
    /// Panics when the creation or the page write fails.
    fn create(account: &PolygonAccount, domain: &str) -> Self {
        let domain = Domain::parse(domain).expect("a valid domain");
        let paths = SitePaths::for_site(account.name(), &domain);
        let pool_path =
            PoolPaths::for_pool(polygon_distro(), account.name(), &PolygonPhp::version())
                .config_path;
        // Taken before the creation writes them, so a failing test cannot leave
        // a pool naming a user who no longer resolves in the shared tree.
        let pool = PolygonConfigFile::at(&pool_path);
        let vhost = PolygonConfigFile::at(&paths.config_path);

        create_site(
            &ProcessSiteHost::new(),
            &ProcessPhpHost::new(),
            polygon_distro(),
            &CreateSiteInput {
                account: account.name().clone(),
                domain: domain.clone(),
                aliases: Vec::new(),
                kind: SiteKind::Php {
                    version: PolygonPhp::version(),
                },
                certificate: None,
            },
            POLYGON_WORKERS,
            &[],
        )
        .unwrap_or_else(|error: SitesOpError| panic!("creating a PHP site must succeed: {error}"));

        let written = maran_ops::files::write_file(
            &ProcessFilesHost::new(),
            &WriteFileInput {
                account: account.name().clone(),
                path: RelativePath::parse(&format!("sites/{}/index.php", domain.as_str()))
                    .expect("the page path must be valid"),
                contents: format!("<?php echo '{SERVED_BODY}';\n").into_bytes(),
                mode: FileMode::parse(0o644).expect("a plain permission mode"),
            },
            &maran_agent_core::utils::free_space_floor::FreeSpaceFloor::from_mebibytes(1)
                .expect("a one-mebibyte floor"),
        );
        assert!(written.is_ok(), "the page write must succeed: {written:?}");

        Self {
            fresh_pool: std::fs::read_to_string(&pool_path).expect("the pool must exist"),
            fresh_vhost: std::fs::read_to_string(&paths.config_path).expect("the vhost must exist"),
            account_name: account.name().as_str().to_owned(),
            domain,
            pool_path,
            vhost_path: paths.config_path,
            _pool: pool,
            _vhost: vhost,
        }
    }

    /// Rewrites the pool and the vhost to name the old directory, the state an
    /// earlier release left behind.
    ///
    /// # Panics
    ///
    /// Panics when a file does not name the new directory to begin with, which
    /// would make the rewrite a no-op and the test about nothing.
    fn rewrite_to_old_directory(&self) {
        for (path, fresh) in [
            (&self.pool_path, &self.fresh_pool),
            (&self.vhost_path, &self.fresh_vhost),
        ] {
            assert!(
                fresh.contains(NEW_DIRECTORY) && !fresh.contains(OLD_DIRECTORY),
                "a fresh render must name {NEW_DIRECTORY} and not {OLD_DIRECTORY}: {path:?}\n{fresh}"
            );
            std::fs::write(path, fresh.replace(NEW_DIRECTORY, OLD_DIRECTORY))
                .expect("the file must be rewritable by root");
        }
    }

    /// The request that sets the site's PHP version to the one it already has.
    fn same_version_request(&self) -> UpdateSitePhpVersionRequest {
        UpdateSitePhpVersionRequest {
            account_username: self.account_name.clone(),
            domain: self.domain.as_str().to_owned(),
            php_version: PolygonPhp::VERSION.to_owned(),
            site: Some(SiteSpec {
                backend_type: SiteBackendType::Php as i32,
                php_version: PolygonPhp::VERSION.to_owned(),
                ..SiteSpec::default()
            }),
            max_children: POLYGON_WORKERS,
            ..UpdateSitePhpVersionRequest::default()
        }
    }

    /// What nginx answers for the site's front page right now.
    fn fetch(&self) -> String {
        PolygonWeb::fetch(self.domain.as_str(), "/")
    }

    /// Asks until the site answers `200` with [`SERVED_BODY`], or the deadline passes.
    fn fetch_served(&self) -> String {
        PolygonWeb::fetch_until(self.domain.as_str(), "/", |response| {
            response.starts_with("HTTP/1.1 200 ") && response.contains(SERVED_BODY)
        })
    }

    /// The pool's socket under `directory`.
    fn socket_under(&self, directory: &str) -> PathBuf {
        Path::new(directory).join(format!(
            "{}-{}.sock",
            self.account_name,
            PolygonPhp::VERSION
        ))
    }

    /// Both files' bytes and modification times, for "nothing was written".
    fn file_state(&self) -> Vec<(String, std::time::SystemTime)> {
        [&self.pool_path, &self.vhost_path]
            .into_iter()
            .map(|path| {
                (
                    std::fs::read_to_string(path).expect("the file must exist"),
                    std::fs::metadata(path)
                        .and_then(|metadata| metadata.modified())
                        .expect("the file must have a modification time"),
                )
            })
            .collect()
    }
}

/// Sends `request` to a running in-process agent and requires an `Ok` answer.
///
/// # Panics
///
/// Panics on a transport error or a typed error payload.
fn update_site(
    runtime: &tokio::runtime::Runtime,
    agent: &InProcessAgent,
    request: UpdateSitePhpVersionRequest,
) {
    let response = runtime
        .block_on(SitesServiceClient::new(agent.channel()).update_site_php_version(request))
        .expect("the rpc itself succeeds: a failure is a typed payload")
        .into_inner();

    assert!(
        matches!(
            response.result,
            Some(update_site_php_version_response::Result::Ok(_))
        ),
        "a same-version update must succeed, got {response:?}"
    );
}

/// A multi-threaded runtime for driving the in-process agent from a sync test.
fn runtime() -> tokio::runtime::Runtime {
    tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .expect("a tokio runtime")
}

#[test]
#[ignore = "starts a real php-fpm master and signals it: polygon only"]
fn a_reloaded_php_fpm_master_binds_the_pools_new_socket_and_lets_the_old_one_go() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polysockmeasure");
    let paths = PoolPaths::for_pool(polygon_distro(), account.name(), &PolygonPhp::version());
    let _pool = PolygonConfigFile::at(&paths.config_path);
    let host = ProcessPhpHost::new();

    // The pool as the agent renders it today, then the same text naming the old
    // directory: the second is what an earlier release left on disk.
    write_pool(&host, polygon_distro(), &pool_input(&account))
        .unwrap_or_else(|error| panic!("the first pool write must succeed: {error}"));
    let current = std::fs::read_to_string(&paths.config_path).expect("the pool must exist");
    assert!(
        current.contains(NEW_DIRECTORY) && !current.contains(OLD_DIRECTORY),
        "the rendered pool must name {NEW_DIRECTORY} only:\n{current}"
    );
    let old_socket = Path::new(OLD_DIRECTORY).join(format!("{}.sock", paths.pool_name));
    let _old_directory = OldSocketDirectory::create();
    std::fs::write(
        &paths.config_path,
        current.replace(NEW_DIRECTORY, OLD_DIRECTORY),
    )
    .expect("the pool must be rewritable by root");

    let master = PolygonPhpMaster::start(polygon_distro(), &paths.config_path, "measure");

    // The control: the master really serves on the old path and not yet on the
    // new one, so the two answers below are about the reload and not about a
    // master that always listens somewhere else.
    assert!(
        PolygonPhpMaster::poll(SOCKET_BUDGET, || PolygonPhpMaster::accepts_connections(
            &old_socket
        )),
        "before the reload the master must accept connections on {old_socket:?}"
    );
    assert!(
        !PolygonPhpMaster::accepts_connections(&paths.socket_path),
        "before the reload nothing may listen on {:?}",
        paths.socket_path
    );

    // The agent's own write: the pool differs from what is on disk, so it is
    // replaced, validated, and the php-fpm unit is reloaded through systemctl.
    write_pool(&host, polygon_distro(), &pool_input(&account))
        .unwrap_or_else(|error| panic!("the migrating pool write must succeed: {error}"));

    assert!(
        PolygonPhpMaster::poll(SOCKET_BUDGET, || PolygonPhpMaster::accepts_connections(
            &paths.socket_path
        )),
        "after the reload the master must accept connections on {:?}",
        paths.socket_path
    );
    assert!(
        PolygonPhpMaster::poll(SOCKET_BUDGET, || !old_socket.exists()),
        "after the reload the old socket {old_socket:?} must be gone"
    );
    assert!(
        master.is_alive(),
        "the reload must not have killed the master"
    );
}

#[test]
#[ignore = "starts a real nginx and php-fpm and serves a PHP page: polygon only"]
fn a_site_on_the_old_socket_directory_is_served_again_after_a_same_version_update_and_a_second_changes_nothing()
 {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polysockconverge");
    PolygonWeb::ensure_running(polygon_distro());
    let runtime = runtime();
    // Started before anything is recorded: the agent's own startup may reload
    // php-fpm, and that reload is not what the second call is accused of.
    let agent = runtime.block_on(InProcessAgent::start());

    let site = ServedSite::create(&account, "converge.example.test");
    site.rewrite_to_old_directory();
    let _old_directory = OldSocketDirectory::create();
    let master = PolygonPhpMaster::start(polygon_distro(), &site.pool_path, "converge");
    PolygonWeb::reload(polygon_distro());

    // The master really listens on the old socket, so the 502 below is about the
    // directory and not about a master that never started.
    assert!(
        PolygonPhpMaster::poll(SOCKET_BUDGET, || PolygonPhpMaster::accepts_connections(
            &site.socket_under(OLD_DIRECTORY)
        )),
        "the master must listen on the old socket before the update"
    );

    // The #101 defect itself: /run/maran is root:<service group> 0750, so the web
    // user cannot traverse it to the socket and nginx answers 502 although the
    // socket exists and accepts connections.
    let broken = PolygonWeb::fetch_until(site.domain.as_str(), "/", |response| {
        response.starts_with("HTTP/1.1 502 ")
    });
    assert!(
        broken.starts_with("HTTP/1.1 502 "),
        "a vhost naming a socket the web user cannot reach must answer 502:\n{broken}"
    );

    forget_reloads();
    update_site(&runtime, &agent, site.same_version_request());

    // The positive control for the "no reload" check below: this call DID reload
    // both services, and the marker saw it.
    assert_eq!(
        reloaded_units().len(),
        2,
        "the converging call must have reloaded nginx and php-fpm; the stand-in recorded {:?}",
        reloaded_units()
    );
    let served = site.fetch_served();
    assert!(
        served.starts_with("HTTP/1.1 200 ") && served.contains(SERVED_BODY),
        "after one same-version update the site must be served again:\n{served}"
    );
    let pool = std::fs::read_to_string(&site.pool_path).expect("the pool must exist");
    let vhost = std::fs::read_to_string(&site.vhost_path).expect("the vhost must exist");
    assert!(
        pool.contains(NEW_DIRECTORY) && !pool.contains(OLD_DIRECTORY),
        "the pool must name {NEW_DIRECTORY} only:\n{pool}"
    );
    assert!(
        vhost.contains(NEW_DIRECTORY) && !vhost.contains(OLD_DIRECTORY),
        "the vhost must name {NEW_DIRECTORY} only:\n{vhost}"
    );
    assert_eq!(
        pool, site.fresh_pool,
        "the pool must be what a fresh render writes"
    );
    assert_eq!(
        vhost, site.fresh_vhost,
        "the vhost must be what a fresh render writes"
    );

    let before = site.file_state();
    forget_reloads();
    update_site(&runtime, &agent, site.same_version_request());

    assert_eq!(
        reloaded_units(),
        Vec::<String>::new(),
        "a second update of a converged site must reload nothing"
    );
    assert_eq!(
        site.file_state(),
        before,
        "a second update must leave both files byte-equal and untouched"
    );
    assert!(master.is_alive(), "the master must outlive both calls");
}

#[test]
#[ignore = "starts a real nginx and php-fpm and serves a PHP page: polygon only"]
fn a_site_already_on_the_new_socket_directory_stays_served_and_the_same_update_writes_nothing() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polysockcontrol");
    PolygonWeb::ensure_running(polygon_distro());
    let runtime = runtime();
    let agent = runtime.block_on(InProcessAgent::start());

    let site = ServedSite::create(&account, "control.example.test");
    let _master = PolygonPhpMaster::start(polygon_distro(), &site.pool_path, "control");
    PolygonWeb::reload(polygon_distro());

    let served = site.fetch_served();
    assert!(
        served.starts_with("HTTP/1.1 200 ") && served.contains(SERVED_BODY),
        "a site created on the new layout must serve:\n{served}"
    );

    let before = site.file_state();
    forget_reloads();
    update_site(&runtime, &agent, site.same_version_request());

    assert_eq!(
        site.file_state(),
        before,
        "updating a site that is already current must write neither file"
    );
    assert_eq!(
        reloaded_units(),
        Vec::<String>::new(),
        "updating a site that is already current must reload nothing"
    );
    let after = site.fetch();
    assert!(
        after.starts_with("HTTP/1.1 200 ") && after.contains(SERVED_BODY),
        "the site must still be served after the no-op update:\n{after}"
    );
}
