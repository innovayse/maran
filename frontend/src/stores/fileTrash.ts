import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFilesApi } from '../composables/apis/useFilesApi'
import { ApiError } from '../composables/useApi'
import type { FileBatchResult, TrashEntry } from '../types/file'
import type { ListLoad, ListRequest } from '../types/list'
import type { Submission } from '../types/loading'

/**
 * Owns the trash page: the page of trashed entries last answered, which rows are ticked, and the
 * restore and empty actions. A trashed entry is addressed by its trash id, never by a path.
 *
 * Error text is never generated here: a refusal's already-localized message is handed back
 * verbatim (rules/vue.md). The account and the page, search and sort live in the URL
 * (`useServerTable`); this store only reads a page for them.
 */
export const useFileTrashStore = defineStore('fileTrash', () => {
  const api = useFilesApi()

  /** The rows of the page last answered by the panel; empty before the first answer. */
  const entries: Ref<TrashEntry[]> = ref([])

  /** The trash ids of the ticked rows. Only rows of the current page. */
  const selectedIds: Ref<string[]> = ref([])

  /** Where the latest restore or empty stands; `failed` carries the panel's reason. */
  const submission: Ref<Submission> = ref({ state: 'idle' })

  /** The ticked rows themselves, in the order the page lists them. */
  const selectedEntries: ComputedRef<TrashEntry[]> = computed(() => {
    return entries.value.filter((entry) => {return selectedIds.value.includes(entry.id)})
  })

  /** Whether every row of the page is ticked; false for an empty page. */
  const allSelected: ComputedRef<boolean> = computed(() => {
    return entries.value.length > 0 && selectedEntries.value.length === entries.value.length
  })

  /**
   * Reads one page of the trash into {@link entries} for the page's `useServerTable`. Ticks that
   * name a row no longer on the page are dropped, so a bulk action never acts on an unseen row.
   * @param accountId The account whose trash is listed.
   * @param request The page, size, search and sort.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (accountId: string, request: ListRequest): Promise<ListLoad<TrashEntry>> => {
    try {
      const page = await api.listTrash({
        accountId,
        list: { page: request.page, pageSize: request.pageSize, search: request.search, sort: request.sort },
      })
      entries.value = page.items
      selectedIds.value = selectedIds.value.filter((id) => {return page.items.some((entry) => {return entry.id === id})})
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Ticks or unticks one row.
   * @param id The row's trash id.
   * @param selected Whether the row should be ticked.
   * @returns Nothing.
   */
  const select = (id: string, selected: boolean): void => {
    const without = selectedIds.value.filter((held) => {return held !== id})
    selectedIds.value = selected ? [...without, id] : without
  }

  /**
   * Ticks every row of the page, or clears every tick.
   * @param selected Whether the rows should be ticked.
   * @returns Nothing.
   */
  const selectAll = (selected: boolean): void => {
    selectedIds.value = selected ? entries.value.map((entry) => {return entry.id}) : []
  }

  /**
   * Forgets the page, the ticks and the last outcome, as when the person switches account.
   * @returns Nothing.
   */
  const reset = (): void => {
    entries.value = []
    selectedIds.value = []
    submission.value = { state: 'idle' }
  }

  /**
   * Restores entries to where they were.
   * @param accountId The account.
   * @param trashIds The entries.
   * @returns One outcome per entry (a target that exists again is a per-item refusal), or `null` when the request failed.
   */
  const restore = async (accountId: string, trashIds: string[]): Promise<FileBatchResult | null> => {
    submission.value = { state: 'sending' }
    try {
      const result = await api.restore({ accountId, trashIds })
      submission.value = { state: 'done' }
      return result
    } catch (error) {
      submission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return null
    }
  }

  /**
   * Removes everything in the trash for good.
   * @param accountId The account.
   * @returns How many entries were removed, or `null` when the request failed.
   */
  const empty = async (accountId: string): Promise<number | null> => {
    submission.value = { state: 'sending' }
    try {
      const emptied = await api.emptyTrash(accountId)
      submission.value = { state: 'done' }
      return emptied.count
    } catch (error) {
      submission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return null
    }
  }

  return { entries, selectedIds, submission, selectedEntries, allSelected, list, select, selectAll, reset, restore, empty }
})
