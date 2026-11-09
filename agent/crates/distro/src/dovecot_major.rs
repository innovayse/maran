//! The Dovecot configuration generations the agent renders for.

/// The major version of Dovecot a host runs, as far as its configuration language is concerned.
///
/// Dovecot 2.4 rewrote the configuration language: `dovecot_config_version` comes first, settings
/// are no longer hierarchical, `plugin {}` blocks are gone, variables use the `%{user}` syntax,
/// `passdb`/`userdb` are named blocks and protocols are no longer enabled by default. A 2.3 file
/// is refused by a 2.4 daemon and the reverse, so the templates come in one family per major and
/// the agent must know which one a host needs BEFORE it renders (`research_notes/mail-stack-inventory.md`
/// section 1). It is a closed enum and not a number: a major the templates do not exist for is
/// not a value of it, so `None` from [`crate::MailFacts::dovecot_major`] is the only way to say
/// "unknown release" and no caller can render for a guess.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum DovecotMajor {
    /// Dovecot 2.3, the generation with `conf.d/` and `plugin {}` blocks.
    V23,
    /// Dovecot 2.4, the generation with the rewritten configuration language.
    V24,
}
