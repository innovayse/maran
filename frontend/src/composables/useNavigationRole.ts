import { computed, type ComputedRef } from 'vue'
import { useAuthStore } from '../stores/auth'
import type { UserRole } from '../types/auth'
import type { NavigationRole } from '../types/navigation'

/**
 * The navigation role of a signed-in role. Written as a switch so that a role added to
 * {@link UserRole} (the reseller, issue #179) fails the type check here until it is mapped.
 * @param role The signed-in user's role, or undefined before sign-in has resolved.
 * @returns The role the tree is filtered for; an unknown caller is offered the administrator's menu,
 *   which is advice that errs permissive (rules/security.md) — the endpoints still refuse.
 */
const navigationRoleOf = (role: UserRole | undefined): NavigationRole => {
  switch (role) {
    case 'customer':
      return 'customer'
    case 'admin':
    case undefined:
      return 'admin'
  }
}

/**
 * The role the navigation tree is filtered for, for every reader of the tree: the menu itself
 * (`useNavigation`) and the places that locate a screen in it (the breadcrumb, the page eyebrow).
 * One answer for all of them, because the tree lists the same page in a different place for a
 * different role (the customer's tasks under Activity, an operator's under Monitoring, issue #312).
 * @returns The caller's navigation role, reactive to sign-in and sign-out.
 */
export const useNavigationRole = (): ComputedRef<NavigationRole> => {
  const authStore = useAuthStore()

  return computed((): NavigationRole => {
    return navigationRoleOf(authStore.user?.role)
  })
}
