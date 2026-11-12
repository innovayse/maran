//! Reads this host's own IPv6 addresses out of the kernel's interface table.

use std::net::Ipv6Addr;

/// The number of hex digits an address occupies in the first column.
const ADDRESS_DIGITS: usize = 32;

/// The addresses `text` (the content of the kernel's `if_inet6` table) lists,
/// each once, in the order first seen.
///
/// The first column of a row is the address as 32 hexadecimal digits with no
/// separator; the rest of the row (interface index, prefix length, scope, flags,
/// name) is not needed. A row whose first column is not exactly 32 hexadecimal
/// digits is skipped.
#[must_use]
pub fn parse_if_inet6(text: &str) -> Vec<Ipv6Addr> {
    let mut found: Vec<Ipv6Addr> = Vec::new();

    for line in text.lines() {
        let Some(column) = line.split_whitespace().next() else {
            continue;
        };
        if column.len() != ADDRESS_DIGITS || !column.bytes().all(|byte| byte.is_ascii_hexdigit()) {
            continue;
        }
        if let Ok(value) = u128::from_str_radix(column, 16) {
            let address = Ipv6Addr::from(value);
            if !found.contains(&address) {
                found.push(address);
            }
        }
    }

    found
}

#[cfg(test)]
#[path = "../tests/utils/parse_if_inet6_tests.rs"]
mod tests;
