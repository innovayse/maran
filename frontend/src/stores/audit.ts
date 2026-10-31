import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuditApi } from '../composables/apis/useAuditApi'
import { ApiError } from '../composables/useApi'
import type { AuditActionChoice, AuditEvent } from '../types/audit'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the audit journal the admin screen renders: one server page of it, and the actions its
 * filter offers. The page reads state from here and calls its actions — it never touches the API
 * composable (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Nothing here mutates the journal: it is append-only, written by the backend from inside the
 * handlers that perform an action, and this store's whole surface is reads.
 */
export const useAuditStore = defineStore('audit', () => {
  const api = useAuditApi()

  /** The entries of the page last answered by the panel; empty before the first answer. */
  const events: Ref<AuditEvent[]> = ref([])

  /**
   * The actions the journal holds, for the action filter. Empty until read, and empty when the
   * read failed: the filter then offers "any action" alone, and the journal itself is unaffected.
   */
  const actions: Ref<AuditActionChoice[]> = ref([])

  /**
   * Loads one page of the journal into {@link events} and reports how the read ended, for the
   * audit page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<AuditEvent>> => {
    try {
      const page = await api.list(request)
      events.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the actions the journal holds into {@link actions}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadActions = async (): Promise<void> => {
    try {
      actions.value = await api.actions()
    } catch {
      // The filter is an aid, not the journal: an unreadable list leaves it offering "any action".
      actions.value = []
    }
  }

  return { events, actions, list, loadActions }
})
