import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDatabasesApi } from '../composables/apis/useDatabasesApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type {
  CreateDatabaseRequest,
  Database,
  RevealedDatabaseCredential,
} from '../types/database'

/**
 * Owns the page of the databases list on screen, the create workflow, and the one-time credential a
 * create or a password reset produces. The databases page reads state from here and calls its actions — it
 * never touches the API composable directly (rules/vue.md: "API composables are called from Pinia
 * stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 *
 * **The credential is held in memory and nowhere else.** It is a plain `ref`, never written to
 * `localStorage`, `sessionStorage`, the URL or a history entry, so a reload loses it — which is the
 * truth about it, since the server kept no copy either. The page clears it on unmount so a
 * navigation loses it too.
 */
export const useDatabasesStore = defineStore('databases', () => {
  const api = useDatabasesApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const databases: Ref<Database[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (password reset, drop) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed mutation (password reset, drop), or
   * `null`. Kept apart from the list's own read state, which the page's `useServerTable` holds, so a
   * refused drop is reported above the list rather than replacing it.
   */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart
   * from the list's read state so a rejected form does not blank the list's own error.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * The password the panel has just produced, or `null` when there is none to show.
   *
   * Set by exactly two actions — {@link create} and {@link resetPassword} — and cleared by
   * {@link dismissCredential}. Nothing reads it back from anywhere else, because nowhere else has
   * it: this ref is the only copy in existence once the response body is gone.
   */
  const revealedCredential: Ref<RevealedDatabaseCredential | null> = ref(null)

  /**
   * Loads one page of the database list into {@link databases} and reports how the read ended, for
   * the page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<Database>> => {
    try {
      const page = await api.list(request)
      databases.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Forgets the credential currently on screen. Called when the operator closes the dialog and
   * again when the page unmounts, so leaving the screen ends the one chance to read it.
   * @returns Nothing.
   */
  const dismissCredential = (): void => {
    revealedCredential.value = null
  }

  /**
   * Creates a database and holds the password it answered with for exactly one showing. The held
   * page is not patched: where the new row falls depends on the server's sort, search and paging,
   * so the page reads its list again after an accepted create.
   * @param request The owning account and the two names the customer chose.
   * @returns True when the panel created the database — the caller reads
   * {@link createErrorMessage} for the reason when it did not.
   */
  const create = async (request: CreateDatabaseRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // The password goes into the credential and nowhere else — never into `databases`, where a
      // later render could show it beside a row long after the operator dismissed the dialog.
      revealedCredential.value = {
        databaseFullName: created.fullName,
        dbUserName: created.dbUserName,
        password: created.password,
      }
      createErrorMessage.value = null
      return true
    } catch (error) {
      // Validation, plan-limit and conflict errors arrive already localized; stored verbatim
      // rather than replaced with frontend copy that would hide the reason.
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      creating.value = false
    }
  }

  /**
   * Sets a new password on a database's user and holds it for exactly one showing. This is the
   * only recovery a lost password has: nothing anywhere keeps a copy of the old one.
   * @param id The database whose user to re-credential.
   * @returns True when the panel set a new password.
   */
  const resetPassword = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      const reset = await api.resetPassword(id)
      const owner = databases.value.find((database) => {
        return database.id === reset.id
      })
      revealedCredential.value = {
        // Read off the held row, never assembled from the account name and the suffix: the
        // prefixed form is the server's answer, and a guess that drifted from it would send an
        // operator to a database that does not exist.
        databaseFullName: owner?.fullName ?? null,
        dbUserName: reset.dbUserName,
        password: reset.password,
      }
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Drops a database. The data goes with it. The held page is not patched: the total and the rows
   * that move up from the next page are the server's, so the page reads its list again after an
   * accepted drop.
   * @param id The database to drop.
   * @returns True when the panel dropped it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return {
    databases,
    creating,
    acting,
    actionErrorMessage,
    createErrorMessage,
    revealedCredential,
    list,
    create,
    resetPassword,
    remove,
    dismissCredential,
  }
})
