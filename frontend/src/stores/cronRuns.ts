import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useCronRunsApi } from '../composables/apis/useCronRunsApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { CronRun, CronRunSummary } from '../types/cronRun'

/**
 * Owns running a cron entry by hand, one page of an entry's run history, and the one run whose output
 * is on screen (issue #158). Pages read state here and call its actions; they never touch the API
 * composable (rules/vue.md).
 *
 * Error text is never generated here: a refusal's already-localized sentence is stored verbatim, and
 * only "no answer at all" is left for the page to say in its own words.
 */
export const useCronRunsStore = defineStore('cronRuns', () => {
  const api = useCronRunsApi()

  /** The rows of the history page last answered; empty before the first answer. */
  const runs: Ref<CronRunSummary[]> = ref([])

  /** The entry a run by hand is in flight for, or the empty string when none is. */
  const runningEntryId: Ref<string> = ref('')

  /** The run whose result and output are on screen, or `null` when none is. */
  const shown: Ref<CronRun | null> = ref(null)

  /** Whether the result dialog is open; true while its run is still being fetched too. */
  const isShownOpen: Ref<boolean> = ref(false)

  /** True while a historical run's output is being fetched. */
  const shownLoading: Ref<boolean> = ref(false)

  /**
   * Backend-localized reason the latest run or read was refused, `''` when no answer arrived (the
   * dialog says that in its own words), or `null` when nothing failed.
   */
  const shownErrorMessage: Ref<string | null> = ref(null)

  /**
   * Turns a failure into what the dialog shows: the server's sentence, or the empty string for "no
   * answer", which the dialog replaces with its own text.
   * @param error What the request threw.
   * @returns The message to keep.
   */
  const reasonOf = (error: unknown): string => {
    return error instanceof ApiError ? error.message : ''
  }

  /**
   * Runs the entry now and opens the dialog on its result. The dialog opens at once, holding the
   * spinner, because the run lasts as long as the job does.
   * @param entryId The entry to run.
   * @param accountId The account whose crontab holds it.
   * @returns True when the panel recorded a run, whatever the job's own outcome.
   */
  const run = async (entryId: string, accountId: string): Promise<boolean> => {
    runningEntryId.value = entryId
    shown.value = null
    shownErrorMessage.value = null
    isShownOpen.value = true
    try {
      shown.value = await api.run(entryId, accountId)
      return true
    } catch (error) {
      shownErrorMessage.value = reasonOf(error)
      return false
    } finally {
      runningEntryId.value = ''
    }
  }

  /**
   * Loads one page of the entry's history into {@link runs} and reports how it ended, for the
   * history page's `useServerTable`.
   * @param entryId The entry whose runs to list.
   * @param accountId The account whose crontab holds it.
   * @param request The page the URL describes.
   * @returns The page, the panel's refusal with its message, or `unreachable`.
   */
  const list = async (entryId: string, accountId: string, request: ListRequest): Promise<ListLoad<CronRunSummary>> => {
    try {
      const page = await api.list(entryId, accountId, request)
      runs.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Opens the dialog on one historical run and fetches what it printed.
   * @param entryId The entry the run belongs to.
   * @param accountId The account whose crontab holds it.
   * @param runId The run.
   * @returns Resolves once the read has settled.
   */
  const open = async (entryId: string, accountId: string, runId: string): Promise<void> => {
    shown.value = null
    shownErrorMessage.value = null
    isShownOpen.value = true
    shownLoading.value = true
    try {
      shown.value = await api.get(entryId, accountId, runId)
    } catch (error) {
      shownErrorMessage.value = reasonOf(error)
    } finally {
      shownLoading.value = false
    }
  }

  /**
   * Closes the result dialog and forgets its run.
   * @returns Nothing.
   */
  const close = (): void => {
    isShownOpen.value = false
    shown.value = null
    shownErrorMessage.value = null
  }

  return {
    runs,
    runningEntryId,
    shown,
    isShownOpen,
    shownLoading,
    shownErrorMessage,
    run,
    list,
    open,
    close,
  }
})
