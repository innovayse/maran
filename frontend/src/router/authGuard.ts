import { useAuthStore } from '../stores/auth'
import type { NavigationGuardWithThis, RouteLocationNormalized } from 'vue-router'

/** Route name of the sign-in screen. */
const LOGIN_ROUTE = 'login'

/** Route name of the second-factor screen. */
const TWO_FACTOR_ROUTE = 'login-two-factor'

/** Route name of the first-run setup screen. */
const SETUP_ROUTE = 'setup'

/** The screens a signed-out visitor may reach. */
const PUBLIC_ROUTES: readonly string[] = [LOGIN_ROUTE, TWO_FACTOR_ROUTE, SETUP_ROUTE]

/**
 * Creates the guard that decides whether a visitor may see a route at all.
 *
 * It runs before the module-access guard, and the order matters: asking whether a
 * module is licensed for somebody who has not signed in is a question with no
 * meaning, and answering it would show an anonymous visitor which products the
 * server has.
 * @returns A navigation guard to install with `router.beforeEach`.
 */
export const createAuthGuard = (): NavigationGuardWithThis<undefined> => {
  return async (to: RouteLocationNormalized) => {
    const authStore = useAuthStore()

    // A panel with no administrator has exactly one meaningful screen, and every
    // other route is a dead end until somebody claims it.
    await authStore.loadSetupState()
    if (authStore.isSetupComplete === false) {
      return to.name === SETUP_ROUTE ? true : { name: SETUP_ROUTE }
    }

    if (to.name === SETUP_ROUTE) {
      return { name: LOGIN_ROUTE }
    }

    // On a page load the store holds no token yet — the refresh cookie is the only
    // thing that survives — so a visitor with a live session must not be bounced to
    // the login screen before that has been tried once.
    await authStore.restore()

    if (authStore.isAuthenticated) {
      // A signed-in user on the sign-in screen belongs in the panel.
      return PUBLIC_ROUTES.includes(String(to.name)) ? { name: 'system-status' } : true
    }

    if (PUBLIC_ROUTES.includes(String(to.name))) {
      return true
    }

    // The intended path travels in the query so the login screen can return the
    // user to what they were actually trying to open.
    return { name: LOGIN_ROUTE, query: { redirect: to.fullPath } }
  }
}
