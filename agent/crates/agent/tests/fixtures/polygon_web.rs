//! The polygon's own nginx, and a plain HTTP client for asking it questions.
//!
//! The helpers the PHP socket-migration suite needs from the web server: start
//! the master once, reload it, and fetch a page from it over a real socket. A
//! hand-written request rather than an HTTP client dependency, because one GET
//! with a `Host` header is the whole protocol this needs (rules/security.md
//! item 11 makes a new dependency something to justify).

use std::io::{Read as _, Write as _};
use std::path::Path;
use std::time::{Duration, Instant};

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;

/// How long one HTTP exchange against the polygon's nginx is given.
///
/// Bounded on purpose: a request to a socket nothing answers would otherwise
/// hang the run, and a hang is read as a flaky runner and retried.
const HTTP_TIMEOUT: Duration = Duration::from_secs(15);

/// How long [`PolygonWeb::fetch_until`] keeps asking before it reports what it got.
const SERVE_DEADLINE: Duration = Duration::from_secs(20);

/// How long [`PolygonWeb::fetch_until`] waits between two attempts.
const SERVE_POLL_INTERVAL: Duration = Duration::from_millis(100);

/// Linux reports process start times in units of 1/100 s on every architecture this runs on
/// (`USER_HZ`), whatever the kernel's own tick rate.
const CLOCK_TICKS_PER_SECOND: f64 = 100.0;

/// The directories whose files nginx's configuration reads, newest of which decides whether a
/// worker is current: the distribution's `conf.d`, the agent's include directory, the per-site
/// vhost directory beneath it and the directory the panel's certificate and key are read from.
const CONFIGURATION_DIRECTORIES: [&str; 4] = [
    "/etc/nginx/conf.d",
    "/etc/maran/nginx",
    "/etc/maran/nginx/sites",
    AgentPaths::PANEL_TLS_DIRECTORY,
];

/// The polygon's nginx: one unit, because starting it, reloading it and asking
/// it for a page are one subject — what this host's web server answers.
pub struct PolygonWeb;

impl PolygonWeb {
    /// Starts an nginx master if this run has not started one yet.
    ///
    /// Without a running master the stand-in service manager has nothing to
    /// signal and answers a reload with success, so the reload step would be a
    /// no-op no test could tell from a real one. Idempotent: starting a second
    /// master would fail on the listening socket.
    ///
    /// # Panics
    ///
    /// Panics when nginx cannot be started.
    pub fn ensure_running(distro: &dyn DistroAdapter) {
        if std::fs::read_to_string("/run/nginx.pid")
            .ok()
            .and_then(|pid| pid.trim().parse::<u32>().ok())
            .is_some_and(|pid| Path::new(&format!("/proc/{pid}")).exists())
        {
            return;
        }

        let started = std::process::Command::new(distro.nginx_binary())
            .output()
            .expect("the polygon image installs nginx");
        assert!(
            started.status.success(),
            "nginx must start in the polygon:\n{}",
            String::from_utf8_lossy(&started.stderr)
        );
    }

    /// Reloads the running nginx so the configuration on disk is the one served.
    ///
    /// # Panics
    ///
    /// Panics when nginx refuses the reload.
    pub fn reload(distro: &dyn DistroAdapter) {
        let reloaded = std::process::Command::new(distro.nginx_binary())
            .args(["-s", "reload"])
            .output()
            .expect("the polygon image installs nginx");
        assert!(
            reloaded.status.success(),
            "nginx must reload in the polygon:\n{}",
            String::from_utf8_lossy(&reloaded.stderr)
        );
    }

    /// Asks the polygon's nginx for `path` on `host` over a real socket and
    /// returns the whole response, status line first.
    ///
    /// # Panics
    ///
    /// Panics when nothing answers on port 80 within [`HTTP_TIMEOUT`].
    pub fn fetch(host: &str, path: &str) -> String {
        let mut stream = std::net::TcpStream::connect_timeout(
            &std::net::SocketAddr::from(([127, 0, 0, 1], 80)),
            HTTP_TIMEOUT,
        )
        .expect("nginx must be listening on port 80 in the polygon");
        stream
            .set_read_timeout(Some(HTTP_TIMEOUT))
            .expect("a read timeout can always be set on a connected socket");
        stream
            .set_write_timeout(Some(HTTP_TIMEOUT))
            .expect("a write timeout can always be set on a connected socket");

        // `Connection: close`, so the read ends at end-of-stream instead of
        // waiting out a keep-alive timeout on a response it already has.
        let request = format!("GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n");
        stream
            .write_all(request.as_bytes())
            .expect("the request must reach nginx");

        let mut response = Vec::new();
        stream
            .read_to_end(&mut response)
            .expect("nginx must answer within the timeout");

        String::from_utf8_lossy(&response).into_owned()
    }

    /// Asks for `path` on `host` until `accepted` is satisfied or the deadline
    /// passes, and returns the LAST response either way.
    ///
    /// It returns instead of panicking so a site that never becomes reachable
    /// fails at the call site with the wrong answer printed, not with a timeout
    /// message that says nothing about what nginx served.
    pub fn fetch_until(host: &str, path: &str, accepted: impl Fn(&str) -> bool) -> String {
        let deadline = Instant::now() + SERVE_DEADLINE;
        let mut last = Self::fetch(host, path);

        while !accepted(&last) && Instant::now() < deadline {
            std::thread::sleep(SERVE_POLL_INTERVAL);
            last = Self::fetch(host, path);
        }

        if accepted(&last) {
            Self::wait_until_the_previous_workers_are_gone();
            last = Self::fetch(host, path);
        }

        last
    }

    /// Waits until every nginx worker was started after the newest configuration file was written,
    /// or the deadline passes.
    ///
    /// A reload is asynchronous: the master re-reads the tree, starts new workers and only then
    /// tells the old ones to finish, and until they have, the old ones share the listening sockets
    /// and answer some connections from the OLD tree. One answer from the new tree therefore proves
    /// nothing about the next connection, which is how a check that follows "it is served" came to
    /// see a 404 for a site that had just been served, or the previous certificate, on a runner
    /// slow to retire its workers. Asked of the processes themselves, because nothing else says
    /// it: a worker started before the newest file under the directories nginx reads was started
    /// by a configuration that did not have that file, and one already told to finish says so in
    /// its title (`nginx: worker process is shutting down`). It returns after the deadline
    /// instead of panicking, and the caller's own assertion reports the wrong answer.
    pub fn wait_until_the_previous_workers_are_gone() {
        let deadline = Instant::now() + SERVE_DEADLINE;
        while Self::a_worker_predates_the_configuration() && Instant::now() < deadline {
            std::thread::sleep(SERVE_POLL_INTERVAL);
        }
    }

    /// Whether any nginx worker is retiring or was started before the newest configuration file.
    fn a_worker_predates_the_configuration() -> bool {
        let newest_age = CONFIGURATION_DIRECTORIES
            .iter()
            .filter_map(|directory| std::fs::read_dir(directory).ok())
            .flat_map(|entries| entries.flatten())
            .filter_map(|entry| entry.metadata().ok()?.modified().ok())
            .filter_map(|modified| modified.elapsed().ok())
            .min();
        let Ok(uptime) = std::fs::read_to_string("/proc/uptime") else {
            return false;
        };
        let Some(uptime) = uptime
            .split_whitespace()
            .next()
            .and_then(|seconds| seconds.parse::<f64>().ok())
        else {
            return false;
        };
        let Ok(processes) = std::fs::read_dir("/proc") else {
            return false;
        };

        processes.flatten().any(|process| {
            let path = process.path();
            let Ok(title) = std::fs::read(path.join("cmdline")) else {
                return false;
            };
            let title = String::from_utf8_lossy(&title);
            if !title.contains("nginx: worker process") {
                return false;
            }
            if title.contains("is shutting down") {
                return true;
            }
            // Field 22 of /proc/<pid>/stat is the start time in clock ticks since boot; the command
            // name before it may contain spaces, so the fields are counted from the closing bracket.
            let Some(started) = std::fs::read_to_string(path.join("stat"))
                .ok()
                .and_then(|stat| {
                    stat.rsplit_once(')')?
                        .1
                        .split_whitespace()
                        .nth(19)?
                        .parse::<f64>()
                        .ok()
                })
            else {
                return false;
            };
            let worker_age = uptime - started / CLOCK_TICKS_PER_SECOND;
            newest_age.is_some_and(|age| worker_age > age.as_secs_f64())
        })
    }
}
