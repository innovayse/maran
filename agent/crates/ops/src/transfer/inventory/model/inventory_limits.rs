//! Every bound an inventory read holds itself to.

/// The ceilings of an inventory read.
///
/// The source is hostile or may become so (threat note, section 1): each number
/// here caps what one hostile host can make this root process hold in memory or
/// spend time on. Reaching a ceiling is never an error and never a crash. It
/// raises a TRUNCATED warning and the read goes on with what it has, so an
/// incomplete inventory is always marked as one.
pub struct InventoryLimits;

impl InventoryLimits {
    /// Most accounts one read sends.
    pub const MAX_ACCOUNTS: usize = 5000;
    /// Most bytes of one command's output a parser looks at (1 MiB).
    pub const MAX_OUTPUT_BYTES: usize = 1024 * 1024;
    /// Most lines of one command's output a parser looks at.
    pub const MAX_LINES: usize = 20_000;
    /// Longest line a parser reads; a longer one is refused, not cut.
    pub const MAX_LINE_BYTES: usize = 4096;
    /// Most warnings one read sends before it sends one TRUNCATED and stops.
    pub const MAX_WARNINGS: u32 = 500;
    /// Most configuration files read from the web server's directories.
    pub const MAX_VHOST_FILES: usize = 1000;
    /// Most bytes of configuration files held in memory at once (16 MiB).
    pub const MAX_VHOST_BYTES: usize = 16 * 1024 * 1024;
    /// Most virtual hosts one read keeps.
    pub const MAX_VHOSTS: usize = 10_000;
    /// Most PHP versions one read keeps.
    pub const MAX_PHP_VERSIONS: usize = 64;
    /// Deepest chain of `include` files that is followed.
    pub const MAX_INCLUDE_DEPTH: usize = 4;
    /// Most files entered and `include` statements resolved while expanding one
    /// root configuration file.
    pub const MAX_INCLUDE_WORK: usize = 2000;
    /// Most tokens one expanded configuration holds.
    pub const MAX_CONFIG_TOKENS: usize = 200_000;
    /// Longest single word of a configuration file.
    pub const MAX_WORD_BYTES: usize = 1024;
    /// Deepest nesting of blocks in a configuration file.
    pub const MAX_BLOCK_DEPTH: usize = 32;
    /// Most words in one configuration statement.
    pub const MAX_STATEMENT_WORDS: usize = 512;
    /// Most host names one virtual host keeps.
    pub const MAX_SITE_NAMES: usize = 256;
    /// Longest logical line of an Apache file once its continuations are joined.
    pub const MAX_JOINED_LINE_BYTES: usize = 16 * 1024;
}
