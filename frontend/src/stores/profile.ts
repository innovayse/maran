import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { useProfileApi } from '../composables/apis/useProfileApi'
import { ApiError } from '../composables/useApi'
import type { ChangePasswordRequest } from '../types/auth'
import type { LoadingState } from '../types/loading'
import type { Profile } from '../types/profile'

/**
 * Owns the signed-in person's own profile. The profile screen reads state from here
 * and never touches the API composable (rules/vue.md).
 */
export const useProfileStore = defineStore('profile', () => {
  const api = useProfileApi()
  const authApi = useAuthApi()

  /** The profile as last loaded, or `null` before it has loaded or after a failure. */
  const profile: Ref<Profile | null> = ref(null)

  /** Backend-localized message from the last failed read, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /** Where the last read stands: on its way, arrived, or refused. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** True while a password change is on its way. */
  const passwordSaving: Ref<boolean> = ref(false)

  /** Backend-localized message from the last refused password change, rendered verbatim. */
  const passwordErrorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the caller's profile.
   *
   * The held profile is dropped before the read, not after it: sign-out routes
   * without a page reload, so a profile left in place would show the previous
   * person's details to the next one until the answer arrived.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    profile.value = null
    loadState.value = 'loading'
    errorMessage.value = null
    try {
      profile.value = await api.get()
      loadState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  /**
   * Changes the caller's own password.
   *
   * The backend is the judge of everything here — the current password, the policy's
   * minimum length — and its already-localized refusal is kept as sent. On success the
   * profile is read again in place, without the loading state, because every other
   * sign-in has just ended and the session count on screen is now out of date.
   * @param request The current password and its replacement.
   * @returns True when the password was changed; false when the panel refused.
   */
  const changePassword = async (request: ChangePasswordRequest): Promise<boolean> => {
    passwordSaving.value = true
    passwordErrorMessage.value = null
    try {
      await authApi.changePassword(request)
    } catch (error) {
      passwordErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      passwordSaving.value = false
    }

    try {
      profile.value = await api.get()
    } catch {
      // The change itself succeeded; a stale session count is not worth reporting it as failed.
    }
    return true
  }

  return {
    profile,
    errorMessage,
    loadState,
    passwordSaving,
    passwordErrorMessage,
    load,
    changePassword,
  }
})
