import type { NavigationGuard, RouteLocationRaw } from 'vue-router'
import { useModulesStore } from '../stores/modules'
import { useModuleAccess } from '../composables/useModuleAccess'

/**
 * Route meta a route can carry to declare which module it belongs to.
 * Registered once here (rather than per-route) so every route file shares
 * the same typed shape.
 */
declare module 'vue-router' {
  /**
   * Augments vue-router's route meta with the optional module this route belongs to.
   */
  interface RouteMeta {
    /** Machine name of the module this route belongs to, if any. Ungated routes (system status, upgrade, 404) omit it. */
    module?: string
  }
}

/**
 * Global navigation guard enforcing licence gating cosmetically at the
 * router level (rules/vue.md: "A disabled module's routes resolve to the
 * upgrade page, never a blank screen or a 403 dump" — the same rule applies
 * to a route reached directly, not just to a hidden nav entry).
 *
 * The module catalogue is fetched once, lazily, on the first navigation
 * that needs it. Vue Router awaits an async guard before it swaps the
 * routed component in, so the current view (or, on first load, nothing yet)
 * stays on screen for the brief wait rather than the destination rendering
 * blank-then-populated — there is no separate loading UI to build for this.
 * A failed load leaves the catalogue empty and `isLoaded` false; every
 * gated route then still resolves (`canUse` is false for an unknown module,
 * so the guard sends the user to the upgrade page rather than throwing or
 * hanging navigation).
 *
 * @returns A Vue Router navigation guard to register with `router.beforeEach`.
 */
export const createModuleAccessGuard = (): NavigationGuard => {
  return async (to): Promise<boolean | RouteLocationRaw> => {
    const moduleName = to.meta.module
    if (moduleName === undefined) {
      return true
    }

    const modulesStore = useModulesStore()
    if (!modulesStore.isLoaded) {
      // First gated navigation of the session: block until the catalogue is
      // known so the guard below judges real data, not an empty default.
      await modulesStore.load()
    }

    const access = useModuleAccess()
    if (access.canUse(moduleName)) {
      return true
    }

    return { name: 'upgrade', params: { module: moduleName } }
  }
}
