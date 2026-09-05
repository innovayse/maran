//! Per-account crontabs against the real `crontab(1)` and a real cron daemon,
//! which is the only place `ops::cron` means anything.
//!
//! Three claims are settled here and nowhere else, and every one of them is a
//! claim about a program's behaviour that a fake host answers by agreeing with
//! whatever the code assumed:
//!
//! - **`crontab(1)` accepts the rendered table, on both families.** The
//!   document renders a banner, a `MAILTO`, a `SHELL`, marker lines and one
//!   command line per entry. Every unit test in `ops::cron` asserts what that
//!   text SAYS; only the program can say whether it will take it. Two cron
//!   lineages ship under that name, and this suite runs against both.
//! - **`no crontab for` is genuinely what an empty account prints.**
//!   `ProcessCronHost` matches that STRING rather than an exit status, on the
//!   argument that the sentence is documented and the status is not. That is a
//!   bet on a program's wording, and this is the assertion that comes back red
//!   the day it changes — before every untouched account starts reporting an
//!   error instead of an empty list.
//! - **A command containing `#` and `%` really runs, and its output is really
//!   captured.** Those two characters killed two earlier designs: cron rewrites
//!   the first unescaped `%` on its line into a newline, and `#` starts a
//!   comment. The current design keeps the command out of the crontab
//!   altogether, and the only way to know that worked is to let a real daemon
//!   execute a real entry and read what came back.
//!
//! The daemon is started by the suite's own fixture as a real foreground
//! process (`PolygonCron`), because a stand-in for cron would make the claim
//! "cron runs this" circular.
//!
//! These tests need `docker run --privileged`. On the Debian family cron's PAM
//! stack includes `pam_loginuid`, which needs a capability an unprivileged
//! container does not have; without it the daemon refuses to authorise the
//! account and nothing runs — the tests then fail loudly rather than passing on
//! an entry that was never executed.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_cron.rs"]
mod polygon_cron;

use std::os::unix::fs::MetadataExt as _;
use std::path::PathBuf;
use std::process::Command;
use std::time::{Duration, SystemTime};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::cron::{
    CronError, ProcessCronHost, create_cron_entry, delete_cron_entry, get_cron_entry_output,
    list_cron_entries, set_cron_entry_enabled, update_cron_entry,
};

use polygon_account::PolygonAccount;
use polygon_cron::{CRON_TICK_DEADLINE, PolygonCron};

/// The sentence both cron lineages print for an account with no table.
///
/// The same constant `ProcessCronHost` matches on, written out again here on
/// purpose: a test that imported the private constant would agree with the
/// implementation by construction and could never catch a change in the
/// program. This one is compared against what the real tool printed.
const NO_CRONTAB_MARKER: &str = "no crontab for";

/// The schedule every timed entry in this suite uses: every minute.
fn every_minute() -> CronSchedule {
    CronSchedule::parse("*", "*", "*", "*", "*").expect("a valid schedule")
}

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

/// Installs an entry through the operation under test, and returns its id.
///
/// # Panics
///
/// Panics when the operation refuses.
fn install(account: &AccountName, command: &str) -> CronEntryId {
    create_cron_entry(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account,
        &every_minute(),
        &CronCommand::parse(command).expect("a valid command"),
    )
    .unwrap_or_else(|error| panic!("installing a cron entry must succeed: {error}"))
}

/// What `crontab -u <account> -l` printed, both streams and the status.
///
/// Run as a separate process on purpose: it asks the same program the agent
/// asks, through its documented interface, rather than reading the spool
/// directly — where that spool lives and what owns it is the program's business
/// on each family, which is the whole reason `ops::cron` shells out to it.
fn crontab_of(account: &AccountName) -> std::process::Output {
    Command::new(polygon_distro().crontab_binary())
        .args(["-u", account.as_str(), "-l"])
        .output()
        .expect("the polygon image installs crontab")
}

/// The installed table as text, asserting the program answered.
fn installed_table(account: &AccountName) -> String {
    let listed = crontab_of(account);
    assert!(
        listed.status.success(),
        "crontab -l must succeed for an account with a table: {}",
        String::from_utf8_lossy(&listed.stderr)
    );

    String::from_utf8_lossy(&listed.stdout).into_owned()
}

/// A path under the account's own cron directory, for a file a test's command
/// writes.
fn sentinel_path(account: &AccountName, name: &str) -> PathBuf {
    AgentPaths::account_cron_dir(account).join(name)
}

/// Removes any crontab left over for `account` by an earlier run.
///
/// A crontab is keyed by NAME, not by uid, and whether `userdel` takes one with
/// it is a detail of each family's shadow package. These suites reuse their
/// account names on every run, so a table that outlived its account would be
/// read by the next run as state the test had created — the shape of shared
/// fixture rules/testing.md forbids. Removing it is cheap and removes the
/// question; a failure is ignored because "there was nothing to remove" is the
/// ordinary case.
fn clear_crontab(account: &AccountName) {
    let _ = Command::new(polygon_distro().crontab_binary())
        .args(["-u", account.as_str(), "-r"])
        .output();
}

/// Everything in the account's cron directory, for a failure message.
///
/// What a timed test needs when it goes red is which of two things happened:
/// cron never ran the line, or it ran it and the command failed. The first
/// leaves no `.log` and no `.exit`; the second leaves both, and the `.exit`
/// holds the status. Printing the directory says which.
fn entry_directory(account: &AccountName) -> String {
    let directory = AgentPaths::account_cron_dir(account);
    let Ok(entries) = std::fs::read_dir(&directory) else {
        return format!("{} could not be read at all", directory.display());
    };

    let mut described = format!("{} holds:\n", directory.display());
    for entry in entries.flatten() {
        let path = entry.path();
        let size = entry.metadata().map(|data| data.len()).unwrap_or_default();
        let content = std::fs::read_to_string(&path).unwrap_or_default();
        described.push_str(&format!(
            "  {} ({size} bytes) {:?}\n",
            path.display(),
            content.chars().take(120).collect::<String>()
        ));
    }

    described
}

#[test]
#[ignore = "installs a real crontab through the real crontab(1): polygon only"]
fn crontab_accepts_the_rendered_table_and_the_command_lives_in_a_file_beside_it() {
    PolygonCron::require_polygon();
    let account = PolygonAccount::create("polycronone");
    clear_crontab(account.name());
    let command = "echo scheduled";

    let entry = install(account.name(), command);
    let table = installed_table(account.name());

    // 1. The program took the table. Everything below only means anything
    //    because this succeeded: a refused install leaves the previous table
    //    live, so an assertion on the file contents would be describing a
    //    document cron never saw.
    assert!(
        table.contains("# maran-entry: "),
        "the installed table must carry the marker the parser reads back:\n{table}"
    );

    // 2. The line cron reads names the command FILE and carries no byte of the
    //    command. This is the design's central claim, and it is asserted
    //    against what the program stored rather than against what the renderer
    //    returned.
    let command_file = AgentPaths::cron_cmd_path(account.name(), &entry);
    assert!(
        table.contains(&format!("{}", command_file.display())),
        "the installed line must name the command file:\n{table}"
    );
    assert!(
        !table.contains(command),
        "the customer's command must not appear in the crontab at all:\n{table}"
    );
    assert!(
        table.contains(polygon_distro().sh_binary()),
        "the line must name the interpreter by absolute path:\n{table}"
    );

    // 3. The command file holds the command verbatim, and belongs to the
    //    account rather than to root — a root-owned file in a 0700 directory
    //    the customer owns is a file the customer cannot fix and cron reads.
    let stored = std::fs::read_to_string(&command_file).expect("the command file must exist");
    assert_eq!(stored, format!("{command}\n"));

    let owner = std::fs::metadata(&command_file).expect("the command file must exist");
    assert_eq!(
        owner.uid(),
        account.ids().uid(),
        "the command file must belong to the account"
    );
    // And it is under the home the SYSTEM created for this account, not merely
    // under a path the agent composed. The two agree only because `AgentPaths`'
    // home root matches what `useradd` did; a mismatch would put every
    // customer's commands in a directory nothing owns.
    assert!(
        AgentPaths::account_cron_dir(account.name()).starts_with(account.home()),
        "the entry's files must live under the account's real home ({})",
        account.home().display()
    );
    assert_eq!(
        owner.mode() & 0o777,
        0o600,
        "the command file holds a customer's command and nothing else may read it"
    );

    // 4. And the listing reads back what was installed, through the same
    //    program — including the command, which it has to fetch from the file.
    let listed = list_cron_entries(&ProcessCronHost::new(polygon_distro()), account.name())
        .expect("listing an account with a table must succeed");
    assert_eq!(listed.len(), 1);
    assert_eq!(listed[0].id, entry);
    assert_eq!(listed[0].command.as_deref(), Some(command));
    assert!(listed[0].enabled);
}

#[test]
#[ignore = "asks the real crontab(1) about an untouched account: polygon only"]
fn an_account_with_no_table_prints_no_crontab_for_and_lists_as_empty() {
    PolygonCron::require_polygon();
    let account = PolygonAccount::create("polycrontwo");
    clear_crontab(account.name());

    // THE assertion behind a design decision. `ProcessCronHost` reads "this
    // account has no crontab" out of the program's SENTENCE rather than its
    // exit status, because the sentence is what both lineages document and the
    // status is not. That is a bet, and this is where it is checked against the
    // program actually installed on this family — it comes back red the day
    // either lineage rewords the message, which is exactly when every untouched
    // account would otherwise start reporting an error instead of an empty
    // list.
    let listed = crontab_of(account.name());
    let said = format!(
        "{}{}",
        String::from_utf8_lossy(&listed.stdout),
        String::from_utf8_lossy(&listed.stderr)
    );
    assert!(
        said.contains(NO_CRONTAB_MARKER),
        "an account with no crontab must still print {NO_CRONTAB_MARKER:?}; \
         this family printed: {said:?}"
    );

    // And the agent reads that as an empty list rather than as a failure.
    let entries = list_cron_entries(&ProcessCronHost::new(polygon_distro()), account.name())
        .expect("an account with no crontab lists as empty, never as an error");
    assert!(entries.is_empty());
}

#[test]
#[ignore = "lets a real cron daemon execute real entries; takes about a minute: polygon only"]
fn cron_runs_what_the_agent_installed_captures_its_output_and_leaves_a_disabled_entry_alone() {
    PolygonCron::require_polygon();
    let account = PolygonAccount::create("polycronthree");
    clear_crontab(account.name());

    // Three entries, installed together and waited for once. Separately they
    // would cost a cron minute each; together the enabled pair is the positive
    // control for the disabled one, in the same tick.
    let plain = sentinel_path(account.name(), "plain.sentinel");
    let awkward = sentinel_path(account.name(), "awkward.sentinel");
    let disabled = sentinel_path(account.name(), "disabled.sentinel");

    let running = install(
        account.name(),
        &format!("echo ran > {} && echo captured", plain.display()),
    );

    // The two characters that killed two earlier designs, in one command, each
    // placed where it would BREAK the line if the command were inlined into the
    // crontab — which is the only placement that tests anything.
    //
    // `#` comes before the redirection, not after it. A trailing `# comment`
    // proves nothing: truncating there still leaves `echo … > file` intact and
    // the sentinel is written anyway, so the assertion would pass against the
    // very design this test exists to rule out. Here, truncation at `#` leaves
    // `echo 100%` with no redirection at all and no sentinel is written.
    //
    // `%` is what cron rewrites into a newline on its own line, and it sits in
    // the text that must reach the file. Both are ordinary characters only
    // because the command never reaches the crontab at all.
    let killers = install(
        account.name(),
        &format!(
            "echo 100% '#' not-a-comment > {} && echo captured-too",
            awkward.display()
        ),
    );

    let switched_off = install(
        account.name(),
        &format!("echo ran > {}", disabled.display()),
    );
    set_cron_entry_enabled(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account.name(),
        &switched_off,
        false,
    )
    .expect("disabling an entry must succeed");

    // The disabled entry's line is commented out in the table the program
    // holds, which is what stops cron reading its schedule at all.
    let table = installed_table(account.name());
    assert!(
        table.contains("#off# "),
        "a disabled entry must be commented out in the installed table:\n{table}"
    );

    // The daemon starts HERE, after the tables are installed, and the ordering
    // is deliberate. Started first, this test was FLAKY: measured on the
    // ubuntu24 polygon, one run in three never executed the entry within 90
    // seconds while the other two fired in 5 and 22 — the spread of "the next
    // minute boundary" plus one run where the boundary came and went. What is
    // racy there is cron NOTICING a table installed while it is already
    // running, which is the daemon's own reload detection and not anything the
    // agent does. Started after, cron reads a complete spool at startup and the
    // proposition under test is untouched: a real daemon, executing a real line
    // this agent installed, as the account.
    //
    // A flaky test is a P1 bug (rules/testing.md), so this is a fix rather than
    // a retry loop — and what it costs is stated: nothing here now exercises
    // cron's reload path, which IS the production path when the panel adds an
    // entry to a host whose cron is up. That path is cron's to get right.
    let _daemon = PolygonCron::start();

    // 1. Cron reached the enabled entries.
    //
    //    The failure message carries the entry's own directory, because the two
    //    ways this can fail need different answers and look identical from the
    //    sentinel alone: cron never ran the line at all (no `.exit`, no `.log`),
    //    or it ran it and the command failed (an `.exit` holding a status). A
    //    test that only said "the file is not there" would send its reader to
    //    the wrong half.
    assert!(
        PolygonCron::wait_for(&plain),
        "cron must run an installed `* * * * *` entry within {CRON_TICK_DEADLINE:?}; \
         nothing was written to {}.\n{}\nthe daemon said:\n{}",
        plain.display(),
        entry_directory(account.name()),
        PolygonCron::log()
    );
    assert!(
        PolygonCron::wait_for(&awkward),
        "a command carrying `%` and `#` must run too; nothing was written to {}.\n{}",
        awkward.display(),
        entry_directory(account.name())
    );

    // 2. The command ran AS THE ACCOUNT. A sentinel owned by root would mean
    //    cron executed the line with the wrong identity, which is the failure
    //    that turns a customer's scheduled task into a root shell.
    let owner = std::fs::metadata(&plain).expect("the sentinel must exist");
    assert_eq!(
        owner.uid(),
        account.ids().uid(),
        "the entry must have run as the account"
    );

    // 3. Both characters survived into the file the command wrote. Under
    //    either of the two disproved designs there would be nothing to read:
    //    truncation at `#` drops the redirection along with the rest of the
    //    line, and a `%` rewritten into a newline cuts the command in half
    //    before it reaches the redirection either. The sentinel existing at all
    //    is most of the assertion; its CONTENTS are the rest, because they show
    //    both characters arriving at the shell as ordinary text.
    let captured =
        std::fs::read_to_string(&awkward).expect("the awkward entry's sentinel must exist");
    assert_eq!(
        captured.trim(),
        "100% # not-a-comment",
        "both `%` and `#` must reach the shell as ordinary text"
    );

    // 4. The agent reads the run back: the output tail, the exit status, and
    //    when it finished. All three come from files the run left behind.
    let output = get_cron_entry_output(
        &ProcessCronHost::new(polygon_distro()),
        account.name(),
        &running,
    )
    .expect("reading a run's output must succeed");
    assert_eq!(
        output.output.as_deref().map(str::trim),
        Some("captured"),
        "the output tail must return what the command printed"
    );

    let record = output.last_run.expect("an entry that ran has a run record");
    assert_eq!(record.exit_code, Some(0), "the command exited successfully");
    let age = SystemTime::now()
        .duration_since(record.ran_at)
        .expect("the run finished before now");
    assert!(
        age < Duration::from_secs(600),
        "the run timestamp must be the run's own, not the epoch: {age:?} old"
    );

    // 5. And the disabled entry did NOT run — asserted only now, after the tick
    //    the other two were executed in has demonstrably happened. Asserting it
    //    on its own would pass on a host where cron never ran at all.
    assert!(
        !disabled.exists(),
        "a disabled entry must not run: {} was written",
        disabled.display()
    );

    // Belt: it is disabled, not deleted. The panel still lists it, so a
    // customer can switch it back on.
    let listed = list_cron_entries(&ProcessCronHost::new(polygon_distro()), account.name())
        .expect("listing must succeed");
    assert_eq!(listed.len(), 3);
    assert!(
        listed
            .iter()
            .any(|entry| entry.id == switched_off && !entry.enabled),
        "the disabled entry must still be listed, and listed as disabled"
    );
    assert_eq!(listed.iter().filter(|entry| entry.enabled).count(), 2);
    assert!(listed.iter().any(|entry| entry.id == killers));
}

#[test]
#[ignore = "rewrites a real crontab that already had a line in it: polygon only"]
fn a_line_the_account_wrote_itself_survives_every_managed_change() {
    PolygonCron::require_polygon();
    let account = PolygonAccount::create("polycronfour");
    clear_crontab(account.name());

    // A table the ACCOUNT could have written: a comment, an environment
    // assignment, and an entry of its own. Installed through the same program
    // the agent uses, so what the agent later reads is what the program stored.
    let foreign = "# my own notes\nPATH=/usr/local/bin:/usr/bin:/bin\n30 4 * * * /bin/true\n";
    let staged = std::env::temp_dir().join("maran-polygon-foreign-crontab");
    std::fs::write(&staged, foreign).expect("a table to install");
    let installed = Command::new(polygon_distro().crontab_binary())
        .args(["-u", account.name().as_str()])
        .arg(&staged)
        .output()
        .expect("the polygon image installs crontab");
    assert!(
        installed.status.success(),
        "the foreign table must install: {}",
        String::from_utf8_lossy(&installed.stderr)
    );

    // Every managed change in turn, over the top of it.
    let entry = install(account.name(), "echo managed");
    update_cron_entry(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account.name(),
        &entry,
        &CronSchedule::parse("15", "3", "*", "*", "*").expect("a valid schedule"),
        &CronCommand::parse("echo updated").expect("a valid command"),
    )
    .expect("updating must succeed");

    let after_update = installed_table(account.name());
    for line in [
        "# my own notes",
        "PATH=/usr/local/bin:/usr/bin:/bin",
        "30 4 * * * /bin/true",
    ] {
        assert!(
            after_update.contains(line),
            "the account's own line {line:?} must survive a managed change:\n{after_update}"
        );
    }
    // Position, not merely presence: a cron environment assignment governs the
    // lines BELOW it, so a foreign `PATH=` moved under the agent's own block
    // would silently start governing different entries.
    let notes = after_update
        .find("# my own notes")
        .expect("the comment survives");
    let path = after_update
        .find("PATH=/usr/local")
        .expect("the assignment survives");
    let own = after_update.find("30 4 * * *").expect("the entry survives");
    let banner = after_update
        .find("# maran:")
        .expect("the agent's region is there");
    assert!(
        notes < path && path < own && own < banner,
        "the account's lines must keep their order and stay above the managed region:\n{after_update}"
    );

    // And after the managed entry is deleted the foreign table is what is left.
    delete_cron_entry(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account.name(),
        &entry,
    )
    .expect("deleting must succeed");

    let after_delete = installed_table(account.name());
    assert!(after_delete.contains("30 4 * * * /bin/true"));
    assert!(
        !after_delete.contains("# maran-entry: "),
        "no managed entry may be left behind:\n{after_delete}"
    );

    // A second deletion converges on NotFound rather than failing, which is
    // what makes a retry after a lost response safe.
    let again = delete_cron_entry(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account.name(),
        &entry,
    );
    assert!(
        matches!(again, Err(CronError::NotFound)),
        "a repeated deletion must converge, got {again:?}"
    );

    let _ = std::fs::remove_file(&staged);
}
