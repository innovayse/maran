import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useProfileApi } from '../composables/apis/useProfileApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { Profile } from '../types/profile'

/**
 * Owns the signed-in person's own profile. The profile screen reads state from here
 * and never touches the API composable (rules/vue.md).
 */
export const useProfileStore = defineStore('profile', () => {
  const api = useProfileApi()

  /** The profile as last loaded, or `null` before it has loaded or after a failure. */
  const profile: Ref<Profile | null> = ref(null)

  /** Backend-localized message from the last failed read, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /** Where the last read stands: on its way, arrived, or refused. */
  const loadState: Ref<LoadingState> = ref('loading')

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

  return { profile, errorMessage, loadState, load }
})
