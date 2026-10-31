/**
 * Which pages a module's sidebar entry opens into, for the modules that have more than one.
 *
 * The module catalogue reports no menu, so until a module's manifest carries one this SPA states,
 * once and beside `moduleLandingRoute.ts`, which of its own routes form each module's submenu. It is
 * keyed on the entry's stable key (the module machine name), never on a localized display name.
 *
 * A child whose route this bundle does not register is dropped by `useNavigation` rather than
 * linked: a submenu entry for a page that is not built would throw while rendering the sidebar.
 */

import type { NavigationChildSpec } from '../types/navigation'

/**
 * Entry key to its submenu, landing page first. A module absent from this map, or with a single
 * page, stays a plain link.
 */
const CHILDREN: Readonly<Record<string, readonly NavigationChildSpec[]>> = {
  accounts: [
    {
      key: 'accounts-list',
      route: 'accounts',
      labelKey: 'app.nav.children.accountsList',
      adminOnly: false,
    },
    {
      key: 'accounts-new',
      route: 'accounts-new',
      labelKey: 'app.nav.children.accountsNew',
      adminOnly: true,
    },
    // Plans are the Accounts module's reference data: they are what an account is created on.
    { key: 'plans', route: 'plans', labelKey: 'app.nav.plans', adminOnly: true },
  ],
  sites: [
    { key: 'sites-list', route: 'sites', labelKey: 'app.nav.children.sitesList', adminOnly: false },
    {
      key: 'sites-new',
      route: 'sites-new',
      labelKey: 'app.nav.children.sitesNew',
      adminOnly: false,
    },
    // The PHP runtimes are what a site runs on, so their page sits with the sites (issue #209: it
    // had no entry at all and was reachable only by its address).
    { key: 'php-versions', route: 'php-versions', labelKey: 'php.title', adminOnly: true },
  ],
  // The FTPS page is offered under the SFTP module's entry: the two protocols are one subject to the
  // reader ("File transfer"), and two modules on the server. Neither is administrators-only — a
  // customer manages their own logins of both kinds.
  sftp: [
    {
      key: 'file-transfer-sftp',
      route: 'file-transfer-sftp',
      labelKey: 'app.nav.children.sftpLogins',
      adminOnly: false,
    },
    {
      key: 'file-transfer-ftps',
      route: 'file-transfer-ftps',
      labelKey: 'app.nav.children.ftpsLogins',
      adminOnly: false,
    },
  ],
  databases: [
    {
      key: 'databases-list',
      route: 'databases',
      labelKey: 'app.nav.children.databasesList',
      adminOnly: false,
    },
    {
      key: 'databases-grant-repair',
      route: 'database-grant-repair',
      labelKey: 'app.nav.children.databasesGrantRepair',
      adminOnly: true,
    },
  ],
  // Every firewall page is administrators-only, like the module: who may reach the host is not a
  // tenant's business. Three pages because the bans and the whitelist are each server-paged and keep
  // their page, search and sort in the URL; the rules stay at the bare `/firewall`, the landing page.
  // Each child is labelled by the title of the list it opens (issue #209), so the sidebar, the
  // breadcrumb and the page heading name it with one string.
  firewall: [
    { key: 'firewall-rules', route: 'firewall', labelKey: 'firewall.rules.heading', adminOnly: true },
    { key: 'firewall-bans', route: 'firewall-bans', labelKey: 'firewall.bans.heading', adminOnly: true },
    {
      key: 'firewall-whitelist',
      route: 'firewall-whitelist',
      labelKey: 'firewall.whitelist.heading',
      adminOnly: true,
    },
  ],
  backups: [
    {
      key: 'backups-list',
      route: 'backups',
      labelKey: 'app.nav.children.backupsList',
      adminOnly: false,
    },
    {
      key: 'backups-schedule',
      route: 'backup-schedule',
      labelKey: 'app.nav.children.backupsSchedule',
      adminOnly: true,
    },
    {
      key: 'backups-destinations',
      route: 'backup-destinations',
      labelKey: 'app.nav.children.backupsDestinations',
      adminOnly: true,
    },
  ],
}

/**
 * Answers which pages a module's entry opens into.
 * @param key The entry's stable key (module machine name).
 * @returns The declared children, landing page first; empty for a single-page module.
 */
export const moduleNavigationChildren = (key: string): readonly NavigationChildSpec[] => {
  return Object.hasOwn(CHILDREN, key) ? (CHILDREN[key] ?? []) : []
}

/**
 * Answers which entry's submenu lists a route, the reverse of {@link moduleNavigationChildren}.
 * @param routeName The route's name.
 * @returns The key of the entry whose submenu holds the route, or `undefined` when none does.
 */
export const moduleOfNavigationChild = (routeName: string): string | undefined => {
  return Object.keys(CHILDREN).find((key): boolean => {
    return (CHILDREN[key] ?? []).some((child): boolean => {
      return child.route === routeName
    })
  })
}
