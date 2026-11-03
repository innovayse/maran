import { computed, type ComputedRef } from 'vue'
import { useAuthStore } from '../stores/auth'
import type { UserRole } from '../types/auth'

/** The signed-in caller's role, as the exact role and one flag per question the SPA asks of it. */
export interface CallerRole {
  /** The signed-in caller's role, or `null` before sign-in has resolved. */
  role: ComputedRef<UserRole | null>
  /** True when the caller is a customer: someone who owns one hosting account. */
  isCustomer: ComputedRef<boolean>
  /** True when the caller is an administrator, and only then. */
  isAdmin: ComputedRef<boolean>
  /** True when the caller is a reseller, and only then. */
  isReseller: ComputedRef<boolean>
  /** True when the caller is an administrator or a reseller: someone who manages other people's accounts. */
  isManager: ComputedRef<boolean>
}

/**
 * Reads the caller's role for the SPA's presentation choices: which links to draw, whether a form
 * offers an account picker. It is advice only, the backend enforces every rule on its own.
 *
 * **Every flag is role-exact.** `isAdmin` is `role === 'admin'`, so a reseller is never taken for an
 * administrator and is not offered the server, security or settings screens; `isManager` names
 * the two roles that manage accounts, instead of "anyone who is not a customer". A role added to
 * {@link UserRole} later is therefore offered nothing until it is named here, which costs a hidden
 * link at worst: the endpoint behind every screen refuses on its own regardless.
 * @returns The role and its flags, reactive to sign-in and sign-out.
 */
export const useCallerRole = (): CallerRole => {
  const authStore = useAuthStore()

  const role = computed((): UserRole | null => {
    return authStore.user === null ? null : authStore.user.role
  })

  const isCustomer = computed((): boolean => {
    return role.value === 'customer'
  })

  const isAdmin = computed((): boolean => {
    return role.value === 'admin'
  })

  const isReseller = computed((): boolean => {
    return role.value === 'reseller'
  })

  const isManager = computed((): boolean => {
    return role.value === 'admin' || role.value === 'reseller'
  })

  return { role, isCustomer, isAdmin, isReseller, isManager }
}
