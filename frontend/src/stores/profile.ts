import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { useProfileApi } from '../composables/apis/useProfileApi'
import { ApiError } from '../composables/useApi'
import type { ChangePasswordRequest } from '../types/auth'
import type { LoadingState, Read, Submission } from '../types/loading'
import type { Profile } from '../types/profile'
import { loadingStateOf } from '../utils/loadingStateOf'

/**
 * Owns the signed-in person's own profile. The profile screen reads state from here
 * and never touches the API composable (rules/vue.md).
 */
export const useProfileStore = defineStore('profile', () => {
  const api = useProfileApi()
  const authApi = useAuthApi()

  /** The read of the profile: on its way, arrived, or refused. */
  const read: Ref<Read<Profile>> = ref({ state: 'loading' })

  /** Where the last password change stands. */
  const passwordChange: Ref<Submission> = ref({ state: 'idle' })

  /** The profile as last loaded, or `null` before it has loaded or after a failure. */
  const profile: ComputedRef<Profile | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  /** Backend-localized message from the last failed read, rendered verbatim. */
  const errorMessage: ComputedRef<string | null> = computed(() => {
    return read.value.state === 'failed' ? read.value.message : null
  })

  /** Where the last read stands, in the words the loading region understands. */
  const loadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(read.value)
  })

  /** True while a password change is on its way. */
  const passwordSaving: ComputedRef<boolean> = computed(() => {
    return passwordChange.value.state === 'sending'
  })

  /** Backend-localized message from the last refused password change, rendered verbatim. */
  const passwordErrorMessage: ComputedRef<string | null> = computed(() => {
    return passwordChange.value.state === 'failed' ? passwordChange.value.message : null
  })

  /**
   * Reads the caller's profile.
   *
   * The held profile is dropped before the read, not after it: sign-out routes
   * without a page reload, so a profile left in place would show the previous
   * person's details to the next one until the answer arrived.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    try {
      read.value = { state: 'loaded', value: await api.get() }
    } catch (error) {
      read.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
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
    passwordChange.value = { state: 'sending' }
    try {
      await authApi.changePassword(request)
    } catch (error) {
      passwordChange.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
    passwordChange.value = { state: 'done' }

    try {
      read.value = { state: 'loaded', value: await api.get() }
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
