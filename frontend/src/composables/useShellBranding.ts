import { computed, onBeforeUnmount, watch, type ComputedRef } from 'vue'
import { useAuthStore } from '../stores/auth'
import { useBrandingStore } from '../stores/branding'
import { ACCENT_FILL_PROPERTY, ACCENT_HOVER_PROPERTY, brandingAccentStyle } from '../utils/brandingAccentStyle'
import { useCallerRole } from './useCallerRole'

/**
 * Keeps the shell wearing the branding of the reseller that owns the signed-in customer's account:
 * reads it when a customer's session starts, drops it when the identity changes, and paints the
 * reseller's accent colour over the panel's accent fill.
 *
 * Only the FILL is replaced (`--acf`, and `--ach` as a darker mix of it), because those are the two
 * tokens white text sits on; the panel refuses to store a colour white text cannot be read on, so the
 * contrast the design measured for the default holds for the reseller's colour too. The graphic and
 * text accents (`--ac`, `--act`) are left alone: a reseller's colour was never checked against the
 * dark surfaces. The hover is derived in CSS (`color-mix`) so no colour arithmetic lives in the SPA.
 *
 * Only a customer is asked: the endpoint answers every role, but an administrator or a reseller has
 * nothing to brand their own shell with.
 *
 * Call it once, from the layout that is the shell.
 * @returns Nothing.
 */
export const useShellBranding = (): void => {
  const authStore = useAuthStore()
  const brandingStore = useBrandingStore()
  const { isCustomer } = useCallerRole()

  /**
   * What identifies the session the branding belongs to, or `null` when nobody is signed in. A
   * view-as session is another user, so it changes the key like any sign-in does.
   */
  const identity: ComputedRef<string | null> = computed(() => {
    const user = authStore.user
    return user === null ? null : `${user.id}:${user.role}:${user.accountId ?? ''}`
  })

  /**
   * Removes the accent override, so the panel's own colours show again.
   * @returns Nothing.
   */
  const unpaint = (): void => {
    document.documentElement.style.removeProperty(ACCENT_FILL_PROPERTY)
    document.documentElement.style.removeProperty(ACCENT_HOVER_PROPERTY)
  }

  /**
   * Paints the accent colour the branding holds, or removes the override when it holds none.
   * @param color The accent colour, or `null` for the panel's own.
   * @returns Nothing.
   */
  const paint = (color: string | null): void => {
    if (color === null) {
      unpaint()
      return
    }

    for (const [property, value] of Object.entries(brandingAccentStyle(color))) {
      document.documentElement.style.setProperty(property, value)
    }
  }

  watch(
    identity,
    (current: string | null): void => {
      brandingStore.clear()
      if (current !== null && isCustomer.value) {
        void brandingStore.load()
      }
    },
    { immediate: true },
  )

  watch(
    (): string | null => {
      return brandingStore.branding.accentColor
    },
    paint,
    { immediate: true },
  )

  // The store outlives the shell: an override nobody removes would colour the sign-in screen too.
  onBeforeUnmount((): void => {
    brandingStore.clear()
    unpaint()
  })
}
