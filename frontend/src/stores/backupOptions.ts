import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupOptionsApi } from '../composables/apis/useBackupOptionsApi'
import { ApiError } from '../composables/useApi'
import type { BackupOptions } from '../types/backupOptions'

/**
 * Holds the closed sets the backup screens select from: the history filters' statuses and
 * reasons, the destinations filter's kinds of storage, and the schedule form's cadences, weekdays,
 * hours and retention counts. The screens read state from here and never touch the API composable
 * (rules/vue.md "API composables are called from Pinia stores ONLY").
 *
 * **Nothing in this store is a domain constant.** The lists are what the panel answered, named in
 * the language the request carried, so a screen shows a list that is empty (and says why) when the
 * read failed rather than one the SPA wrote itself. The names are language-dependent, which makes
 * a language switch a reason to read again: the screens call {@link load} when the locale changes.
 */
export const useBackupOptionsStore = defineStore('backupOptions', () => {
  const api = useBackupOptionsApi()

  /** The lists last answered; every list is empty until the first read arrives or when it failed. */
  const options: Ref<BackupOptions> = ref({
    statuses: [],
    kinds: [],
    destinationKinds: [],
    frequencies: [],
    weekdays: [],
    hours: [],
    retainCounts: [],
    customerFrequencies: [],
    customerRetainCounts: [],
  })

  /** The backend-localized reason the last read failed, or `null` while it has not. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the lists, replacing what was held.
   *
   * A failure keeps the previous lists (a language switch whose read failed still has usable
   * values) and records the backend's own message for the pickers to show.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      options.value = await api.get()
      errorMessage.value = null
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  return { options, errorMessage, load }
})
