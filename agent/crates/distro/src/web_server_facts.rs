//! The web server and TLS tooling: nginx, Apache, the identities they run as, the Node.js
//! launchers a site's unit runs, and openssl.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use maran_agent_core::validation::web::node_major::NodeMajor;

use crate::adapter_binary::AdapterBinary;
use crate::site_web_server::SiteWebServer;

/// The web server and TLS tooling: nginx, Apache, the identities they run as, the Node.js
/// launchers a site's unit runs, and openssl.
pub trait WebServerFacts: Send + Sync {
    /// Absolute path of the nginx binary, for the process-execution allow-list.
    ///
    /// Both families install nginx at `/usr/sbin/nginx`, but the value still comes
    /// from the adapter rather than a literal in `ops`: a binary is only spawnable
    /// because this trait named it, not because the path happens to agree today.
    ///
    /// Like every `*_binary` method below that names a program the agent
    /// executes, it returns an [`AdapterBinary`], a type only this crate can
    /// construct, so the allow-list is enforced by the compiler: a seam that
    /// takes `&AdapterBinary` cannot be handed a string from anywhere else.
    /// (`sh_binary` stays text on purpose: it is written into a crontab line
    /// and never executed by the agent.)
    fn nginx_binary(&self) -> AdapterBinary;

    /// Name of the nginx service unit, for `systemctl reload`/`restart`.
    fn nginx_service(&self) -> &'static str;

    /// The user the web server runs as, which must own nothing and be able to
    /// read a site's document root.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family. The
    /// document root's group is set to this so the server can read files the
    /// account owns without either of them being able to write the other's.
    fn web_server_user(&self) -> &'static str;

    /// The group an account's home directory is group-owned by, so the web
    /// server can traverse into a document root the account owns.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family — the same
    /// word as the user on both, and asked as its own question all the same:
    /// a group is what a directory is owned by, and the two names agreeing is a
    /// fact about these two distributions rather than a rule.
    fn web_server_group(&self) -> &'static str;

    /// Absolute path of the Apache program whose `-t` checks the whole configuration tree, for
    /// the process-execution allow-list.
    ///
    /// The agent runs it with `-t` before and after every vhost write, the way it runs nginx's own
    /// `-t` for the other site web server, and with `-v` to read the version. `apachectl` on the
    /// RHEL family, `apache2ctl` on the Debian family, `httpd` on the SUSE family (whose
    /// `apache2` package ships no control script).
    fn apache_binary(&self) -> AdapterBinary;

    /// Name of the Apache service unit, for `systemctl reload`/`restart`.
    ///
    /// `apache2` on the Debian and SUSE families, `httpd` on the RHEL family.
    fn apache_service(&self) -> &'static str;

    /// The user the Apache workers run as, which must own nothing and be able to read a
    /// site's document root.
    ///
    /// Asked separately from [`Self::web_server_user`]: that is nginx's identity, and the
    /// two differ on every family (`apache` against `nginx` on RHEL, `wwwrun` against
    /// `nginx` on SUSE).
    fn apache_user(&self) -> &'static str;

    /// The group the Apache workers run in, which an account's home is group-owned by when
    /// Apache is the site web server.
    fn apache_group(&self) -> &'static str;

    /// Absolute path of the Node.js interpreter of `major` that a site's unit runs: the same
    /// file [`AppFacts::node_binary`](crate::AppFacts::node_binary) names, so a site declared on
    /// a major runs on that major and on no other.
    ///
    /// Text and not an [`AdapterBinary`], like `sh_binary`: the agent never executes it, it
    /// is written into a unit the service manager runs under the site's account.
    fn node_executable(&self, major: &NodeMajor) -> String;

    /// Absolute path of the npm launcher of `major` that a site's unit may run, text for the
    /// reason [`Self::node_executable`] is.
    fn npm_executable(&self, major: &NodeMajor) -> String;

    /// Absolute path of the binary whose `-t` checks `server`'s whole configuration tree.
    fn site_web_server_binary(&self, server: SiteWebServer) -> AdapterBinary {
        match server {
            SiteWebServer::Nginx => self.nginx_binary(),
            SiteWebServer::Apache => self.apache_binary(),
        }
    }

    /// Name of the service unit that `systemctl reload` reaches `server` through.
    fn site_web_server_service(&self, server: SiteWebServer) -> &'static str {
        match server {
            SiteWebServer::Nginx => self.nginx_service(),
            SiteWebServer::Apache => self.apache_service(),
        }
    }

    /// The user `server`'s workers run as.
    fn site_web_server_user(&self, server: SiteWebServer) -> &'static str {
        match server {
            SiteWebServer::Nginx => self.web_server_user(),
            SiteWebServer::Apache => self.apache_user(),
        }
    }

    /// The group `server`'s workers run in, which an account's home is group-owned by.
    fn site_web_server_group(&self, server: SiteWebServer) -> &'static str {
        match server {
            SiteWebServer::Nginx => self.web_server_group(),
            SiteWebServer::Apache => self.apache_group(),
        }
    }

    /// Absolute path of the openssl binary, for the process-execution
    /// allow-list.
    ///
    /// The certificate operations run it to read a certificate's public key
    /// and its expiry, and to generate the self-signed placeholder a site
    /// serves before a real certificate arrives. Both families install it at
    /// the same path today; it is still asked of the adapter, because `ops`
    /// names no absolute binary path of its own.
    fn openssl_binary(&self) -> AdapterBinary;
}
