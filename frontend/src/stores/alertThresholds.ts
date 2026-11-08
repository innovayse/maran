import { defineStore } from 'pinia'
import { useAlertThresholdsApi } from '../composables/apis/useAlertThresholdsApi'
import { useStoredSetting } from '../composables/useStoredSetting'
import type { AlertThresholds, SaveAlertThresholdsRequest } from '../types/alertSettings'

/** Owns the thresholds at which the panel alarms about the disk, the processor and the memory. */
export const useAlertThresholdsStore = defineStore('alertThresholds', () => {
  const api = useAlertThresholdsApi()

  // The read and save state is the same for every singleton setting; the composable owns it.
  const setting = useStoredSetting<AlertThresholds, SaveAlertThresholdsRequest>(api)

  return {
    /** The thresholds the panel holds, with their bounds, or `null` until they have been read. */
    thresholds: setting.value,
    loadState: setting.loadState,
    loadMessage: setting.loadMessage,
    submitMessage: setting.submitMessage,
    saved: setting.saved,
    saving: setting.saving,
    load: setting.load,
    save: setting.save,
  }
})
