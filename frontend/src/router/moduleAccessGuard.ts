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
 *
 * **When the catalogue could not be loaded, this guard steps aside.** It used
 * to do the opposite: a failed load left the catalogue empty, `canUse` was
 * false for every module, and so EVERY gated route — sites, databases,
 * firewall, cron — redirected to the upgrade page. One unreachable panel or
 * one 500 therefore told a fully licensed operator to buy back the product
 * they already own, and did it on every screen at once. That is the failure
 * this rule exists to prevent: the SPA's licence gate is cosmetic, the backend
 * checks the licence on every request independently (rules/architecture.md,
 * rules/vue.md "the frontend gate is cosmetic only"), so when the SPA does not
 * KNOW it must let the navigation through and let the server answer. The same
 * choice `auth.loadSetupState` already makes for the same reason: an
 * unreachable panel is not an un-set-up one.
 *
 * A catalogue that loaded and simply does not list the module is a different
 * question with a real answer — the panel said it has no such module — and
 * that one still lands on the upgrade page.
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

    if (!modulesStore.isLoaded) {
      // The load above failed, so the SPA holds no opinion about this module.
      // Err permissive: the backend is the licence boundary and will refuse the
      // requests this screen makes if it must.
      return true
    }

    const access = useModuleAccess()
    if (access.canUse(moduleName)) {
      return true
    }

    return { name: 'upgrade', params: { module: moduleName } }
  }
}
