<script setup lang="ts">
/**
 * The sidebar's identity card: the signed-in person's avatar, name and role as the
 * account menu's trigger, and a plain "Sign out" control beneath it.
 *
 * **The name and the menu are ONE control.** A separate identity block beside a dropdown that
 * repeats the same name is a bug at 390px: the dropdown takes half of a 245px footer and leaves
 * the identity block 26px, enough for "r…" and "Adm". Naming someone twice is the mistake;
 * shrinking the type would only hide it. So the trigger's name line is the only place the person
 * is named, and it `truncate`s so a long login shortens instead of pushing the card wider. A
 * line that is cut (the login, or a long role word in another language) carries its whole text as
 * a `title`, so what the card shortens can still be read. The
 * account-menu spec finds the card and that line by `data-testid` (`shell-identity`,
 * `shell-identity-name`) — hooks that say what they are for and survive a restyle.
 *
 * **Sign out is also a visible button, not only a menu entry.** The redesign puts
 * it on the card: ending a session is the one account action a person expects to
 * find without opening anything. The menu keeps its own "Sign out" item (a keyboard
 * user inside the menu should not have to leave it to find the button), and both
 * call the same function.
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
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
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
 * Whether to offer the "my account" entry.
 *
 * Its own question, and the only role test this menu makes: does the signed-in
 * person own a hosting account at all. `/api/v1/accounts/me` 404s for anybody with
 * no account (every administrator included), so hiding the link for
 * `accountId === null` avoids offering a screen that can only answer "not found".
 */
const hasOwnAccount: ComputedRef<boolean> = computed(() => {
  return authStore.user !== null && authStore.user.accountId !== null
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
  <div class="flex min-w-0 flex-col gap-3">
    <!-- Signed in: avatar, name and role are the account menu's trigger, so the card
         names the person once and the menu opens from where a user expects — the
         block showing who they are. -->
    <UiDropdown
      v-if="name !== null"
      class="min-w-0"
      align="start"
      variant="bare"
      :label="name"
      :aria-label="t('app.shell.accountMenu')"
    >
      <template #trigger>
        <ShellAvatar />
        <span class="min-w-0 flex-1 text-left">
          <span class="block truncate text-sm font-bold text-text-primary" data-testid="shell-identity-name" :title="name">{{ name }}</span>
          <span class="block truncate text-xs text-text-muted" :title="role ?? undefined">{{ role }}</span>
        </span>
      </template>

      <!-- Only offered to a person who owns an account: the endpoint behind it answers
           "the account YOU own" and 404s for anybody without one. -->
      <UiDropdownItem v-if="hasOwnAccount" @select="go('my-account')">
        {{ t('app.shell.menu.myAccount') }}
      </UiDropdownItem>
      <UiDropdownItem @select="go('sessions')">{{ t('app.shell.menu.sessions') }}</UiDropdownItem>
      <UiDropdownItem @select="go('two-factor')">{{ t('app.shell.menu.twoFactor') }}</UiDropdownItem>
      <!-- Sessions, two-factor and the audit journal are the only entries on purpose: this
           menu had grown into the place every unlinked screen was put, and none of the
           others (audit, mail, security policy, PHP runtimes) is a setting about the person
           signed in. They are reached from the navigation and from the screens that use them. -->
      <UiDropdownItem destructive @select="signOut">{{ t('app.auth.signOut') }}</UiDropdownItem>
    </UiDropdown>

    <!-- Nobody signed in: there is no account to offer a menu for, so the card is
         plain text beside a neutral mark. -->
    <div v-else class="flex min-w-0 items-center gap-3">
      <ShellAvatar />
      <span class="min-w-0 flex-1 truncate text-sm text-text-muted">{{ t('app.shell.signedOut') }}</span>
    </div>

    <UiButton v-if="name !== null" variant="secondary" class="w-full justify-center" @click="signOut">
      <UiIcon name="logOut" size="md" />
      {{ t('app.auth.signOut') }}
    </UiButton>
  </div>
</template>
