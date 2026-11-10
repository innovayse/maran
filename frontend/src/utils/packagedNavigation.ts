import type { RouteLocationRaw } from 'vue-router'
import { PACKAGED_GROUP } from '../router/navigationTree'
import type { ModuleLifecycle, PanelModule } from '../types/module'
import type { NavigationContext, NavigationEntry, NavigationEntryState, NavigationIcon } from '../types/navigation'

/**
 * The navigation of installed (packaged) modules. Their menu is not in the navigation tree: the
 * module declares it and the backend localizes it per request, so the entries are built from the
 * catalogue entry itself, drawn under the group of installed modules, and labelled with the
 * backend's text. A packaged module therefore needs no entry in `router/moduleLandingRoute.ts`
 * and no change to this bundle.
 */

/** Route name of the upgrade prompt a degraded module's entries lead to. */
const UPGRADE_ROUTE = 'upgrade'

/** The glyph an unknown icon key falls back to. */
const NEUTRAL_ICON: NavigationIcon = 'grid'

/**
 * The closed icon vocabulary the backend's `ModuleIconKeys` names. Typed as a record over the
 * navigation icons so a glyph added to one side and not the other fails to compile.
 */
const ICON_KEYS: Readonly<Record<NavigationIcon, true>> = {
  house: true,
  grid: true,
  users: true,
  earth: true,
  globe: true,
  mail: true,
  database: true,
  folderKey: true,
  archive: true,
  shieldCheck: true,
  server: true,
  chartLine: true,
  settings: true,
  listChecks: true,
  user: true,
}

/**
 * The name of the shell route a packaged module's declared route is registered under.
 * @param moduleId The module's machine name.
 * @param routeName The route's name within the module.
 * @returns The shell route name.
 */
export const packagedRouteName = (moduleId: string, routeName: string): string => {
  return `module-${moduleId}-${routeName}`
}

/**
 * The name of the layout record all of one module's routes are registered under.
 * @param moduleId The module's machine name.
 * @returns The layout route name.
 */
export const packagedLayoutName = (moduleId: string): string => {
  return `module-${moduleId}`
}

/**
 * The key of the navigation entry a module's menu item becomes.
 * @param moduleId The module's machine name.
 * @param itemKey The menu item's key within the module.
 * @returns The entry key, unique across modules.
 */
export const packagedEntryKey = (moduleId: string, itemKey: string): string => {
  return `${moduleId}/${itemKey}`
}

/**
 * The glyph for a module-supplied icon key.
 * @param iconKey The key the module's menu item named.
 * @returns The glyph, or the neutral one when the key is not in the closed vocabulary.
 */
export const packagedIcon = (iconKey: string): NavigationIcon => {
  return Object.hasOwn(ICON_KEYS, iconKey) ? (iconKey as NavigationIcon) : NEUTRAL_ICON
}

/**
 * Where a module's lifecycle puts its entries: a running module opens its screens, any other
 * module that has entries leads them to the upgrade page, which says why.
 * @param lifecycle The module's lifecycle; absent is read as active.
 * @returns True when the module's screens may be opened.
 */
const isRunning = (lifecycle: ModuleLifecycle | undefined): boolean => {
  return lifecycle === undefined || lifecycle === 'active'
}

/**
 * The state of one entry of a packaged module.
 * @param module The module.
 * @param routeName The route name the entry opens, within the module.
 * @param context What the shell knows about the panel.
 * @returns The entry state, or `null` when the entry must not be drawn.
 */
const stateOf = (module: PanelModule, routeName: string, context: NavigationContext): NavigationEntryState | null => {
  const upgrade: RouteLocationRaw = { name: UPGRADE_ROUTE, params: { module: module.name } }
  if (module.state === 'locked' || !isRunning(module.lifecycle)) {
    return { kind: 'locked', target: upgrade }
  }
  const target = packagedRouteName(module.name, routeName)
  // Never a route name the router does not know: linking to one throws while rendering the shell.
  return context.hasRoute(target) ? { kind: 'open', target: { name: target } } : null
}

/**
 * Builds the navigation entries of one packaged module from its catalogue entry.
 *
 * A module that is installed but not yet composed is absent: its routes do not exist until the
 * panel restarts. A running module offers each declared menu item that opens a registered route. A
 * degraded or failed module offers its menu items (or, with none, one row under its own name) as
 * locked rows leading to the page that explains why, so the operator sees it rather than a module
 * that quietly vanished.
 * @param module A catalogue entry with `isPackaged`.
 * @param context What the shell knows about the panel.
 * @returns The entries in the module's own order.
 */
export const packagedEntries = (module: PanelModule, context: NavigationContext): NavigationEntry[] => {
  if (module.lifecycle === 'installed') {
    return []
  }
  const items = [...(module.menu ?? [])].sort((first, second): number => {
    return first.order - second.order
  })
  if (items.length === 0) {
    // A running module with no menu draws nothing. One that is not running still shows, locked,
    // under its own name, so a failed module is not invisible.
    if (isRunning(module.lifecycle) && module.state !== 'locked') {
      return []
    }
    return [
      {
        key: packagedEntryKey(module.name, module.name),
        groupKey: PACKAGED_GROUP,
        state: { kind: 'locked', target: { name: UPGRADE_ROUTE, params: { module: module.name } } },
        label: { kind: 'text', text: module.displayName ?? module.name },
        icon: NEUTRAL_ICON,
        badge: null,
        searchTermsKey: null,
        children: [],
      },
    ]
  }
  return items.flatMap((item): NavigationEntry[] => {
    const state = stateOf(module, item.routeName, context)
    if (state === null) {
      return []
    }
    return [
      {
        key: packagedEntryKey(module.name, item.key),
        groupKey: PACKAGED_GROUP,
        state,
        label: { kind: 'text', text: item.label },
        icon: packagedIcon(item.iconKey),
        badge: null,
        searchTermsKey: null,
        children: [],
      },
    ]
  })
}
