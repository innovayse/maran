import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { ApiError } from '../composables/useApi'
import type { Session } from '../types/auth'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the signed-in user's list of live sessions (the signed-in devices screen) and ending one
 * of them. Signing this browser out, here or everywhere, is the session's business (`auth.ts`).
 */
export const useSessionsStore = defineStore('sessions', () => {
  const api = useAuthApi()

  /**
   * The page of the caller's live sessions last answered by the panel; empty before the first
   * answer. Where its read stands is the sessions page's `useServerTable`'s to hold.
   */
  const sessions: Ref<Session[]> = ref([])

  /** Backend-localized message from the last refused end of a session, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Forgets the list. Called by the root component when nobody is signed in any more, so the next
   * identity on this tab never sees the previous one's devices.
   * @returns Nothing; state is cleared synchronously.
   */
  const clear = (): void => {
    sessions.value = []
  }

  /**
   * Forgets the last failure's message. A message belongs to the screen that produced it, so the
   * root component calls this on every navigation between screens (`App.vue`).
   * @returns Nothing; `errorMessage` is cleared synchronously.
   */
  const dismissError = (): void => {
    errorMessage.value = null
  }

  /**
   * Loads one page of the caller's live sessions into {@link sessions} and reports how the read
   * ended, for the sessions page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listSessions = async (request: ListRequest): Promise<ListLoad<Session>> => {
    try {
      const page = await api.listSessions(request)
      sessions.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Ends one of the caller's sessions. The page reads its list again on success rather than
   * dropping the row here: the removal pulls a row up from the next page, and the total moves.
   * @param id The session to end.
   * @returns True when it was ended.
   */
  const revokeSession = async (id: string): Promise<boolean> => {
    errorMessage.value = null
    try {
      await api.revokeSession(id)
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    }
  }

  return {
    sessions,
    errorMessage,
    clear,
    dismissError,
    listSessions,
    revokeSession,
  }
})
