//! Why a family's Node.js offers could not be listed.

/// The reason the host's Node.js module streams gave no offer, carried beside a list of apps that
/// is otherwise complete (issue #532): a gap in Node.js hides the Node.js apps and never the
/// web servers or PHP.
///
/// The two reasons are different facts. `NoModuleStreams` is an answer (the sources carry no
/// module of that name, as a release without modularity says); `ListingFailed` is the absence of
/// one (the package manager could not be asked, or printed what the adapter does not read). A
/// reader that treats the second as the first would tell an operator the host has no Node.js
/// when it merely could not look.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NodejsUnavailable {
    /// The sources carry no module streams at all, so no Node.js major is offered.
    NoModuleStreams,
    /// The module listing could not be run or read, so what is offered is unknown.
    ListingFailed,
}
