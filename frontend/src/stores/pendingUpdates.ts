import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMonitoringApi } from '../composables/apis/useMonitoringApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { PendingUpdate } from '../types/monitoring'

/**
 * Owns the page of pending operating-system updates on the Updates screen. The page reads state
 * from here and calls its actions; it never touches the API layer (rules/vue.md).
 *
 * The list is a server page read through `useServerTable`, so the refusal travels back to the table
 * through {@link listPendingUpdates} as the panel's own localized message. A refusal is a display
 * state and never an empty page: an agent that could not read the package cache must not look like
 * a server with nothing to update.
 */
export const usePendingUpdatesStore = defineStore('pendingUpdates', () => {
  const api = useMonitoringApi()

  /** The rows of the page last answered by the panel; empty before the first answer. */
  const items: Ref<PendingUpdate[]> = ref([])

  /**
   * Loads one page of pending updates into {@link items} and reports how the read ended.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listPendingUpdates = async (request: ListRequest): Promise<ListLoad<PendingUpdate>> => {
    try {
      const page = await api.pendingUpdates(request)
      items.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      // Rows from an earlier page must not stay on screen beside a failure message.
      items.value = []
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  return { items, listPendingUpdates }
})
