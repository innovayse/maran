import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFtpsServerApi } from '../composables/apis/useFtpsServerApi'
import { ApiError } from '../composables/useApi'
import type { EnableFtpsRequest, FtpsServerFailure, FtpsStatus } from '../types/ftpsStatus'

/** The HTTP status the panel answers a caller who may not read the FTPS server's state. */
const FORBIDDEN = 403

/**
 * Owns the FTPS daemon's measured state and the administrator's switch.
 *
 * **A 403 here is not an error and is not stored as one.** `GET /api/v1/ftps-server` is
 * administrator-only, so a customer's load is refused by design — the status carries a certificate
 * path on the host, which is operator-facing text a customer must never be shown. The refusal lands
 * in {@link isDisclosed} as `false`, and the screens read that as "this panel will not tell me",
 * which is a different sentence from "FTPS is off" and from "the server is broken". Folding it into
 * {@link loadFailure} would put a red alert on every customer's FTPS logins page for a request
 * the panel was right to refuse.
 *
 * **Every other failure is kept, and kept as a failure.** A real failure's already-localized
 * `detail` is stored verbatim (rules/vue.md); a failure that carries no sentence — the request never
 * reached the panel, or the answer had no text — is stored as `unexpected`, which the screen words
 * with its own generic sentence. It is never dropped: a click that changes nothing and says nothing
 * is the defect this distinction exists to end.
 *
 * Loading and acting fail into two fields, because they are read in two places: a failed load leaves
 * no status to draw the server panel from, so it is shown on the page; a failed switch is shown on
 * the panel whose button was pressed.
 */
export const useFtpsServerStore = defineStore('ftpsServer', () => {
  const api = useFtpsServerApi()

  /**
   * The daemon's state as last measured, or `null` when it has not been read — either because
   * nothing has asked yet or because this caller may not be told.
   */
  const status: Ref<FtpsStatus | null> = ref(null)

  /**
   * Whether the panel disclosed the status to this caller.
   *
   * `false` until a load succeeds, and `false` again after a 403. A screen must read this rather
   * than test `status === null`, because the two are the same value for "not asked yet" and
   * "refused", and only one of them is worth a sentence to the reader.
   */
  const isDisclosed: Ref<boolean> = ref(false)

  /** True while the status request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while an enable or disable request is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Why the most recent status read failed, or `null` when it did not. A 403 never lands here — see
   * this store's own note about why.
   */
  const loadFailure: Ref<FtpsServerFailure | null> = ref(null)

  /** Why the most recent enable or disable failed, or `null` when it did not. */
  const actionFailure: Ref<FtpsServerFailure | null> = ref(null)

  /**
   * Classifies anything a request threw. A problem the panel answered with keeps its own sentence;
   * everything else — a network failure, a problem with no text — is `unexpected`, never nothing.
   * @param error Whatever the request threw.
   * @returns The failure as the screen words it.
   */
  const failureOf = (error: unknown): FtpsServerFailure => {
    if (error instanceof ApiError && error.message.trim().length > 0) {
      return { kind: 'server', message: error.message }
    }
    return { kind: 'unexpected' }
  }

  /**
   * Reads the daemon's state.
   *
   * A 403 is recorded as non-disclosure and NOT as a failure, so a customer's screen shows the
   * facts it does have instead of an alert about a refusal that was correct.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      status.value = await api.status()
      isDisclosed.value = true
      loadFailure.value = null
    } catch (error) {
      if (error instanceof ApiError && error.status === FORBIDDEN) {
        status.value = null
        isDisclosed.value = false
        loadFailure.value = null
        return
      }
      loadFailure.value = failureOf(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Turns the daemon on for a hostname this panel serves, and holds the state measured afterwards.
   * @param request The hostname, and the passive address for a host behind NAT.
   * @returns True when the panel brought the daemon up.
   */
  const enable = async (request: EnableFtpsRequest): Promise<boolean> => {
    acting.value = true
    try {
      // The response is the state measured AFTER the change, so it replaces what is held rather
      // than being merged into it: a status assembled from what was asked for would report a
      // healthy daemon on a host where the unit failed to start.
      status.value = await api.enable(request)
      isDisclosed.value = true
      actionFailure.value = null
      return true
    } catch (error) {
      actionFailure.value = failureOf(error)
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Turns the daemon off, and holds the state measured afterwards — which may still say the service
   * is running, and the screen reports that rather than the off it asked for. The firewall is
   * deliberately left alone; the server panel's own warning is what makes leftover open ports visible.
   * @returns True when the panel answered the request with a status.
   */
  const disable = async (): Promise<boolean> => {
    acting.value = true
    try {
      status.value = await api.disable()
      isDisclosed.value = true
      actionFailure.value = null
      return true
    } catch (error) {
      actionFailure.value = failureOf(error)
      return false
    } finally {
      acting.value = false
    }
  }

  return { status, isDisclosed, loading, acting, loadFailure, actionFailure, load, enable, disable }
})
