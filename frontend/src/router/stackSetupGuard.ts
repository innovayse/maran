import type { RouteLocationNormalized, RouteLocationRaw } from 'vue-router'
import { useCallerRole } from '../composables/useCallerRole'
import { useAuthStore } from '../stores/auth'
import { useStackStore } from '../stores/stack'

/**
 * The stack guard's shape: it reads the target alone and answers with its return value. Narrower
 * than `NavigationGuard` (which also passes `from` and `next`) and assignable to it.
 */
export type StackSetupGuard = (to: RouteLocationNormalized) => Promise<boolean | RouteLocationRaw>

/** Route name of the first-run wizard that chooses the site web server and the runtimes. */
const STACK_SETUP_ROUTE = 'stack-setup'

/** Route name of the "panel is being set up" screen a non-administrator is held on. */
const STACK_PENDING_ROUTE = 'stack-pending'

/** Route name of the screen a visitor lands on once the host is ready for sites. */
const HOME_ROUTE = 'system-status'

/**
 * Global navigation guard that holds a panel with no site web server on the screen that fixes it
 * (issue #517).
 *
 * A fresh host has no site web server, and without one no site can exist. While that is true an
 * administrator is sent to the setup wizard from every URL, and anyone else to a plain "the panel is
 * being set up" screen, because they cannot fix it and every other screen would only answer with the
 * backend's `apps.stackNotReady` refusal. Once a web server is installed both screens send the
 * visitor home, so the wizard cannot be re-entered by a bookmark.
 *
 * **Advice, never the boundary.** The backend refuses to create or change a site while no web server
 * is installed, whatever the browser does (rules/security.md). That is why a stack status that could
 * not be read lets the visit through: guessing "not ready" would lock a working panel's
 * administrator out on one failed request, and a wrong "ready" costs one refused request.
 *
 * Registered after the auth guard, which settles who the visitor is, and before the role guard: the
 * role guard would otherwise send a customer to "not available" for a screen that is only
 * unreachable because the host is not finished.
 * @returns A Vue Router navigation guard to register with `router.beforeEach`.
 */
export const createStackSetupGuard = (): StackSetupGuard => {
  return async (to): Promise<boolean | RouteLocationRaw> => {
    const authStore = useAuthStore()

    // Nobody signed in has nothing to be held on, and a login owing a password change or a second
    // factor holds a token that reaches those endpoints only; the auth guard owns both cases.
    if (!authStore.isAuthenticated || authStore.requiresPasswordChange || authStore.requiresTwoFactorSetup) {
      return true
    }

    const stackStore = useStackStore()
    await stackStore.load()
    const ready = stackStore.isReady
    if (ready === null) {
      return true
    }

    const onStackScreen = to.name === STACK_SETUP_ROUTE || to.name === STACK_PENDING_ROUTE
    if (ready) {
      return onStackScreen ? { name: HOME_ROUTE, replace: true } : true
    }

    const { isAdmin } = useCallerRole()
    const wanted = isAdmin.value ? STACK_SETUP_ROUTE : STACK_PENDING_ROUTE
    return to.name === wanted ? true : { name: wanted, replace: true }
  }
}
