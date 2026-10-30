import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSftpApi } from '../composables/apis/useSftpApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type {
  CreateSftpUserRequest,
  RevealedSftpCredential,
  SftpUser,
} from '../types/sftpUser'

/**
 * Owns one page of the customer SFTP logins, the create workflow, and the one-time credential a
 * create or a password reset produces. The SFTP logins page reads state from here and calls its
 * actions — it never touches the API composable directly (rules/vue.md: "API composables are called
 * from Pinia stores ONLY").
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

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const sftpUsers: Ref<SftpUser[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (password reset, removal) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed mutation (password reset, removal), or
   * `null`. Reported above the list rather than in place of it.
   */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart
   * from the list's own state so a rejected form does not blank the list.
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
   * Loads one page of the login list into {@link sftpUsers} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search, sort and account filter, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<SftpUser>> => {
    try {
      const page = await api.list(request)
      sftpUsers.value = page.items
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
   * Creates an SFTP login and holds the password it answered with for exactly one showing. The held
   * page is not patched: where the new row falls depends on the server's sort and search, so the
   * list page reads its page again.
   * @param request The owning account and the name the customer chose.
   * @returns True when the panel created the login — the caller reads {@link createErrorMessage}
   * for the reason when it did not.
   */
  const create = async (request: CreateSftpUserRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // Only the login and its password are kept, and only here: the password must never reach
      // `sftpUsers`, where a later render could show it beside a row.
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
   * Removes a login, dropping it from the held page; the list page then reads the page again so it
   * refills from the next one. The account's files stay on disk.
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
    sftpUsers,
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
