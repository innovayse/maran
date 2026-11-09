import { defineStore } from 'pinia'
import { useAlertRecipientsApi } from '../composables/apis/useAlertRecipientsApi'
import { useStoredSetting } from '../composables/useStoredSetting'
import type { AlertRecipients, SaveAlertRecipientsRequest } from '../types/alertSettings'

/** Owns the list of addresses alert mail is sent to, for the alert settings screen. */
export const useAlertRecipientsStore = defineStore('alertRecipients', () => {
  const api = useAlertRecipientsApi()

  // The read and save state is the same for every singleton setting; the composable owns it.
  const setting = useStoredSetting<AlertRecipients, SaveAlertRecipientsRequest>(api)

  return {
    /** The addresses the panel holds, with the cap, or `null` until they have been read. */
    recipients: setting.value,
    loadState: setting.loadState,
    loadMessage: setting.loadMessage,
    submitMessage: setting.submitMessage,
    saved: setting.saved,
    saving: setting.saving,
    load: setting.load,
    save: setting.save,
  }
})
