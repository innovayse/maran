namespace Maran.ArchitectureTests;

/// <summary>
/// The one place a vocabulary member may be excused from carrying a localized display name, and the
/// evidence for each excuse.
/// </summary>
/// <remarks>
/// <para>
/// A law with silent exceptions decays into a list of things that used to be true, so nothing here
/// is taken on trust: <see cref="DisplayNameLawTests"/> resolves every locale key template in
/// English, Russian and Armenian, opens every SPA file named as evidence and requires it to still
/// mention the member, and asserts that every entry — the pending ones most of all — still names a
/// member the law would otherwise flag. An entry that has become unnecessary fails the suite until
/// it is deleted.
/// </para>
/// <para>
/// Adding an entry therefore costs a sentence and a piece of evidence, which is the price this
/// register is for. If neither can be written, the member needs a display name, not an entry.
/// </para>
/// </remarks>
public static class DisplayNameExemptions
{
    /// <summary>Every declared exemption, grouped by the module the DTO belongs to.</summary>
    public static readonly IReadOnlyList<DisplayNameExemption> Declared =
    [
        new(
            "Maran.Modules.Cron.Common.CronRunStoppedDto",
            "Limit",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "cron.runs.limit.{member}",
            "The agent's two ceilings, carried only by a run that one of them stopped, runtime and output: a closed set the result dialog explains in one "
            + "sentence each (issue #158)."),
        new(
            "Maran.Modules.Accounts.Common.AccountResourceUsageDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "accounts.resources.kinds.{member}",
            "The resource kinds are a closed Sdk enum, one per counting module, and the summary card "
            + "labels each row with the same words the customer's limits screen already uses."),
        new(
            "Maran.Modules.Backups.Common.BackupDestinationDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "backups.destinations.kinds.{member}.label",
            "The kind of storage is named and explained by the SPA, which needs both a label and a "
            + "sentence for it; a display name on the wire would carry the label and leave the "
            + "sentence homeless. The row's own English label is a different problem and is solved "
            + "on the wire by DisplayName."),
        new(
            "Maran.Modules.Backups.Common.BackupScheduleDto",
            "Frequency",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/pages/backups/BackupSchedulePage.vue",
            "The cadence is only echoed back into the schedule form's own picker, whose choices and "
            + "their names are the backend's (GET /api/v1/backup-options, issue #330), not a label "
            + "drawn from this field; no screen prints the field as a name."),
        new(
            "Maran.Modules.Backups.Common.BackupScheduleDto",
            "DayOfWeekUtc",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/pages/backups/BackupSchedulePage.vue",
            "A weekday is only echoed back into the schedule form's own picker, whose choices and "
            + "their names are the backend's (GET /api/v1/backup-options, issue #330): the "
            + "picker never prints this field as a name, so a display name beside it on the wire "
            + "would be a second, redundant translation of a word the options list already names."),
        new(
            "Maran.Modules.Firewall.Common.BanDto",
            "Reason",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "firewall.bans.reasons.{member}",
            "Manual or brute force: two values the panel itself writes, shown as a column of one "
            + "word each."),
        new(
            "Maran.Modules.Firewall.Common.FirewallPresetDto",
            "Key",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/firewall/FirewallPresetButtons.vue",
            "The key is the preset's identity, which the card uses to tell one preset from another "
            + "and to ask for it by name; the words an operator reads are the sibling Name, resolved "
            + "by the panel for the request's culture (issue #330). No screen prints the key."),
        new(
            "Maran.Modules.Firewall.Common.FirewallRuleDto",
            "Protocol",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/firewall/FirewallRulesTable.vue",
            "The protocol is the key the rule table looks up in the panel's own list of protocol "
            + "choices (GET /api/v1/firewall/rules/protocols, issue #330), whose protocolName is the "
            + "backend's word for it; the SPA holds no label of its own, so a display name beside "
            + "this field on the wire would repeat what the choices list already names."),
        new(
            "Maran.Modules.Identity.Common.AuthenticatedUserDto",
            "Role",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "app.auth.role.{member}",
            "Administrator or customer, decided by this product and shown in the shell's own "
            + "chrome beside the user's name."),
        new(
            "Maran.Modules.Identity.Common.ProfileDto",
            "Role",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "app.auth.role.{member}",
            "The same administrator-or-customer vocabulary as AuthenticatedUserDto.Role, shown on "
            + "the profile page through the same keys."),
        new(
            "Maran.Modules.ServerMigration.Common.DiscoveryWarningDto",
            "Kind",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/migrationSource.ts",
            "The kind is the closed machine name of the warning, carried so the screen can group and "
            + "badge warnings; the sentence the operator reads is the dto's own localized message."),
        new(
            "Maran.Modules.Monitoring.Common.MetricsChartDto",
            "Range",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/monitoring.ts",
            "The range is the value the SPA asked for, echoed back so a late answer can be matched "
            + "to the control that requested it. The screen prints its own control's label and "
            + "never this value."),
        new(
            "Maran.Modules.Monitoring.Common.ServiceStatusDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "monitoring.services.{member}",
            "Up, down and not-known are three badge tones as much as three words, and the badge "
            + "must carry text so the tone is never the only thing saying which state it is. The "
            + "service's identity beside it is the opposite case and is named by the backend."),
        new(
            "Maran.Modules.Monitoring.Common.ClockSyncDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "monitoring.clock.{member}",
            "The clock card's badge, a tone as much as a word, like ServiceStatusDto.State. The "
            + "sentence saying what the state means is the backend's, in Description (issue #150)."),
        new(
            "Maran.Modules.Sites.Common.SiteLogEndDto",
            "Reason",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "sites.detail.logs.endReason.{member}",
            "Why a log tail stopped is the SPA's own chrome around a stream it is holding open, "
            + "and it is shown while no request is outstanding to ask the panel for wording."),
        new(
            "Maran.Modules.Ssl.Common.CertificateDto",
            "Source",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "certificates.source.{member}",
            "Issued here or uploaded: two values the panel writes itself, shown as a column."),
        new(
            "Maran.Modules.Ssl.Common.CertificateDto",
            "LastRenewalErrorCode",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/certificate.ts",
            "Carried for diagnosis beside the failure count; no screen prints it. The sentence a "
            + "screen shows is the LastRenewalError member beside it, resolved by the backend in the "
            + "reader's language. It is on this list rather than off the wire because a support "
            + "session reads the code from the response, and the day a screen does show it this "
            + "entry stops being true — which is what the evidence check is for."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "DesiredState",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "ftp.server.desiredStates.{member}",
            "Never configured, on and off are the three things the panel can want of its one FTPS "
            + "daemon, drawn as the panel-setting badge; the set is closed by the domain."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "TlsEnforcement",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "ftp.server.tlsEnforcement.{member}",
            "Enforced, not enforced and not observed are three sentences the server panel draws under "
            + "the encryption line; the set is closed by the agent's own three-valued answer, and "
            + "not-observed is worded as an admission rather than as off."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "Certificate",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/ftp/FtpsServerPanel.vue",
            "A decision input and never a printed name: the panel only compares it with the self-signed "
            + "case to show its own warning sentence, and the certificate line prints the file path."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "Agreement",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/ftp/FtpsServerPanel.vue",
            "A verdict the server panel turns into an explanatory sentence, or into nothing when "
            + "intention and observation agree; it is never printed as the name of anything."),
        new(
            "Maran.Host.Modules.ModuleDto",
            "State",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/module.ts",
            "Decides whether a module's entries are drawn open or locked and is never printed as a name: "
            + "the locked badge's text is the shell's own chrome, and the module's own name travels "
            + "beside it as DisplayName."),
        new(
            "Maran.Host.Modules.ModuleDto",
            "Lifecycle",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/module.ts",
            "Decides whether the shell registers a packaged module's routes and frames its screen, and is "
            + "never printed as a name: the sentence an operator reads for a degraded or failed module "
            + "travels beside it, localized, as FailureDisplay."),
        new(
            "Maran.Modules.Identity.Common.SubUserDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "subUsers.states.{member}",
            "Active, invited, invited while the account is suspended, and suspended: a closed set the "
            + "team table draws as its status column, worded by the bundle beside the column's header."),
        new(
            "Maran.Modules.Sites.Common.PhpVersionDto",
            "DefaultStatus",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "php.table.defaultStatus.{member}",
            "Default, not default and not established are the three answers of the versions table's "
            + "default column, which the page words as yes, no and not established beside the badge tone."),
        new(
            "Maran.Modules.Identity.Common.ViewingAsDto",
            "Kind",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/shell/ShellViewingAsBanner.vue",
            "Chooses which of the banner's two sentences is drawn, account or reseller, and is never "
            + "printed as a name: each sentence is the shell's own chrome and the viewed subject's own "
            + "name travels beside it as SubjectName (issues #317, #314)."),
        new(
            "Maran.Modules.Tasks.Common.TaskFailureDto",
            "Code",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/tasks/TaskLivePane.vue",
            "Shown framed as an identifier — \"Code: X\" — beneath its sibling Message, the "
            + "sentence the backend resolves for the reader's culture (issue #272). The defect this "
            + "law is about is a code printed AS the name of the thing; a code labelled as a code "
            + "beside its own sentence is the cure for it, not an instance of it."),
        new(
            "Maran.Modules.Files.Common.FileBatchItemResultDto",
            "Code",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/file.ts",
            "Read only to branch on the outcome and never printed: the file manager's batch notice words each "
            + "failed item with its sibling Message, the sentence the backend resolves for the reader's culture, "
            + "so no screen draws the code as the name of anything."),
        new(
            "Maran.Modules.Mail.Common.MailServerStatusDto",
            "ObservedEnabled",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "An observation of the host (yes, no, or not observed) that only the administrator's mail server screen reads; the screen and its wording arrive with it, in the same change as the SPA locale keys."),
        new(
            "Maran.Modules.Mail.Common.MailServerStatusDto",
            "TlsInUse",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "An observation of the host (yes, no, or not observed) that only the administrator's mail server screen reads; the screen and its wording arrive with it, in the same change as the SPA locale keys."),
        new(
            "Maran.Modules.Mail.Common.MailServerStatusDto",
            "SieveAvailable",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "An observation of the host (yes, no, or not observed) that only the administrator's mail server screen reads; the screen and its wording arrive with it, in the same change as the SPA locale keys."),
        new(
            "Maran.Modules.Mail.Common.MailServerStatusDto",
            "PasswordScheme",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "A closed vocabulary of the mail stack that only the administrator's mail server screen reads; the screen and its wording arrive with it."),
        new(
            "Maran.Modules.Mail.Common.MailDaemonStatusDto",
            "Kind",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "A closed vocabulary of the mail stack that only the administrator's mail server screen reads; the screen and its wording arrive with it."),
        new(
            "Maran.Modules.Mail.Common.MailDaemonStatusDto",
            "Installed",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "An observation of the host (yes, no, or not observed) that only the administrator's mail server screen reads; the screen and its wording arrive with it, in the same change as the SPA locale keys."),
        new(
            "Maran.Modules.Mail.Common.MailDaemonStatusDto",
            "Active",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "An observation of the host (yes, no, or not observed) that only the administrator's mail server screen reads; the screen and its wording arrive with it, in the same change as the SPA locale keys."),
        new(
            "Maran.Modules.Mail.Common.MailDaemonStatusDto",
            "ListenersObserved",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "An observation of the host (yes, no, or not observed) that only the administrator's mail server screen reads; the screen and its wording arrive with it, in the same change as the SPA locale keys."),
        new(
            "Maran.Modules.Mail.Common.MailQueueEntryDto",
            "DeferralClass",
            DisplayNameExemptionKind.PendingFix,
            "the administrator's mail server screen, which is not built yet",
            "A closed vocabulary of the mail stack that only the administrator's mail server screen reads; the screen and its wording arrive with it."),
        new(
            "Maran.Modules.Mail.Common.MailDnsRecordDto",
            "Type",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/mailDomain.ts",
            "The DNS record type as DNS itself spells it (MX, TXT): a protocol token the customer copies into their zone, framed as one, with the record's purpose named beside it in their language."),
        new(
            "Maran.Host.Dashboard.FirstRunStepDto",
            "Key",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "firstRun.steps.{member}.title",
            "The five steps of the first-run checklist are a closed set the card words itself, a title, a description and an action each, in the bundle beside the card; a step this bundle has no wording for is left out rather than drawn as a token."),
        new(
            "Maran.Host.Dashboard.FirstRunStepDto",
            "RouteName",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/dashboard/DashboardFirstRunChecklist.vue",
            "A closed route name the card maps to a page it has and never prints; the words an operator reads are the step's own title and action, drawn from the step key."),
        new(
            "Maran.Host.Search.SearchGroupDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "search.groups.{member}",
            "Accounts and sites are the two things the jump-to palette searches, and it heads each group with the same word the shell already uses for them."),
        new(
            "Maran.Host.Search.SearchGroupDto",
            "State",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/shell/ShellCommandPalette.vue",
            "Read only to decide whether a group lists hits or a muted note; the note is the shell's own sentence about the group, so the state is never printed as the name of anything."),
        new(
            "Maran.Modules.Databases.Common.DatabasePrivilegeDto",
            "Key",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/databases/DatabaseUserPrivilegesDialog.vue",
            "The key is the privilege's identity, which the dialog ticks and sends back; the words an operator reads are the sibling Name, resolved by the panel for the request's culture. No screen prints the key."),
        new(
            "Maran.Modules.Databases.Common.DatabasePrivilegePresetDto",
            "Key",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/databases/DatabaseUserPrivilegesDialog.vue",
            "The key is the preset's identity, which the dialog uses to tell one preset from another and to apply it; the words an operator reads are the sibling Name, resolved by the panel for the request's culture. No screen prints the key."),
        new(
            "Maran.Modules.Dns.Common.DnsStatusDto",
            "RecursionRefused",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.server.policy.{member}",
            "Not observed, in force and not in force are the three answers of the server panel's policy rows, worded as an admission when the agent did not look rather than as off; the set is closed by the agent's own three-valued answer."),
        new(
            "Maran.Modules.Dns.Common.DnsStatusDto",
            "ResponseRateLimiting",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.server.policy.{member}",
            "The same three-valued answer as RecursionRefused, drawn by the same row component through the same keys."),
        new(
            "Maran.Modules.Dns.Common.RecordTypeDto",
            "Type",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/dns/DnsRecordDialog.vue",
            "The record type as DNS itself spells it (A, MX), the value the picker sends back; the words an operator reads are the sibling Label and Description of the same catalogue row, resolved by the panel for the request's culture."),
        new(
            "Maran.Modules.Dns.Common.ZoneDnssecDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.dnssec.states.{member}",
            "Not signed, signed and withdrawing are the three steps of a zone's DNSSEC life, a set the server closes; the zone's DNSSEC panel words each one next to the registrar instructions that go with it."),
        new(
            "Maran.Modules.Dns.Common.ZoneDnssecObservationDto",
            "Mode",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.dnssec.states.{member}",
            "The step the name server reports for the zone is one of the same three steps, worded by the same keys as the zone's own."),
        new(
            "Maran.Modules.Dns.Common.ZoneDnssecObservationDto",
            "ModeObserved",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.dnssec.observed.reports.{member}",
            "Not observed, in force and not in force are the three answers of an observation, worded as an admission when the agent did not look rather than as off; the set is closed by the agent's own three-valued answer."),
        new(
            "Maran.Modules.Dns.Common.ZoneDnssecObservationDto",
            "PublishedKeyObserved",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.dnssec.observed.reports.{member}",
            "The same three-valued answer as ModeObserved, drawn through the same keys."),
        new(
            "Maran.Modules.Dns.Common.ZoneDto",
            "DnssecState",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "dns.dnssec.states.{member}",
            "The zone's DNSSEC step as the zone list and the delete confirmation show it, worded by the same keys as the zone's DNSSEC panel."),
        new(
            "Maran.Modules.Dns.Common.ZoneRecordDto",
            "Type",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/dns/DnsRecordsTable.vue",
            "The record type as DNS itself spells it, the key the table looks up in the panel's own type catalogue, whose Label is the backend's word for it; the raw token is only the fallback while the catalogue has not arrived."),
        new(
            "Maran.Modules.Notifications.Common.AlertChannelStatusDto",
            "State",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/dashboard/DashboardAttentionList.vue",
            "Read only to decide whether a channel needs attention; the sentence an operator reads is the sibling FailureMessage, resolved by the panel for the reader's culture, and the state itself is never printed."),
        new(
            "Maran.Modules.Notifications.Common.NotificationDto",
            "Source",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/notification.ts",
            "Which part of the panel raised the entry, carried for scripts and support; no screen draws it, the headline and detail beside it are the panel's sentences in the reader's language."),
        new(
            "Maran.Modules.Notifications.Common.NotificationDto",
            "Severity",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/shell/ShellNotificationRow.vue",
            "Chooses the glyph and colour of the entry's mark and is never printed as a word; the headline sentence beside it, worded by the panel, carries the meaning, so the colour is never the only signal."),
        new(
            "Maran.Modules.Sites.Common.PhpSettingItemDto",
            "Kind",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/sites/PhpSettingsForm.vue",
            "Chooses which control edits the setting (a size, a number, a switch, a choice, a time zone) and is never printed; the words an operator reads are the sibling Label and the unit, worded by the panel."),
        new(
            "Maran.Modules.Sites.Common.SiteOptionsDto",
            "CanonicalHost",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/sites/SiteCanonicalHostCard.vue",
            "The current value echoed into the card's picker, whose choices and their names are the panel's own (SiteOptionsChoicesDto.CanonicalHosts); the card never prints this field as a name."),
        new(
            "Maran.Modules.Sites.Common.SiteOptionsDto",
            "IpAccessMode",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/composables/useSiteOptionsForm.ts",
            "The current value echoed into the access-rules picker, whose choices and their names are the panel's own (SiteOptionsChoicesDto.IpAccessModes); no screen prints this field as a name."),
        new(
            "Maran.Modules.Sites.Common.SiteRedirectDto",
            "Match",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/sites/SiteRedirectsEditor.vue",
            "The current value echoed into the rule's picker, whose choices and their names are the panel's own (SiteOptionsChoicesDto.RedirectMatches); the editor never prints this field as a name."),
        new(
            "Maran.Modules.Sites.Common.SiteRedirectDto",
            "Status",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/sites/SiteRedirectsEditor.vue",
            "The current value echoed into the rule's picker, whose choices and their names are the panel's own (SiteOptionsChoicesDto.RedirectStatuses); the editor never prints this field as a name."),
        new(
            "Maran.Modules.Marketplace.Common.MarketplaceModuleDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "marketplace.states.{member}",
            "Available, installed, update available, revoked and incompatible are the five things a catalogue row can be on this panel, a set the panel closes; the badge words each one, and the entry's tone repeats it."),
        new(
            "Maran.Modules.Marketplace.Common.InstalledModuleDto",
            "Lifecycle",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "marketplace.lifecycle.{member}",
            "Installed, active, degraded, failed and removed are the loader's five steps for a module, a set the Sdk closes; the installed list words each one in its state badge."),
        new(
            "Maran.Modules.ServerMigration.Common.MigrationFindingDto",
            "Code",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/migration/MigrationFindingList.vue",
            "Read only to tell the cron notice from the other findings and as the row's key, and never printed: the sentence an operator reads is the sibling Message, resolved by the panel for the reader's culture."),
        new(
            "Maran.Modules.ServerMigration.Common.MigrationAccountFindingDto",
            "Code",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/migration/MigrationFindingList.vue",
            "The same finding as MigrationFindingDto, about one source account, drawn by the same list: the code tells the cron notice apart and is never printed, the sentence is the sibling Message."),
        new(
            "Maran.Modules.ServerMigration.Common.MigrationSummaryFileDto",
            "ContentType",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/composables/useApi.ts",
            "The media type of a file the controller streams back, set on the response header and read by the client as the saved blob's type; it is not part of any JSON body and no screen prints it."),
        new(
            "Maran.Modules.Ssl.Common.IssuanceOptionsDto",
            "WildcardUnavailableCode",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/certificate.ts",
            "Carried for scripts and support beside the sentence; no screen reads it. What the SSL tab shows is the sibling WildcardUnavailableMessage, resolved by the panel in the reader's language."),
    ];
}
