//! Masks the host-dependent values of a ruleset's tail so two renders compare.

/// What replaces the uid in the output chain's rule lines, and the address
/// list in its sets, when two renders are compared (see
/// [`mask_host_facts`]).
const MASKED: &str = "<host fact>";

/// The token that precedes the customer uid floor in the output chain.
const SKUID_OPERATOR: &str = ">=";

/// The line prefix of a set's element list.
pub(crate) const ELEMENTS_PREFIX: &str = "elements =";

/// The lines of a ruleset's tail with the two host-dependent values replaced
/// by [`MASKED`]: the number after `>=` on a rule line (the customer uid
/// floor) and the element list of an address set.
///
/// Both change without anybody editing the ruleset — `login.defs` is edited,
/// an address is leased — and a file this agent rendered an hour ago must
/// still be one it can read. Nothing else in the tail is masked, so the sets'
/// names and types, the chain's hook and policy, and the shape and ports of
/// both rules still have to match the template exactly.
///
/// **A masked value is checked to be the thing it stands for**, or the whole
/// tail is refused (`None`): the token after `>=` is a decimal uid and an
/// element list is `{ address-or-network, ... }` and nothing else. A mask that
/// accepted any text would let a second statement ride in on the line.
pub(crate) fn mask_host_facts(lines: &[&str]) -> Option<Vec<String>> {
    lines
        .iter()
        .map(|line| {
            if let Some(rest) = line.strip_prefix(ELEMENTS_PREFIX) {
                return is_element_list(rest).then(|| format!("{ELEMENTS_PREFIX} {MASKED}"));
            }
            let mut previous_was_operator = false;
            let mut tokens = Vec::new();
            for token in line.split_whitespace() {
                if previous_was_operator {
                    if !is_uid(token) {
                        return None;
                    }
                    tokens.push(MASKED);
                } else {
                    tokens.push(token);
                }
                previous_was_operator = token == SKUID_OPERATOR;
            }
            if previous_was_operator {
                // An operator with nothing after it is not a uid floor.
                return None;
            }

            Some(tokens.join(" "))
        })
        .collect()
}

/// Whether `token` is a uid as the template writes it: decimal digits that fit
/// a `u32`.
fn is_uid(token: &str) -> bool {
    token.bytes().all(|byte| byte.is_ascii_digit()) && token.parse::<u32>().is_ok()
}

/// Whether `text` is an nft element list of addresses and networks:
/// `{ 127.0.0.0/8, 203.0.113.7 }` or `{ ::1, 2001:db8::1 }`.
fn is_element_list(text: &str) -> bool {
    let Some(inner) = text
        .trim()
        .strip_prefix('{')
        .and_then(|rest| rest.strip_suffix('}'))
    else {
        return false;
    };

    inner
        .split(',')
        .all(|element| is_address_or_network(element.trim()))
}

/// Whether `element` is an IP address, or an address and a prefix length.
fn is_address_or_network(element: &str) -> bool {
    let (address, prefix) = match element.split_once('/') {
        Some((address, prefix)) => (address, Some(prefix)),
        None => (element, None),
    };
    let Ok(address) = address.parse::<std::net::IpAddr>() else {
        return false;
    };
    let longest = if address.is_ipv4() { 32 } else { 128 };

    prefix.is_none_or(|prefix| {
        !prefix.is_empty()
            && prefix.bytes().all(|byte| byte.is_ascii_digit())
            && prefix.parse::<u8>().is_ok_and(|bits| bits <= longest)
    })
}
