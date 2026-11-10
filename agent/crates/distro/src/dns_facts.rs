//! The authoritative DNS daemon (BIND 9): its checker and control binaries, the
//! identity it drops to and the directories it may write.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by
//! concern so the one adapter seam stays under the file-size trigger
//! (rules/rust.md "Distro adapter").
//!
//! What is NOT here, because it is the same on every family and so lives in
//! `AgentPaths`: the rendered configuration path, the control key, the control
//! port and the unit name. What is NOT here either: package and packaged-unit
//! names. Only the installer installs the package and masks the packaged unit,
//! and it spells them in `installer/lib/89-dns.sh`.

use crate::adapter_binary::AdapterBinary;

/// Platform facts about the DNS daemon.
pub trait DnsFacts: Send + Sync {
    /// Absolute path of `named-checkconf`, the daemon's own configuration
    /// checker, which `ops::dns` runs on the rendered file at its real path.
    fn dns_checkconf_binary(&self) -> AdapterBinary;

    /// Absolute path of `named-checkzone`, the daemon's own zone checker, which
    /// `ops::dns::put_zone` runs on the rendered zone file at its real path.
    fn dns_checkzone_binary(&self) -> AdapterBinary;

    /// Absolute path of `rndc`, the client of the daemon's control channel.
    fn dns_control_binary(&self) -> AdapterBinary;

    /// The system user the daemon drops to once it has bound its sockets.
    fn dns_user(&self) -> &'static str;

    /// The system group the daemon runs in. The control key and the daemon's
    /// configuration are readable by it and by nobody else but root.
    fn dns_group(&self) -> &'static str;

    /// The directory the daemon uses as its working directory and for the
    /// zone list it keeps itself (`rndc addzone`). It is the one place the
    /// daemon WRITES, so it sits under the location each family's mandatory
    /// access control already lets the daemon write.
    fn dns_state_directory(&self) -> &'static str;

    /// The directory the agent writes zone files into, one per zone. Below
    /// [`Self::dns_state_directory`], root-owned and readable by the daemon's
    /// group but not writable by it: the daemon reads zone files and never
    /// changes them.
    fn dns_zone_directory(&self) -> &'static str;

    /// The directory the agent writes the zone files of SIGNED zones into, one
    /// per zone: `signed-zones/` below [`Self::dns_state_directory`]. Unlike
    /// [`Self::dns_zone_directory`] the daemon WRITES here: inline signing keeps
    /// the signed copy and its journal beside the zone file. Root-owned and
    /// group-writable by the daemon's group, so a zone that moves between
    /// unsigned and signed moves between the two directories and the plain one
    /// stays read-only to the daemon for every other zone.
    fn dns_signed_zone_directory(&self) -> &'static str;

    /// The directory a signed zone's key files live in: `keys/` below
    /// [`Self::dns_state_directory`] (the zone statement's `key-directory`).
    /// The agent writes `K<zone>.+013+<tag>.key` and `.private` here as root,
    /// readable by the daemon's group; the daemon writes the `.state` files of
    /// the keys beside them, so the directory is group-writable like
    /// [`Self::dns_signed_zone_directory`].
    fn dns_key_directory(&self) -> &'static str;
}
