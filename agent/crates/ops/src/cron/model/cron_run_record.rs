//! What an entry's last run reported about itself.

use std::time::SystemTime;

/// The exit status of an entry's last run, and when that run finished.
///
/// Both halves come from ONE file — `~/.maran/cron/<id>.exit`, which the
/// installed crontab line fills with `echo $? > …`. The file's CONTENT is the
/// status and its MTIME is the moment the run ended. That is why the installed
/// line calls no `date`, and therefore carries no `%`: cron rewrites the first
/// unescaped `%` on a line into a newline, so a timestamp taken by a second
/// command would have been the one byte in the line that could not be there.
///
/// **This is the account's own report, not an audit record.** The file lives
/// inside the account's home and the account can write it, so a customer who
/// wants to can claim any status at any time. Nothing in the panel may treat it
/// as evidence — it is what the customer's own machine says happened, shown
/// back to them, and it is exactly as trustworthy as the command that wrote it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CronRunRecord {
    /// The status the last run exited with.
    ///
    /// `None` when the file holds something that is not a status — a customer
    /// wrote over it, or a run was killed between the redirect and the `echo`.
    /// Reported as "unknown" rather than as a failure, because inventing a code
    /// for it would be indistinguishable from a real one.
    pub exit_code: Option<i32>,
    /// When the last run finished, from the exit file's modification time.
    pub ran_at: SystemTime,
}
