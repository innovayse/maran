import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMailChoicesApi } from '../composables/apis/useMailChoicesApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { MailChoices } from '../types/mailChoice'

/**
 * Owns the closed sets and bounds the mail screens select from (statuses, alias kinds, quota and
 * password bounds), read once from the panel. The mail pages read state from here and call its
 * action; they never touch the API composable (rules/vue.md).
 *
 * The panel names every choice in the caller's language, so a language switch remounts the routed
 * page and the page loads again; nothing here caches across languages.
 */
export const useMailChoicesStore = defineStore('mailChoices', () => {
  const api = useMailChoicesApi()

  /** The sets and bounds last read, or `null` before the first successful read. */
  const choices: Ref<MailChoices | null> = ref(null)

  /** Where the read stands: on its way, arrived, or refused. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** The panel's localized message from the last failed read, or `null`. Rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the sets and bounds, replacing whatever was held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    try {
      choices.value = await api.get()
      errorMessage.value = null
      loadState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  return { choices, loadState, errorMessage, load }
})
