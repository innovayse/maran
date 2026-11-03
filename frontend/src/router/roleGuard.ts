import type { NavigationGuard, RouteLocationRaw } from 'vue-router'
import { useCallerRole } from '../composables/useCallerRole'

/** Route name of the screen that tells a caller a page is not part of their area. */
const NOT_AVAILABLE_ROUTE = 'not-available'

/** Route name of the server dashboard, the home of an administrator and a customer. */
const HOME_ROUTE = 'system-status'

/** Route name of the reseller's own overview, which is the reseller's home instead of the server dashboard. */
const RESELLER_HOME_ROUTE = 'reseller-overview'

/**
 * Global navigation guard that keeps a caller out of the screens their role does not own
 * (issues #202 and #179).
 *
 * **Advice, never the boundary.** Every endpoint behind a flagged route refuses the wrong role on
 * its own, whatever the browser does (rules/security.md). This guard exists so a caller who follows
 * an old bookmark or types a URL sees one plain, localized sentence instead of another role's screen
 * wrapped around a refusal.
 *
 * Both route flags are role-exact: `meta.adminOnly` lets only an administrator in, so a reseller
 * is turned away from the server and panel screens exactly as a customer is; `meta.managerOnly`
 * lets an administrator or a reseller in and turns a customer away. A reseller who lands on the
 * server dashboard is sent to their own overview, because that is their home.
 *
 * It errs permissive on one thing only: a visitor whose role is not known yet (not restored) passes
 * through and the server answers. A signed-in caller of ANY known role that the flag does not name
 * is refused, so a role added later never inherits a screen by default.
 *
 * Registered after the auth guard (a role means nothing before sign-in) and before the module
 * access guard, so a caller is never sent to the upgrade page for a screen that is not theirs to
 * buy.
 * @returns A Vue Router navigation guard to register with `router.beforeEach`.
 */
export const createRoleGuard = (): NavigationGuard => {
  return (to): boolean | RouteLocationRaw => {
    const { role, isAdmin, isManager, isReseller } = useCallerRole()

    if (to.name === HOME_ROUTE && isReseller.value) {
      return { name: RESELLER_HOME_ROUTE, replace: true }
    }

    // Role not known: nothing to refuse on, the endpoints answer.
    if (role.value === null) {
      return true
    }

    const allowed = (to.meta.adminOnly !== true || isAdmin.value) && (to.meta.managerOnly !== true || isManager.value)
    if (allowed) {
      return true
    }

    // `replace` so the browser's Back button does not walk the caller into the same redirect.
    return { name: NOT_AVAILABLE_ROUTE, replace: true }
  }
}
