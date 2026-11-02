import { computed, type ComputedRef } from 'vue'
import { useCallerRole } from './useCallerRole'
import type { NavigationRole } from '../types/navigation'

/**
 * The role the navigation tree is filtered for, for every reader of the tree: the menu itself
 * (`useNavigation`), the tab bar, and the places that locate a screen in it (the breadcrumb, the
 * page eyebrow). One answer for all of them, because the tree lists the same page in a different
 * place for a different role (the customer's tasks under Activity, an operator's under Monitoring,
 * issue #312).
 *
 * Derived from `useCallerRole`, the one place the SPA branches on the signed-in role, so the two
 * cannot disagree: a customer is the customer's menu, and anyone else — including a role this
 * bundle does not know yet, and the moment before sign-in resolves — is offered the administrator's
 * menu, which is advice that errs permissive (rules/security.md); the endpoints still refuse.
 * @returns The caller's navigation role, reactive to sign-in and sign-out.
 */
export const useNavigationRole = (): ComputedRef<NavigationRole> => {
  const { isCustomer } = useCallerRole()

  return computed((): NavigationRole => {
    return isCustomer.value ? 'customer' : 'admin'
  })
}
