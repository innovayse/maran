import { useAuthStore } from '../stores/auth'
import type { NavigationGuardWithThis, RouteLocationNormalized } from 'vue-router'

/** Route name of the sign-in screen. */
const LOGIN_ROUTE = 'login'

/** Route name of the second-factor screen. */
const TWO_FACTOR_ROUTE = 'login-two-factor'

/** Route name of the first-run setup screen. */
const SETUP_ROUTE = 'setup'

/** Route name of the screen that asks for a password-reset link. */
const FORGOT_PASSWORD_ROUTE = 'forgot-password'

/** Route name of the screen that spends a reset token. */
const RESET_PASSWORD_ROUTE = 'reset-password'

/**
 * Route name of the enrolment screen a steered administrator is held on.
 *
 * Deliberately not `two-factor`: that one is the settings page inside the shell,
 * and the shell is exactly what a steered administrator must not be given, since
 * their token reaches nothing but the enrolment endpoints.
 */
const TWO_FACTOR_SETUP_ROUTE = 'two-factor-setup'

/**
 * The screens a signed-out visitor may reach.
 *
 * The two password-reset screens are here because the person using them has, by
 * definition, no way to sign in first.
 */
const PUBLIC_ROUTES: readonly string[] = [
  LOGIN_ROUTE,
  TWO_FACTOR_ROUTE,
  SETUP_ROUTE,
  FORGOT_PASSWORD_ROUTE,
  RESET_PASSWORD_ROUTE,
]

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
      // A panel that forces administrators to hold a second factor issues a token
      // that reaches ONLY the enrolment endpoints. Checked before anything else a
      // signed-in caller may do, and without exempting the public screens, so the
      // answer is the same from every URL — a bookmark, a deep link, or the sign-in
      // screen itself. Without this the shell would render over screens that can
      // only answer 403, and the person would read a permissions failure where the
      // truth is an unfinished enrolment.
      if (authStore.requiresTwoFactorSetup) {
        return to.name === TWO_FACTOR_SETUP_ROUTE ? true : { name: TWO_FACTOR_SETUP_ROUTE }
      }

      // A signed-in user on the sign-in screen belongs in the panel.
      return PUBLIC_ROUTES.includes(String(to.name)) ? { name: 'system-status' } : true
    }

    // Nobody is signed in, so there is nothing to steer: the enrolment screen is not
    // public, and a caller who reaches for it is sent to sign in like any other.

    if (PUBLIC_ROUTES.includes(String(to.name))) {
      return true
    }

    // The intended path travels in the query so the login screen can return the
    // user to what they were actually trying to open.
    return { name: LOGIN_ROUTE, query: { redirect: to.fullPath } }
  }
}
