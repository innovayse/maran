import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMailDomainsApi } from '../composables/apis/useMailDomainsApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { ListLoad, ListRequest } from '../types/list'
import type {
  CreateMailDomainRequest,
  MailDnsRecord,
  MailDomain,
  MailDomainChoicesRead,
} from '../types/mailDomain'

/** How many domains the domain picker lists per search; more are reached by typing. */
const CHOICES_PAGE_SIZE = 10

/**
 * Owns one page of the mail domains, the create workflow, the DKIM rotation and removal, the DNS
 * records of the domain whose dialog is open, and the page of domains a picker offers. The mail
 * pages read state from here and call its actions — they never touch the API composable directly
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useMailDomainsStore = defineStore('mailDomains', () => {
  const api = useMailDomainsApi()
  const latestRead = useLatestRead()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const domains: Ref<MailDomain[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (DKIM rotation, removal) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed mutation, or `null`. Reported above the list. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed create, or `null`. Kept apart from the list's own state. */
  const createErrorMessage: Ref<string | null> = ref(null)

  /** The domain whose DNS records are open in the dialog, or `null` when none is. */
  const recordsDomain: Ref<MailDomain | null> = ref(null)

  /** The DNS records of {@link recordsDomain}; empty until they have been read. */
  const records: Ref<MailDnsRecord[]> = ref([])

  /** Where the records read stands. */
  const recordsState: Ref<LoadingState> = ref('loading')

  /** The panel's localized message from the last failed records read, or `null`. */
  const recordsErrorMessage: Ref<string | null> = ref(null)

  /** What the domain picker last read: its page, the panel's refusal, or nothing yet. */
  const choicePage: Ref<MailDomainChoicesRead> = ref({ kind: 'idle' })

  /** Sequence number of the latest picker search; an answer carrying an older number is dropped. */
  let choicesTicket = 0

  /**
   * Loads one page of the domain list into {@link domains} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<MailDomain>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        domains.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the first domains whose name matches what was typed into an open picker. An answer to an
   * older search arriving after a newer one was sent is dropped.
   * @param term What was typed; empty for the first domains the panel lists.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const searchChoices = async (term: string): Promise<void> => {
    choicesTicket += 1
    const ticket = choicesTicket
    choicePage.value = { kind: 'idle' }
    let outcome: ListLoad<MailDomain>
    try {
      const page = await api.list({ page: 1, pageSize: CHOICES_PAGE_SIZE, search: term, sort: 'name', filters: {} })
      outcome = { kind: 'loaded', page }
    } catch (error) {
      outcome = error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
    if (ticket === choicesTicket) {
      choicePage.value = outcome
    }
  }

  /**
   * Adds a mail domain. The held page is not patched: where the new row falls depends on the
   * server's sort and search, so the list page reads its page again.
   * @param request The owning account and the domain name.
   * @returns True when the panel created the domain — the caller reads {@link createErrorMessage}
   * for the reason when it did not.
   */
  const create = async (request: CreateMailDomainRequest): Promise<boolean> => {
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
   * Removes a domain, dropping it from the held page; the list page then reads the page again.
   * @param id The domain to remove.
   * @returns True when the panel removed it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      domains.value = domains.value.filter((domain) => {
        return domain.id !== id
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

  /**
   * Starts a new DKIM key for a domain and replaces its held row, so the new selector shows.
   * @param id The domain whose key rotates.
   * @returns True when the panel rotated the key.
   */
  const rotateDkim = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      const rotated = await api.rotateDkim(id)
      domains.value = domains.value.map((domain) => {
        return domain.id === id ? rotated : domain
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

  /**
   * Opens the DNS records dialog of a domain and reads its records. The previous domain's records
   * are cleared first, so one domain's values are never shown under another's name.
   * @param domain The domain whose records are shown.
   * @returns Resolves once the read has settled, successfully or not.
   */
  const openRecords = async (domain: MailDomain): Promise<void> => {
    recordsDomain.value = domain
    records.value = []
    recordsState.value = 'loading'
    recordsErrorMessage.value = null
    try {
      const read = await api.dnsRecords(domain.id)
      // A dialog closed or reopened for another domain while the read was in flight owns the state now.
      if (recordsDomain.value?.id === domain.id) {
        records.value = read
        recordsState.value = 'loaded'
      }
    } catch (error) {
      if (recordsDomain.value?.id === domain.id) {
        recordsErrorMessage.value = error instanceof ApiError ? error.message : null
        recordsState.value = 'error'
      }
    }
  }

  /**
   * Closes the DNS records dialog and forgets what it held.
   * @returns Nothing.
   */
  const closeRecords = (): void => {
    recordsDomain.value = null
    records.value = []
  }

  return {
    domains,
    creating,
    acting,
    actionErrorMessage,
    createErrorMessage,
    recordsDomain,
    records,
    recordsState,
    recordsErrorMessage,
    choicePage,
    list,
    searchChoices,
    create,
    remove,
    rotateDkim,
    openRecords,
    closeRecords,
  }
})
