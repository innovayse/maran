//! The cron daemon: its binary, its unit and the shell a crontab line names.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The cron daemon: its binary, its unit and the shell a crontab line names.
pub trait SchedulingFacts: Send + Sync {
    /// Absolute path of `crontab`, for the process-execution allow-list.
    ///
    /// A per-account crontab is installed by running this program, never by
    /// writing the spool directory directly: where that spool lives, what owns
    /// it, what mode it carries and how the daemon learns it changed are the
    /// program's business on each family, and asking it to do the work is what
    /// keeps all four out of `ops`. That is the part this crate knows.
    ///
    /// HOW it is run — what the argument vector is and where the table comes
    /// from — is the caller's contract and is documented there, on
    /// `ops::cron`'s `ProcessCronHost`. This comment used to assert one and was
    /// wrong about it, which is the reason the convention is now stated in one
    /// place instead of two.
    ///
    /// Both families install it at `/usr/bin/crontab` — from `cron` on the
    /// Debian family and from `cronie` on the RHEL family — which is an
    /// agreement between two packages rather than a rule, verified on both
    /// polygon images and asked of the adapter all the same.
    fn crontab_binary(&self) -> AdapterBinary;

    /// Absolute path of the POSIX shell a crontab line names.
    ///
    /// The agent never spawns this and never builds a command line for it:
    /// running a shell is forbidden outright (rules/rust.md "Process
    /// execution"). It is a path WRITTEN into a crontab line, and `cron` is
    /// what runs it. What that line looks like belongs to the unit that
    /// renders it, not here.
    ///
    /// Both families answer `/bin/sh`, and what stands behind that path is NOT
    /// the same program: `dash` on the Debian family, `bash` on the RHEL family
    /// (verified on both polygon images). The path is the contract; the shell
    /// behind it is not, so a customer command written against bash builtins
    /// runs on one family and fails on the other.
    ///
    /// `/bin/sh` and not the `/usr/bin/sh` that `command -v sh` answers on both
    /// images: they are one file, and the rule under [`DistroAdapter`](crate::adapter::DistroAdapter) answers
    /// the spelling a crontab line and a shebang name.
    fn sh_binary(&self) -> &'static str;

    /// Name of the cron service unit.
    ///
    /// The one place the two families' cron packaging shows: `cron` on the
    /// Debian family, `crond` on the RHEL family, the same daemon's job under
    /// two package names. Writing either literal into `ops` would install
    /// crontabs correctly on one family and fail to restart the daemon on the
    /// other.
    fn cron_service(&self) -> &'static str;
}
