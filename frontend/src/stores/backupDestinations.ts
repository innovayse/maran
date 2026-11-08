import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupDestinationsApi } from '../composables/apis/useBackupDestinationsApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { BackupDestination, BackupDestinationEncryptionOutcome } from '../types/backupDestination'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the backup destinations list and the one setting an administrator can change on a row, whether
 * new backups are encrypted. The destinations page reads state from here and calls its
 * action — it never touches the API composable directly (rules/vue.md: "API composables are called
 * from Pinia stores ONLY").
 *
 * Error text is never generated here: a refusal's already-localized message is handed to the list
 * verbatim (rules/vue.md: "the backend owns their text").
 */
export const useBackupDestinationsStore = defineStore('backupDestinations', () => {
  const api = useBackupDestinationsApi()
  const latestRead = useLatestRead()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const destinations: Ref<BackupDestination[]> = ref([])

  /**
   * Loads one page of the destinations into {@link destinations} and reports how the read ended,
   * for the destinations page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<BackupDestination>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        destinations.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Switches encryption of new backups on or off for one destination and, once the panel recorded
   * it, mirrors the new setting onto the row already on screen so the list needs no second read.
   * @param id The destination.
   * @param enabled Whether new backups written there are sealed.
   * @returns `saved`, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const setEncryption = async (id: string, enabled: boolean): Promise<BackupDestinationEncryptionOutcome> => {
    try {
      await api.setEncryption(id, enabled)
      const row = destinations.value.find((destination) => {
        return destination.id === id
      })
      if (row !== undefined) {
        row.encryptArchives = enabled
      }
      return { kind: 'saved' }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  return {
    destinations,
    list,
    setEncryption,
  }
})
