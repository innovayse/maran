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
 * @returns The page, with no plan feature and no badge.
 */
const page = (
  key: string,
  labelKey: string,
  availability: NavigationAvailability,
  roles: readonly NavigationRole[],
  module: string | null,
): NavigationPageSpec => {
  return { key, labelKey, availability, roles, module, planFeature: null, badge: null }
}

/** Administrators only: the host and the panel itself. */
const ADMIN: readonly NavigationRole[] = ['admin']

/** Administrators and resellers: managing other people's accounts. */
const MANAGERS: readonly NavigationRole[] = ['admin', 'reseller']

/** Every role: a page each caller sees within their own scope. */
const EVERYONE: readonly NavigationRole[] = ['admin', 'reseller', 'customer']

/** The customer alone: the account owner's own view of what an operator sees from above. */
const CUSTOMER: readonly NavigationRole[] = ['customer']

/** The signed-in reseller's own settings, which an administrator keeps under the account menu. */
const RESELLER: readonly NavigationRole[] = ['reseller']

/**
 * Catalogue modules that deliberately have no item of their own: `ssl` is a tab on the site it
 * protects, `identity` is the signed-in person's own screens (the account menu), and
 * `notifications` is the outgoing-mail page, which carries no module so that a licence cannot
 * switch off password-reset mail. Any OTHER module the catalogue reports and the tree does not
 * name — a module newer than this bundle — still gets an entry, in the fallback group.
 */
export const MODULES_WITHOUT_ITEM: ReadonlySet<string> = new Set(['ssl', 'identity', 'notifications'])

/** Group a catalogue module the tree does not know is drawn under. */
export const FALLBACK_GROUP = 'panel'

/** The whole navigation, in drawing order. */
export const NAVIGATION_TREE: readonly NavigationGroupSpec[] = [
  {
    key: 'overview',
    nodes: [
      {
        kind: 'link',
        icon: 'house',
        ...page('dashboard', 'app.status.heading', routed('system-status'), EVERYONE, null),
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
          page('resellers', 'app.nav.children.resellers', planned(179), ADMIN, null),
          page('reseller-plans', 'app.nav.children.resellerPlans', planned(179), ADMIN, null),
        ],
      },
      {
        kind: 'section',
        key: 'sites',
        labelKey: 'app.nav.items.sites',
        icon: 'earth',
        children: [
          page('sites-list', 'app.nav.children.sitesList', routed('sites'), EVERYONE, 'sites'),
          page('site-domains', 'app.nav.children.siteDomains', planned(null), EVERYONE, null),
          page('site-certificates', 'app.nav.children.siteCertificates', planned(null), EVERYONE, null),
          page('site-php', 'app.nav.children.sitePhp', planned(null), CUSTOMER, null),
          page('php-versions', 'php.title', routed('php-versions'), ADMIN, null),
        ],
      },
      {
        kind: 'section',
        key: 'dns',
        labelKey: 'app.nav.items.dns',
        icon: 'globe',
        children: [
          page('dns-zones', 'app.nav.children.dnsZones', planned(178), MANAGERS, null),
          page('dns-my-zones', 'app.nav.children.dnsMyZones', planned(178), CUSTOMER, null),
          page('dns-templates', 'app.nav.children.dnsTemplates', planned(178), MANAGERS, null),
          page('dns-nameservers', 'app.nav.children.dnsNameservers', planned(178), MANAGERS, null),
        ],
      },
      {
        kind: 'section',
        key: 'mail',
        labelKey: 'app.nav.items.mail',
        icon: 'mail',
        children: [
          page('mailboxes', 'app.nav.children.mailboxes', planned(177), EVERYONE, null),
          page('mail-forwarders', 'app.nav.children.mailForwarders', planned(177), EVERYONE, null),
          page('mail-autoresponders', 'app.nav.children.mailAutoresponders', planned(177), CUSTOMER, null),
          page('mail-spam-filter', 'app.nav.children.mailSpamFilter', planned(177), CUSTOMER, null),
          page('webmail', 'app.nav.children.webmail', planned(177), CUSTOMER, null),
          page('mail-queue', 'app.nav.children.mailQueue', planned(177), MANAGERS, null),
          page('mail-authentication', 'app.nav.children.mailAuthentication', planned(177), MANAGERS, null),
          page('mail-limits', 'app.nav.children.mailLimits', planned(177), MANAGERS, null),
        ],
      },
      {
        kind: 'section',
        key: 'databases',
        labelKey: 'app.nav.items.databases',
        icon: 'database',
        children: [
          page('databases-list', 'app.nav.children.databasesList', routed('databases'), EVERYONE, 'databases'),
          page(
            'databases-grant-repair',
            'app.nav.children.databasesGrantRepair',
            routed('database-grant-repair'),
            ADMIN,
            'databases',
          ),
          page('database-admin-tool', 'app.nav.children.databaseAdminTool', planned(null), CUSTOMER, null),
        ],
      },
      {
        kind: 'section',
        key: 'files',
        labelKey: 'app.nav.items.files',
        icon: 'folderKey',
        children: [
          page('file-transfer-sftp', 'app.nav.children.sftpLogins', routed('file-transfer-sftp'), EVERYONE, 'sftp'),
          page('file-transfer-ftps', 'app.nav.children.ftpsLogins', routed('file-transfer-ftps'), EVERYONE, 'ftp'),
          page('sftp-ssh-keys', 'app.nav.children.sshKeys', planned(135), CUSTOMER, null),
          page('cron', 'cron.list.heading', routed('cron'), EVERYONE, 'cron'),
          page('file-manager', 'app.nav.children.fileManager', planned(141), CUSTOMER, null),
        ],
      },
      {
        kind: 'section',
        key: 'backups',
        labelKey: 'app.nav.items.backups',
        icon: 'archive',
        children: [
          page('backups-list', 'app.nav.children.backupsList', routed('backups'), EVERYONE, 'backups'),
          page('backups-schedule', 'app.nav.children.backupsSchedule', routed('backup-schedule'), ADMIN, 'backups'),
          page(
            'backups-destinations',
            'app.nav.children.backupsDestinations',
            routed('backup-destinations'),
            ADMIN,
            'backups',
          ),
          page('backups-restore', 'app.nav.children.backupsRestore', planned(null), CUSTOMER, null),
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
          page('firewall-rules', 'firewall.rules.heading', routed('firewall'), ADMIN, 'firewall'),
          page('firewall-bans', 'firewall.bans.heading', routed('firewall-bans'), ADMIN, 'firewall'),
          page('firewall-whitelist', 'firewall.whitelist.heading', routed('firewall-whitelist'), ADMIN, 'firewall'),
          page('security-policy', 'app.securityPolicy.heading', routed('security-policy'), ADMIN, null),
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
          page('servers-updates', 'app.nav.children.serversUpdates', planned(null), ADMIN, null),
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
          { ...page('tasks', 'tasks.list.heading', routed('tasks'), MANAGERS, 'tasks'), badge: 'failedTasks' },
          page('audit', 'app.nav.auditJournal', routed('audit'), ADMIN, null),
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
          page('panel-certificate', 'panelCertificate.heading', routed('panel-certificate'), ADMIN, null),
          page('smtp-settings', 'app.smtp.heading', routed('smtp-settings'), ADMIN, null),
          page('settings-administrators', 'app.nav.children.settingsAdministrators', planned(null), ADMIN, null),
          page('settings-modules', 'app.nav.children.settingsModules', planned(null), ADMIN, null),
          page('profile', 'app.profile.heading', routed('profile'), RESELLER, null),
          page('settings-branding', 'app.nav.children.settingsBranding', planned(179), RESELLER, null),
          page('two-factor', 'app.twoFactor.heading', routed('two-factor'), RESELLER, null),
        ],
      },
    ],
  },
  {
    // The customer's own activity and account (issue #312). The tasks page is the same page the
    // operators reach under Monitoring; a customer has no Monitoring, so it is listed here for them.
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
          page('account-overview', 'app.shell.menu.myAccount', routed('my-account'), CUSTOMER, null),
          page('account-two-factor', 'app.twoFactor.heading', routed('two-factor'), CUSTOMER, null),
          page('account-sessions', 'app.sessions.heading', routed('sessions'), CUSTOMER, null),
          page('account-sub-users', 'app.nav.children.subUsers', planned(315), CUSTOMER, null),
        ],
      },
    ],
  },
]
