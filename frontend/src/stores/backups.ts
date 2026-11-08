import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupsApi } from '../composables/apis/useBackupsApi'
import { ApiError } from '../composables/useApi'
import { restoreChangedTheAccount } from '../utils/restoreChangedTheAccount'
import { restorePartialCounts } from '../utils/restorePartialCounts'
import type { AccountListFilter } from '../types/accountListFilter'
import type { ListLoad, ListRequest } from '../types/list'
import type {
  Backup,
  BackupAttentionLoad,
  CreateBackupRequest,
  RestoreBackupRequest,
  RestoreChoicesLoad,
  RestoreRun,
} from '../types/backup'

/**
 * Owns the backups list, its attention notice, the take-a-backup-now workflow, and the deletion of one. The backups page
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
 * apart without re-reading a message it does not own. So the store decides which arm of `RestoreRun`
 * the ending is, from the code, through `restoreChangedTheAccount`
 * (`src/utils/restoreChangedTheAccount.ts`), and the screen only reads the arm.
 *
 * The store never retries a restore of its own accord, and offers no helper that would: a second
 * attempt over a half-replaced account is how the retry destroys what the first attempt left
 * usable, and the decision belongs to a human who has been told which of the two happened.
 */
export const useBackupsStore = defineStore('backups', () => {
  const api = useBackupsApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const backups: Ref<Backup[]> = ref([])

  /** Whether any account's latest finished backup failed, as the panel last answered. */
  const attention: Ref<BackupAttentionLoad> = ref({ state: 'loading' })

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

  /**
   * Backend-localized message from the most recent failed deletion, or `null`. Kept apart from
   * the list's own refusal so a refused deletion is reported above the list rather than replacing it.
   */
  const removeErrorMessage: Ref<string | null> = ref(null)

  /** The backup whose verification is in flight, or `null`; it disables that row's Verify now button. */
  const verifyingId: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent verification request the panel refused, or
   * `null`. A verification that RAN and found the artifact unsound is not an error here: it comes
   * back as a record whose state is `failed`, and the row shows it.
   */
  const verifyErrorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart
   * from the list's own refusal so a rejected create does not blank the list's own error.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * Where the most recent restore stands: idle, running, replaced the whole account, refused
   * before anything was touched, or stopped after the account was changed.
   *
   * `running` holds for the whole run rather than for a moment: the panel's restore endpoint
   * answers only when the account has been replaced, which is minutes for a large one. The dialog
   * says so, and refuses to be dismissed while it holds. The `changed` arm is what keeps the screen
   * from offering a retry, and it is decided here from the problem code through
   * `restoreChangedTheAccount` (`src/utils/restoreChangedTheAccount.ts`).
   */
  const restoreRun: Ref<RestoreRun> = ref({ state: 'idle' })

  /**
   * The databases and sites the open restore dialog can narrow to (issue #160): loading until asked
   * for, then the account's lists or `unavailable`. Cleared with the restore run, so a dialog opening
   * on a second row never offers the first row's account's names.
   */
  const restoreChoices: Ref<RestoreChoicesLoad> = ref({ state: 'loading' })

  /** The backup whose lists were asked for last; a reply for any other is stale and dropped. */
  let wantedRestoreChoices: string | null = null

  /**
   * Loads one page of the backup history into {@link backups} and reports how the read ended, for
   * the backups page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<Backup>> => {
    try {
      const page = await api.list(request)
      backups.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Asks the panel whether any account's latest finished backup failed, into {@link attention}. A
   * failure is recorded as `unavailable`, never as "nothing to attend to".
   * @param filter The account the history is narrowed to; empty for every account the caller may see.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadAttention = async (filter: AccountListFilter): Promise<void> => {
    attention.value = { state: 'loading' }
    try {
      attention.value = { state: 'answered', attention: await api.attention(filter) }
    } catch {
      attention.value = { state: 'unavailable' }
    }
  }

  /**
   * Takes a backup of an account. The list page is not patched: where the new row falls depends on
   * the server's sort and filters, so the page reads its page again once this settles.
   *
   * A run that ends in `failed` is a SUCCESSFUL request — the panel answered 201 with a record
   * whose status says what happened — so no error is raised here. The row's own status badge and
   * failure code are what report it.
   * @param request The account to back up.
   * @returns True when the panel answered with a record — the caller reads
   * {@link createErrorMessage} for the reason when it did not.
   */
  const create = async (request: CreateBackupRequest): Promise<boolean> => {
    creating.value = true
    try {
      await api.create(request)
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
   * Deletes a backup. The archive goes with it. The list page reads its page again afterwards, so
   * the total and the page boundaries stay the server's.
   * @param id The backup to delete.
   * @returns True when the panel deleted it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      removeErrorMessage.value = null
      return true
    } catch (error) {
      removeErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Asks the panel to re-read a backup's artifact and record whether it is sound, then replaces
   * that one row with the record the panel answered. Only the row is patched, not the page: a
   * verification changes no field a sort or filter reads, so the row cannot change place.
   * @param id The backup to verify.
   * @returns True when the panel answered with a record, whatever the record's verification state.
   */
  const verify = async (id: string): Promise<boolean> => {
    verifyingId.value = id
    try {
      const verified = await api.verify(id)
      backups.value = backups.value.map((row) => {
        return row.id === id ? verified : row
      })
      verifyErrorMessage.value = null
      return true
    } catch (error) {
      verifyErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      verifyingId.value = null
    }
  }

  /**
   * Forgets the outcome of any previous restore, so a dialog opening on a second row does not show
   * what happened on the first.
   * @returns Nothing.
   */
  const clearRestore = (): void => {
    restoreRun.value = { state: 'idle' }
    restoreChoices.value = { state: 'loading' }
  }

  /**
   * Fetches the databases and sites a restore of this backup could be narrowed to.
   *
   * A failure is the `unavailable` arm and never a thrown error: the lists only widen what the
   * dialog offers, and the whole-account restore, which needs neither, must stay possible when the
   * lists cannot be had. A reply that arrives after the dialog moved to another backup is dropped,
   * so the names of one account are never offered for another's restore.
   * @param id The backup about to be restored.
   * @returns Resolves once the lists are in hand or known to be unobtainable.
   */
  const loadRestoreChoices = async (id: string): Promise<void> => {
    restoreChoices.value = { state: 'loading' }
    wantedRestoreChoices = id
    try {
      const choices = await api.restoreChoices(id)
      if (wantedRestoreChoices === id) {
        restoreChoices.value = { state: 'loaded', choices }
      }
    } catch {
      if (wantedRestoreChoices === id) {
        restoreChoices.value = { state: 'unavailable' }
      }
    }
  }

  /**
   * Replaces an account from one of its backups.
   *
   * A rejection is recorded rather than thrown, because both of its meanings are display states:
   * either nothing was touched (`untouched`) and the operator may try again, or the account was
   * changed (`changed`) and they must not.
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
    restoreRun.value = { state: 'running' }
    try {
      restoreRun.value = { state: 'restored', outcome: await api.restore(id, request) }
      return true
    } catch (error) {
      if (!(error instanceof ApiError)) {
        restoreRun.value = { state: 'unanswered' }
      } else if (restoreChangedTheAccount(error.code)) {
        // The counts of a measured partial ride the same rejection as its code; absent on every
        // other ending, and the dialog renders cleanly without them.
        restoreRun.value = {
          state: 'changed',
          message: error.message,
          partial: restorePartialCounts(error.extensions),
        }
      } else {
        restoreRun.value = { state: 'untouched', message: error.message, code: error.code }
      }
      return false
    }
  }

  return {
    backups,
    attention,
    creating,
    acting,
    removeErrorMessage,
    createErrorMessage,
    verifyingId,
    verifyErrorMessage,
    restoreRun,
    restoreChoices,
    list,
    loadAttention,
    create,
    remove,
    verify,
    clearRestore,
    loadRestoreChoices,
    restore,
  }
})
