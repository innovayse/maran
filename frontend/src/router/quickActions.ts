/**
 * The catalogue of dashboard shortcuts: the one list every consumer reads (the dashboard's
 * quick-actions bar today, the jump-to palette when it offers actions), so a shortcut cannot be
 * named, iconed or aimed differently in two places.
 *
 * It is the SPA's half of a closed vocabulary. The panel's layout says WHICH shortcuts a person
 * sees and in what order, by key (and applies the defaults for their audience and for the modules
 * that are available); this file says what each key is: where it leads, its glyph, its label and the
 * module that must be licensed. A key the panel sends that is not here is skipped, so a newer panel
 * never breaks an older bundle.
 *
 * **Every shortcut is a link, none is an endpoint.** Creating something is `?action=create` on the
 * screen that owns it: that screen scrolls to its own form and focuses the first field, so there is
 * no second copy of any form, no second validation path and no cross-feature import. Whether the
 * caller may do the thing is the owning screen's server-side policy; the module filter here only
 * keeps a shortcut from ending at the upgrade page.
 *
 * Planned shortcuts (migrating a server, installing a module) are not listed: a page that is not
 * built yet has no route, and this catalogue never invents one.
 */

import type { QuickActionSpec } from '../types/quickAction'

/** The query parameter a screen reads to open its own create form. */
export const QUICK_ACTION_PARAMETER = 'action'

/** The value of {@link QUICK_ACTION_PARAMETER} that asks for the create form. */
export const QUICK_ACTION_CREATE = 'create'

/**
 * The create form on a screen, as a destination.
 * @param path The screen's route path.
 * @returns The screen with `?action=create`.
 */
const create = (path: string): QuickActionSpec['to'] => {
  return { path, query: { [QUICK_ACTION_PARAMETER]: QUICK_ACTION_CREATE } }
}

/**
 * The shortcuts, in the order the SPA falls back to when the panel's layout cannot be read.
 */
export const QUICK_ACTIONS: readonly QuickActionSpec[] = [
  { key: 'createAccount', to: '/accounts/new', icon: 'users', labelKey: 'app.status.quickActions.createAccount', module: 'accounts' },
  { key: 'addSite', to: '/sites/new', icon: 'earth', labelKey: 'app.status.quickActions.addSite', module: 'sites' },
  { key: 'createDatabase', to: create('/databases'), icon: 'database', labelKey: 'app.status.quickActions.createDatabase', module: 'databases' },
  { key: 'runBackup', to: create('/backups'), icon: 'archive', labelKey: 'quickActions.items.runBackup', module: 'backups' },
  { key: 'banAddress', to: create('/firewall/bans'), icon: 'brickWall', labelKey: 'quickActions.items.banAddress', module: 'firewall' },
  { key: 'addCronJob', to: create('/cron'), icon: 'clock', labelKey: 'quickActions.items.addCronJob', module: 'cron' },
]
