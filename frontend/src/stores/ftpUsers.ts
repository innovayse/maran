import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFtpUsersApi } from '../composables/apis/useFtpUsersApi'
import { ApiError } from '../composables/useApi'
import type { CreateFtpUserRequest, FtpUser, RevealedFtpCredential } from '../types/ftpUser'

/**
 * Owns the customer FTPS logins list, the create workflow, and the one-time credential a create or a
 * password reset produces. The File transfer page reads state from here and calls its actions — it
 * never touches the API composable directly (rules/vue.md).
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text"). That includes
 * the agent's refusal of a busy account — the wire carries no code distinguishing "refused because
 * the account is busy" from "the host failed", and nothing here parses the sentence to invent one.
 *
 * **The credential is held in memory and nowhere else.** A plain `ref`, never written to
 * `localStorage`, `sessionStorage`, the URL or a history entry, so a reload loses it — which is the
 * truth about it, since the server kept no copy either.
 */
export const useFtpUsersStore = defineStore('ftpUsers', () => {
  const api = useFtpUsersApi()

  /** The FTPS logins as last reported by the panel; empty before the first successful load. */
  const ftpUsers: Ref<FtpUser[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (password reset, removal) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** True once the list has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed read or mutation, or `null`. Rendered
   * verbatim; never mapped to a locale key.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart from
   * {@link errorMessage} so a rejected form does not blank the list's own error.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * The FTPS password the panel has just produced, or `null` when there is none to show.
   *
   * Set by exactly two actions — {@link create} and {@link resetPassword} — and cleared by
   * {@link dismissCredential}. This ref is the only copy in existence once the response body is gone.
   */
  const revealedCredential: Ref<RevealedFtpCredential | null> = ref(null)

  /**
   * Loads the FTPS login list, replacing what is held.
   *
   * A failure is captured rather than thrown: the merged screen must still render its SFTP half when
   * the FTPS module is unreachable, which is the whole reason the two lists are two requests.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      ftpUsers.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Forgets the credential currently on screen, ending the only showing it gets.
   * @returns Nothing.
   */
  const dismissCredential = (): void => {
    revealedCredential.value = null
  }

  /**
   * Creates an FTPS login and holds the password it answered with for exactly one showing.
   * @param request The owning account and the name the customer chose.
   * @returns True when the panel created the login — the caller reads {@link createErrorMessage}
   * for the reason when it did not.
   */
  const create = async (request: CreateFtpUserRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // The list row is built from the create response rather than re-fetched, and the password is
      // deliberately not among the fields copied: a value in `ftpUsers` could be rendered beside a
      // row long after the operator dismissed the dialog.
      ftpUsers.value = [
        ...ftpUsers.value,
        {
          id: created.id,
          accountId: created.accountId,
          name: created.name,
          fullName: created.fullName,
          protocol: created.protocol,
          createdAt: created.createdAt,
        },
      ]
      revealedCredential.value = { fullName: created.fullName, password: created.password }
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
   * Sets a new password on a login and holds it for exactly one showing. The only recovery a lost
   * password has: nothing anywhere keeps a copy of the old one.
   * @param id The login to re-credential.
   * @returns True when the panel set a new password.
   */
  const resetPassword = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      const reset = await api.resetPassword(id)
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
      ftpUsers.value = ftpUsers.value.filter((user) => {
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
    ftpUsers,
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
