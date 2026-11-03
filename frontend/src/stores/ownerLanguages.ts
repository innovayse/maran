import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountsApi } from '../composables/apis/useAccountsApi'
import { ApiError } from '../composables/useApi'
import type { OwnerLanguageChoice } from '../types/account'
import type { LoadingState } from '../types/loading'

/**
 * Owns the languages an account owner's invitation can be written in, for the create-account
 * form's language select. The set is the panel's: the SPA holds no list of it, and the create
 * endpoint refuses any language outside it, so the select is advice and the endpoint the boundary.
 *
 * Error text is never generated here: the backend's already-localized message is stored verbatim.
 */
export const useOwnerLanguagesStore = defineStore('ownerLanguages', () => {
  const api = useAccountsApi()

  /** The languages as last loaded, in the panel's order; empty before the first answer. */
  const languages: Ref<OwnerLanguageChoice[]> = ref([])

  /** Where the read of {@link languages} stands; every failure sets `error`, whatever its cause. */
  const languagesState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read, or `null` when it succeeded or none was made. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the languages into {@link languages}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    languagesState.value = 'loading'
    try {
      languages.value = await api.ownerLanguages()
      errorMessage.value = null
      languagesState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      languagesState.value = 'error'
    }
  }

  return { languages, languagesState, errorMessage, load }
})
