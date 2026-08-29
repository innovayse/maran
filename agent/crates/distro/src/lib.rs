#![warn(missing_docs)]
//! maran-distro — the `DistroAdapter` trait and one implementation per
//! supported family (`debian/`, `rhel/`). All distro-specific knowledge
//! (package manager, service names, paths) lives here and nowhere else:
//! `ops` code must not branch on distro names (rules/architecture.md
//! "Agent"). The trait and adapters land with Plan 2.
