import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSftpApi } from '../composables/apis/useSftpApi'
import { ApiError } from '../composables/useApi'
import type {
  CreateSftpUserRequest,
  RevealedSftpCredential,
  SftpUser,
} from '../types/sftpUser'

/**
 * Owns the customer SFTP logins list, the create workflow, and the one-time credential a create or
 * a password reset produces. The SFTP page reads state from here and calls its actions — it never
 * touches the API composable directly (rules/vue.md: "API composables are called from Pinia stores
 * ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 *
 * **The credential is held in memory and nowhere else.** It is a plain `ref`, never written to
 * `localStorage`, `sessionStorage`, the URL or a history entry, so a reload loses it — which is the
 * truth about it, since the server kept no copy either. The page clears it on unmount so a
 * navigation loses it too.
 */
export const useSftpStore = defineStore('sftp', () => {
  const api = useSftpApi()

  /** The logins as last reported by the panel; empty before the first successful load. */
  const sftpUsers: Ref<SftpUser[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (password reset, removal) is in flight. */
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
  const revealedCredential: Ref<RevealedSftpCredential | null> = ref(null)

  /**
   * Loads the login list, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      sftpUsers.value = await api.list()
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
   * Creates an SFTP login and holds the password it answered with for exactly one showing.
   * @param request The owning account and the name the customer chose.
   * @returns True when the panel created the login — the caller reads {@link createErrorMessage}
   * for the reason when it did not.
   */
  const create = async (request: CreateSftpUserRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // The list-shaped row is built from the create response rather than re-fetched: the
      // password must not be put into `sftpUsers`, where a later render could show it beside a
      // row long after the operator dismissed the dialog.
      sftpUsers.value = [
        ...sftpUsers.value,
        {
          id: created.id,
          accountId: created.accountId,
          name: created.name,
          fullName: created.fullName,
          createdAt: created.createdAt,
        },
      ]
      revealedCredential.value = { fullName: created.fullName, password: created.password }
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
   * Sets a new password on a login and holds it for exactly one showing. This is the only recovery
   * a lost password has: nothing anywhere keeps a copy of the old one.
   * @param id The login to re-credential.
   * @returns True when the panel set a new password.
   */
  const resetPassword = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      const reset = await api.resetPassword(id)
      // `fullName` comes from the response, which is the host's own spelling of the login — never
      // assembled here from the account name and the suffix.
      revealedCredential.value = { fullName: reset.fullName, password: reset.password }
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
   * Removes a login, dropping it from the held list. The account's files stay on disk.
   * @param id The login to remove.
   * @returns True when the panel removed it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      sftpUsers.value = sftpUsers.value.filter((user) => {
        return user.id !== id
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
    sftpUsers,
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
