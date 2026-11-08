import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBrandingApi } from '../composables/apis/useBrandingApi'
import type { ResellerBranding } from '../types/branding'

/**
 * Owns the branding the signed-in caller's shell shows: the name, logo and accent colour of the
 * reseller that owns a customer's account.
 *
 * **The branding is cosmetic, so a failed read is not an error.** The shell must render with the
 * panel's own mark when the read cannot be answered, exactly as it does for a customer who has no
 * reseller; showing an alert in the app bar for a logo would be worse than the missing logo.
 */
export const useBrandingStore = defineStore('branding', () => {
  const api = useBrandingApi()

  /** The branding the shell draws; every member `null` means the panel's own. */
  const branding: Ref<ResellerBranding> = ref({ displayName: null, accentColor: null, logo: null })

  /**
   * Reads the branding the caller's shell shows.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      branding.value = await api.getShell()
    } catch {
      // Cosmetic: see the store's description. The shell keeps whatever it already shows.
    }
  }

  /**
   * Forgets the branding, so the next identity does not inherit the previous one's look.
   * @returns Nothing.
   */
  const clear = (): void => {
    branding.value = { displayName: null, accentColor: null, logo: null }
  }

  return { branding, load, clear }
})
