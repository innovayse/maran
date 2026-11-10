<script setup lang="ts">
/**
 * The signed-in person's account menu: a dropdown of their own pages (profile, sessions,
 * two-step verification), opened from whatever the caller puts in the `trigger` slot.
 *
 * It is ONE component for both places that open it, the sidebar's identity card and the app
 * bar's avatar, so the two menus can never list different entries or go to different pages. The
 * caller owns only the look of the trigger (an avatar with a name, or the avatar alone) and the
 * trigger's accessible name; the entries, the navigation and the sign-out are here.
 *
 * `withSignOut` adds a "Sign out" row at the end. The sidebar card has its own icon-only Sign out
 * button beside the menu and leaves it off (a second copy made the menu taller, so it opened up
 * over the group label above it, issue #210); the app bar has no such button, so there the menu is
 * the only way to end the session from the bar.
 *
 * `chevron` is the trigger's trailing "opens a menu" mark. The sidebar card turns it off: the
 * card's right edge belongs to its Sign out button (issue #510), and `aria-haspopup` still says
 * the card opens a menu to assistive technology.
 *
 * Nobody signed in draws nothing: there is no account to offer a menu for.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import { useAuthStore } from '../../stores/auth'
import { useViewingAs } from '../../composables/useViewingAs'

/** Props accepted by {@link ShellAccountMenu}. */
withDefaults(
  defineProps<{
    /** Translated accessible name of the trigger; its visible content is the `trigger` slot. */
    triggerLabel: string
    /** Alignment of the panel against the trigger. */
    align?: 'start' | 'end'
    /** Whether the menu ends with a "Sign out" row. */
    withSignOut?: boolean
    /** Whether the trigger draws its trailing chevron. */
    chevron?: boolean
  }>(),
  { align: 'start', withSignOut: false, chevron: true },
)

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()

/** Whether this is a view-as session, which is refused the sessions page. */
const viewingAs: ComputedRef<boolean> = useViewingAs()

/** The login name, or `null` when nobody is signed in. */
const name: ComputedRef<string | null> = computed(() => {
  return authStore.user?.username ?? null
})

/**
 * Opens one of the account pages.
 * @param target The route name to navigate to.
 * @returns Resolves once the navigation has settled.
 */
const go = async (target: string): Promise<void> => {
  await router.push({ name: target })
}

/**
 * Signs out of this device and returns to the sign-in screen.
 * @returns Resolves once the request has settled.
 */
const signOut = async (): Promise<void> => {
  await authStore.logout()
  await router.push({ name: 'login' })
}
</script>

<template>
  <UiDropdown
    v-if="name !== null"
    :align="align"
    variant="bare"
    :chevron="chevron"
    :label="name"
    :aria-label="triggerLabel"
  >
    <template #trigger>
      <slot name="trigger"></slot>
    </template>

    <!-- Profile first (issue #210): the signed-in person's own profile, the same entry for every
         role. A customer's hosting account is linked from the profile page, never from here. -->
    <UiDropdownItem @select="go('profile')">
      {{ t('app.shell.menu.profile') }}
    </UiDropdownItem>
    <!-- A view-as session is refused the sessions page (issue #513): the entry is not offered. -->
    <UiDropdownItem v-if="!viewingAs" @select="go('sessions')">{{ t('app.shell.menu.sessions') }}</UiDropdownItem>
    <UiDropdownItem @select="go('two-factor')">{{ t('app.shell.menu.twoFactor') }}</UiDropdownItem>
    <UiDropdownItem v-if="withSignOut" @select="signOut">{{ t('app.auth.signOut') }}</UiDropdownItem>
  </UiDropdown>
</template>
