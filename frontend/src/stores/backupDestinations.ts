import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupDestinationsApi } from '../composables/apis/useBackupDestinationsApi'
import { ApiError } from '../composables/useApi'
import type { BackupDestination } from '../types/backupDestination'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the backup destinations list. The destinations page reads state from here and calls its
 * action — it never touches the API composable directly (rules/vue.md: "API composables are called
 * from Pinia stores ONLY").
 *
 * Error text is never generated here: a refusal's already-localized message is handed to the list
 * verbatim (rules/vue.md: "the backend owns their text").
 */
export const useBackupDestinationsStore = defineStore('backupDestinations', () => {
  const api = useBackupDestinationsApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const destinations: Ref<BackupDestination[]> = ref([])

  /**
   * Loads one page of the destinations into {@link destinations} and reports how the read ended,
   * for the destinations page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<BackupDestination>> => {
    try {
      const page = await api.list(request)
      destinations.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  return {
    destinations,
    list,
  }
})
