import { computed, type ComputedRef } from 'vue'
import { useAuthStore } from '../stores/auth'
import type { UserRole } from '../types/auth'
import type { NavigationRole } from '../types/navigation'

/**
 * The navigation role of a signed-in role: the one place the navigation maps {@link UserRole} to a
 * tree. Exhaustive on purpose — the `never` in the default branch makes a role added to `UserRole`
 * a type error here until it is mapped to its own tree, instead of silently receiving the
 * administrator's (Servers, Security, SMTP); the reseller (issue #179) has its own. `vue-tsc` is
 * that check: the SPA has no unit runner by design (rules/testing.md).
 * @param role The signed-in user's role, or undefined before sign-in has resolved.
 * @returns The role the tree is filtered for. Before sign-in resolves the administrator's menu is
 *   offered, which is advice that errs permissive (rules/security.md) — the endpoints still refuse.
 */
const navigationRoleOf = (role: UserRole | undefined): NavigationRole => {
  switch (role) {
    case 'customer':
      return 'customer'
    case 'reseller':
      return 'reseller'
    case 'admin':
    case undefined:
      return 'admin'
    default: {
      const unmapped: never = role
      return unmapped
    }
  }
}

/**
 * The role the navigation tree is filtered for, for every reader of the tree: the menu itself
 * (`useNavigationState`), the tab bar, and the places that locate a screen in it (the breadcrumb, the
 * page eyebrow). One answer for all of them, because the tree lists the same page in a different
 * place for a different role (the customer's tasks under Activity, an operator's under Monitoring,
 * issue #312).
 * @returns The caller's navigation role, reactive to sign-in and sign-out.
 */
export const useNavigationRole = (): ComputedRef<NavigationRole> => {
  const authStore = useAuthStore()

  return computed((): NavigationRole => {
    return navigationRoleOf(authStore.user?.role)
  })
}
