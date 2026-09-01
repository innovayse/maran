import { computed, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useModulesStore } from '../stores/modules'
import { NO_LANDING_ROUTE, moduleLandingRoute } from '../router/moduleLandingRoute'
import type { NavigationEntry } from '../types/navigation'

/**
 * Route name of the always-present system status screen. It ships with the
 * shell itself rather than as a licensed module, so it is not read from the
 * catalogue.
 */
const SYSTEM_STATUS_ROUTE = 'system-status'

/**
 * Route name of the upgrade prompt a locked module's entry points to.
 */
const UPGRADE_ROUTE = 'upgrade'

/**
 * Builds the sidebar navigation from the module catalogue: one entry per
 * module the panel reported, in catalogue order, plus the always-present
 * system status entry first. A locked module's entry stays visible and
 * routes to the upgrade page instead of disappearing (rules/architecture.md:
 * "renders the module's entries as locked rather than hiding the product's
 * existence").
 *
 * A module's label is the panel's own, already localized, so no translation key
 * is added here for one. Where a module's entry LEADS is this SPA's own fact and
 * is stated in `router/moduleLandingRoute.ts`, never guessed from its name.
 *
 * @returns A computed list of {@link NavigationEntry} to render in order.
 */
export const useNavigation = (): ComputedRef<NavigationEntry[]> => {
  const modulesStore = useModulesStore()
  const router = useRouter()

  return computed<NavigationEntry[]>(() => {
    const systemStatusEntry: NavigationEntry = {
      key: 'system-status',
      target: { name: SYSTEM_STATUS_ROUTE },
      labelKey: 'app.nav.systemStatus',
      label: null,
      moduleName: null,
      icon: 'pulse',
      locked: false,
    }

    const moduleEntries: NavigationEntry[] = modulesStore.modules.flatMap((module) => {
      const landing = moduleLandingRoute(module.name)

      // A module whose interface lives inside another module's screens gets no entry of its own.
      // SSL is the case: its tab is on the site it protects, so there is nowhere for a sidebar
      // entry to lead, and every candidate is worse than none.
      if (module.isEnabled && landing === NO_LANDING_ROUTE) {
        return []
      }

      // The upgrade page answers exactly two questions and no others: a module the licence does
      // not permit, and a module this SPA has no screen for yet. A module that IS licensed and
      // DOES have a screen must never land there — which is what "Users and access" and "SSL
      // certificates" both did, because the entry guessed that a module named `x` has a route
      // named `x` and sent everything else to the upgrade wall.
      const reachable =
        module.isEnabled && typeof landing === 'string' && router.hasRoute(landing)
      const upgrade = { name: UPGRADE_ROUTE, params: { module: module.name } }

      return [
        {
          key: module.name,
          // Never a route name the router does not know: linking to one throws while rendering
          // the sidebar, which takes the whole shell down with it.
          target: reachable && typeof landing === 'string' ? { name: landing } : upgrade,
          // The label comes from the panel, already localized: the SPA cannot own translations for
          // modules it learns about at runtime. The machine name is the honest fallback.
          labelKey: null,
          label: module.displayName ?? module.name,
          moduleName: module.name,
          // The catalogue reports no icon, so every module gets the same neutral glyph rather than
          // a guessed one: inventing a per-module icon would be inventing data the backend never sent.
          icon: 'grid',
          locked: !module.isEnabled,
        },
      ]
    })

    return [systemStatusEntry, ...moduleEntries]
  })
}
