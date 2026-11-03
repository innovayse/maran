//! The programs a backup archive is made with.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The programs a backup archive is made with.
pub trait BackupFacts: Send + Sync {
    /// Absolute path of `tar`, for the process-execution allow-list.
    ///
    /// A backup archive is `tar` told which compressor to run, by the absolute
    /// path [`Self::gzip_binary`] answers — never `--gzip`, which makes `tar`
    /// look the compressor up on `PATH`. Both families install `tar` at
    /// `/usr/bin/tar` — verified `command -v tar` on both polygon images, in
    /// both cases the real file and not a symlink.
    fn tar_binary(&self) -> AdapterBinary;

    /// Absolute path of `gzip`, and the value every backup and restore argv
    /// carries as `tar`'s `--use-compress-program`.
    ///
    /// **This value is consumed, not merely recorded.** It was measured on both
    /// polygons and for a while it was named here and used nowhere, while the
    /// argv said `--gzip` — and `--gzip` makes `tar` resolve the compressor
    /// through `PATH` in a child of the root daemon, which was a `PATH`-shadow
    /// hole for as long as the measured fact went unspent (see
    /// `ArchiveSpec::arguments`). A binary this trait names and nothing spawns
    /// is a check that reports on nothing.
    ///
    /// It also lets the backup preflight assert the host has the compressor
    /// before a scheduled backup discovers its absence at 03:00, rather than
    /// after `tar` has already started writing. Both families install it at
    /// `/usr/bin/gzip` — verified `command -v gzip` on both polygon images, in
    /// both cases the real file and not a symlink.
    fn gzip_binary(&self) -> AdapterBinary;

    /// Absolute path of the database dump client, for the process-execution
    /// allow-list.
    ///
    /// **Measured, not assumed** (rules/architecture.md): both families were
    /// suspected to disagree here — historically Debian's `mariadb-client`
    /// shipped a real `mysqldump` while AlmaLinux 9's `mariadb` 10.5 was
    /// expected to ship `mariadb-dump` with `mysqldump` only as a compatibility
    /// symlink. `command -v` for BOTH names, `readlink -f` and `ls -l`,
    /// verified on both polygon images on 2026-09-05, found the
    /// two families agree after all: on Ubuntu 24.04's `mariadb-client` package
    /// AND on AlmaLinux 9's `mariadb` package, `/usr/bin/mariadb-dump` is the
    /// real file and `/usr/bin/mysqldump` is a symlink to it
    /// (`dpkg -S`/`rpm -qf` both attribute the real file to the package). This
    /// method therefore answers the REAL name, `/usr/bin/mariadb-dump`, on
    /// both families — the symlink a future package drop could remove is never
    /// the path this trait hands to a spawn.
    ///
    /// Also measured, because it decides an argv rather than a doc comment:
    /// `--no-tablespaces` is ACCEPTED by the dump client on both families
    /// (confirmed by contrast — an actually-unknown flag fails immediately
    /// with `unknown option`, while `--no-tablespaces` is parsed and the
    /// client proceeds to attempt a connection). Both polygon images run
    /// MariaDB (10.11 on Debian, 10.5.29 on RHEL), not MySQL proper, so the
    /// flag that MySQL 8 requires without `PROCESS` and that MariaDB's own
    /// client was expected to reject outright turned out to be accepted by
    /// both — this crate states the fact and leaves what `ops::backup` does
    /// with it to that area's own argv, per rules/rust.md ("ops never names a
    /// platform literal").
    fn database_dump_binary(&self) -> AdapterBinary;
}
