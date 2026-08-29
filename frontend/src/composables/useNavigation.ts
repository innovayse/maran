import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useModulesStore } from '../stores/modules'
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
 * Each module's label key follows the `app.nav.modules.<name>` convention.
 * No module exists in the catalogue yet (`GET /api/v1/modules` returns `[]`
 * today), so no such key is added speculatively — the first module to ship
 * adds its own real `en`/`ru`/`hy` translation for the key its name implies,
 * alongside its page.
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
      locked: false,
    }

    const moduleEntries: NavigationEntry[] = modulesStore.modules.map((module) => ({
      key: module.name,
      // An enabled module links to its own page once that page exists; until then — and always for
      // a locked one — the entry points at the upgrade page, which needs the module as a param.
      // Linking to a route name the router does not know would throw while rendering the sidebar.
      target:
        module.isEnabled && router.hasRoute(module.name)
          ? { name: module.name }
          : { name: UPGRADE_ROUTE, params: { module: module.name } },
      // The label comes from the panel, already localized: the SPA cannot own translations for
      // modules it learns about at runtime. The machine name is the honest fallback.
      labelKey: null,
      label: module.displayName ?? module.name,
      moduleName: module.name,
      locked: !module.isEnabled,
    }))

    return [systemStatusEntry, ...moduleEntries]
  })
}
