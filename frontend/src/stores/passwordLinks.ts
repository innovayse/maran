import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { ApiError } from '../composables/useApi'
import type { AcceptInvitationRequest, ResetPasswordRequest } from '../types/auth'

/**
 * Owns the password flows a signed-out visitor reaches from an e-mail: asking for a reset link,
 * setting a new password from it, and setting the first password from an invitation. Changing the
 * password while signed in is the profile's business (`profile.ts`).
 */
export const usePasswordLinksStore = defineStore('passwordLinks', () => {
  const api = useAuthApi()

  /** True while a reset request, a reset or an invitation acceptance is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** Backend-localized message from the last refused request, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * The login name the last accepted invitation created, or `null`. Shown on the success screen so
   * the person knows what to sign in with; the panel returns it only to the holder of the token.
   */
  const invitedUsername: Ref<string | null> = ref(null)

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
   * Asks the panel to send a password-reset link.
   *
   * **Reports nothing about the address, and that is the whole point.** The backend
   * answers identically for an address that has an account and one that does not,
   * so this action returns `void` rather than a boolean: a caller with a boolean
   * would eventually branch on it, and a screen that renders two outcomes is the
   * account-enumeration oracle the backend spent effort closing. A transport
   * failure is remembered as an error because it says nothing about the address —
   * it says the panel could not be reached.
   * @param email The address to send the link to.
   * @returns Resolves once the request has settled, whatever it settled as.
   */
  const requestPasswordReset = async (email: string): Promise<void> => {
    loading.value = true
    errorMessage.value = null
    try {
      await api.requestPasswordReset(email)
    } catch (error) {
      remember(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Sets a new password from a reset link.
   * @param request The token from the mail and the new password.
   * @returns True when the password was changed; false when the panel refused.
   */
  const resetPassword = async (request: ResetPasswordRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    try {
      await api.resetPassword(request)
      return true
    } catch (error) {
      // The backend gives one refusal for a token that never existed, one that has
      // expired and one already spent. It is rendered as sent, and nothing here
      // inspects it: any branch on the reason would re-create the distinction the
      // single message exists to hide.
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  /**
   * Sets the first password for an invited login.
   * @param request The token from the invitation mail and the chosen password.
   * @returns True when the password was set; false when the panel refused.
   */
  const acceptInvitation = async (request: AcceptInvitationRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    invitedUsername.value = null
    try {
      const result = await api.acceptInvitation(request)
      invitedUsername.value = result.username
      return true
    } catch (error) {
      // Same non-branching rule as resetPassword: an unknown, expired and spent token all
      // arrive as one refusal, and this store must not reconstruct the distinction.
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  return {
    loading,
    errorMessage,
    invitedUsername,
    dismissError,
    requestPasswordReset,
    resetPassword,
    acceptInvitation,
  }
})
