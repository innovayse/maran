//! The host facts the local-submission rule stands on (issue #518).

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::local_submission_hazard::LocalSubmissionHazard;

/// The mode `postdrop` must have: setgid, world-executable, writable by root only.
const POSTDROP_MODE: u32 = 0o2755;

/// The settings section of the Rspamd rule, as `local.d/maran_local_submit.conf`
/// names it.
const RULE_SECTION: &str = "maran_local_submit";

/// One line of the group database.
struct GroupEntry<'a> {
    name: &'a str,
    gid: &'a str,
    members: Vec<&'a str>,
}

fn group_entries(text: &str) -> Vec<GroupEntry<'_>> {
    text.lines()
        .filter_map(|line| {
            let mut fields = line.split(':');
            let name = fields.next()?;
            let gid = fields.nth(1)?;
            let members = fields
                .next()
                .map(|list| {
                    list.split(',')
                        .filter(|member| !member.is_empty())
                        .collect()
                })
                .unwrap_or_default();
            Some(GroupEntry { name, gid, members })
        })
        .collect()
}

/// The login name and primary group id of the user with `uid`, if the passwd text has one.
fn user_with_uid(text: &str, uid: u32) -> Option<(&str, &str)> {
    let wanted = uid.to_string();
    text.lines().find_map(|line| {
        let mut fields = line.split(':');
        let name = fields.next()?;
        let user_id = fields.nth(1)?;
        let gid = fields.next()?;
        (user_id == wanted).then_some((name, gid))
    })
}

/// Refuses to bind local submission for the system user `uid` unless the maildrop
/// road is sealed: `postdrop` is mode 2755 owned by root and the drop group, and
/// the user is in neither the drop group nor the Postfix group (primary or
/// supplementary). Either break would let the user write the queue or reach the
/// filter without the uid stamp the rule trusts.
///
/// # Errors
///
/// Returns [`MailError::UnsafeLocalSubmission`] naming the hazard (including a uid
/// that is not in the user database), and
/// [`MailError::ConfigUnreadable`] when a database or `postdrop` exists and
/// cannot be inspected.
pub(crate) fn assert_drop_road_sealed(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    uid: u32,
) -> Result<(), MailError> {
    let passwd = host
        .read_config(Path::new(distro.passwd_database()))?
        .unwrap_or_default();
    let group_text = host
        .read_config(Path::new(distro.group_database()))?
        .unwrap_or_default();
    let groups = group_entries(&group_text);

    let drop_group = distro.postdrop_group();
    let Some(drop_entry) = groups.iter().find(|entry| entry.name == drop_group) else {
        return Err(hazard(LocalSubmissionHazard::DropGroupUnknown {
            group: drop_group.to_owned(),
        }));
    };

    let unsafe_binary = || {
        hazard(LocalSubmissionHazard::DropBinaryUnsafe {
            path: distro.postdrop_program().to_owned(),
            group: drop_group.to_owned(),
        })
    };
    match host.path_facts(Path::new(distro.postdrop_program()))? {
        Some(facts)
            if facts.mode == POSTDROP_MODE
                && facts.uid == 0
                && facts.gid.to_string() == drop_entry.gid => {}
        _ => return Err(unsafe_binary()),
    }

    // A uid the file does not list is NOT a safe user: the system user may come
    // from a directory service the agent does not read here, with groups the
    // check cannot see. Refuse rather than pass vacuously.
    let Some((login, primary_gid)) = user_with_uid(&passwd, uid) else {
        return Err(hazard(LocalSubmissionHazard::SubmitterUnknown { uid }));
    };
    for guarded in [drop_group, distro.postfix_user()] {
        let Some(entry) = groups.iter().find(|entry| entry.name == guarded) else {
            continue;
        };
        if entry.gid == primary_gid || entry.members.contains(&login) {
            return Err(hazard(LocalSubmissionHazard::MemberOfPrivilegedGroup {
                group: guarded.to_owned(),
            }));
        }
    }
    Ok(())
}

/// Refuses unless Rspamd reports the rule's settings after the reload.
///
/// `rspamadm configdump maran_local_submit` prints the section the reloaded
/// configuration holds. The rendered settings name the four map files, so an
/// output without them means the section was not picked up (the file renamed, the
/// include order changed): the Lua rule would then be registered only as its
/// refusing stand-in. The account is not admitted by `postdrop` in that case.
///
/// # Errors
///
/// Returns [`MailError::UnsafeLocalSubmission`] with
/// [`LocalSubmissionHazard::RuleNotLoaded`] when the tool fails or its output lacks
/// the settings, and the host's errors when the tool cannot be run.
pub(crate) fn assert_rule_loaded(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
) -> Result<(), MailError> {
    let outcome = host.run(
        &distro.rspamadm_binary(),
        &["configdump", RULE_SECTION],
        CallClass::Reload,
    )?;
    if outcome.status != 0 || !outcome.stdout.contains("accounts_map") {
        return Err(hazard(LocalSubmissionHazard::RuleNotLoaded));
    }
    Ok(())
}

fn hazard(hazard: LocalSubmissionHazard) -> MailError {
    MailError::UnsafeLocalSubmission { hazard }
}
