//! The app store's package names and Apache preparation on the RHEL family.

use maran_agent_core::validation::apps::nodejs_module_stream::NodejsModuleStream;
use maran_agent_core::validation::web::node_major::NodeMajor;

use crate::adapter_binary::AdapterBinary;
use crate::nodejs_packaging::NodejsPackaging;
use crate::nodejs_stream_listing::NodejsStreamListing;
use crate::package_query_failure::PackageQueryFailure;
use crate::rhel::rhel_generation::RhelGeneration;

/// The RPM leaves `httpd` stopped and disabled after the install.
/// UNVERIFIED: confirm with `dnf install httpd; systemctl is-active httpd` on AlmaLinux 9.
#[must_use]
pub fn apache_install_starts_service() -> bool {
    false
}

/// The package that provides nginx.
#[must_use]
pub fn nginx_package() -> &'static str {
    "nginx"
}

/// The package that provides Apache.
#[must_use]
pub fn apache_package() -> &'static str {
    "httpd"
}

/// TLS is a separate package on the RHEL family.
#[must_use]
pub fn apache_extra_packages() -> &'static [&'static str] {
    &["mod_ssl"]
}

/// RHEL's `httpd` brings no companion package that goes with it on removal; what it pulls in as
/// plain dependencies is reported by the dry run as unused dependencies.
#[must_use]
pub fn apache_companion_packages() -> &'static [&'static str] {
    &[]
}

/// A file in `conf.d`, which the packaged `httpd.conf` includes.
///
/// Observed in `almalinux:9` (9.8) after `dnf install httpd mod_ssl`: `httpd.conf` line 358 is
/// `IncludeOptional conf.d/*.conf`, and the package runs as `User apache` / `Group apache`.
#[must_use]
pub fn apache_include_snippet_path() -> &'static str {
    "/etc/httpd/conf.d/maran.conf"
}

/// `httpd` loads the proxy, rewrite, headers and expires modules from `conf.modules.d` already,
/// and has no module enabler.
///
/// Observed in `almalinux:9` (9.8) with `grep -h LoadModule /etc/httpd/conf.modules.d/*.conf
/// /etc/httpd/conf/httpd.conf`: `alias`, `auth_basic`, `authn_file`, `authz_core`, `authz_host`,
/// `authz_user`, `dir`, `expires`, `headers`, `mime`, `rewrite` (`httpd.conf` side) and `proxy`,
/// `proxy_fcgi`, `proxy_http`, `ssl` (`00-proxy.conf`, `00-ssl.conf`, the latter from `mod_ssl`)
/// are all loaded.
#[must_use]
pub fn apache_required_modules() -> &'static [&'static str] {
    &[]
}

/// No module enabler on this family.
#[must_use]
pub fn apache_module_enabler() -> Option<AdapterBinary> {
    None
}

/// The major the 10 generation's plain `nodejs` package is, which every other major's package
/// names in its own name (`nodejs24`).
///
/// Observed in `almalinux:10` (10.2) with `dnf list --available 'nodejs*'`: `nodejs` is 22.23.4,
/// `nodejs24` is 24.22.1, and there is no `nodejs22`, `nodejs20` or `nodejs18`
/// (`dnf_list_available_nodejs_el10.txt`). UNVERIFIED: a later 10.x minor may move the default;
/// the install would then leave `/usr/bin/node-22` absent and be reported as not completed
/// rather than as a success.
const EL10_DEFAULT_NODEJS_MAJOR: &str = "22";

/// How the generation packages Node.js.
///
/// The 9 generation ships Node.js as module streams (issue #532); the 10 generation ships no
/// modules and one plain package per major (issue #542).
#[must_use]
pub fn nodejs_packaging(generation: RhelGeneration) -> NodejsPackaging {
    match generation {
        RhelGeneration::El9 => NodejsPackaging::ModuleStreams,
        RhelGeneration::El10 => NodejsPackaging::PerMajorPackages,
    }
}

/// The package that provides Node.js `major`.
///
/// The 9 generation: observed in `almalinux:9` (9.8) with `dnf module list nodejs` and `dnf list
/// --available 'nodejs*'`, there is NO `nodejs20`. Node.js 18, 20, 22 and 24 are AppStream MODULE
/// STREAMS (`nodejs:20`, installed with `dnf module install nodejs:20/common`) whose packages are
/// all named `nodejs`/`npm`, so the package a stream installs has one name for every major, and
/// which major it is depends on the stream that is enabled. The stream is chosen by the module
/// commands of `maran-app-nodejs-install@.service` (issue #532), never by this name.
///
/// The 10 generation: `nodejs` for the default major (`EL10_DEFAULT_NODEJS_MAJOR`) and
/// `nodejs<major>` for the others, which the availability query finds absent for a major the
/// sources do not carry, so that major is not offered.
#[must_use]
pub fn nodejs_package(generation: RhelGeneration, major: &str) -> String {
    match generation {
        RhelGeneration::El9 => "nodejs".to_owned(),
        RhelGeneration::El10 if major == EL10_DEFAULT_NODEJS_MAJOR => "nodejs".to_owned(),
        RhelGeneration::El10 => format!("nodejs{major}"),
    }
}

/// The npm package of Node.js `major`, installed after the interpreter.
///
/// None on the 9 generation: a stream's `common` profile installs `npm` with `nodejs`. On the 10
/// generation npm is a package of each major (`nodejs-npm`, `nodejs24-npm`; there is no `npm`),
/// listed as an extra so the install names it and the probe asks for its launcher. Observed in
/// `almalinux:10` 10.2: `dnf install nodejs` brings `nodejs-npm` along, and the dry run of
/// removing `nodejs24` lists `nodejs24-npm` as a dependent package, so the npm package needs its
/// interpreter and not the other way round; whether `dnf install nodejs24` brings `nodejs24-npm`
/// is NOT observed (the two were installed together), which is why both names are installed.
#[must_use]
pub fn nodejs_extra_packages(generation: RhelGeneration, major: &str) -> Vec<String> {
    match generation {
        RhelGeneration::El9 => Vec::new(),
        RhelGeneration::El10 => vec![format!("{}-npm", nodejs_package(generation, major))],
    }
}

/// What the `common` profile of a stream installs and its removal may therefore take.
///
/// Observed in `almalinux:9` (9.8): `dnf module install nodejs:20/common` installed `nodejs`,
/// `npm`, `nodejs-docs`, `nodejs-full-i18n` and `libbrotli`, and `dnf module remove --assumeno
/// nodejs:20/common` removed `nodejs` and `npm` and listed the other three as unused
/// dependencies (`dnf_module_remove_simulation_nodejs20.txt`). `nodejs-libs`, the library package
/// the interpreter is split from, is listed as an unused dependency of the removal of
/// `nodejs:22/common` (`dnf_module_remove_simulation_nodejs22.txt`). A package missing from a plan
/// costs nothing, one extra here widens what a removal may take.
#[must_use]
pub fn nodejs_module_packages() -> &'static [&'static str] {
    &[
        "nodejs",
        "npm",
        "nodejs-libs",
        "nodejs-docs",
        "nodejs-full-i18n",
    ]
}

/// The arguments of the cache-only listing of the `nodejs` module.
///
/// `-C` is `--cacheonly`: dnf reads the metadata already on disk and contacts no repository,
/// the same guard the pending-updates and candidate reads have. `module list nodejs` takes a
/// fixed module name, so no caller value reaches the run.
#[must_use]
pub fn nodejs_stream_list_arguments() -> Vec<String> {
    ["-C", "module", "list", "nodejs"]
        .iter()
        .map(|argument| (*argument).to_owned())
        .collect()
}

/// What dnf prints on standard error when the sources have no module of the asked name at all
/// (`Error: No matching Modules to list`), with a metadata cache that exists.
const NO_MATCHING_MODULES: &str = "No matching Modules";

/// Reads the output of [`nodejs_stream_list_arguments`]: the catalogue's streams that list the
/// `common` profile, and the streams the host has enabled.
///
/// A row of the listing is `Name Stream Profiles Summary`; the stream may be followed by a state
/// marker (`20 [e]`) and each profile by markers (`common [d] [i],`). Lines whose first word is
/// not `nodejs` (the repository name, the column header, the hint, dnf's own status lines) are
/// not rows. A `nodejs` row too short to hold a stream and a profile, or without a profile name,
/// makes the whole read fail, so a layout the reader does not know never reads as a smaller
/// offer. A stream outside the catalogue, or without a `common` profile, is read and left out of
/// the offer; the `[e]` marker on a stream makes it enabled, offered or not.
///
/// A status other than 0 is never an empty listing. The one exit that is told apart is sources
/// that carry no module streams (`dnf -C module list nodejs` exits 1 with `Error: No matching
/// Modules to list` on standard error and nothing on standard output, which is what the 10
/// generation prints; it is not asked there, it packages Node.js per major, issue #542; a 9
/// generation host whose sources carry no module metadata is UNVERIFIED to answer the same): that is
/// [`PackageQueryFailure::NoModules`], where the same exit with `Error: Cache-only enabled but no
/// cache for 'appstream'` (a cache never built, recorded on both generations) is
/// [`PackageQueryFailure::Unsuccessful`].
///
/// Recorded: `dnf -C module list nodejs` on `almalinux:9` (dnf 4.14.0), the fixtures
/// `dnf_module_list_nodejs_cached.txt`, `dnf_module_list_nodejs_installed.txt` and
/// `dnf_module_list_nodejs_unmanaged_plain_install.txt`; on `almalinux:10` (dnf 4.20.0) and
/// `almalinux:9`, the standard error files `dnf_module_list_nodejs_no_modules.stderr.txt` and
/// `dnf_module_list_nodejs_no_cache.stderr.txt`.
///
/// # Errors
///
/// Returns [`PackageQueryFailure::NoModules`] for sources without modules,
/// [`PackageQueryFailure::Unsuccessful`] for any other status than 0 and
/// [`PackageQueryFailure::UnrecognisedLine`] for a `nodejs` row outside the grammar.
pub fn read_nodejs_streams(
    status: i32,
    stdout: &str,
    stderr: &str,
) -> Result<NodejsStreamListing, PackageQueryFailure> {
    if status != 0 {
        return Err(if stderr.contains(NO_MATCHING_MODULES) {
            PackageQueryFailure::NoModules
        } else {
            PackageQueryFailure::Unsuccessful { status }
        });
    }

    let mut listing = NodejsStreamListing::default();
    for (index, line) in stdout.lines().enumerate() {
        let mut words = line.split_whitespace();
        if words.next() != Some("nodejs") {
            continue;
        }
        let unrecognised = PackageQueryFailure::UnrecognisedLine { line: index + 1 };
        let stream = words.next().ok_or(unrecognised)?;
        let rest: Vec<&str> = words.collect();
        let markers = rest.iter().take_while(|word| is_marker(word)).count();
        if rest[..markers].contains(&"[e]")
            && !stream.is_empty()
            && stream.chars().all(|character| character.is_ascii_digit())
            && !listing.enabled.iter().any(|enabled| enabled == stream)
        {
            listing.enabled.push(stream.to_owned());
        }
        let profiles = profile_names(&rest[markers..]);
        if profiles.is_empty() {
            return Err(unrecognised);
        }

        if !profiles.contains(&"common") {
            continue;
        }
        if let Ok(major) = NodeMajor::parse(stream) {
            let stream = NodejsModuleStream::from_major(major);
            if !listing.offered.contains(&stream) {
                listing.offered.push(stream);
            }
        }
    }
    Ok(listing)
}

/// The command an operator runs to give up an enabled `nodejs` stream that nothing is installed
/// from: the install unit resets the module first, which would drop that choice, so the panel
/// leaves the choice to its owner.
#[must_use]
pub fn nodejs_stream_release_command() -> &'static str {
    "dnf module reset nodejs"
}

/// The command an operator runs to take off a Node.js that was installed with the plain package
/// (no module stream enabled): `nodejs` and what needs it, `npm` included.
#[must_use]
pub fn nodejs_unmanaged_removal_command() -> &'static str {
    "dnf remove nodejs"
}

/// Whether `word` is a state marker of the listing: `[d]`, `[e]`, `[i]`, `[x]`, with the comma
/// that separates profiles when it ends a profile.
fn is_marker(word: &str) -> bool {
    word.starts_with('[') && (word.ends_with(']') || word.ends_with("],"))
}

/// The profile names at the start of `words`: a name, its markers, and a comma that says
/// another name follows. What comes after the last name is the summary and is not read.
fn profile_names<'a>(words: &[&'a str]) -> Vec<&'a str> {
    let mut names = Vec::new();
    let mut remaining = words.iter().copied().peekable();
    while let Some(word) = remaining.next() {
        if is_marker(word) {
            break;
        }
        let (name, mut more) = word
            .strip_suffix(',')
            .map_or((word, false), |name| (name, true));
        if name.is_empty() {
            break;
        }
        names.push(name);
        while let Some(marker) = remaining.next_if(|next| is_marker(next)) {
            more |= marker.ends_with(',');
        }
        if !more {
            break;
        }
    }
    names
}

/// The Node.js interpreter of `major`.
///
/// The 9 generation: a module stream installs one interpreter at a time, at the unversioned path,
/// and enabling another stream replaces it. Observed in `almalinux:9` (9.8) with `dnf module
/// install nodejs:20/common` and `rpm -ql nodejs | grep bin/`: only `/usr/bin/node` (v20.20.2);
/// there is no `/usr/bin/node-20` and no per-major file to name. Whether the installed stream is
/// the asked-for `major` is read from `node --version` by the caller.
///
/// The 10 generation: the majors are separate packages that coexist, and each installs a
/// versioned interpreter. Observed in `almalinux:10` (10.2) with `nodejs` and `nodejs24`
/// installed: `/usr/bin/node-22` (v22.23.4) and `/usr/bin/node-24` (v24.22.1) both run, and the
/// unversioned `/usr/bin/node` is a link to `node-22` that exists only while `nodejs` does; with
/// `nodejs24` alone there is no `/usr/bin/node` at all, and `alternatives --list` names no node
/// (`el10_nodejs_binaries.txt`). The versioned file is therefore the one that is `major`'s on every
/// host.
#[must_use]
pub fn node_binary(generation: RhelGeneration, major: &NodeMajor) -> AdapterBinary {
    match generation {
        RhelGeneration::El9 => AdapterBinary::fixed("/usr/bin/node"),
        RhelGeneration::El10 => {
            AdapterBinary::versioned(format!("/usr/bin/node-{}", major.as_str()))
        }
    }
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_apps_tests.rs"]
mod tests;
