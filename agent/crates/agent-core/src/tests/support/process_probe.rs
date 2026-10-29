//! Test support for the spawn core's tests that look at real processes.
//!
//! Mounted once from `utils/mod.rs` under `#[cfg(test)]`, because its users are
//! the tests of four different units (`run_bounded`, `run_for_window`,
//! `run_unthreaded`, `launch_child`) and the same three helpers written four
//! times would be four chances to get "gone" wrong.
//!
//! What it offers: find a process by its EXACT argument vector in `/proc`, say
//! whether a process is gone (absent, or a zombie nobody reaped), poll a
//! condition with a timeout, kill what a test left behind, and re-enter the test
//! binary so a case can run its inner half under a poisoned environment or a
//! chosen standard input.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::process::Stdio;
use std::time::{Duration, Instant};

use rustix::process::{Pid, Signal, kill_process};

/// The stand-in that runs until it is killed.
pub(crate) const SLEEP: &str = "/usr/bin/sleep";

/// The stand-in that forks and execs [`SLEEP`], leaving a grandchild in the
/// leader's process group.
pub(crate) const FIND: &str = "/usr/bin/find";

/// A sleep length no other process can have: the case number and this test
/// process's pid, so a scan of `/proc` for the exact argument vector finds this
/// case's processes and nobody else's.
pub(crate) fn unique_length(case: u32) -> String {
    format!("{}.{}", 6000 + case, std::process::id())
}

/// The arguments of the leader of the grandchild scenario: `find` runs `sleep`
/// as its child and waits for it, with no shell involved.
pub(crate) fn find_sleep_arguments(length: &str) -> Vec<String> {
    ["/", "-maxdepth", "0", "-exec", SLEEP, length, ";"]
        .iter()
        .map(|part| (*part).to_owned())
        .collect()
}

/// Borrows a list of owned arguments as the `&[&str]` the spawn entry points take.
pub(crate) fn as_strs(owned: &[String]) -> Vec<&str> {
    owned.iter().map(String::as_str).collect()
}

/// The pids of every process whose argument vector is exactly `argv`.
pub(crate) fn pids_with_argv(argv: &[&str]) -> Vec<u32> {
    let mut wanted: Vec<u8> = Vec::new();
    for part in argv {
        wanted.extend_from_slice(part.as_bytes());
        wanted.push(0);
    }

    let mut found = Vec::new();
    let Ok(entries) = fs::read_dir("/proc") else {
        return found;
    };
    for entry in entries.flatten() {
        let name = entry.file_name();
        let Some(pid) = name.to_str().and_then(|text| text.parse::<u32>().ok()) else {
            continue;
        };
        // A process that exits between the listing and the read is simply absent.
        if let Ok(cmdline) = fs::read(format!("/proc/{pid}/cmdline"))
            && cmdline == wanted
        {
            found.push(pid);
        }
    }

    found
}

/// The pids of the grandchild scenario's `sleep` for `length`.
pub(crate) fn grandchild_pids(length: &str) -> Vec<u32> {
    pids_with_argv(&[SLEEP, length])
}

/// The pids of the grandchild scenario's `find` leader for `length`.
pub(crate) fn leader_pids(length: &str) -> Vec<u32> {
    let arguments = find_sleep_arguments(length);
    let mut argv = vec![FIND];
    argv.extend(as_strs(&arguments));

    pids_with_argv(&argv)
}

/// Whether `pid` is gone: absent from `/proc`, or a zombie (state `Z`).
///
/// A zombie counts because inside a container the test binary can be PID 1 and
/// reap no orphans, so a killed grandchild stays a zombie for ever there; that
/// has nothing to do with whether the kill worked.
pub(crate) fn is_gone(pid: u32) -> bool {
    let Ok(stat) = fs::read(format!("/proc/{pid}/stat")) else {
        return true;
    };

    state_of(&String::from_utf8_lossy(&stat)).is_some_and(|state| state == 'Z')
}

/// The process group of `pid`, read from `/proc/<pid>/stat`, or `None` when the
/// process is gone or the line cannot be read.
pub(crate) fn process_group_of(pid: u32) -> Option<u32> {
    let stat = fs::read(format!("/proc/{pid}/stat")).ok()?;
    let text = String::from_utf8_lossy(&stat).into_owned();
    let (_, after_name) = text.rsplit_once(')')?;

    // After the closing parenthesis: state, parent pid, process group.
    after_name.split_whitespace().nth(2)?.parse().ok()
}

/// The scheduler state letter of a `/proc/<pid>/stat` line.
fn state_of(stat: &str) -> Option<char> {
    let (_, after_name) = stat.rsplit_once(')')?;

    after_name.trim_start().chars().next()
}

/// Polls `condition` every ten milliseconds until it holds or `limit` passes.
///
/// Returns whether it held. This is the only waiting the tests do: a poll with a
/// timeout, never a sleep sized by hope.
pub(crate) fn wait_until(limit: Duration, mut condition: impl FnMut() -> bool) -> bool {
    let end = Instant::now() + limit;

    loop {
        if condition() {
            return true;
        }
        if Instant::now() >= end {
            return false;
        }
        std::thread::sleep(Duration::from_millis(10));
    }
}

/// Sends `SIGKILL` to the single process `pid`, ignoring every failure.
pub(crate) fn kill_pid(pid: u32) {
    if let Some(target) = i32::try_from(pid).ok().and_then(Pid::from_raw) {
        let _ = kill_process(target, Signal::KILL);
    }
}

/// Kills every process with the exact argument vector it holds when dropped, so
/// a failing assertion cannot leave a six-thousand-second `sleep` behind.
pub(crate) struct KillOnDrop {
    /// The exact argument vector to hunt for.
    pub(crate) argv: Vec<String>,
}

impl Drop for KillOnDrop {
    /// Kills every process whose argument vector is exactly the held one.
    fn drop(&mut self) {
        for pid in pids_with_argv(&as_strs(&self.argv)) {
            kill_pid(pid);
        }
    }
}

/// Re-enters this test binary to run the inner half of one case.
///
/// The outer half calls this with the case's full test path (`--exact` matches
/// the whole path, and a filter that matches nothing runs zero tests and exits
/// 0, which is why the output must say `1 passed`), the marker variable the
/// inner half looks for, the standard input to give it, and the environment to
/// plant. Only `std::process::Command` does this: the planted variables must
/// reach the INNER TEST PROCESS, so that the assertion is about what the core
/// takes away from a child, not about what a test harness is given.
pub(crate) fn run_inner_half(
    case: &str,
    marker: &str,
    marker_value: &str,
    stdin: Stdio,
    planted: &[(&str, &str)],
) {
    let output = std::process::Command::new(std::env::current_exe().expect("this test binary"))
        .args([case, "--exact", "--nocapture", "--test-threads=1"])
        .env(marker, marker_value)
        .envs(planted.iter().copied())
        .stdin(stdin)
        .output()
        .expect("the inner half of this case starts");

    let reported = String::from_utf8_lossy(&output.stdout);
    let quoted_report = quoted(&reported);
    assert!(
        output.status.success(),
        "the inner half failed:\n{quoted_report}\n{}",
        quoted(&String::from_utf8_lossy(&output.stderr))
    );
    assert!(
        reported.contains("1 passed"),
        "the inner half ran no test; {case} no longer names it:\n{quoted_report}"
    );
}

/// `text` with every line prefixed, so that the inner half's own libtest report,
/// quoted inside the outer half's failure message, is not read by a log parser
/// as a second test result of the outer run (`test ... FAILED`, `failures:`,
/// `test result:`). Each of those lines starts in column zero when it is the
/// real thing, and none of them does once quoted.
fn quoted(text: &str) -> String {
    text.lines()
        .map(|line| format!("    inner> {line}"))
        .collect::<Vec<_>>()
        .join("\n")
}
