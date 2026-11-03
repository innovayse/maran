import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountsApi } from '../composables/apis/useAccountsApi'
import { ApiError } from '../composables/useApi'
import type { AccountStatusChoice } from '../types/account'

/**
 * Owns the states a hosting account can be in, with their names, for what offers or names one:
 * the accounts list's status filter, its chips and its `?status=` link check. The SPA holds no list
 * of the states and no name for them (rules/architecture.md "The backend owns the data, the SPA
 * renders it"); the set is what the panel answered, in the language the request carried. The
 * badges on the rows and on the account pages print the `statusName` each account carries.
 *
 * **The server stays the authority.** {@link accepts} only decides whether a `?status=` link is
 * worth sending, and it is permissive until the set has arrived: a link naming a state is not
 * discarded on the strength of a list the SPA has not read yet, and the accounts endpoint refuses
 * a state it does not have either way.
 *
 * Error text is never generated here: the backend's already-localized message is stored verbatim.
 */
export const useAccountStatusesStore = defineStore('accountStatuses', () => {
  const api = useAccountsApi()

  /** The states as last answered, in the panel's order; empty before the first answer or when unreadable. */
  const statuses: Ref<AccountStatusChoice[]> = ref([])

  /** Whether the panel has answered at least once, so an empty {@link statuses} can be told from "not asked yet". */
  const answered: Ref<boolean> = ref(false)

  /** The panel's message for the last failed read, or `null` when it succeeded or none was made. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the states into {@link statuses}.
   *
   * A failure keeps what was held (a language switch whose read failed still has usable names) and
   * records the backend's own message. The names are language-dependent, so the screens call this
   * again when the interface language changes.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      statuses.value = await api.statuses()
      answered.value = true
      errorMessage.value = null
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Names a state as the panel named it.
   * @param status The account's state as the wire spells it (an account's `status`, or a `?status=` value).
   * @returns The panel's name for it; the wire value itself while the names have not arrived or
   * when the panel did not list it, so a badge is never blank.
   */
  const nameOf = (status: string): string => {
    return (
      statuses.value.find((choice) => {
        return choice.status === status
      })?.statusName ?? status
    )
  }

  /**
   * Whether a `?status=` value is worth sending to the accounts endpoint.
   * @param value The value from the URL.
   * @returns True while the set has not arrived (the server decides), otherwise whether the panel listed it.
   */
  const accepts = (value: string): boolean => {
    if (!answered.value) {
      return true
    }
    return statuses.value.some((choice) => {
      return choice.status === value
    })
  }

  return { statuses, answered, errorMessage, load, nameOf, accepts }
})
