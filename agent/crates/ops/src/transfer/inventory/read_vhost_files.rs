//! Fetching the web-server configuration files a listing names.

use maran_agent_core::validation::fs::remote_path::RemotePath;

use super::inventory_run::InventoryRun;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::model::vhost_budget::VhostBudget;
use super::model::vhost_file::VhostFile;
use super::parse_vhost_file_list::parse_vhost_file_list;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Runs `list` (a listing command, see `InventoryRun::listing`), then reads each
/// file it names with `read`.
///
/// The files read are exactly the ones the listing names, in its order: the
/// paths went through the remote-path grammar, and nothing a file says adds to
/// them. A listing or a file that cannot be read is a `SiteUnreadable` warning
/// and the others are still read. A file over the output bound is not parsed
/// (half a configuration says nothing reliable); it is reported as `Truncated`.
/// `budget` is shared by the nginx and the Apache calls and bounds the files and
/// bytes held; when it runs out a `Truncated` is sent and the remaining files are
/// not asked for.
///
/// # Errors
///
/// Every connection error the shell returns.
pub fn read_vhost_files(
    run: &mut InventoryRun<'_>,
    list: &RemoteCommand,
    read: &dyn Fn(RemotePath) -> RemoteCommand,
    budget: &mut VhostBudget,
) -> Result<Vec<VhostFile>, TransferOpError> {
    let unreadable = || SourceWarningRecord::about_nothing(SourceWarningKind::SiteUnreadable);
    let Some(listing) = run.listing(list, unreadable())? else {
        return Ok(Vec::new());
    };
    let paths = parse_vhost_file_list(&listing.text);
    run.warn_each(paths.warnings);

    let mut files = Vec::new();
    for path in paths.items {
        if !budget.take_file() {
            run.warn(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
            break;
        }
        let Some(body) = run.text(&read(path.clone()), unreadable())? else {
            continue;
        };
        if body.truncated {
            continue;
        }
        if !budget.take_bytes(body.text.len()) {
            run.warn(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
            break;
        }
        files.push(VhostFile {
            path,
            content: body.text,
        });
    }
    Ok(files)
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_vhost_files_tests.rs"]
mod tests;
