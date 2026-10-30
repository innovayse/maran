import type { NavigationGuard, RouteLocationRaw } from 'vue-router'
import { useCallerRole } from '../composables/useCallerRole'

/** Route name of the screen that tells a customer a page is not part of their area. */
const NOT_AVAILABLE_ROUTE = 'not-available'

/**
 * Global navigation guard that keeps a customer out of administrator screens (issue #202).
 *
 * **Advice, never the boundary.** Every endpoint behind an `adminOnly` route refuses a customer on
 * its own, whatever the browser does (rules/security.md). This guard exists so a customer who
 * follows an old bookmark or types a URL sees one plain, localized sentence instead of an
 * administrator's screen wrapped around a refusal.
 *
 * It errs permissive: only a caller positively known to be a customer is redirected. A role the
 * SPA does not recognise, or a visitor not yet restored, passes through and the server answers.
 *
 * Registered after the auth guard (a role means nothing before sign-in) and before the module
 * access guard, so a customer is never sent to the upgrade page for a screen that is not theirs to
 * buy.
 * @returns A Vue Router navigation guard to register with `router.beforeEach`.
 */
export const createRoleGuard = (): NavigationGuard => {
  return (to): boolean | RouteLocationRaw => {
    if (to.meta.adminOnly !== true) {
      return true
    }

    const { isCustomer } = useCallerRole()
    if (!isCustomer.value) {
      return true
    }

    // `replace` so the browser's Back button does not walk the customer into the same redirect.
    return { name: NOT_AVAILABLE_ROUTE, replace: true }
  }
}
