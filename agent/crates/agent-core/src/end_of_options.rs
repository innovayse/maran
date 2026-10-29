//! The argument that ends option parsing, for the programs the agent runs.

/// `--`, the operand every `getopt`-family parser reads as "no more options".
///
/// Placed between a program's options and a name or a path that came from a
/// caller, so that value can never be read as an option even if it began with a
/// hyphen. The validated names this agent accepts cannot, today; the guard makes
/// that independent of the validator staying as it is.
///
/// **Only for programs whose parser is known to honour it on both distribution
/// families, and only where an operand follows it.** A program with its own
/// hand-written argument loop (`vsftpd`) treats `--` as an unknown option, and one
/// with no operand has nothing for it to protect. Use sites that do not follow
/// the `getopt` convention (the `nft` command words in `firewall/`) say in a
/// comment which parser they rely on.
pub const END_OF_OPTIONS: &str = "--";

#[cfg(test)]
#[path = "tests/end_of_options_tests.rs"]
mod tests;
