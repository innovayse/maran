import type { RouteLocationNormalizedLoaded } from 'vue-router'
import { moduleOfLandingRoute } from '../router/moduleLandingRoute'
import { moduleOfNavigationChild } from '../router/moduleNavigationChildren'
import { shellEntryOfRoute } from '../router/shellEntryRoutes'
import { navigationGroupOf } from './groupNavigation'
import type { NavigationLocation } from '../types/navigation'

/**
 * Places a screen in the navigation, from the same sources the sidebar is drawn from: where each
 * entry leads (`router/shellEntryRoutes.ts`, `router/moduleLandingRoute.ts`), which pages each
 * submenu lists (`router/moduleNavigationChildren.ts`) and which group each entry is drawn under
 * (`utils/groupNavigation.ts`). The breadcrumb and the page eyebrow both read the answer, so
 * neither keeps a second, hand-written copy that can disagree with the sidebar (issue #209).
 */

/**
 * Answers which entry, and which child of it, a route is the destination of. An entry's landing
 * page is checked before the submenus, so the list page a section opens on is the entry itself,
 * not a level below it.
 * @param routeName The route's name.
 * @returns The entry and child, or `undefined` when the navigation does not list the route.
 */
const destinationOf = (routeName: string): Omit<NavigationLocation, 'groupKey' | 'leaf'> | undefined => {
  const entryKey = shellEntryOfRoute(routeName) ?? moduleOfLandingRoute(routeName)
  if (entryKey !== undefined) {
    return { entryKey, childRoute: null }
  }
  const parentKey = moduleOfNavigationChild(routeName)
  return parentKey === undefined ? undefined : { entryKey: parentKey, childRoute: routeName }
}

/**
 * Places a route in the navigation: the destination itself, or — for a screen the navigation does
 * not list, such as a record or a form — the destination its `meta.navigationParent` names, one
 * level up.
 * @param route The route to place.
 * @returns Its location, or `null` for a screen outside the navigation (a profile, a 404).
 */
export const locateNavigation = (route: RouteLocationNormalizedLoaded): NavigationLocation | null => {
  const routeName = typeof route.name === 'string' ? route.name : ''
  const direct = destinationOf(routeName)
  const parent = route.meta.navigationParent
  const destination = direct ?? (parent === undefined ? undefined : destinationOf(parent))
  if (destination === undefined) {
    return null
  }
  return {
    ...destination,
    groupKey: navigationGroupOf(destination.entryKey),
    leaf: direct === undefined,
  }
}
