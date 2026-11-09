import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDemoApi } from '../composables/apis/useDemoApi'
import type { DemoStatus } from '../types/demo'

/**
 * Owns what the public demo says about itself: when it resets next, the logins it publishes and the
 * reason each read-only area gives.
 *
 * **Everything here is display.** The refusals themselves are the backend's: a refused action
 * answers with its own localized problem whatever this store holds, so a status that failed to load
 * costs a missing notice, never a permission. A failed read is therefore not an error state; the
 * demo chrome simply draws nothing it cannot back with an answer.
 */
export const useDemoStore = defineStore('demo', () => {
  const api = useDemoApi()

  /** The last status the panel reported, or `null` before the first answer or after a failure. */
  const status: Ref<DemoStatus | null> = ref(null)

  /** True while a read is on its way, so two components mounting together ask once. */
  const loading: Ref<boolean> = ref(false)

  /**
   * Reads the demo's status again. Called on mount and on a language switch, because the refusal
   * reasons arrive localized per request.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    if (loading.value) {
      return
    }
    loading.value = true
    try {
      status.value = await api.status()
    } catch {
      // Cosmetic: see the store's description. The previous answer, if any, stays on screen.
    } finally {
      loading.value = false
    }
  }

  /**
   * Finds the reason an area is read-only.
   * @param area The area id the backend uses.
   * @returns The backend's sentence, or `null` when the status holds none for the area.
   */
  const reasonFor = (area: string): string | null => {
    const found = status.value?.refusalReasons.find((candidate) => {
      return candidate.area === area
    })
    return found?.reason ?? null
  }

  return { status, loading, load, reasonFor }
})
