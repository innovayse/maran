import { computed, type ComputedRef } from 'vue'
import { useAuthStore } from '../stores/auth'

/** The signed-in caller's role as two flags, for presentation decisions only. */
export interface CallerRole {
  /** True when the caller is a customer: someone who owns one hosting account. */
  isCustomer: ComputedRef<boolean>
  /** True when the caller is signed in and is anything other than a customer. */
  isAdmin: ComputedRef<boolean>
}

/**
 * Reads the caller's role for the SPA's presentation choices: which links to draw, whether a form
 * offers an account picker. It is advice only, the backend enforces every rule on its own.
 *
 * `isAdmin` is written as "signed in and not a customer" rather than `role === 'admin'`, so a role
 * added later errs towards showing a link the server then refuses, never towards hiding a screen
 * an operator needs (rules/security.md: SPA checks must err permissive).
 * @returns The two role flags, reactive to sign-in and sign-out.
 */
export const useCallerRole = (): CallerRole => {
  const authStore = useAuthStore()

  const isCustomer = computed((): boolean => {
    return authStore.user !== null && authStore.user.role === 'customer'
  })

  const isAdmin = computed((): boolean => {
    return authStore.user !== null && authStore.user.role !== 'customer'
  })

  return { isCustomer, isAdmin }
}
