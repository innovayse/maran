//! Reading the kernel's TCP socket tables for the sockets that listen.

use crate::mail::model::listening_socket::ListeningSocket;

/// The state column value of a socket in `LISTEN`, as the kernel prints it (hex).
const LISTEN_STATE: &str = "0A";

/// Reads the listening sockets out of the text of `/proc/net/tcp` and
/// `/proc/net/tcp6`.
///
/// A line that does not have the table's shape is skipped rather than guessed at:
/// the caller asks "is port N bound", and a malformed line that was read as a
/// socket would answer yes for a reason nobody can inspect. The header line is
/// skipped by the same rule, because its fields are not hex.
///
/// A socket is [`loopback_only`](ListeningSocket::loopback_only) when its local
/// address is in `127.0.0.0/8`, is `::1`, or is an IPv4-mapped loopback address.
/// An address the table prints in the kernel's host byte order is reversed per
/// four-byte group, so the checks below read the groups as the kernel prints them
/// and not as the address is written.
#[must_use]
pub fn parse_listening_sockets(ipv4_table: &str, ipv6_table: &str) -> Vec<ListeningSocket> {
    ipv4_table
        .lines()
        .chain(ipv6_table.lines())
        .filter_map(parse_line)
        .collect()
}

/// Reads one table row, or `None` when it is not a listening socket of the shape
/// the kernel prints.
fn parse_line(line: &str) -> Option<ListeningSocket> {
    let mut columns = line.split_whitespace();
    let _slot = columns.next()?;
    let local = columns.next()?;
    let _remote = columns.next()?;
    let state = columns.next()?;
    if state != LISTEN_STATE {
        return None;
    }
    let (address, port) = local.split_once(':')?;
    let port = u16::from_str_radix(port, 16).ok()?;
    let loopback_only = is_loopback(address)?;
    Some(ListeningSocket {
        port,
        loopback_only,
    })
}

/// Whether the hex address the kernel printed is a loopback address, or `None`
/// when it is neither an IPv4 nor an IPv6 address of the printed width.
fn is_loopback(address: &str) -> Option<bool> {
    if !address.bytes().all(|byte| byte.is_ascii_hexdigit()) {
        return None;
    }
    match address.len() {
        // One group, least significant byte first: 127.0.0.1 is `0100007F`.
        8 => Some(address.ends_with("7F")),
        // Four groups, each reversed: `::1` is `00000000000000000000000001000000`,
        // and `::ffff:127.0.0.1` is `0000000000000000FFFF00000100007F`.
        32 => Some(
            address == "00000000000000000000000001000000"
                || (address.starts_with("0000000000000000FFFF0000") && address.ends_with("7F")),
        ),
        _ => None,
    }
}

#[cfg(test)]
#[path = "../tests/mail/parse_listening_sockets_tests.rs"]
mod tests;
