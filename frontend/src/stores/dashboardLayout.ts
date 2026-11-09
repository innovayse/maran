import { defineStore } from 'pinia'
import { useDashboardLayoutApi } from '../composables/apis/useDashboardLayoutApi'
import { useStoredSetting } from '../composables/useStoredSetting'
import type { DashboardLayout, SaveDashboardLayoutRequest } from '../types/dashboardLayout'

/**
 * Owns the signed-in person's dashboard layout: which widgets are drawn and in what order, and
 * which shortcuts the quick-actions bar offers. The panel keeps it per person and answers the
 * EFFECTIVE list (its defaults for the audience and the licensed modules, with this person's choices
 * applied), so this store never works out a default itself.
 *
 * The read and save state is the same as for every singleton setting, so the shared composable owns
 * it. Saving replaces the whole layout and reads it back, so what is drawn afterwards is what the
 * panel holds.
 */
export const useDashboardLayoutStore = defineStore('dashboardLayout', () => {
  const api = useDashboardLayoutApi()
  const setting = useStoredSetting<DashboardLayout, SaveDashboardLayoutRequest>(api)

  return {
    /** The layout the panel holds for this person, or `null` until it has been read. */
    layout: setting.value,
    loadState: setting.loadState,
    loadMessage: setting.loadMessage,
    submitMessage: setting.submitMessage,
    saving: setting.saving,
    load: setting.load,
    save: setting.save,
  }
})
