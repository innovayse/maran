import type { NavigationGuard, RouteLocationRaw } from 'vue-router'
import { useModulesStore } from '../stores/modules'
import { useModuleAccess } from '../composables/useModuleAccess'
import { useCallerRole } from '../composables/useCallerRole'

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
 * **When the catalogue could not be loaded, this guard steps aside.** A failed
 * load leaves the catalogue empty and `canUse` false for every module, so if
 * the guard acted on it EVERY gated route — sites, databases, firewall, cron —
 * would redirect to the upgrade page. One unreachable panel or one 500 would
 * then tell a fully licensed operator to buy back the product they already
 * own, on every screen at once. That is the failure this rule exists to
 * prevent: the SPA's licence gate is cosmetic, the backend
 * checks the licence on every request independently (rules/architecture.md,
 * rules/vue.md "the frontend gate is cosmetic only"), so when the SPA does not
 * KNOW it must let the navigation through and let the server answer. The same
 * choice `setup.loadSetupState` already makes for the same reason: an
 * unreachable panel is not an un-set-up one.
 *
 * A catalogue that loaded and simply does not list the module is a different
 * question with a real answer — the panel said it has no such module. For an
 * administrator that still lands on the upgrade page. For a customer it does
 * not (issue #202): the backend filters the catalogue by role, so a module
 * missing from a customer's catalogue is one that is not part of their area,
 * and telling them to buy it would be wrong. They land on the "not available"
 * screen instead. Only a module the catalogue lists as locked is an upgrade
 * question for a customer.
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

    // A customer's catalogue leaves out what is not theirs, so "absent" means "not for you",
    // not "not bought". Locked is the one state that is genuinely about the licence.
    const { isCustomer } = useCallerRole()
    if (isCustomer.value && !modulesStore.isLocked(moduleName)) {
      return { name: 'not-available', replace: true }
    }

    return { name: 'upgrade', params: { module: moduleName } }
  }
}
