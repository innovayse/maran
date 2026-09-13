//! ListBans: the bans the kernel is holding right now.

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;
use maran_distro::DistroAdapter;
use serde_json::Value;

use crate::firewall::ensure_bans_table::{BANNED_V4_SET, BANNED_V6_SET, BANS_TABLE, TABLE_FAMILY};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::model::active_ban::ActiveBan;

/// Asks `nft` for machine-readable output instead of its own listing syntax.
const JSON: &str = "-j";

/// `nft`'s verb for showing an object.
const LIST: &str = "list";

/// `nft`'s noun for a named set.
const SET: &str = "set";

/// The key `nft`'s JSON wraps everything in.
const DOCUMENT_KEY: &str = "nftables";

/// The key of a set object inside that document.
const SET_KEY: &str = "set";

/// The key holding a set's members.
const ELEMENTS_KEY: &str = "elem";

/// The key holding one member's value, when the member carries a timeout and
/// is therefore an object rather than a bare string.
const VALUE_KEY: &str = "val";

/// The key holding how many seconds of a member's timeout are left.
const EXPIRES_KEY: &str = "expires";

/// Lists every address the firewall is currently dropping, from both family
/// sets.
///
/// Read-only, so it does not take
/// the module lock every mutation is serialised by, and does not ensure
/// the bans table: a host with no bans table has no bans, which is an answer
/// rather than a reason to write to `/etc`.
///
/// The remaining lifetime reported is what the KERNEL is counting down, not
/// what the ban was created with. That is what the panel's reconciler needs
/// after a restart, and it is the only one of the two the agent knows — the
/// agent keeps no record of a ban beyond the element itself (R6).
///
/// # What a refusal from `nft` means here
///
/// An `nft` that runs and exits non-zero is read as "the set is not there",
/// which on this host means the bans table has never been loaded — and a
/// table that is not loaded holds no bans. An `nft` that cannot be RUN is a
/// different thing entirely and is an error: the difference between "there
/// are no bans" and "I could not find out" is exactly the difference the
/// panel would otherwise paper over.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking` —
/// never awaited on a runtime worker. Unlike this area's mutations it takes no
/// lock, so nothing panics when a caller gets this wrong; it just spawns `nft`
/// on the worker, stalling every other command sharing it, and the first
/// symptom under load is an unrelated timeout naming nothing. Nothing here can
/// catch that at run time — a blocking-pool thread and a worker are
/// indistinguishable through tokio's public API, see `firewall_lock`. The call
/// sites are checked instead, by `firewall_service_tests.rs` in the `agent`
/// crate.
///
/// # Errors
///
/// - [`FirewallError::NftFailed`] when `nft` cannot be started at all.
/// - [`FirewallError::UnreadableNftOutput`] when `nft` succeeds but answers
///   in JSON this agent cannot read — a shape this version of `nft` produces
///   and this agent does not know. It is deliberately not silence: reporting
///   an empty ban list because the output could not be parsed is how a panel
///   comes to believe an attacker is blocked.
pub fn list_bans(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<ActiveBan>, FirewallError> {
    let mut bans = Vec::new();

    for set in [BANNED_V4_SET, BANNED_V6_SET] {
        let listed = host.run(
            distro.nft_binary(),
            &[JSON, LIST, SET, TABLE_FAMILY, BANS_TABLE, set],
        )?;
        if listed.status != 0 {
            continue;
        }

        bans.extend(read_set(&listed.stdout)?);
    }

    Ok(bans)
}

/// Reads the bans out of one `nft -j list set` document.
///
/// The document is an object with one `nftables` array in it, holding a
/// metainfo object and one `set` object. Only the `set` object is read;
/// anything else in the array is skipped, so a future `nft` that adds a
/// sibling does not break this.
///
/// # Errors
///
/// Returns [`FirewallError::UnreadableNftOutput`] when the text is not JSON,
/// does not carry the `nftables` array every `nft -j` answer is wrapped in,
/// carries no readable `set` object at all, carries a `set` that is not an
/// object, or carries an `elem` key that is not an array.
///
/// Every one of those is an error rather than an empty list, and for one
/// reason: a set with no members omits the `elem` key entirely and a set that
/// does not exist makes `nft` exit non-zero, so anything else reaching here
/// means this agent and this `nft` disagree about the format. Answering "no
/// bans" to a disagreement is how a panel comes to believe an attacker is
/// blocked. The only shape skipped rather than refused is an object with no
/// `set` key at all, which is the metainfo object `nft` puts first.
fn read_set(json: &str) -> Result<Vec<ActiveBan>, FirewallError> {
    let document: Value =
        serde_json::from_str(json).map_err(|_| FirewallError::UnreadableNftOutput)?;

    let objects = document
        .get(DOCUMENT_KEY)
        .and_then(Value::as_array)
        .ok_or(FirewallError::UnreadableNftOutput)?;

    let mut bans = Vec::new();
    let mut read_a_set = false;
    for object in objects {
        // Not every object in the array is a set — `nft -j` puts a metainfo
        // object first — so an object without the key is skipped rather than
        // refused.
        let Some(set) = object.get(SET_KEY) else {
            continue;
        };
        // A `set` that is PRESENT but is not an object is a different thing
        // entirely: it is a shape this agent does not know, and reading it as
        // "no bans" is the same silence the `elem` handling below refuses.
        let set = set.as_object().ok_or(FirewallError::UnreadableNftOutput)?;
        read_a_set = true;
        // The two cases below are distinguishable and are kept apart, because
        // collapsing them reports "no bans" for output that could not be read
        // — the exact silence `UnreadableNftOutput` exists to prevent. A set
        // with nothing in it carries no `elem` key AT ALL (verified against
        // real nft v1.0.9); an `elem` that is there but is not an array is a
        // shape this agent does not know.
        let elements = match set.get(ELEMENTS_KEY) {
            None => continue,
            Some(members) => members
                .as_array()
                .ok_or(FirewallError::UnreadableNftOutput)?,
        };

        for element in elements {
            bans.push(read_element(element)?);
        }
    }

    // The same argument as the `elem` handling above, one level up. This
    // agent asks for ONE set by name and `nft` exits non-zero when it does not
    // have it, so a successful answer that carries no set this agent could
    // read means the two disagree about the format — and answering "no bans"
    // to that is how a panel comes to believe an attacker is blocked.
    if !read_a_set {
        return Err(FirewallError::UnreadableNftOutput);
    }

    Ok(bans)
}

/// Reads one set member.
///
/// `nft` writes a member two ways: a bare string when it has no timeout, and
/// an object wrapping `val` and `expires` when it has one. Both are read
/// here, because a permanent ban and a timed one are both bans.
///
/// An address that is not one this agent could have added — anything
/// [`BanAddress::parse_existing`] refuses — is reported as unreadable output
/// rather than skipped. That constructor and not [`BanAddress::parse`]: the
/// parse a ban goes through also refuses loopback, and a host upgraded from a
/// version without that refusal can hold a loopback element the old code
/// placed. Reading it back must not make the whole list unreadable, since a
/// ban an operator cannot see is a ban they cannot lift. Nothing else writes to these sets, so such a value means this
/// agent and this `nft` disagree about the format, and answering "no ban on
/// that address" would be the panel believing an attacker is blocked when
/// nobody knows whether they are.
///
/// The wrapper key is `elem` at both levels — a set's members live under
/// `elem`, and a member with a timeout is itself an object under `elem`. That
/// is `nft`'s own shape, not a mistake here, which is why one constant names
/// both.
///
/// # Errors
///
/// Returns [`FirewallError::UnreadableNftOutput`] for a member that is
/// neither of those two shapes, whose value is not an address, or whose
/// remaining lifetime is present but is not a whole number of seconds. The
/// last one is strict for the same reason as the rest: a lifetime this agent
/// could not read would be reported as "no expiry", which is a ban the panel
/// would then believe is permanent.
fn read_element(element: &Value) -> Result<ActiveBan, FirewallError> {
    let (text, expires) = match element.as_str() {
        Some(text) => (text, None),
        None => {
            let inner = element
                .get(ELEMENTS_KEY)
                .ok_or(FirewallError::UnreadableNftOutput)?;
            let text = inner
                .get(VALUE_KEY)
                .and_then(Value::as_str)
                .ok_or(FirewallError::UnreadableNftOutput)?;
            let expires = match inner.get(EXPIRES_KEY) {
                Some(remaining) => Some(
                    remaining
                        .as_u64()
                        .ok_or(FirewallError::UnreadableNftOutput)?,
                ),
                None => None,
            };

            (text, expires)
        }
    };

    let address =
        BanAddress::parse_existing(text).map_err(|_| FirewallError::UnreadableNftOutput)?;

    Ok(ActiveBan {
        address,
        expires_in: expires.map(Duration::from_secs),
    })
}

#[cfg(test)]
#[path = "../tests/firewall/list_bans_tests.rs"]
mod tests;
