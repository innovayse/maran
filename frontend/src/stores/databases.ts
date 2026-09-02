import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDatabasesApi } from '../composables/apis/useDatabasesApi'
import { ApiError } from '../composables/useApi'
import type {
  CreateDatabaseRequest,
  Database,
  RevealedDatabaseCredential,
} from '../types/database'

/**
 * Owns the customer databases list, the create workflow, and the one-time credential a create or a
 * password reset produces. The databases page reads state from here and calls its actions — it
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

  /** The databases as last reported by the panel; empty before the first successful load. */
  const databases: Ref<Database[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (password reset, drop) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** True once the list has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed read or mutation, or `null` when the
   * last one succeeded or none has been attempted. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart
   * from {@link errorMessage} so a rejected form does not blank the list's own error.
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
   * Loads the database list, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      databases.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
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
   * Creates a database and holds the password it answered with for exactly one showing.
   * @param request The owning account and the two names the customer chose.
   * @returns True when the panel created the database — the caller reads
   * {@link createErrorMessage} for the reason when it did not.
   */
  const create = async (request: CreateDatabaseRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // The list-shaped row is built from the create response rather than re-fetched: the
      // password must not be put into `databases`, where a later render could show it beside a
      // row long after the operator dismissed the dialog.
      databases.value = [
        ...databases.value,
        {
          id: created.id,
          accountId: created.accountId,
          name: created.name,
          fullName: created.fullName,
          dbUserName: created.dbUserName,
          createdAt: created.createdAt,
        },
      ]
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
   * Drops a database, removing it from the held list. The data goes with it.
   * @param id The database to drop.
   * @returns True when the panel dropped it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      databases.value = databases.value.filter((database) => {
        return database.id !== id
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

  return {
    databases,
    loading,
    creating,
    acting,
    isLoaded,
    errorMessage,
    createErrorMessage,
    revealedCredential,
    load,
    create,
    resetPassword,
    remove,
    dismissCredential,
  }
})
