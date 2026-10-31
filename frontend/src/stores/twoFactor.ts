import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { ApiError } from '../composables/useApi'
import type { RecoveryCodes, TotpEnrolment } from '../types/auth'

/**
 * Owns the signed-in user's second factor: starting and confirming an enrolment, and turning it
 * off. Completing a sign-in that stopped for a code is the session's business (`auth.ts`), not
 * this store's.
 */
export const useTwoFactorStore = defineStore('twoFactor', () => {
  const api = useAuthApi()

  /** Backend-localized message from the last refused enrolment step, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads a failure's backend-localized text, or clears the message for anything
   * that is not an API error.
   * @param error The caught error.
   * @returns Nothing; `errorMessage` is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Forgets the last failure's message. A message belongs to the screen that produced it, so the
   * root component calls this on every navigation between screens (`App.vue`).
   * @returns Nothing; `errorMessage` is cleared synchronously.
   */
  const dismissError = (): void => {
    errorMessage.value = null
  }

  /**
   * Starts a two-factor enrolment. Nothing is enabled until it is confirmed.
   * @returns The secret and its provisioning URI, or `null` when the request failed.
   */
  const beginTwoFactorEnrolment = async (): Promise<TotpEnrolment | null> => {
    errorMessage.value = null
    try {
      return await api.beginTwoFactorEnrolment()
    } catch (error) {
      remember(error)
      return null
    }
  }

  /**
   * Completes an enrolment.
   * @param secret The secret handed out by the enrolment step.
   * @param code A code the user's app produced from it.
   * @returns The recovery codes, shown once, or `null` when the code was refused.
   */
  const confirmTwoFactorEnrolment = async (secret: string, code: string): Promise<RecoveryCodes | null> => {
    errorMessage.value = null
    try {
      return await api.confirmTwoFactorEnrolment(secret, code)
    } catch (error) {
      remember(error)
      return null
    }
  }

  /**
   * Turns the second factor off.
   * @param code A current code or a recovery code.
   * @returns True when it was turned off.
   */
  const disableTwoFactor = async (code: string): Promise<boolean> => {
    errorMessage.value = null
    try {
      await api.disableTwoFactor(code)
      return true
    } catch (error) {
      remember(error)
      return false
    }
  }

  return {
    errorMessage,
    dismissError,
    beginTwoFactorEnrolment,
    confirmTwoFactorEnrolment,
    disableTwoFactor,
  }
})
