import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupsApi } from '../composables/apis/useBackupsApi'
import { ApiError } from '../composables/useApi'
import { restorePartialCounts } from '../utils/restorePartialCounts'
import type {
  Backup,
  CreateBackupRequest,
  RestoreBackupRequest,
  RestoreOutcome,
  RestorePartial,
} from '../types/backup'

/**
 * Owns the backups list, the take-a-backup-now workflow, and the deletion of one. The backups page
 * reads state from here and calls its actions — it never touches the API composable directly
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 *
 * **The restore action is here, and it is the one that can destroy a working account.** Its state
 * is kept apart from every other action's for a reason the other actions do not have: a failed
 * restore is not one condition but two. Most codes mean nothing was touched; `RestorePartial` and
 * `RestoreTruncated` mean the account has been CHANGED, and the screen must be able to tell those
 * apart without re-reading a message it does not own. So the code is kept beside the message, and
 * `restoreChangedTheAccount` (`src/utils/restoreChangedTheAccount.ts`) is what reads it.
 *
 * The store never retries a restore of its own accord, and offers no helper that would: a second
 * attempt over a half-replaced account is how the retry destroys what the first attempt left
 * usable, and the decision belongs to a human who has been told which of the two happened.
 */
export const useBackupsStore = defineStore('backups', () => {
  const api = useBackupsApi()

  /** The backups as last reported by the panel; empty before the first successful load. */
  const backups: Ref<Backup[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /**
   * True while a create request is in flight.
   *
   * This stays true for the whole run, not for a moment: the panel's create endpoint does not
   * answer until the archive is written, which is minutes for a large account. The screen says so
   * rather than looking hung.
   */
  const creating: Ref<boolean> = ref(false)

  /** True while a deletion is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** True once the list has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed read or deletion, or `null` when the
   * last one succeeded or none has been attempted. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart
   * from {@link errorMessage} so a rejected create does not blank the list's own error.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * True while a restore is in flight.
   *
   * Like {@link creating} this stays true for the whole run rather than for a moment: the panel's
   * restore endpoint answers only when the account has been replaced, which is minutes for a large
   * one. The dialog says so, and refuses to be dismissed while it holds.
   */
  const restoring: Ref<boolean> = ref(false)

  /**
   * What the most recent restore replaced, or `null` when none has succeeded since the dialog was
   * opened. Present only for a whole restore — the panel answers 200 for nothing else.
   */
  const restoreOutcome: Ref<RestoreOutcome | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed restore, or `null`. Rendered verbatim.
   */
  const restoreErrorMessage: Ref<string | null> = ref(null)

  /**
   * Machine-stable problem code of the most recent failed restore, or the empty string when the
   * last one succeeded or none has been attempted.
   *
   * Held for BEHAVIOUR only — whether the screen may offer the operation again — and never looked
   * up in a translation table (rules/vue.md: the backend owns the text of a server outcome).
   */
  const restoreErrorCode: Ref<string> = ref('')

  /**
   * The counts a PARTIAL restore's failure carried — what the server had already replaced when it
   * stopped — or `null` when the failure stated none: a refusal that touched nothing, a truncated
   * stream, or an older panel that predates the counts. Read beside {@link restoreErrorCode} by
   * the dialog's changed-account arm; the numbers are shown, never re-judged.
   */
  const restorePartial: Ref<RestorePartial | null> = ref(null)

  /**
   * Loads the backup list, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      backups.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Takes a backup of an account and puts the finished record at the head of the held list.
   *
   * A run that ends in `failed` is a SUCCESSFUL request — the panel answered 201 with a record
   * whose status says what happened — so it is prepended like any other and no error is raised
   * here. The row's own status badge and failure code are what report it.
   * @param request The account to back up.
   * @returns True when the panel answered with a record — the caller reads
   * {@link createErrorMessage} for the reason when it did not.
   */
  const create = async (request: CreateBackupRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // Prepended rather than appended: the list is newest-first, as the panel sends it.
      backups.value = [created, ...backups.value]
      createErrorMessage.value = null
      return true
    } catch (error) {
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      creating.value = false
    }
  }

  /**
   * Deletes a backup, removing it from the held list. The archive goes with it.
   * @param id The backup to delete.
   * @returns True when the panel deleted it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      backups.value = backups.value.filter((backup) => {
        return backup.id !== id
      })
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Forgets the outcome of any previous restore, so a dialog opening on a second row does not show
   * what happened on the first.
   * @returns Nothing.
   */
  const clearRestore = (): void => {
    restoreOutcome.value = null
    restoreErrorMessage.value = null
    restoreErrorCode.value = ''
    restorePartial.value = null
  }

  /**
   * Replaces an account from one of its backups.
   *
   * A rejection is recorded rather than thrown, because both of its meanings are display states:
   * either nothing was touched and the operator may try again, or the account was changed and they
   * must not. The caller reads {@link restoreErrorCode} through
   * `restoreChangedTheAccount` (`src/utils/restoreChangedTheAccount.ts`) to tell which.
   *
   * The held list is NOT reloaded on success. A restore creates a `preRestore` safety copy on the
   * server, so the list is genuinely stale afterwards — but re-reading it here would replace the
   * rows underneath a dialog that is still reporting what happened, and the screen would lose the
   * one statement the operator opened it for. The list is loaded again when the page is next
   * visited, which is also when it is next read.
   * @param id The backup to restore from.
   * @param request The typed confirmation.
   * @returns True when the account was wholly replaced.
   */
  const restore = async (id: string, request: RestoreBackupRequest): Promise<boolean> => {
    restoring.value = true
    clearRestore()
    try {
      restoreOutcome.value = await api.restore(id, request)
      return true
    } catch (error) {
      restoreErrorMessage.value = error instanceof ApiError ? error.message : null
      restoreErrorCode.value = error instanceof ApiError ? error.code : ''
      // The counts of a measured partial ride the same rejection as its code; absent on every
      // other ending, and the dialog renders cleanly without them.
      restorePartial.value = error instanceof ApiError ? restorePartialCounts(error.extensions) : null
      return false
    } finally {
      restoring.value = false
    }
  }

  return {
    backups,
    loading,
    creating,
    acting,
    isLoaded,
    errorMessage,
    createErrorMessage,
    restoring,
    restoreOutcome,
    restoreErrorMessage,
    restoreErrorCode,
    restorePartial,
    load,
    create,
    remove,
    clearRestore,
    restore,
  }
})
