import type { NavigationEntry } from '../types/navigation'
import type { NavigationGroup } from '../types/navigationGroup'

/**
 * How the shell's expanded navigation groups its entries under small labels.
 *
 * Grouping is a fact about how THIS SPA lays out its sidebar, the same kind of fact as
 * `router/moduleLandingRoute.ts` (where an entry leads) and `utils/moduleNavigationIcon.ts` (what
 * it is drawn with): the module catalogue reports no group and never will, so it is stated here,
 * once, keyed on the entry's own `key` — the module machine name, or the shell's own
 * `system-status` / `audit-journal` — never on a display name the backend localizes.
 */

/**
 * Group a module the map does not know falls into. A licensed module this bundle was built before
 * must still appear in the navigation, so an unknown key is never an error and never dropped: it
 * lands in this trailing group, whose label is the catalogue's original neutral "Panel".
 */
const FALLBACK_GROUP = 'panel'

/**
 * Groups in the order they are drawn. The fallback is last on purpose: a known module should
 * never be pushed below one the bundle has never heard of.
 */
const GROUP_ORDER: readonly string[] = ['overview', 'hosting', 'security', 'system', FALLBACK_GROUP]

/**
 * Entry key to the group it is drawn under.
 *
 * - **overview**: what the server is doing right now and what it has been doing — system status,
 *   monitoring, the background-task feed. Things read, not configured.
 * - **hosting**: the customer-facing things the panel exists to host — accounts and the plans they
 *   are created on, sites, databases, file transfer, scheduled jobs.
 * - **security**: what protects the host and its data — the firewall, and backups (a copy is the
 *   last defence against loss, and the approved design files it beside the firewall).
 * - **system**: the panel's own settings and records — the audit journal, the identity module's
 *   sessions and two-step screens, and the outgoing-mail (SMTP) settings.
 *
 * `ssl` and `ftp` have no entry of their own (`router/moduleLandingRoute.ts`), so they are absent
 * here by construction rather than by omission.
 */
const ENTRY_GROUPS: Readonly<Record<string, string>> = {
  'system-status': 'overview',
  monitoring: 'overview',
  tasks: 'overview',
  accounts: 'hosting',
  plans: 'hosting',
  sites: 'hosting',
  databases: 'hosting',
  sftp: 'hosting',
  cron: 'hosting',
  firewall: 'security',
  backups: 'security',
  'audit-journal': 'system',
  identity: 'system',
  notifications: 'system',
}

/**
 * Answers which group an entry is drawn under.
 * @param key The entry's stable key.
 * @returns The mapped group, or the fallback group for a key the map does not know.
 */
const groupOf = (key: string): string => {
  return Object.hasOwn(ENTRY_GROUPS, key) ? (ENTRY_GROUPS[key] ?? FALLBACK_GROUP) : FALLBACK_GROUP
}

/**
 * Splits the navigation entries into the labelled groups the expanded sidebar draws.
 *
 * Every entry appears exactly once, inside its group and in the catalogue's order. Groups with no
 * entries are omitted — a customer's catalogue has no firewall, and an empty "Security" label
 * over nothing would read as a bug.
 * @param entries The entries `useNavigation` built.
 * @returns The non-empty groups, in drawing order.
 */
export const groupNavigation = (entries: readonly NavigationEntry[]): NavigationGroup[] => {
  return GROUP_ORDER.flatMap((key): NavigationGroup[] => {
    const members = entries.filter((entry): boolean => {
      return groupOf(entry.key) === key
    })
    return members.length === 0 ? [] : [{ key, labelKey: `app.nav.groups.${key}`, entries: members }]
  })
}
