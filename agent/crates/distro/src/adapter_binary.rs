//! The type of a program the agent is allowed to execute.

use std::borrow::Cow;
use std::ffi::OsStr;
use std::fmt;
use std::path::Path;

/// The absolute path of one program the agent may start as root.
///
/// Every adapter method that names a program the agent executes returns one
/// of these, and every process seam in `ops` takes `&AdapterBinary` rather than
/// a string. At those host seams the compiler checks it: a path read from a
/// request, a `format!`ed string or a literal typed in `ops` cannot become one.
///
/// That guarantee stops at the seam. The bounded spawn core in `agent-core`
/// (`spawn_argv`, `run_bounded`, `run_unthreaded`, `run_for_window`) takes
/// `&Path`, because `agent-core` cannot depend on `distro` and the tests drive
/// the core with a fixture program, so the type system alone does not stop a
/// caller from handing it a path that did not come from here. What keeps that
/// true is `maran structure` (check 17d): it confines the core's callers to the
/// listed process-host files and rejects a program built from a path literal.
/// It is a textual check, not a proof; a path laundered through a variable is
/// left to review.
///
/// The field is private and the only constructors are `pub(crate)`, so nothing
/// outside this crate can build a value, in any build, test builds of other
/// crates included. There is deliberately no `From`, `FromStr`, `Default`,
/// `Deserialize`, public constructor or cargo feature: features unify across
/// the workspace, and a test-only constructor behind one would reach production
/// builds. Tests use a real adapter's value, or drive the private `&Path`
/// body of a host.
///
/// The type says "the agent may execute this", so it is NOT put on a value that
/// is only written somewhere (the shell a crontab line names, the login shell
/// of an account), nor on one nothing executes.
///
/// A string cannot reach a seam that takes one:
///
/// ```compile_fail
/// fn seam(_program: &maran_distro::AdapterBinary) {}
/// seam("/usr/bin/id");
/// ```
///
/// Nor can a caller outside the crate build one:
///
/// ```compile_fail
/// let _ = maran_distro::AdapterBinary::fixed("/usr/bin/id");
/// ```
///
/// Nor can the crate-private constructor for versioned paths:
///
/// ```compile_fail
/// let _ = maran_distro::AdapterBinary::versioned(String::from("/usr/bin/id"));
/// ```
///
/// Nor the struct literal, because the field is private:
///
/// ```compile_fail
/// let _ = maran_distro::AdapterBinary(std::borrow::Cow::Borrowed("/usr/bin/id"));
/// ```
///
/// Nor a conversion from a string:
///
/// ```compile_fail
/// let _: maran_distro::AdapterBinary = "/usr/bin/id".into();
/// ```
///
/// Nor a parse:
///
/// ```compile_fail
/// let _ = "/usr/bin/id".parse::<maran_distro::AdapterBinary>();
/// ```
///
/// Nor a default value:
///
/// ```compile_fail
/// let _ = maran_distro::AdapterBinary::default();
/// ```
///
/// The inverse control, so the failures above are known to be about the
/// type and not about any compile error: a real adapter's value reaches the
/// same kind of call.
///
/// ```
/// use maran_distro::{adapter_for, DistroAdapter, DistroFamily};
///
/// fn seam(program: &maran_distro::AdapterBinary) -> &str {
///     program.as_str()
/// }
/// let adapter = adapter_for(DistroFamily::Debian);
/// assert!(seam(&adapter.id_binary()).starts_with('/'));
///
/// // And it reaches a process builder the way a seam uses it.
/// let nginx = adapter.nginx_binary();
/// let _command = std::process::Command::new(&nginx);
/// ```
#[derive(Clone, PartialEq, Eq, Hash)]
pub struct AdapterBinary(Cow<'static, str>);

impl AdapterBinary {
    /// A binary whose path is a literal in this crate.
    ///
    /// `const` and taking `&'static str`, so only a literal (or another
    /// `'static` constant) written in `distro` can reach it. The caller supplies
    /// an absolute path; `fixed` does not check it. Every adapter method is
    /// pinned absolute and `..`-free by the per-family adapter tests, whose
    /// completeness check is read from the trait source.
    pub(crate) const fn fixed(path: &'static str) -> Self {
        Self(Cow::Borrowed(path))
    }

    /// A binary whose path is built from a validated version.
    ///
    /// Used by `php_fpm_binary` alone, whose argument is a
    /// `maran_agent_core::validation::web::php_version::PhpVersion`, so the
    /// built string is exactly as closed as the version grammar (two ASCII
    /// digit components): it cannot carry a separator, a `..` or a control
    /// character.
    pub(crate) fn versioned(path: String) -> Self {
        Self(Cow::Owned(path))
    }

    /// The path as text.
    #[must_use]
    pub fn as_str(&self) -> &str {
        self.0.as_ref()
    }

    /// The path as a [`Path`].
    #[must_use]
    pub fn as_path(&self) -> &Path {
        Path::new(self.0.as_ref())
    }
}

impl fmt::Debug for AdapterBinary {
    /// Prints the path; a program path is not a secret.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_tuple("AdapterBinary")
            .field(&self.as_str())
            .finish()
    }
}

impl fmt::Display for AdapterBinary {
    /// Prints the path.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(self.as_str())
    }
}

impl AsRef<OsStr> for AdapterBinary {
    /// So `Command::new(binary)` works.
    fn as_ref(&self) -> &OsStr {
        OsStr::new(self.as_str())
    }
}

impl AsRef<Path> for AdapterBinary {
    /// The path as a [`Path`].
    fn as_ref(&self) -> &Path {
        self.as_path()
    }
}

impl AsRef<str> for AdapterBinary {
    /// The path as text.
    fn as_ref(&self) -> &str {
        self.as_str()
    }
}

impl PartialEq<str> for AdapterBinary {
    /// Lets a test compare the path with a literal.
    fn eq(&self, other: &str) -> bool {
        self.as_str() == other
    }
}

impl PartialEq<&str> for AdapterBinary {
    /// Lets a test compare the path with a literal.
    fn eq(&self, other: &&str) -> bool {
        self.as_str() == *other
    }
}

#[cfg(test)]
#[path = "tests/adapter_binary_tests.rs"]
mod tests;
