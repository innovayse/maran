//! Bringing one daemon to its rendered configuration and starting it.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_stage::MailStage;
use crate::mail::model::mail_unit::MailUnit;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};

/// Writes `stage`'s files, has the daemon's own check read them where it reads
/// them, starts or reloads the unit, and puts it in the boot sequence.
///
/// Idempotent: when every file already holds exactly the rendered bytes AND the
/// unit is active, nothing is written and nothing is restarted. That is not a
/// nicety: a reconcile that re-applied the same file would bounce every live IMAP
/// session and every message in flight once per run. "Converged" is judged on the
/// daemon as well as on the files, because the same bytes with the unit stopped
/// (a disable leaves them) are not a running stack.
///
/// The unit is unmasked first, since the installer masked it; a masked unit
/// refuses the start the reload step would make. Enabling comes LAST, after the
/// unit is observed active, so a configuration that failed never leaves a unit in
/// the boot sequence.
///
/// # Errors
///
/// Returns [`MailError::ServiceRefused`] when the service manager refuses to
/// unmask or enable the unit, or the unit is not active afterwards;
/// [`MailError::ConfigRejected`] when the daemon's check refuses the files (they
/// are put back); and the config-write protocol's other failures as
/// [`MailError::ConfigWrite`], [`MailError::SpawnFailed`] and
/// [`MailError::Bound`]. Returns [`MailError::ConfigUnreadable`] when a live file
/// exists and cannot be read.
pub fn converge_mail_stage(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    stage: &MailStage,
) -> Result<(), MailError> {
    let manager = distro.service_manager();
    let unit = MailUnit::new(&stage.unit);

    if files_are_current(host, stage)? && unit_is_active(host, distro, &unit)? {
        return require_success(
            host.run(&manager, &unit.enable(), CallClass::Reload)?
                .status,
            &stage.unit,
        );
    }

    require_success(
        host.run(&manager, &unit.unmask(), CallClass::Reload)?
            .status,
        &stage.unit,
    )?;

    let files: Vec<ConfigFile<'_>> = stage
        .files
        .iter()
        .map(|staged| ConfigFile {
            target: &staged.target,
            contents: &staged.contents,
            mode: staged.mode,
        })
        .collect();
    let validator_arguments: Vec<&str> = stage
        .validator_arguments
        .iter()
        .map(String::as_str)
        .collect();
    let reload_arguments: Vec<&str> = stage.reload_arguments.iter().map(String::as_str).collect();
    host.write_config_set(
        stage.daemon,
        &stage.unit,
        &files,
        &Validator {
            program: &stage.validator_program,
            arguments: &validator_arguments,
            class: stage.validator_class,
        },
        &Reload {
            program: &manager,
            arguments: &reload_arguments,
        },
    )?;

    // The reload step returned when the service manager did; whether the daemon is
    // still alive a moment later is a separate question, and is asked.
    if !unit_is_active(host, distro, &unit)? {
        return Err(MailError::ServiceRefused {
            unit: stage.unit.clone(),
        });
    }
    require_success(
        host.run(&manager, &unit.enable(), CallClass::Reload)?
            .status,
        &stage.unit,
    )
}

/// Whether every file of the stage already holds exactly the rendered bytes.
fn files_are_current(host: &dyn MailHost, stage: &MailStage) -> Result<bool, MailError> {
    for staged in &stage.files {
        if host.read_config(&staged.target)?.as_deref() != Some(staged.contents.as_str()) {
            return Ok(false);
        }
    }
    Ok(true)
}

/// Whether the service manager reports the unit active.
fn unit_is_active(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    unit: &MailUnit<'_>,
) -> Result<bool, MailError> {
    let outcome = host.run(
        &distro.service_manager(),
        &unit.is_active(),
        CallClass::Query,
    )?;
    Ok(outcome.status == 0)
}

/// Turns a non-zero status from the service manager into a refusal naming the unit.
fn require_success(status: i32, unit: &str) -> Result<(), MailError> {
    if status == 0 {
        return Ok(());
    }
    Err(MailError::ServiceRefused {
        unit: unit.to_owned(),
    })
}

#[cfg(test)]
#[path = "../tests/mail/converge_mail_stage_tests.rs"]
mod tests;
