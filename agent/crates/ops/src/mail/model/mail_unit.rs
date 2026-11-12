//! The service-manager commands the mail lifecycle runs against one unit.

use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// One systemd unit and the argument vectors the lifecycle ever hands the
/// service manager for it.
///
/// Every vector ends with the end-of-options marker and then the unit name, so a
/// name is never read as an option. The unit name is a distro fact, passed in;
/// the program is never named here either — it is
/// `DistroAdapter::service_manager()`.
#[derive(Debug, Clone, Copy)]
pub struct MailUnit<'a> {
    /// The unit's name, as the adapter spells it.
    name: &'a str,
}

impl<'a> MailUnit<'a> {
    /// Wraps the name of a unit.
    #[must_use]
    pub fn new(name: &'a str) -> Self {
        Self { name }
    }

    /// The unit's name.
    #[must_use]
    pub fn name(&self) -> &'a str {
        self.name
    }

    /// Remove the mask the installer put on the packaged unit.
    #[must_use]
    pub fn unmask(&self) -> [&'a str; 3] {
        ["unmask", END_OF_OPTIONS, self.name]
    }

    /// Put the unit in the boot sequence.
    #[must_use]
    pub fn enable(&self) -> [&'a str; 3] {
        ["enable", END_OF_OPTIONS, self.name]
    }

    /// Ask whether the unit is active; exits zero when it is.
    #[must_use]
    pub fn is_active(&self) -> [&'a str; 3] {
        ["is-active", END_OF_OPTIONS, self.name]
    }

    /// Start the unit when it is stopped and ask it to re-read its configuration
    /// when it runs, which is the one verb that does the right thing in both
    /// states and so is what makes a repeated enable converge.
    #[must_use]
    pub fn reload_or_restart(&self) -> [&'a str; 3] {
        ["reload-or-restart", END_OF_OPTIONS, self.name]
    }

    /// Ask a RUNNING unit to re-read its configuration. Unlike
    /// [`Self::reload_or_restart`] it never starts a stopped unit: the service manager
    /// refuses it for one, which is what a caller that must not bring a server up needs.
    #[must_use]
    pub fn reload(&self) -> [&'a str; 3] {
        ["reload", END_OF_OPTIONS, self.name]
    }

    /// Stop the unit and take it out of the boot sequence, in one command: a
    /// stopped unit that stays enabled is an operator's decision undone by the
    /// next reboot.
    #[must_use]
    pub fn disable_now(&self) -> [&'a str; 4] {
        ["disable", "--now", END_OF_OPTIONS, self.name]
    }

    /// Mask the unit, so that nothing — a package upgrade's post-install script
    /// included — can start it until the lifecycle unmasks it again.
    #[must_use]
    pub fn mask(&self) -> [&'a str; 3] {
        ["mask", END_OF_OPTIONS, self.name]
    }
}

#[cfg(test)]
#[path = "../../tests/mail/mail_unit_tests.rs"]
mod tests;
