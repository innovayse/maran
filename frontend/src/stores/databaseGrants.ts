import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useDatabaseGrantsApi } from '../composables/apis/useDatabaseGrantsApi'
import { ApiError } from '../composables/useApi'
import type { GrantRepairReport } from '../types/grantRepair'

/** The HTTP status the panel answers a caller who may not read the host's grant table. */
const FORBIDDEN = 403

/**
 * Owns the database server's grant-table census and the repair that acts on it.
 *
 * **A 403 here is not an error and is not stored as one.** Both endpoints are administrator-only, so a
 * customer's load is refused by design — the census spans every account on the host and a refused row
 * carries identifiers they do not own. The refusal lands in {@link isDisclosed} as `false`, and the
 * screen reads that as "this panel will not tell me", which is a different sentence from "this host
 * has no broken grants" and from "the server is broken". Folding it into {@link errorMessage} would
 * put a red alert on the screen for a request the panel was right to refuse.
 *
 * **The repair is reachable only from a report this store is holding.** {@link repair} sends the
 * figure the held report gave, and refuses to send anything when no report has been read; the server
 * re-classifies the host and answers 409 if the figure has moved. Two gates rather than one, on
 * purpose: the client one makes the screen honest, and the server one is the one that counts.
 *
 * Error text is never generated here: a real failure's already-localized `detail` is stored verbatim
 * (rules/vue.md).
 */
export const useDatabaseGrantsStore = defineStore('databaseGrants', () => {
  const api = useDatabaseGrantsApi()

  /**
   * The census as last read, or `null` when none has been — either because nothing has asked yet or
   * because this caller may not be told.
   */
  const report: Ref<GrantRepairReport | null> = ref(null)

  /**
   * Whether the panel disclosed the census to this caller.
   *
   * `false` until a load succeeds, and `false` again after a 403. A screen must read this rather than
   * test `report === null`, because the two are the same value for "not asked yet" and "refused", and
   * only one of them is worth a sentence to the reader.
   */
  const isDisclosed: Ref<boolean> = ref(false)

  /** True while the report request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while the repair request is in flight. */
  const repairing: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent genuine failure, or `null`. A 403 never lands
   * here — see this store's own note about why.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Whether the held census is an inspection that has not been acted on yet.
   *
   * The screen offers the repair on this and on nothing else: after a repair the same shape comes back
   * with `isReportOnly` false, and offering "repair" again over the result of one would invite a
   * second host-wide rewrite nobody had inspected.
   */
  const canRepair: ComputedRef<boolean> = computed(() => {
    return report.value !== null && report.value.isReportOnly
  })

  /**
   * Reads what a repair would change on this host. Changes nothing on the server.
   *
   * A 403 is recorded as non-disclosure and NOT as a failure, so a customer who reaches the URL sees
   * the sentence about who may run this rather than an alert about a refusal that was correct.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      report.value = await api.report()
      isDisclosed.value = true
      errorMessage.value = null
    } catch (error) {
      if (error instanceof ApiError && error.status === FORBIDDEN) {
        report.value = null
        isDisclosed.value = false
        errorMessage.value = null
        return
      }
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Performs the repair, confirming the figure the held report gave.
   *
   * Refuses to send anything when no report is held: without one there is no figure the operator has
   * seen, and the request would be a host-wide rewrite asked for blind. The server enforces the same
   * rule independently and is the boundary; this check only keeps the screen from offering what the
   * server would refuse.
   * @returns True when the panel performed the repair.
   */
  const repair = async (): Promise<boolean> => {
    const held = report.value
    if (held === null || !held.isReportOnly) {
      return false
    }

    repairing.value = true
    try {
      // The response is the census measured AFTER the change, so it REPLACES the report rather than
      // being merged into it: a screen assembled from what was planned would show a rewrite that the
      // server may have refused row by row.
      report.value = await api.repair({ expectedRepairCount: held.wouldRepair.length })
      isDisclosed.value = true
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      repairing.value = false
    }
  }

  return { report, isDisclosed, loading, repairing, errorMessage, canRepair, load, repair }
})
