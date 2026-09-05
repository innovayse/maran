import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSmtpSettingsApi } from '../composables/apis/useSmtpSettingsApi'
import { ApiError } from '../composables/useApi'
import type { SaveSmtpSettingsRequest, SmtpSettings } from '../types/smtpSettings'

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

  /** The settings the panel holds, or `null` until they have been read. */
  const settings: Ref<SmtpSettings | null> = ref(null)

  /** True while a read, a save or a test send is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /** True once the last test message was accepted by the mail server. */
  const testSent: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the last failure, rendered verbatim.
   *
   * For the test send this is the mail server's own refusal as the panel relayed it
   * (the RFC 7807 `detail`), and it is the answer the operator pressed the button
   * for: a generic "sending failed" would hide the one sentence that says which
   * setting is wrong.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads a failure's backend-localized text, or clears it for anything that is not
   * an API error.
   * @param error The caught error.
   * @returns Nothing; `errorMessage` is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the panel's mail settings.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    errorMessage.value = null
    try {
      settings.value = await api.get()
    } catch (error) {
      remember(error)
    } finally {
      loading.value = false
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
    loading.value = true
    errorMessage.value = null
    saved.value = false
    testSent.value = false
    try {
      await api.save(request)
      settings.value = await api.get()
      saved.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  /**
   * Sends one test message and keeps the refusal, if there is one, exactly as sent.
   * @param recipient Where to send it.
   * @returns True when the mail server accepted it.
   */
  const sendTest = async (recipient: string): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    testSent.value = false
    saved.value = false
    try {
      await api.sendTest(recipient)
      testSent.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  return { settings, loading, saved, testSent, errorMessage, load, save, sendTest }
})
