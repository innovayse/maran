<script setup lang="ts">
/**
 * The sidebar's identity card: the signed-in person's avatar, name and role as the
 * account menu's trigger, and an icon-only "Sign out" button at its right edge.
 *
 * **The name and the menu are ONE control.** A separate identity block beside a dropdown that
 * repeats the same name is a bug at 390px: the dropdown takes half of a 245px footer and leaves
 * the identity block 26px, enough for "r…" and "Adm". Naming someone twice is the mistake;
 * shrinking the type would only hide it. So the trigger's name line is the only place the person
 * is named, and it `truncate`s so a long login shortens instead of pushing the card wider. A
 * login that is cut carries its whole text as a `title`. The role line WRAPS instead of being cut
 * (issue #221): "Ադմինիստրատոր" does not fit beside the avatar, and a role read as "Ադմինիստրա…"
 * is a role nobody can read. The
 * account-menu spec finds the card and that line by `data-testid` (`shell-identity`,
 * `shell-identity-name`) — hooks that say what they are for and survive a restyle.
 *
 * **Sign out is a visible icon button, and only that.** Ending a session is the one account action a
 * person expects to find without opening anything, so it is on the card, in the place the menu's
 * chevron used to take (issue #510): a full-width button under the card cost a row of the sidebar
 * and said what the icon says. The menu used to repeat it directly above the button (issue #210);
 * the copy is gone, so the menu is three rows and covers less of the navigation when it opens
 * upwards. The icon carries the translated name and tooltip, since it has no text.
 *
 * The person comes from the auth store, which holds what the backend reported at
 * sign-in. The role is translated here (`app.auth.role.*`) because the contract
 * carries a machine role (`admin`, `customer`) and no localized label beside it —
 * a documented exception to "the backend owns server-side text" that holds only
 * while the role set stays these two words; an unknown role renders as its key.
 * When nobody is signed in the card says so and offers no menu, which on this
 * shell only happens for the moment before the session is restored.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import ShellAccountMenu from './ShellAccountMenu.vue'
import ShellAvatar from './ShellAvatar.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()

/** The login name, or `null` when nobody is signed in. */
const name: ComputedRef<string | null> = computed(() => {
  return authStore.user?.username ?? null
})

/** The signed-in person's role as a display string, or `null` when nobody is signed in. */
const role: ComputedRef<string | null> = computed(() => {
  const signedIn = authStore.user
  return signedIn === null ? null : t(`app.auth.role.${signedIn.role}`)
})

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
  <div class="flex min-w-0 items-center gap-1">
    <!-- Signed in: avatar, name and role are the account menu's trigger, so the card
         names the person once and the menu opens from where a user expects — the
         block showing who they are. The chevron is off: the sign-out button below takes its place. -->
    <ShellAccountMenu
      v-if="name !== null"
      class="min-w-0 flex-1"
      :chevron="false"
      :trigger-label="t('app.shell.accountMenu')"
    >
      <template #trigger>
        <ShellAvatar />
        <span class="min-w-0 flex-1 text-left">
          <span
            class="block truncate text-sm font-bold text-text-primary"
            data-testid="shell-identity-name"
            :title="name"
            >{{ name }}</span
          >
          <span class="block text-xs wrap-anywhere text-text-muted">{{ role }}</span>
        </span>
      </template>
    </ShellAccountMenu>

    <!-- Nobody signed in: there is no account to offer a menu for, so the card is
         plain text beside a neutral mark. -->
    <div v-else class="flex min-w-0 items-center gap-3">
      <ShellAvatar />
      <span class="min-w-0 flex-1 truncate text-sm text-text-muted">{{
        t('app.shell.signedOut')
      }}</span>
    </div>

    <UiButton
      v-if="name !== null"
      variant="ghost"
      size="icon"
      class="shrink-0"
      :aria-label="t('app.auth.signOut')"
      :title="t('app.auth.signOut')"
      @click="signOut"
    >
      <UiIcon name="logOut" size="md" />
    </UiButton>
  </div>
</template>
