import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDnsRecordsApi } from '../composables/apis/useDnsRecordsApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { DnsRecord, DnsRecordRequest } from '../types/dnsRecord'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the page of a zone's records on screen and the record mutations. The records tab reads
 * state from here and calls its actions; it never touches the API composable (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md).
 */
export const useDnsRecordsStore = defineStore('dnsRecords', () => {
  const api = useDnsRecordsApi()
  const latestRead = useLatestRead()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const records: Ref<DnsRecord[]> = ref([])

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True while a delete is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** The panel's reason the last save failed, already localized, or `null`; shown inside the dialog. */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** The panel's reason the last delete failed, already localized, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of a zone's records into {@link records} and reports how the read ended, for
   * the tab's `useServerTable`.
   * @param zoneId The zone.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (zoneId: string, request: ListRequest): Promise<ListLoad<DnsRecord>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(zoneId, request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        records.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Adds a record, or replaces one when a record id is given. The held page is not patched: the
   * server's order decides where the row falls, so the tab reads its page again.
   * @param zoneId The zone.
   * @param recordId The record to replace, or `null` to add a new one.
   * @param request The record as it should be.
   * @returns True when the panel accepted it; otherwise {@link saveErrorMessage} holds the reason.
   */
  const save = async (zoneId: string, recordId: string | null, request: DnsRecordRequest): Promise<boolean> => {
    saving.value = true
    try {
      if (recordId === null) {
        await api.create(zoneId, request)
      } else {
        await api.update(zoneId, recordId, request)
      }
      saveErrorMessage.value = null
      return true
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      saving.value = false
    }
  }

  /**
   * Deletes a record.
   * @param zoneId The zone.
   * @param recordId The record.
   * @returns True when the panel deleted it; otherwise {@link actionErrorMessage} holds the reason.
   */
  const remove = async (zoneId: string, recordId: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(zoneId, recordId)
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
   * Forgets the last refusals, so a reopened dialog or tab does not start with a stale reason.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    saveErrorMessage.value = null
    actionErrorMessage.value = null
  }

  return { records, saving, acting, saveErrorMessage, actionErrorMessage, list, save, remove, clearErrors }
})
