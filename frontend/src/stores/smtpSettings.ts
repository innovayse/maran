import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useSmtpSettingsApi } from '../composables/apis/useSmtpSettingsApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState, Read, Submission } from '../types/loading'
import type { SaveSmtpSettingsRequest, SmtpSecurityChoice, SmtpSettings } from '../types/smtpSettings'
import { loadingStateOf } from '../utils/loadingStateOf'

/**
 * Owns the panel's outgoing mail settings for the settings screen.
 *
 * **Nothing in this store ever holds the stored password.** The read model has no
 * field for one, so there is no value here for a screen to render by accident; the
 * only password this store sees is one the administrator has just typed, on its way
 * out in a save.
 */
export const useSmtpSettingsStore = defineStore('smtpSettings', () => {
  const api = useSmtpSettingsApi()

  /**
   * The read of the settings: on its way, arrived, or refused. A save or a test send never moves
   * it, so their refusals leave the form on screen with the alert above it.
   */
  const read: Ref<Read<SmtpSettings>> = ref({ state: 'loading' })

  /**
   * The ways the connection can be protected, for the security picker. Empty until read, and empty
   * when the read failed: the picker then offers nothing to change and the stored mode stays.
   */
  const securityModes: Ref<SmtpSecurityChoice[]> = ref([])

  /** Where the last save stands; `done` lets the screen confirm it. */
  const saveSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** Where the last test message stands; `done` means the mail server accepted it. */
  const testSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** The settings the panel holds, or `null` until they have been read. */
  const settings: ComputedRef<SmtpSettings | null> = computed(() => {
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

  /**
   * The panel's reason the last save or test send was refused, rendered verbatim; `null` otherwise.
   *
   * For the test send this is the mail server's own refusal as the panel relayed it
   * (the RFC 7807 `detail`), and it is the answer the operator pressed the button
   * for: a generic "sending failed" would hide the one sentence that says which
   * setting is wrong.
   */
  const submitMessage: ComputedRef<string | null> = computed(() => {
    if (saveSubmission.value.state === 'failed') {
      return saveSubmission.value.message
    }

    return testSubmission.value.state === 'failed' ? testSubmission.value.message : null
  })

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: ComputedRef<boolean> = computed(() => {
    return saveSubmission.value.state === 'done'
  })

  /** True once the last test message was accepted by the mail server. */
  const testSent: ComputedRef<boolean> = computed(() => {
    return testSubmission.value.state === 'done'
  })

  /** True while a read, a save or a test send is in flight. */
  const loading: ComputedRef<boolean> = computed(() => {
    return (
      read.value.state === 'loading' ||
      saveSubmission.value.state === 'sending' ||
      testSubmission.value.state === 'sending'
    )
  })

  /**
   * Reads a failure's backend-localized text, or nothing for anything that is not an API error.
   * @param error The caught error.
   * @returns The text, or `null`.
   */
  const messageOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the panel's mail settings.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    try {
      read.value = { state: 'loaded', value: await api.get() }
    } catch (error) {
      read.value = { state: 'failed', message: messageOf(error) }
    }
  }

  /**
   * Reads the protection modes the picker offers into {@link securityModes}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadSecurityModes = async (): Promise<void> => {
    try {
      securityModes.value = await api.securityModes()
    } catch {
      // The picker is an aid to the form, not the form: the settings themselves read separately.
      securityModes.value = []
    }
  }

  /**
   * Replaces the mail settings.
   *
   * Re-reads afterwards rather than holding what was sent, because the answer to
   * "is a password stored" is the server's to give: a save that omitted the password
   * left the stored one alone, and only the server knows whether there was one.
   * @param request The settings to store, with the password omitted to keep the stored one.
   * @returns True when they were stored.
   */
  const save = async (request: SaveSmtpSettingsRequest): Promise<boolean> => {
    saveSubmission.value = { state: 'sending' }
    testSubmission.value = { state: 'idle' }
    try {
      await api.save(request)
      read.value = { state: 'loaded', value: await api.get() }
      saveSubmission.value = { state: 'done' }
      return true
    } catch (error) {
      saveSubmission.value = { state: 'failed', message: messageOf(error) }
      return false
    }
  }

  /**
   * Sends one test message and keeps the refusal, if there is one, exactly as sent.
   * @param recipient Where to send it.
   * @returns True when the mail server accepted it.
   */
  const sendTest = async (recipient: string): Promise<boolean> => {
    testSubmission.value = { state: 'sending' }
    saveSubmission.value = { state: 'idle' }
    try {
      await api.sendTest(recipient)
      testSubmission.value = { state: 'done' }
      return true
    } catch (error) {
      testSubmission.value = { state: 'failed', message: messageOf(error) }
      return false
    }
  }

  return {
    settings,
    securityModes,
    loading,
    loadState,
    loadMessage,
    saved,
    testSent,
    submitMessage,
    load,
    loadSecurityModes,
    save,
    sendTest,
  }
})
