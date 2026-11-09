import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAuditApi } from '../composables/apis/useAuditApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { AuditActionChoice, AuditEvent, AuditExportFormat, AuditOutcomeChoice, AuditScope } from '../types/audit'
import type { ListLoad, ListRequest } from '../types/list'
import type { Submission } from '../types/loading'
import { saveDownload } from '../utils/saveDownload'

/**
 * Owns the audit journal the audit screen renders: one server page of it, and the actions and
 * outcomes its filters offer. The administrator reads the whole journal and a reseller its own part of
 * it (issue #370); the {@link AuditScope} says which, and the panel decides what each holds. The page reads state from here and calls its actions — it never touches the API
 * composable (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Nothing here mutates the journal: it is append-only, written by the backend from inside the
 * handlers that perform an action, and this store's whole surface is reads, including the export
 * of what the current filters match.
 */
export const useAuditStore = defineStore('audit', () => {
  const api = useAuditApi()
  const latestRead = useLatestRead()

  /** The entries of the page last answered by the panel; empty before the first answer. */
  const events: Ref<AuditEvent[]> = ref([])

  /**
   * The actions the journal holds, for the action filter. Empty until read, and empty when the
   * read failed: the filter then offers "any action" alone, and the journal itself is unaffected.
   */
  const actions: Ref<AuditActionChoice[]> = ref([])

  /**
   * The outcomes the journal can be filtered by. Empty until read, and empty when the read failed:
   * the filter then offers "any outcome" alone, and the journal itself is unaffected.
   */
  const outcomes: Ref<AuditOutcomeChoice[]> = ref([])

  /** Where the last export stands; `failed` carries the panel's refusal, such as "narrow the window". */
  const exportState: Ref<Submission> = ref({ state: 'idle' })

  /** The panel's reason the last export was refused, rendered verbatim; `null` otherwise. */
  const exportMessage: ComputedRef<string | null> = computed(() => {
    return exportState.value.state === 'failed' ? exportState.value.message : null
  })

  /** True while an export is being produced and downloaded. */
  const exporting: ComputedRef<boolean> = computed(() => {
    return exportState.value.state === 'sending'
  })

  /**
   * Loads one page of the journal into {@link events} and reports how the read ended, for the
   * audit page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @param scope Whose journal to read: the administrator's whole one, or a reseller's part of it.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest, scope: AuditScope): Promise<ListLoad<AuditEvent>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request, scope)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        events.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the actions the journal holds into {@link actions}.
   * @param scope Whose journal to read the actions of.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadActions = async (scope: AuditScope): Promise<void> => {
    try {
      actions.value = await api.actions(scope)
    } catch {
      // The filter is an aid, not the journal: an unreadable list leaves it offering "any action".
      actions.value = []
    }
  }

  /**
   * Reads the outcomes the journal can be filtered by into {@link outcomes}.
   * @param scope Whose journal the filter is for.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOutcomes = async (scope: AuditScope): Promise<void> => {
    try {
      outcomes.value = await api.outcomes(scope)
    } catch {
      // The filter is an aid, not the journal: an unreadable list leaves it offering "any outcome".
      outcomes.value = []
    }
  }

  /**
   * Downloads the journal narrowed by the given search and filters, and hands the file to the
   * browser. The panel caps the size and refuses a larger export instead of truncating it; that
   * refusal is kept in {@link exportMessage} for the menu to show.
   * @param request The table's current request; its search and filters are sent, its page and sort are not.
   * @param format The file format asked for.
   * @returns True when the file was received and saved.
   */
  const exportJournal = async (request: ListRequest, format: AuditExportFormat): Promise<boolean> => {
    exportState.value = { state: 'sending' }
    try {
      saveDownload(await api.exportJournal(request, format))
      exportState.value = { state: 'done' }
      return true
    } catch (error) {
      exportState.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
  }

  return {
    events,
    actions,
    outcomes,
    exportState,
    exportMessage,
    exporting,
    list,
    loadActions,
    loadOutcomes,
    exportJournal,
  }
})
