//! A census of the call classes a production source file names.
//!
//! The class a spawn site runs under decides how long a legitimate job may
//! live: a dump under a sixty-second class would be killed at sixty seconds and
//! every test of the dump would still pass, because none of them waits that
//! long. So the class is pinned at the source, the one place it is written,
//! without adding a production hook only a test would read.

/// Marker after which a source file holds only its test module.
const TEST_MODULE_MARKER: &str = "#[cfg(test)]";

/// Prefix every class is written with.
const CLASS_PREFIX: &str = "CallClass::";

/// Returns, in source order, the variant name of every `CallClass::<Variant>`
/// that `source` writes in code.
///
/// Comment lines (`//`, `///`, `//!`) are skipped, because documentation names
/// the class it describes and would otherwise count as a use. Everything from
/// the file's `#[cfg(test)]` declaration on is skipped too.
pub(crate) fn classes_named_in_code(source: &str) -> Vec<String> {
    let production = source.split(TEST_MODULE_MARKER).next().unwrap_or(source);
    let mut found = Vec::new();
    for line in production.lines() {
        if line.trim_start().starts_with("//") {
            continue;
        }
        let mut rest = line;
        while let Some(at) = rest.find(CLASS_PREFIX) {
            rest = &rest[at + CLASS_PREFIX.len()..];
            let name: String = rest
                .chars()
                .take_while(|character| character.is_ascii_alphanumeric())
                .collect();
            found.push(name);
        }
    }
    found
}
