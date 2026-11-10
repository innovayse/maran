//! Reads the output of `ssh-keyscan` into host keys.

use maran_agent_core::validation::web::host_key::HostKey;

use crate::transfer::model::scanned_host_key::ScannedHostKey;
use crate::transfer::ssh_key_fingerprint::ssh_key_fingerprint;

/// The longest line that is looked at; a longer one is skipped without being
/// parsed.
const LONGEST_LINE: usize = 4096;

/// The most keys kept: one per algorithm of the allow-list.
const MOST_KEYS: usize = 5;

/// The order in which keys are offered to the administrator: the algorithms
/// this agent prefers first.
const PREFERENCE: [&str; MOST_KEYS] = [
    "ssh-ed25519",
    "ecdsa-sha2-nistp256",
    "ecdsa-sha2-nistp384",
    "ecdsa-sha2-nistp521",
    "ssh-rsa",
];

/// What `ssh-keyscan` printed, read into at most one key per algorithm.
///
/// A line is `<host> <algorithm> <base64>`; the first field (the address the
/// tool connected to) is not used and not trusted. Comment lines (`#`), lines
/// over 4096 bytes, lines of another shape, algorithms outside the allow-list
/// and keys [`HostKey`] refuses are skipped, never repaired. The fingerprint of
/// each kept key is computed here from the key itself. The result is in the
/// agent's order of preference and is empty when nothing usable was printed;
/// the caller decides what an empty answer means.
#[must_use]
pub fn parse_scanned_host_keys(output: &str) -> Vec<ScannedHostKey> {
    let mut found: Vec<ScannedHostKey> = Vec::new();

    for line in output.lines() {
        if line.starts_with('#') || line.len() > LONGEST_LINE {
            continue;
        }
        let fields: Vec<&str> = line.split_whitespace().collect();
        let [_, algorithm, encoded] = fields.as_slice() else {
            continue;
        };
        let Ok(key) = HostKey::parse(&format!("{algorithm} {encoded}")) else {
            continue;
        };
        if found
            .iter()
            .any(|kept| kept.key.algorithm() == key.algorithm())
        {
            continue;
        }
        let fingerprint = ssh_key_fingerprint(&key);
        found.push(ScannedHostKey { key, fingerprint });
    }

    found.sort_by_key(|kept| {
        PREFERENCE
            .iter()
            .position(|algorithm| *algorithm == kept.key.algorithm())
            .unwrap_or(MOST_KEYS)
    });

    found
}

#[cfg(test)]
#[path = "../tests/transfer/parse_scanned_host_keys_tests.rs"]
mod tests;
