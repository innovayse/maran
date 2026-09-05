//! What a crontab keeps, what it rebuilds, and what reaches the line cron runs.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use crate::cron::create_cron_entry;
use crate::cron::delete_cron_entry;
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::crontab_document::CrontabDocument;
use crate::cron::recording_cron_host::{
    FIRST_ID, RecordingCronHost, account, assignment, command, distro, entry_id,
    every_five_minutes, schedule,
};
use crate::cron::set_cron_entry_enabled;
use crate::cron::set_cron_environment;
use crate::cron::update_cron_entry;

/// A crontab an administrator wrote by hand, with nothing of ours in it.
const FOREIGN: &str = "PATH=/usr/local/bin:/usr/bin\n\
                       # a note the administrator left\n\
                       \n\
                       30 4 * * 1 /opt/backup.sh --keep 7 %Y\n";

/// The banner that opens the region this agent owns.
const BANNER: &str = "# maran: managed section - every line below is rewritten by the panel";

/// The line that follows the marker for `id` in `text`.
fn entry_line_of(text: &str, id: &str) -> String {
    let lines: Vec<&str> = text.lines().collect();
    let marker = format!("# maran-entry: {id}");
    let at = lines
        .iter()
        .position(|line| *line == marker)
        .expect("the marker is in the table");

    (*lines.get(at + 1).expect("a line after the marker")).to_owned()
}

/// A document holding one enabled entry, rendered for the test account.
fn rendered_with_one_entry(command_text: &str) -> String {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command(command_text),
    )
    .expect("created");

    host.crontab().expect("a table was installed")
}

/// Every byte of the installed line comes from the agent, and none from the
/// command.
#[test]
fn the_installed_line_contains_no_caller_supplied_byte() {
    // The command is deliberately made of the bytes that broke the two earlier
    // designs on a real host — a `%`, which cron rewrites into a newline, and a
    // `#`, which starts a comment — plus quotes, a semicolon and a redirect.
    // None of them can matter, because none of them is on the line: the command
    // lives in a file and the line names the file.
    let hostile = "echo \"a%b\" # note; rm -rf / > /dev/null";
    let table = rendered_with_one_entry(hostile);
    let line = entry_line_of(&table, FIRST_ID);

    // The expectation is built from LITERALS, never from the same `account()`,
    // `entry_id()` and `every_five_minutes()` values that reach the line. An
    // earlier version derived it from those, which made the walk compare the
    // line against itself: a newline emitted by `CronSchedule`'s `Display`, or
    // a `..` surviving `AccountName`, would have appeared on both sides and
    // matched. Written out, a hostile byte in any of the three shows up here as
    // a mismatch rather than as a passing test.
    //
    // What the walk proves: the line's SHAPE, and that the command is absent
    // from it. What it delegates: the account name, the entry id and the
    // schedule DO reach the line, and that none of them can carry a hostile
    // byte is a property of `AccountName`, `CronEntryId` and `CronSchedule`,
    // each tested where it is defined.
    let command_file = format!("/home/alice/.maran/cron/{FIRST_ID}.cmd");
    let log_file = format!("/home/alice/.maran/cron/{FIRST_ID}.log");
    let exit_file = format!("/home/alice/.maran/cron/{FIRST_ID}.exit");

    // The literals are only worth asserting against because they are what the
    // agent's own sources really produce. Pinned here, so that a change to any
    // of them fails loudly instead of quietly making the walk vacuous.
    assert_eq!(every_five_minutes().to_string(), "*/5 * * * *");
    assert_eq!(distro().sh_binary(), "/bin/sh");
    assert_eq!(
        AgentPaths::cron_cmd_path(&account(), &entry_id(FIRST_ID))
            .display()
            .to_string(),
        command_file
    );

    let segments = [
        "*/5 * * * *",
        " ",
        "/bin/sh",
        " ",
        command_file.as_str(),
        " > ",
        log_file.as_str(),
        " 2>&1; echo $? > ",
        exit_file.as_str(),
    ];

    let mut rest = line.as_str();
    for segment in segments {
        assert!(
            rest.starts_with(segment),
            "the line diverges from its agent-derived text at {rest:?}"
        );
        rest = &rest[segment.len()..];
    }
    assert!(
        rest.is_empty(),
        "the line carries bytes no agent source accounts for: {rest:?}"
    );

    assert!(
        !line.contains("echo \"a%b\""),
        "the command must not reach the crontab: {line}"
    );
    assert!(!line.contains('%'), "a % on a cron line becomes a newline");
}

/// Every install re-emits an empty MAILTO and the platform's own sh.
#[test]
fn mailto_and_shell_are_always_rendered_empty_and_bin_sh() {
    // Rendered from a crontab that already sets both to something else, so the
    // test pins that ours replace them rather than that ours arrive first.
    let host = RecordingCronHost::with_crontab(
        "MAILTO=admin@example.com\nSHELL=/bin/bash\n# maran: managed section - every line below is rewritten by the panel\nMAILTO=admin@example.com\n",
    );

    set_cron_environment(&host, distro(), &account(), Vec::new()).expect("installed");

    let table = host.crontab().expect("a table was installed");
    let lines: Vec<&str> = table.lines().collect();
    let banner = lines
        .iter()
        .position(|line| *line == BANNER)
        .expect("the banner is in the table");

    assert_eq!(lines.get(banner + 1).copied(), Some("MAILTO=\"\""));
    assert_eq!(lines.get(banner + 2).copied(), Some("SHELL=/bin/sh"));
    // The pair above the banner is a foreign line and is left exactly as it
    // was; ours is the pair below it, which is what governs our own entries.
    assert_eq!(lines.first().copied(), Some("MAILTO=admin@example.com"));
}

/// A foreign line comes back byte for byte after every operation in the area.
#[test]
fn a_foreign_crontab_line_survives_every_mutation_byte_for_byte() {
    let host = RecordingCronHost::with_crontab(FOREIGN);
    let account = account();
    let id = entry_id(FIRST_ID);

    create_cron_entry(
        &host,
        distro(),
        &account,
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");
    assert_foreign_survived(&host, "after a creation");

    set_cron_entry_enabled(&host, distro(), &account, &id, false).expect("disabled");
    assert_foreign_survived(&host, "after a disable");

    update_cron_entry(
        &host,
        distro(),
        &account,
        &id,
        &schedule("0", "3", "*", "*", "*"),
        &command("echo two"),
    )
    .expect("updated");
    assert_foreign_survived(&host, "after an update");

    set_cron_environment(
        &host,
        distro(),
        &account,
        vec![assignment("TZ", "Europe/Yerevan")],
    )
    .expect("environment set");
    assert_foreign_survived(&host, "after an environment change");

    delete_cron_entry(&host, distro(), &account, &id).expect("deleted");
    assert_foreign_survived(&host, "after a deletion");
}

/// The foreign block is still the first bytes of the installed table.
fn assert_foreign_survived(host: &RecordingCronHost, when: &str) {
    let table = host.crontab().expect("a table was installed");
    assert!(
        table.starts_with(FOREIGN),
        "the foreign region changed {when}: {table:?}"
    );
}

/// A foreign assignment keeps its position, so it still governs what it did.
#[test]
fn a_foreign_env_assignment_stays_above_the_managed_region_and_below_nothing_new() {
    // Position and not merely bytes: a cron assignment applies to the lines
    // beneath it, so an agent line inserted above the foreign `PATH=` would
    // change which foreign entries that `PATH=` governs.
    let host = RecordingCronHost::with_crontab(FOREIGN);

    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("created");

    let table = host.crontab().expect("a table was installed");
    let lines: Vec<&str> = table.lines().collect();

    assert_eq!(
        lines.first().copied(),
        Some("PATH=/usr/local/bin:/usr/bin"),
        "nothing of ours may be written above a foreign assignment"
    );
    let banner = lines
        .iter()
        .position(|line| *line == BANNER)
        .expect("the banner is in the table");
    let foreign_entry = lines
        .iter()
        .position(|line| line.starts_with("30 4 * * 1"))
        .expect("the foreign entry is in the table");
    assert!(
        foreign_entry < banner,
        "the whole foreign region stays above the managed one"
    );
}

/// An empty crontab parses into an empty document rather than failing.
#[test]
fn an_empty_crontab_parses_into_an_empty_document() {
    let document = CrontabDocument::parse("");

    assert!(document.entries().is_empty());
    assert!(document.environment().is_empty());
}

/// A marker whose next line is not a schedule is two ordinary lines.
#[test]
fn a_marker_followed_by_prose_is_carried_across_as_foreign_text() {
    // Believing it would mean rewriting a line whose meaning was never
    // established; carrying it across changes nothing about what the host does.
    let text = format!("# maran-entry: {FIRST_ID}\nthis is not a schedule\n");

    let document = CrontabDocument::parse(&text);

    assert!(document.entries().is_empty());
    assert_eq!(
        document.render(&account(), "/bin/sh"),
        format!("{text}{BANNER}\nMAILTO=\"\"\nSHELL=/bin/sh\n")
    );
}

/// A marker with an id that is not a plain uuid is an ordinary comment.
#[test]
fn a_marker_carrying_something_that_is_not_a_uuid_is_not_an_entry() {
    let text = "# maran-entry: ../../etc/cron.d/evil\n* * * * * /bin/sh /tmp/x\n";

    let document = CrontabDocument::parse(text);

    assert!(document.entries().is_empty());
    assert!(document.render(&account(), "/bin/sh").starts_with(text));
}

/// A disabled entry is rendered as a comment cron cannot read.
#[test]
fn a_disabled_entry_renders_behind_the_off_prefix() {
    let mut document = CrontabDocument::parse("");
    document.append(CronEntry {
        id: entry_id(FIRST_ID),
        schedule: every_five_minutes(),
        enabled: false,
        command: None,
    });

    let line = entry_line_of(&document.render(&account(), "/bin/sh"), FIRST_ID);

    assert!(line.starts_with("#off# "), "cron must not read it: {line}");
}

/// A crontab this agent wrote parses back into the same document.
#[test]
fn a_rendered_crontab_parses_back_into_the_document_that_produced_it() {
    let mut document = CrontabDocument::parse(FOREIGN);
    document.set_environment(vec![assignment("TZ", "Europe/Yerevan")]);
    document.append(CronEntry {
        id: entry_id(FIRST_ID),
        schedule: every_five_minutes(),
        enabled: true,
        command: None,
    });

    let rendered = document.render(&account(), "/bin/sh");

    assert_eq!(CrontabDocument::parse(&rendered), document);
}

/// A crontab with no final newline gains one and loses nothing else.
#[test]
fn a_crontab_without_a_final_newline_gains_one_and_keeps_its_lines() {
    let document = CrontabDocument::parse("# a note with no terminator");

    let rendered = document.render(&account(), "/bin/sh");

    assert!(rendered.starts_with("# a note with no terminator\n"));
    assert!(rendered.ends_with('\n'));
}

/// A line appended below the banner is deleted, and the loss is deliberate.
#[test]
fn a_line_appended_below_the_banner_is_deleted_rather_than_moved_above_it() {
    // The realistic case, not an exotic one: appending a job at the bottom of
    // your own crontab is the most natural thing a shell user does. The banner
    // warns, and the alternative is worse — carrying the line up into the
    // foreign region would put it ABOVE our environment block, which for an
    // assignment silently changes which lines it governs. Pinned so the loss
    // stays a decision rather than becoming a surprise.
    let text = format!("{BANNER}\nPATH=/usr/local/bin\n# a note\n30 4 * * 1 /opt/backup.sh\n");

    let rendered = CrontabDocument::parse(&text).render(&account(), "/bin/sh");

    assert!(
        rendered.contains("PATH=/usr/local/bin"),
        "a valid assignment below the banner is adopted, not lost: {rendered}"
    );
    assert!(
        !rendered.contains("# a note"),
        "a comment below the banner is rewritten away: {rendered}"
    );
    assert!(
        !rendered.contains("/opt/backup.sh"),
        "a job below the banner is rewritten away: {rendered}"
    );
}

/// A forged marker makes the render replace the job written beneath it.
#[test]
fn a_forged_marker_rewrites_the_foreign_job_beneath_it() {
    // `parse` believes any syntactically valid uuid, not only one this agent
    // minted, so a marker an administrator pastes above their own job adopts
    // that job — and the render then replaces its command with a line naming a
    // `.cmd` file that does not exist. The counterpart, a marker followed by
    // prose, is refused and has its own test; this half is the one that costs
    // something, so it is pinned rather than described.
    let text = format!("# maran-entry: {FIRST_ID}\n30 4 * * 1 /opt/backup.sh --keep 7\n");

    let document = CrontabDocument::parse(&text);
    let rendered = document.render(&account(), "/bin/sh");

    assert_eq!(document.entries().len(), 1, "the marker was believed");
    assert!(
        !rendered.contains("/opt/backup.sh"),
        "the adopted job's own command is replaced: {rendered}"
    );
    assert!(
        rendered.contains(&format!("/home/alice/.maran/cron/{FIRST_ID}.cmd")),
        "and replaced by a line naming this agent's command file: {rendered}"
    );
}

/// A crontab written with carriage returns still finds its managed entries.
#[test]
fn a_crontab_with_windows_line_endings_still_finds_its_managed_entries() {
    // Without this the banner comparison fails, every `# maran-entry: <id>\r`
    // fails `CronEntryId::parse`, and the whole managed region orphans into the
    // foreign text: the panel lists nothing for an account whose entries are
    // still firing, `delete` answers "not found", and the next install writes a
    // SECOND banner below the first.
    let text = format!(
        "PATH=/opt/bin\r\n{BANNER}\r\nMAILTO=\"\"\r\nSHELL=/bin/sh\r\nTZ=UTC\r\n# maran-entry: {FIRST_ID}\r\n*/5 * * * * /bin/sh /home/alice/.maran/cron/{FIRST_ID}.cmd\r\n"
    );

    let document = CrontabDocument::parse(&text);

    assert_eq!(document.entries().len(), 1, "the entry is found");
    assert_eq!(document.entries()[0].id, entry_id(FIRST_ID));
    assert_eq!(document.environment().len(), 1, "and so is the assignment");
    let rendered = document.render(&account(), "/bin/sh");
    assert_eq!(
        rendered.matches(BANNER).count(),
        1,
        "exactly one banner, never a second below the first: {rendered}"
    );
    // The foreign line keeps its own bytes, carriage return included: the law
    // is about what this agent did not write, and it still holds exactly.
    assert!(rendered.starts_with("PATH=/opt/bin\r\n"));
}

/// A duplicated managed block is disabled in full, not in part.
#[test]
fn disabling_an_entry_stops_every_copy_of_it() {
    // An account with shell access can read its own id out of `crontab -l` and
    // paste a second copy of the block. Changing only the first would leave the
    // panel reporting the entry disabled while a copy of it kept running —
    // the panel asserting a state the host does not have, on exactly the
    // operation an operator reaches for when a job is misbehaving.
    let block = format!("# maran-entry: {FIRST_ID}\n*/5 * * * * /bin/sh /tmp/x\n");
    let mut document = CrontabDocument::parse(&format!("{block}{block}"));
    assert_eq!(document.entries().len(), 2, "both copies were adopted");

    assert!(document.set_enabled(&entry_id(FIRST_ID), false));

    let rendered = document.render(&account(), "/bin/sh");
    assert_eq!(
        rendered.matches("#off# ").count(),
        2,
        "every copy is commented out, not the first: {rendered}"
    );
    assert!(
        !rendered
            .lines()
            .any(|line| line.starts_with("*/5 * * * * ")),
        "no copy is left running: {rendered}"
    );
}

/// A duplicated managed block is rescheduled in full, not in part.
#[test]
fn rescheduling_an_entry_moves_every_copy_of_it() {
    let block = format!("# maran-entry: {FIRST_ID}\n*/5 * * * * /bin/sh /tmp/x\n");
    let mut document = CrontabDocument::parse(&format!("{block}{block}"));

    assert!(document.set_schedule(&entry_id(FIRST_ID), &schedule("0", "3", "*", "*", "*")));

    let rendered = document.render(&account(), "/bin/sh");
    assert_eq!(
        rendered.matches("0 3 * * * ").count(),
        2,
        "every copy moves: {rendered}"
    );
    assert!(
        !rendered.contains("*/5 * * * * "),
        "no copy keeps the old schedule: {rendered}"
    );
}
