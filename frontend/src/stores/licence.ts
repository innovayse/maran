import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useLicenceApi } from '../composables/apis/useLicenceApi'
import { ApiError } from '../composables/useApi'
import type { LicenceStatus } from '../types/licence'
import type { LoadingState, Read, Submission } from '../types/loading'
import { loadingStateOf } from '../utils/loadingStateOf'

/**
 * Owns the panel's licence status and the request to install a licence, for the administrator-only
 * "Licence" screen (issue #8).
 *
 * The pasted licence text is passed straight through to the request and is never kept here, so
 * nothing in the store can show it again; the form that holds it clears it once the install
 * succeeded. Every sentence shown for a refusal is the panel's own (rules/vue.md "Errors from the
 * server").
 */
export const useLicenceStore = defineStore('licence', () => {
  const api = useLicenceApi()

  /** The read of the licence status: on its way, arrived, or refused. */
  const read: Ref<Read<LicenceStatus>> = ref({ state: 'loading' })

  /** Where the last install request stands. */
  const installSubmission: Ref<Submission> = ref({ state: 'idle' })

  /**
   * Numbers each status read, so only the latest answer is kept: a live language switch re-reads
   * the screen, and an install changes the status while an older read may still be in flight.
   * Not reactive, because nothing renders it.
   */
  let readTicket = 0

  /** The licence status, or `null` until the read has settled. */
  const state: ComputedRef<LicenceStatus | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  /** Where the read stands, in the words the loading region understands. */
  const loadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(read.value)
  })

  /** True while the status read is on its way. */
  const loading: ComputedRef<boolean> = computed(() => {
    return read.value.state === 'loading'
  })

  /** The panel's reason the read failed, rendered verbatim; `null` otherwise. */
  const error: ComputedRef<string | null> = computed(() => {
    return read.value.state === 'failed' ? read.value.message : null
  })

  /** True while the install request is in flight. */
  const installing: ComputedRef<boolean> = computed(() => {
    return installSubmission.value.state === 'sending'
  })

  /** True once the last install was accepted. */
  const installed: ComputedRef<boolean> = computed(() => {
    return installSubmission.value.state === 'done'
  })

  /** True when the last install was refused or got no answer. */
  const installFailed: ComputedRef<boolean> = computed(() => {
    return installSubmission.value.state === 'failed'
  })

  /** The panel's reason the last install was refused, rendered verbatim; `null` otherwise. */
  const installMessage: ComputedRef<string | null> = computed(() => {
    return installSubmission.value.state === 'failed' ? installSubmission.value.message : null
  })

  /**
   * What the lease request made by the last install amounted to, in the panel's own words; `null`
   * unless that install was accepted and its answer is the status now held.
   */
  const leaseRequestMessage: ComputedRef<string | null> = computed(() => {
    if (installSubmission.value.state !== 'done' || state.value === null) {
      return null
    }
    return state.value.leaseRequestDisplayName
  })

  /**
   * Whether the last install's lease request ended well: a lease was stored, the stored one is still recent,
   * or the licence is offline and needs none. It follows the request's own outcome and not the standing of the
   * lease, because a lease can stand as fresh while the request just made failed (a recent lease that is due
   * again, with the service unreachable).
   */
  const leaseRequestSucceeded: ComputedRef<boolean> = computed(() => {
    const request = state.value?.leaseRequest ?? null
    return request === 'renewed' || request === 'notDue' || request === 'skippedOffline'
  })

  /**
   * Reads the licence status. When reads overlap, the latest one wins: an earlier answer or
   * failure is dropped on arrival.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    readTicket += 1
    const ticket = readTicket
    read.value = { state: 'loading' }
    try {
      const answer = await api.status()
      if (ticket === readTicket) {
        read.value = { state: 'loaded', value: answer }
      }
    } catch (cause) {
      if (ticket === readTicket) {
        read.value = { state: 'failed', message: cause instanceof ApiError ? cause.message : null }
      }
    }
  }

  /**
   * Installs a pasted licence. On success the status the panel answers with replaces the held one,
   * and any read still in flight is dropped because it describes the state before the install.
   * @param rawLicenceText The licence text exactly as pasted; it is sent and not kept.
   * @returns True when the panel accepted the licence.
   */
  const install = async (rawLicenceText: string): Promise<boolean> => {
    installSubmission.value = { state: 'sending' }
    try {
      const answer = await api.install(rawLicenceText)
      readTicket += 1
      read.value = { state: 'loaded', value: answer }
      installSubmission.value = { state: 'done' }
      return true
    } catch (cause) {
      installSubmission.value = { state: 'failed', message: cause instanceof ApiError ? cause.message : null }
      return false
    }
  }

  /**
   * Forgets the outcome of the last install, so a refusal or a success does not outlive the edit
   * or the next attempt it no longer describes.
   * @returns Nothing.
   */
  const resetInstall = (): void => {
    if (installSubmission.value.state !== 'sending') {
      installSubmission.value = { state: 'idle' }
    }
  }

  return {
    state,
    loadState,
    loading,
    error,
    installing,
    installed,
    installFailed,
    installMessage,
    leaseRequestMessage,
    leaseRequestSucceeded,
    load,
    install,
    resetInstall,
  }
})
