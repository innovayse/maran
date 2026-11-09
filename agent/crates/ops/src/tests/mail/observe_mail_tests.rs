//! The readings beside a mail state, asked of the daemons' own tools.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::command_outcome::CommandOutcome;

use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro};
use crate::mail::get_mail_status::get_mail_status;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::observe_mail::{
    count_queued_messages, observe_mail, scheme_is_listed, sieve_in_usage,
};

fn observe(host: &FakeMailHost) -> crate::mail::model::observed_mail::ObservedMail {
    let layout = MailLayout::resolve(distro(), VERSION_ID).unwrap();
    let state = get_mail_status(host, distro(), &layout).unwrap();
    observe_mail(host, distro(), state)
}

fn running_stack() -> FakeMailHost {
    FakeMailHost::new()
        .with_running_unit("redis-server")
        .with_running_unit(distro().rspamd_service())
        .with_running_unit(distro().dovecot_service())
        .with_running_unit(distro().postfix_service())
}

#[test]
fn versions_are_read_from_the_daemons_own_tools() {
    let host = running_stack()
        .with_tool_outcome(
            &distro().postconf_binary(),
            &["-h", "mail_version"],
            0,
            "3.8.6\n",
        )
        .with_tool_outcome(
            &distro().doveadm_binary(),
            &["--version"],
            0,
            "2.3.19.1 (9b53102964)\n",
        )
        .with_tool_outcome(
            &distro().rspamadm_binary(),
            &["--version"],
            0,
            "Rspamadm 3.8.4\n",
        );

    let details = observe(&host).details;

    assert_eq!(details.postfix_version.unwrap().text(), "3.8.6");
    let dovecot = details.dovecot_version.unwrap();
    assert_eq!((dovecot.major(), dovecot.minor()), (2, 3));
    assert_eq!(details.rspamd_version.unwrap().text(), "3.8.4");
}

#[test]
fn a_tool_that_prints_no_version_leaves_the_version_unobserved() {
    let host = running_stack();

    let details = observe(&host).details;

    assert_eq!(details.postfix_version, None);
    assert_eq!(details.dovecot_version, None);
    assert_eq!(details.rspamd_version, None);
}

#[test]
fn a_tool_that_exits_non_zero_leaves_its_reading_unobserved() {
    let host = running_stack().with_tool_outcome(
        &distro().doveadm_binary(),
        &["--version"],
        64,
        "2.3.19.1\n",
    );

    assert_eq!(observe(&host).details.dovecot_version, None);
}

#[test]
fn a_missing_program_is_not_asked() {
    let host = FakeMailHost::new().without_rspamd();
    let rspamadm = distro().rspamadm_binary();

    observe(&host);

    let asked_rspamadm = host
        .spawns()
        .iter()
        .any(|spawn| spawn.argv.first().map(String::as_str) == Some(rspamadm.as_str()));
    assert!(!asked_rspamadm);
}

#[test]
fn the_password_scheme_is_listed_only_when_the_imap_server_says_so() {
    let scheme = distro().mail_password_scheme().dovecot_name();
    let listing = format!("CRYPT MD5 {scheme} SHA256\n");
    let doveadm = distro().doveadm_binary();
    let listed = running_stack().with_tool_outcome(&doveadm, &["pw", "-l"], 0, &listing);
    let unlisted = running_stack().with_tool_outcome(&doveadm, &["pw", "-l"], 0, "CRYPT MD5\n");

    assert_eq!(observe(&listed).details.password_scheme_listed, Some(true));
    assert_eq!(
        observe(&unlisted).details.password_scheme_listed,
        Some(false)
    );
}

#[test]
fn a_scheme_listing_that_failed_is_unobserved_and_not_negative() {
    let host = running_stack().with_tool_outcome(&distro().doveadm_binary(), &["pw", "-l"], 1, "");

    assert_eq!(observe(&host).details.password_scheme_listed, None);
}

#[test]
fn a_scheme_is_matched_as_a_whole_word() {
    assert!(scheme_is_listed("CRYPT BLF-CRYPT", "BLF-CRYPT"));
    assert!(scheme_is_listed("crypt blf-crypt", "BLF-CRYPT"));
    assert!(!scheme_is_listed("CRYPT SHA256-CRYPT", "BLF-CRYPT"));
    assert!(!scheme_is_listed("", "BLF-CRYPT"));
}

#[test]
fn the_queue_is_counted_one_message_per_line() {
    let listing = "{\"queue_id\":\"A1\"}\n{\"queue_id\":\"B2\"}\n\n{\"queue_id\":\"C3\"}\n";
    let host = running_stack().with_tool_outcome(&distro().postqueue_binary(), &["-j"], 0, listing);

    assert_eq!(observe(&host).details.queue_messages, Some(3));
}

#[test]
fn an_empty_queue_is_zero_messages_and_not_an_absent_reading() {
    let host = running_stack().with_tool_outcome(&distro().postqueue_binary(), &["-j"], 0, "");

    assert_eq!(observe(&host).details.queue_messages, Some(0));
    assert_eq!(count_queued_messages(""), 0);
}

#[test]
fn a_stopped_mta_has_no_queue_reading() {
    let postqueue = distro().postqueue_binary();
    let host = FakeMailHost::new().with_tool_outcome(&postqueue, &["-j"], 0, "{}\n");

    assert_eq!(observe(&host).details.queue_messages, None);
}

#[test]
fn a_queue_listing_that_failed_is_absent_and_not_zero() {
    let host = running_stack().with_tool_outcome(&distro().postqueue_binary(), &["-j"], 69, "");

    assert_eq!(observe(&host).details.queue_messages, None);
}

fn usage(stdout: &str, stderr: &str) -> CommandOutcome {
    CommandOutcome {
        status: 64,
        stdout: stdout.to_owned(),
        stderr: stderr.to_owned(),
    }
}

#[test]
fn sieve_is_available_when_the_usage_text_lists_its_commands() {
    let outcome = usage(
        "",
        "usage: doveadm [-Dv] <command>\n  sieve list [-u user]\n",
    );

    assert_eq!(sieve_in_usage(&outcome), Some(true));
}

#[test]
fn sieve_is_not_available_when_a_usage_text_lacks_its_commands() {
    let outcome = usage("", "usage: doveadm [-Dv] <command>\n  mailbox list\n");

    assert_eq!(sieve_in_usage(&outcome), Some(false));
}

#[test]
fn sieve_is_unobserved_when_the_tool_printed_nothing_recognisable() {
    assert_eq!(sieve_in_usage(&usage("", "")), None);
    assert_eq!(sieve_in_usage(&usage("segmentation fault", "")), None);
}

#[test]
fn the_sieve_question_is_asked_through_the_hosts_tool() {
    let host = running_stack().with_tool_outcome(
        &distro().doveadm_binary(),
        &["sieve"],
        64,
        "usage: doveadm\n  sieve put\n  sieve list\n",
    );

    assert_eq!(observe(&host).details.sieve_available, Some(true));
}
