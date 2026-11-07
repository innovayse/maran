import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { usePanelCertificateApi } from '../composables/apis/usePanelCertificateApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState, Read, Submission } from '../types/loading'
import type { PanelCertificate, PanelCertificateReading, PanelCertificateStatus } from '../types/panelCertificate'
import { loadingStateOf } from '../utils/loadingStateOf'

/** The code the panel answers with while it serves its self-signed certificate. */
const NOT_ISSUED_CODE = 'PanelCertificateNotIssued'

/**
 * Owns what the panel's own interface is served with, for the panel certificate
 * screen (issue #132).
 *
 * Two answers of the read are states, not failures: "not issued" is the
 * installer's self-signed certificate (a refusal the code identifies), and "no
 * host name" is a configuration the operator has to set (a 200 the
 * `hostNameConfigured` flag identifies). The sentence shown is always the
 * panel's own (rules/vue.md "Errors from the server").
 */
export const usePanelCertificateStore = defineStore('panelCertificate', () => {
  const api = usePanelCertificateApi()

  /**
   * The read of what the panel is served with: on its way, arrived, or refused. An order never
   * moves it to a failure, so a refused order leaves the certificate on screen.
   */
  const read: Ref<Read<PanelCertificateStatus>> = ref({ state: 'loading' })

  /** Where the last order stands; `done` lets the screen confirm that a certificate was installed. */
  const orderSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** Where the last return to a self-signed certificate stands; `done` lets the screen confirm it. */
  const restoreSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** What the panel is served with, or `null` until the read has settled. */
  const status: ComputedRef<PanelCertificateStatus | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  /** Where the read stands, in the words the loading region understands. */
  const loadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(read.value)
  })

  /** The panel's reason the read failed, rendered verbatim; `null` otherwise. */
  const loadMessage: ComputedRef<string | null> = computed(() => {
    return read.value.state === 'failed' ? read.value.message : null
  })

  /** True while an order is in flight. */
  const issuing: ComputedRef<boolean> = computed(() => {
    return orderSubmission.value.state === 'sending'
  })

  /** True once the last order installed a certificate, so the screen can confirm it. */
  const issued: ComputedRef<boolean> = computed(() => {
    return orderSubmission.value.state === 'done'
  })

  /** The panel's reason the last order was refused, rendered verbatim; `null` otherwise. */
  const orderMessage: ComputedRef<string | null> = computed(() => {
    return orderSubmission.value.state === 'failed' ? orderSubmission.value.message : null
  })

  /** True while the return to a self-signed certificate is in flight. */
  const restoring: ComputedRef<boolean> = computed(() => {
    return restoreSubmission.value.state === 'sending'
  })

  /** True once the last return installed a self-signed certificate, so the screen can confirm it. */
  const restored: ComputedRef<boolean> = computed(() => {
    return restoreSubmission.value.state === 'done'
  })

  /** The panel's reason the last return was refused, rendered verbatim; `null` otherwise. */
  const restoreMessage: ComputedRef<string | null> = computed(() => {
    return restoreSubmission.value.state === 'failed' ? restoreSubmission.value.message : null
  })

  /**
   * Turns a recorded certificate into the trusted state.
   * @param certificate The certificate the panel reported.
   * @returns The trusted status.
   */
  const trusted = (certificate: PanelCertificate): PanelCertificateStatus => {
    return { kind: 'trusted', certificate }
  }

  /**
   * Turns the panel's reading into the status the screen shows.
   * @param reading What `GET /api/v1/panel-certificate` answered.
   * @returns The no-host-name state, or the trusted one.
   */
  const statusOf = (reading: PanelCertificateReading): PanelCertificateStatus => {
    if (!reading.hostNameConfigured) {
      return { kind: 'noHostName', message: reading.hostNameNotice }
    }
    return trusted({ hostName: reading.hostName, expiresAt: reading.expiresAt, issuedAt: reading.issuedAt })
  }

  /**
   * Reads what the panel is served with.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    try {
      read.value = { state: 'loaded', value: statusOf(await api.get()) }
    } catch (error) {
      // The refusal that describes a state is the screen's content, not its failure.
      if (error instanceof ApiError && error.code === NOT_ISSUED_CODE) {
        read.value = { state: 'loaded', value: { kind: 'selfSigned' } }
      } else {
        read.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Orders a certificate for the panel's host name and installs it. A refusal
   * leaves the certificate the panel was serving in place, and its sentence on
   * the screen.
   * @returns True when a certificate was installed.
   */
  const issue = async (): Promise<boolean> => {
    orderSubmission.value = { state: 'sending' }
    restoreSubmission.value = { state: 'idle' }
    try {
      read.value = { state: 'loaded', value: trusted(await api.issue()) }
      orderSubmission.value = { state: 'done' }
      return true
    } catch (error) {
      orderSubmission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
  }

  /**
   * Returns the panel to a self-signed certificate; the panel forgets the trusted one. A refusal
   * leaves the certificate the panel was serving in place, and its sentence on the screen. On
   * success the status is read again, so the screen shows what the panel now reports.
   * @returns True when the self-signed certificate was restored.
   */
  const restore = async (): Promise<boolean> => {
    restoreSubmission.value = { state: 'sending' }
    orderSubmission.value = { state: 'idle' }
    try {
      await api.restore()
    } catch (error) {
      restoreSubmission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
    restoreSubmission.value = { state: 'done' }
    await load()
    return true
  }

  return {
    status,
    loadState,
    loadMessage,
    issuing,
    issued,
    orderMessage,
    restoring,
    restored,
    restoreMessage,
    load,
    issue,
    restore,
  }
})
