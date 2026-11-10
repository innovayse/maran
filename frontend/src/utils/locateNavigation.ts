import type { RouteLocationNormalizedLoaded } from 'vue-router'
import { moduleOfLandingRoute } from '../router/moduleLandingRoute'
import { FALLBACK_GROUP, NAVIGATION_TREE, PACKAGED_GROUP } from '../router/navigationTree'
import type { NavigationLocation, NavigationPageSpec, NavigationRole } from '../types/navigation'

/**
 * Places a screen in the navigation, from the tree the sidebar is drawn from
 * (`router/navigationTree.ts`). The breadcrumb and the page eyebrow both read the answer, so
 * neither keeps a second, hand-written copy that can disagree with the sidebar (issue #209).
 */

/**
 * Whether a page is built, offered to the role, and routed under a route name. The role matters
 * because one route can be listed in two places for two roles (tasks: Monitoring for an operator,
 * Activity for a customer, issue #312).
 * @param page The page from the tree.
 * @param routeName The route's name.
 * @param role The caller's navigation role.
 * @returns True when the page is that route.
 */
const isRoute = (page: NavigationPageSpec, routeName: string, role: NavigationRole): boolean => {
  return page.roles.includes(role) && page.availability.state === 'available' && page.availability.route === routeName
}

/**
 * Answers which item, and which page of it, a route is the destination of. A section's first built
 * page is the page the section opens on, so it is the item itself rather than a level below it.
 * A catalogue module the tree does not know is found by its landing route, in the fallback group.
 * @param routeName The route's name.
 * @param role The caller's navigation role.
 * @returns The location without its leaf flag, or `undefined` when the navigation does not list it.
 */
const destinationOf = (routeName: string, role: NavigationRole): Omit<NavigationLocation, 'leaf'> | undefined => {
  for (const group of NAVIGATION_TREE) {
    for (const node of group.nodes) {
      if (node.kind === 'link') {
        if (isRoute(node, routeName, role)) {
          return { groupKey: group.key, entryKey: node.key, child: null }
        }
        continue
      }
      const child = node.children.find((candidate): boolean => {
        return isRoute(candidate, routeName, role)
      })
      if (child !== undefined) {
        const landing = node.children.find((candidate): boolean => {
          return candidate.availability.state === 'available' && candidate.roles.includes(role)
        })
        return { groupKey: group.key, entryKey: node.key, child: child === landing ? null : child }
      }
    }
  }
  const module = moduleOfLandingRoute(routeName)
  return module === undefined ? undefined : { groupKey: FALLBACK_GROUP, entryKey: module, child: null }
}

/**
 * Places a route in the navigation: the destination itself, or — for a screen the navigation does
 * not list, such as a record or a form — the destination its `meta.navigationParent` names, one
 * level up.
 * @param route The route to place.
 * @param role The caller's navigation role, so the screen is placed where that caller's menu lists it.
 * @returns Its location, or `null` for a screen outside the navigation (a 404, the upgrade page).
 */
export const locateNavigation = (route: RouteLocationNormalizedLoaded, role: NavigationRole): NavigationLocation | null => {
  // A packaged module's screen is placed by the entry the module's menu gave it
  // (`meta.packagedEntry`), under the group of installed modules; the static tree cannot know it. A
  // screen the module's menu does not list sits in the same group as a leaf with no entry above it.
  if (route.meta.packaged !== undefined) {
    const entryKey = route.meta.packagedEntry
    return { groupKey: PACKAGED_GROUP, entryKey: entryKey ?? '', child: null, leaf: entryKey === undefined }
  }
  const routeName = typeof route.name === 'string' ? route.name : ''
  const direct = destinationOf(routeName, role)
  const parent = route.meta.navigationParent
  const destination = direct ?? (parent === undefined ? undefined : destinationOf(parent, role))
  return destination === undefined ? null : { ...destination, leaf: direct === undefined }
}
