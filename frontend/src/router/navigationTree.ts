/**
 * The navigation tree: the single source of the sidebar, the collapsed rail, the drawer, the tab
 * bar, the jump-to palette, the breadcrumb and the page eyebrow (issue #311).
 *
 * One tree for every role. The administrator's menu, the reseller's (issue #179) and the customer's
 * (issue #312) are each this tree filtered by {@link NavigationGate.roles}, so a page cannot appear
 * in one role's menu under a different name or place than in another's. Items are grouped by the
 * operator's task, not by URL: the audit journal is under Monitoring and the security policy under
 * Security while both routes stay under `/settings`.
 *
 * A page that is not built yet is listed as `planned` with the issue that builds it, and is drawn
 * disabled. When the page lands, its availability becomes `available` with the route name, and
 * nothing else changes.
 *
 * Offering an item is advice: every endpoint behind a page enforces the caller's role on its own.
 */

import type { AccountFeatureName } from '../types/accountFeature'
import type {
  NavigationAvailability,
  NavigationGroupSpec,
  NavigationPageSpec,
  NavigationRole,
} from '../types/navigation'

/**
 * Shorthand for a built page's availability.
 * @param route The route name the page is registered under.
 * @returns The `available` availability.
 */
const routed = (route: string): NavigationAvailability => {
  return { state: 'available', route }
}

/**
 * Shorthand for a page not built yet.
 * @param issue The issue that builds the page, or null when none is filed yet.
 * @returns The `planned` availability.
 */
const planned = (issue: number | null): NavigationAvailability => {
  return { state: 'planned', issue }
}

/**
 * Builds one page of the tree. Every page states all of its fields; this only keeps each one to a
 * line so the tree reads as the menu it draws.
 * @param key Stable key, unique across the tree.
 * @param labelKey i18n key of the menu label.
 * @param availability Whether the page is built, and its route.
 * @param roles The roles the page is offered to.
 * @param module The licensed module the page belongs to, or null for the panel's own screens.
 * @param planFeature The hosting-plan feature the page needs, spelled as the panel names it
 *   (`GET /api/v1/accounts/me` lists the same names), or null when it needs none.
 * @returns The page, with the given plan feature and no badge.
 */
const page = (
  key: string,
  labelKey: string,
  availability: NavigationAvailability,
  roles: readonly NavigationRole[],
  module: string | null,
  planFeature: AccountFeatureName | null = null,
): NavigationPageSpec => {
  return {
    key,
    labelKey,
    availability,
    roles,
    module,
    planFeature,
    ownerOnly: false,
    viewAsRefused: false,
    runtime: null,
    badge: null,
    searchTermsKey: null,
  }
}

/**
 * Gives a page the extra words the jump-to palette finds it by, beside its menu label.
 * @param spec The page, as {@link page} built it.
 * @param terms Name of the entry under `app.nav.searchTerms` holding the words.
 * @returns The same page with its search words set.
 */
const findBy = (spec: NavigationPageSpec, terms: string): NavigationPageSpec => {
  return { ...spec, searchTermsKey: `app.nav.searchTerms.${terms}` }
}

/** Administrators only: the host and the panel itself. */
const ADMIN: readonly NavigationRole[] = ['admin']

/** Administrators and resellers: managing other people's accounts. */
const MANAGERS: readonly NavigationRole[] = ['admin', 'reseller']

/**
 * The administrator and the customer: pages backed by the tenant modules (sites, databases, file
 * transfer, cron, backups, tasks), which answer a reseller with no rows. A reseller reaches a
 * customer's resources only by viewing as that customer (issue #179), so these are not offered to
 * the reseller's own login.
 */
const ACCOUNT_HOLDERS: readonly NavigationRole[] = ['admin', 'customer']

/** The customer alone: the account owner's own view of what an operator sees from above. */
const CUSTOMER: readonly NavigationRole[] = ['customer']

/** The signed-in reseller's own settings, which an administrator keeps under the account menu. */
const RESELLER: readonly NavigationRole[] = ['reseller']

/**
 * Catalogue modules that deliberately have no item of their own: `ssl` is a tab on the site it
 * protects, `identity` is the signed-in person's own screens (the account menu), and
 * `notifications` is the outgoing-mail page, which carries no module so that a licence cannot
 * switch off password-reset mail, and `idempotency` is a backend facility (the store behind the
 * Idempotency-Key header) with no screen at all, and `updates` serves the "Panel updates" page the Settings
 * section already links (issue #382), and `licensing` serves the "Licence" page the same section links (issue #8). Any OTHER module the catalogue reports and the
 * tree does not name — a module newer than this bundle — still gets an entry, in the fallback group.
 */
export const MODULES_WITHOUT_ITEM: ReadonlySet<string> = new Set([
  'ssl',
  'identity',
  'notifications',
  'idempotency',
  'updates',
  'licensing',
])

/** Group a catalogue module the tree does not know is drawn under. */
export const FALLBACK_GROUP = 'panel'

/**
 * Group the entries of installed (packaged) modules are drawn under. Their entries do not come from
 * this tree: the module declares them and the backend localizes them, so the tree can only name the
 * group (`utils/packagedNavigation.ts` builds the entries).
 */
export const PACKAGED_GROUP = 'modules'

/** The whole navigation, in drawing order. */
export const NAVIGATION_TREE: readonly NavigationGroupSpec[] = [
  {
    key: 'overview',
    nodes: [
      {
        kind: 'link',
        icon: 'house',
        ...page('dashboard', 'app.status.heading', routed('system-status'), ACCOUNT_HOLDERS, null),
        badge: 'needsAttention',
      },
      // The reseller's home: its envelope and what it has used of it, in place of the server
      // dashboard, whose figures are the host's and not theirs to see (issue #179).
      {
        kind: 'link',
        icon: 'house',
        ...page('reseller-overview', 'app.nav.children.resellerOverview', routed('reseller-overview'), RESELLER, 'accounts'),
      },
    ],
  },
  {
    key: 'hosting',
    nodes: [
      {
        kind: 'section',
        key: 'accounts',
        labelKey: 'app.nav.items.accounts',
        icon: 'users',
        children: [
          page('accounts-list', 'app.nav.children.accountsList', routed('accounts'), MANAGERS, 'accounts'),
          page('accounts-new', 'app.nav.children.accountsNew', routed('accounts-new'), MANAGERS, 'accounts'),
          page('plans', 'app.nav.plans', routed('plans'), MANAGERS, 'accounts'),
          page('resellers', 'app.nav.children.resellers', routed('resellers'), ADMIN, 'accounts'),
        ],
      },
      {
        kind: 'section',
        key: 'sites',
        labelKey: 'app.nav.items.sites',
        icon: 'earth',
        children: [
          findBy(
            page('sites-list', 'app.nav.children.sitesList', routed('sites'), ACCOUNT_HOLDERS, 'sites'),
            'sitesList',
          ),
          page('site-domains', 'app.nav.children.siteDomains', planned(null), ACCOUNT_HOLDERS, null),
          page('site-certificates', 'app.nav.children.siteCertificates', planned(null), ACCOUNT_HOLDERS, null),
          findBy(
            { ...page('site-php', 'app.nav.children.sitePhp', routed('php-settings'), CUSTOMER, 'sites', 'php'), runtime: 'php' },
            'sitePhp',
          ),
          findBy(
            { ...page('php-versions', 'app.nav.phpVersions', routed('php-versions'), ADMIN, null), runtime: 'php' },
            'phpVersions',
          ),
        ],
      },
      {
        kind: 'section',
        key: 'dns',
        labelKey: 'app.nav.items.dns',
        icon: 'globe',
        children: [
          // One zones page in two menus, the way tasks is: the administrator's "Zones" lists every
          // account's, the customer's "My zones" their own. Offered to administrators only and not
          // to resellers, whose context shows no DNS rows (issue #178).
          page('dns-zones', 'app.nav.children.dnsZones', routed('dns-zones'), ADMIN, 'dns', 'dns'),
          page('dns-my-zones', 'app.nav.children.dnsMyZones', routed('dns-zones'), CUSTOMER, 'dns', 'dns'),
          page('dns-templates', 'app.nav.children.dnsTemplates', routed('dns-templates'), ADMIN, 'dns'),
          page('dns-nameservers', 'app.nav.children.dnsNameservers', routed('dns-nameservers'), ADMIN, 'dns'),
        ],
      },
      {
        kind: 'section',
        key: 'mail',
        labelKey: 'app.nav.items.mail',
        icon: 'mail',
        children: [
          page('mail-domains', 'app.nav.children.mailDomains', routed('mail-domains'), ACCOUNT_HOLDERS, 'mail', 'mail'),
          page('mailboxes', 'app.nav.children.mailboxes', routed('mailboxes'), ACCOUNT_HOLDERS, 'mail', 'mail'),
          page('mail-forwarders', 'app.nav.children.mailForwarders', routed('mail-aliases'), ACCOUNT_HOLDERS, 'mail', 'mail'),
          page('mail-autoresponders', 'app.nav.children.mailAutoresponders', planned(177), CUSTOMER, null, 'mail'),
          page('mail-spam-filter', 'app.nav.children.mailSpamFilter', planned(177), CUSTOMER, null, 'mail'),
          page('webmail', 'app.nav.children.webmail', planned(177), CUSTOMER, null, 'mail'),
          {
            ...page('mail-queue', 'app.nav.children.mailQueue', routed('mail-queue'), ADMIN, 'mail'),
            badge: 'mailQueue',
          },
          page('mail-authentication', 'app.nav.children.mailAuthentication', planned(177), MANAGERS, null, 'mail'),
          page('mail-limits', 'app.nav.children.mailLimits', planned(177), MANAGERS, null, 'mail'),
        ],
      },
      {
        kind: 'section',
        key: 'databases',
        labelKey: 'app.nav.items.databases',
        icon: 'database',
        children: [
          page(
            'databases-list',
            'app.nav.children.databasesList',
            routed('databases'),
            ACCOUNT_HOLDERS,
            'databases',
            'databases',
          ),
          page(
            'databases-users',
            'app.nav.children.databasesUsers',
            routed('database-users'),
            ACCOUNT_HOLDERS,
            'databases',
            'databases',
          ),
          page(
            'databases-grant-repair',
            'app.nav.children.databasesGrantRepair',
            routed('database-grant-repair'),
            ADMIN,
            'databases',
            'databases',
          ),
          page('database-admin-tool', 'app.nav.children.databaseAdminTool', planned(null), CUSTOMER, null, 'databases'),
        ],
      },
      {
        kind: 'section',
        key: 'files',
        labelKey: 'app.nav.items.files',
        icon: 'folderKey',
        children: [
          page(
            'file-transfer-sftp',
            'app.nav.children.sftpLogins',
            routed('file-transfer-sftp'),
            ACCOUNT_HOLDERS,
            'sftp',
            'fileTransfer',
          ),
          page(
            'file-transfer-ftps',
            'app.nav.children.ftpsLogins',
            routed('file-transfer-ftps'),
            ACCOUNT_HOLDERS,
            'ftp',
            'fileTransfer',
          ),
          page('sftp-ssh-keys', 'app.nav.children.sshKeys', planned(135), CUSTOMER, null, 'fileTransfer'),
          findBy(page('cron', 'cron.list.heading', routed('cron'), ACCOUNT_HOLDERS, 'cron', 'cron'), 'cron'),
          page('file-manager', 'app.nav.children.fileManager', routed('file-manager'), ACCOUNT_HOLDERS, 'files', 'files'),
        ],
      },
      {
        kind: 'section',
        key: 'backups',
        labelKey: 'app.nav.items.backups',
        icon: 'archive',
        children: [
          page('backups-list', 'app.nav.children.backupsList', routed('backups'), ACCOUNT_HOLDERS, 'backups', 'backups'),
          page(
            'backups-schedule',
            'app.nav.children.backupsSchedule',
            routed('backup-schedule'),
            ADMIN,
            'backups',
            'backups',
          ),
          // The customer's own schedule (issue #147). The operator's item above edits a host-wide policy
          // and any account's override; this one edits the signed-in customer's own, so the two never
          // share a route.
          page(
            'backups-my-schedule',
            'app.nav.children.backupsSchedule',
            routed('my-backup-schedule'),
            CUSTOMER,
            'backups',
            'backups',
          ),
          page(
            'backups-destinations',
            'app.nav.children.backupsDestinations',
            routed('backup-destinations'),
            ADMIN,
            'backups',
            'backups',
          ),
          page('backups-restore', 'app.nav.children.backupsRestore', planned(null), CUSTOMER, null, 'backups'),
        ],
      },
    ],
  },
  {
    key: 'infrastructure',
    nodes: [
      {
        kind: 'section',
        key: 'security',
        labelKey: 'app.nav.items.security',
        icon: 'shieldCheck',
        children: [
          findBy(
            page('firewall-rules', 'firewall.rules.heading', routed('firewall'), ADMIN, 'firewall'),
            'firewallRules',
          ),
          findBy(
            page('firewall-bans', 'firewall.bans.heading', routed('firewall-bans'), ADMIN, 'firewall'),
            'firewallBans',
          ),
          findBy(
            page('firewall-whitelist', 'firewall.whitelist.heading', routed('firewall-whitelist'), ADMIN, 'firewall'),
            'firewallWhitelist',
          ),
          findBy(
            page('security-policy', 'app.nav.securityPolicy', routed('security-policy'), ADMIN, null),
            'securityPolicy',
          ),
          page('api-keys', 'app.nav.apiKeys', routed('api-keys'), ADMIN, 'identity'),
        ],
      },
      {
        kind: 'section',
        key: 'servers',
        labelKey: 'app.nav.items.servers',
        icon: 'server',
        children: [
          page('servers-fleet', 'app.nav.children.serversFleet', planned(180), ADMIN, null),
          page('servers-services', 'app.nav.children.serversServices', planned(null), ADMIN, null),
          {
            ...page('servers-updates', 'app.nav.children.serversUpdates', routed('servers-updates'), ADMIN, 'monitoring'),
            badge: 'availableUpdates',
          },
        ],
      },
      {
        kind: 'section',
        key: 'monitoring',
        labelKey: 'app.nav.items.monitoring',
        icon: 'chartLine',
        children: [
          page('monitoring-metrics', 'app.nav.children.monitoringMetrics', routed('monitoring'), ADMIN, 'monitoring'),
          page('monitoring-logs', 'app.nav.children.monitoringLogs', planned(null), ADMIN, null),
          { ...page('tasks', 'tasks.list.heading', routed('tasks'), ADMIN, 'tasks'), badge: 'failedTasks' },
          findBy(page('audit', 'app.nav.auditJournal', routed('audit'), ADMIN, null), 'audit'),
        ],
      },
    ],
  },
  {
    // Moving accounts between servers (issues #127 and #197): the remote servers a migration reads, and
    // the export and import of one account between two Maran servers. Administrators only, and refused
    // while viewing as a login. One section, because they are one task seen from two ends.
    key: 'migration',
    nodes: [
      {
        kind: 'section',
        key: 'migration',
        labelKey: 'app.nav.items.migration',
        icon: 'server',
        children: [
          { ...page('remote-servers', 'app.nav.children.remoteServers', routed('remote-servers'), ADMIN, 'servermigration'), viewAsRefused: true },
          { ...page('migration-jobs', 'app.nav.children.migrationJobs', routed('migration-jobs'), ADMIN, 'servermigration'), viewAsRefused: true },
          { ...page('account-export', 'app.nav.children.accountExport', routed('account-export'), ADMIN, 'servermigration'), viewAsRefused: true },
          { ...page('account-import', 'app.nav.children.accountImport', routed('account-import'), ADMIN, 'servermigration'), viewAsRefused: true },
          { ...page('account-exports', 'app.nav.children.accountExports', routed('account-exports'), ADMIN, 'servermigration'), viewAsRefused: true },
          { ...page('account-imports', 'app.nav.children.accountImports', routed('account-imports'), ADMIN, 'servermigration'), viewAsRefused: true },
          { ...page('export-grants', 'app.nav.children.exportGrants', routed('export-grants'), ADMIN, 'servermigration'), viewAsRefused: true },
        ],
      },
    ],
  },
  {
    key: 'system',
    nodes: [
      {
        kind: 'section',
        key: 'settings',
        labelKey: 'app.nav.items.settings',
        icon: 'settings',
        children: [
          page('settings-panel', 'app.nav.children.settingsPanel', planned(null), ADMIN, null),
          findBy(
            page('panel-certificate', 'panelCertificate.heading', routed('panel-certificate'), ADMIN, null),
            'panelCertificate',
          ),
          page('panel-update', 'panelUpdate.heading', routed('panel-update'), ADMIN, null),
          page('licence', 'licence.heading', routed('licence'), ADMIN, null),
          // The module marketplace (issues #186, #187). It names its module, unlike the two items above:
          // the module declares no menu or routes of its own (`Menu []`), so this item is its one door,
          // and naming the module is what keeps the catalogue fallback from drawing a second one.
          page('marketplace', 'marketplace.title', routed('marketplace'), ADMIN, 'marketplace'),
          // The apps store (issue #517): the site web server and the runtimes sites run on. It names its
          // module, like the marketplace, so the catalogue fallback does not draw a second door.
          findBy({ ...page('apps', 'apps.list.heading', routed('apps'), ADMIN, 'apps'), viewAsRefused: true }, 'apps'),
          findBy(page('smtp-settings', 'app.smtp.heading', routed('smtp-settings'), ADMIN, null), 'smtp'),
          page('alert-settings', 'alertSettings.heading', routed('alert-settings'), ADMIN, null),
          page('settings-administrators', 'app.nav.children.settingsAdministrators', planned(null), ADMIN, null),
          page('settings-modules', 'app.nav.children.settingsModules', planned(null), ADMIN, null),
          page('profile', 'app.profile.heading', routed('profile'), RESELLER, null),
          page('settings-branding', 'branding.heading', routed('reseller-branding'), RESELLER, null),
          { ...page('settings-api-keys', 'app.nav.apiKeys', routed('reseller-api-keys'), RESELLER, null), viewAsRefused: true },
          page('two-factor', 'app.twoFactor.heading', routed('two-factor'), RESELLER, null),
        ],
      },
    ],
  },
  {
    // The customer's own activity and account (issue #312). The tasks page is the same page the
    // operators reach under Monitoring; a customer has no Monitoring, so it is listed here for them.
    // A reseller has none either, and its audit journal is listed here for the same reason.
    key: 'account',
    nodes: [
      {
        kind: 'section',
        key: 'activity',
        labelKey: 'app.nav.items.activity',
        icon: 'listChecks',
        children: [
          { ...page('activity-tasks', 'tasks.list.heading', routed('tasks'), CUSTOMER, 'tasks'), badge: 'failedTasks' },
          page('site-logs', 'app.nav.children.siteLogs', planned(152), CUSTOMER, null),
          // A reseller's own part of the journal (issue #370): the entries about its accounts and its login.
          // It is listed here and not under Monitoring, whose pages are the host's and answer a reseller nothing.
          page('reseller-audit', 'app.nav.auditJournal', routed('reseller-audit'), RESELLER, null),
        ],
      },
      {
        kind: 'section',
        key: 'account',
        labelKey: 'app.nav.items.account',
        icon: 'user',
        children: [
          // Profile and language live on the account page and in the account menu, so the schema's one
          // "Profile & language" item is the account page itself (issue #312).
          findBy(
            page('account-overview', 'app.shell.menu.myAccount', routed('my-account'), CUSTOMER, null),
            'accountOverview',
          ),
          findBy(
            page('account-two-factor', 'app.twoFactor.heading', routed('two-factor'), CUSTOMER, null),
            'accountTwoFactor',
          ),
          // Refused while an operator views as the login (issue #513): the endpoints behind it are the login's own.
          { ...page('account-sessions', 'app.sessions.heading', routed('sessions'), CUSTOMER, null), viewAsRefused: true },
          // Only the owner manages the team: the endpoints refuse a member, so the menu does not offer it.
          {
            ...page('account-sub-users', 'app.nav.children.subUsers', routed('sub-users'), CUSTOMER, null),
            ownerOnly: true,
            viewAsRefused: true,
          },
        ],
      },
    ],
  },
]
