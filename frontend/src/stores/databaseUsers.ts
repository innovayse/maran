import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDatabaseUsersApi } from '../composables/apis/useDatabaseUsersApi'
import { ApiError } from '../composables/useApi'
import type { RevealedDatabaseCredential } from '../types/database'
import type {
  CreateDatabaseUserRequest,
  DatabasePrivilegeCatalogue,
  DatabasePrivilegeKey,
  DatabaseUser,
  DatabaseUserDetail,
} from '../types/databaseUser'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the page of the database-users list on screen, the create workflow, the privileges editor's
 * data, and the one-time credential a create or a password reset produces. The users page reads
 * state from here and calls its actions; it never touches the API composable (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md "Errors from the server").
 *
 * **The credential is held in memory and nowhere else**, exactly as the databases store holds its
 * own: a plain `ref`, never written to storage, the URL or history, cleared by the page on unmount.
 */
export const useDatabaseUsersStore = defineStore('databaseUsers', () => {
  const api = useDatabaseUsersApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const users: Ref<DatabaseUser[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (password reset, drop) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** True while the privileges editor's read or save is in flight. */
  const editing: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed create, or `null`. */
  const createErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed reset or drop, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed editor read or save, or `null`. */
  const editorErrorMessage: Ref<string | null> = ref(null)

  /** The user the privileges editor is open on, with its grantable databases; `null` while closed. */
  const editedUser: Ref<DatabaseUserDetail | null> = ref(null)

  /** The grantable privileges and presets, read once per editor opening; `null` before the first read. */
  const catalogue: Ref<DatabasePrivilegeCatalogue | null> = ref(null)

  /** The password the panel has just produced, or `null` when there is none to show. */
  const revealedCredential: Ref<RevealedDatabaseCredential | null> = ref(null)

  /**
   * Loads one page of users into {@link users} and reports how the read ended, for the page's
   * `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<DatabaseUser>> => {
    try {
      const page = await api.list(request)
      users.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Forgets the credential on screen, ending the only showing it gets.
   * @returns Nothing.
   */
  const dismissCredential = (): void => {
    revealedCredential.value = null
  }

  /**
   * Creates a user and holds its password for exactly one showing.
   * @param request The owning account and the name chosen.
   * @returns True when the panel created the user; {@link createErrorMessage} holds the reason otherwise.
   */
  const create = async (request: CreateDatabaseUserRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(request)
      // The password goes into the credential and nowhere else, never into `users`.
      revealedCredential.value = { databaseFullName: null, dbUserName: created.fullName, password: created.password }
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
   * Sets a new password on a user and holds it for exactly one showing.
   * @param id The user to re-credential.
   * @returns True when the panel set a new password.
   */
  const resetPassword = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      const reset = await api.resetPassword(id)
      revealedCredential.value = { databaseFullName: null, dbUserName: reset.fullName, password: reset.password }
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
   * Drops a user. The held page is not patched: the total and the rows that move up are the
   * server's, so the page reads its list again after an accepted drop.
   * @param id The user to drop.
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

  /**
   * Reads the user and the privilege catalogue into the editor's state.
   * @param id The user to edit.
   * @returns True when both reads succeeded; {@link editorErrorMessage} holds the reason otherwise.
   */
  const openEditor = async (id: string): Promise<boolean> => {
    editing.value = true
    editorErrorMessage.value = null
    try {
      const [detail, privileges] = await Promise.all([api.get(id), api.privilegeCatalogue()])
      editedUser.value = detail
      catalogue.value = privileges
      return true
    } catch (error) {
      editorErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      editing.value = false
    }
  }

  /**
   * Replaces the edited user's privileges on one database, then reads the user again so the editor
   * shows what the panel holds rather than what was sent.
   * @param databaseId The database the privileges are on.
   * @param privileges The complete set to hold there; empty removes the grant.
   * @returns True when the panel applied the change.
   */
  const savePrivileges = async (databaseId: string, privileges: DatabasePrivilegeKey[]): Promise<boolean> => {
    const target = editedUser.value
    if (target === null) {
      return false
    }
    editing.value = true
    try {
      await api.setPrivileges(target.id, databaseId, privileges)
      editedUser.value = await api.get(target.id)
      editorErrorMessage.value = null
      return true
    } catch (error) {
      editorErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      editing.value = false
    }
  }

  /**
   * Closes the editor and forgets what it held.
   * @returns Nothing.
   */
  const closeEditor = (): void => {
    editedUser.value = null
    editorErrorMessage.value = null
  }

  return {
    users,
    creating,
    acting,
    editing,
    createErrorMessage,
    actionErrorMessage,
    editorErrorMessage,
    editedUser,
    catalogue,
    revealedCredential,
    list,
    create,
    resetPassword,
    remove,
    openEditor,
    savePrivileges,
    closeEditor,
    dismissCredential,
  }
})
