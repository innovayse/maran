<script setup lang="ts">
/**
 * The sidebar footer's identity block: the signed-in person's avatar, name and
 * role, drawn as the design canvas draws them — a 24px circle on `--s3` inside
 * a `--b2` border with the initials, the name truncating beside it and the role
 * beneath it in `--t3`.
 *
 * The whole block is ONE control: it is the account menu's trigger, so the
 * person is named exactly once. It used to be an identity block plus a separate
 * dropdown that repeated the same name, and in the 390px drawer that duplicate
 * was fatal — the dropdown took 123px of a 245px footer and left the `flex-1`
 * identity block 26px, enough for "r…" and "Adm". Naming someone twice was the
 * mistake; shrinking the type would only have hidden it.
 *
 * The person comes from the auth store, which holds what the backend reported at
 * sign-in. The canvas's "Dana Keller / Owner" is invented sample data and is not
 * used: a fictional name in front of a real customer is worse than no name
 * (rules/vue.md: the SPA never invents domain data). When nobody is signed in the
 * block says so and offers no menu, which on this shell only happens for the
 * moment before the session is restored.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import { useAuthStore } from '../../stores/auth'

/**
 * The signed-in person, as the sidebar needs to show them.
 *
 * Shaped for the user the panel will report once authentication lands, not for
 * today's absence of one: the fields are exactly what the footer draws, all
 * three already produced by the backend — the initials included, because
 * deriving them in the browser means guessing which part of a name is the
 * family name, and that guess is wrong in most of the world.
 */
export interface ShellUser {
  /** Short initials for the avatar, as the backend produced them. */
  initials: string
  /** Display name, shown on one truncating line. */
  name: string
  /** The role this person holds, already localized by the backend. */
  role: string
}

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()

/**
 * The signed-in person in the shape the footer draws, or `null` when there is none.
 *
 * The initials are taken from the username rather than a display name the panel does
 * not have: a login name is chosen by its owner and its first characters are theirs,
 * where splitting a full name into given and family parts guesses wrong in most of
 * the world.
 */
const user: ComputedRef<ShellUser | null> = computed(() => {
  const signedIn = authStore.user
  if (signedIn === null) {
    return null
  }

  return {
    initials: signedIn.username.slice(0, 2).toUpperCase(),
    name: signedIn.username,
    role: t(`app.auth.role.${signedIn.role}`),
  }
})

/** Whether the signed-in person is an administrator, used to decide what the menu offers. */
const isAdmin: ComputedRef<boolean> = computed(() => {
  return authStore.user?.role === 'admin'
})

/**
 * Opens one of the account pages.
 * @param name The route name to navigate to.
 * @returns Resolves once the navigation has settled.
 */
const go = async (name: string): Promise<void> => {
  await router.push({ name })
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
  <!-- Signed in: avatar, name and role are the account menu's trigger, so the
       footer names the person once and the menu opens from where a user
       expects — the block showing who they are. -->
  <UiDropdown
    v-if="user !== null"
    class="min-w-0 flex-1"
    align="start"
    variant="bare"
    :label="user.name"
    :aria-label="t('app.shell.accountMenu')"
  >
    <template #trigger>
      <!-- The design's 24px avatar circle: raised surface, stronger border. -->
      <span
        class="grid h-6 w-6 shrink-0 place-items-center rounded-full border border-border-strong bg-surface-3 text-sm font-semibold text-text-secondary"
        aria-hidden="true"
      >
        {{ user.initials }}
      </span>
      <span class="min-w-0 flex-1 text-left">
        <span class="block truncate text-base font-medium text-text-primary">{{ user.name }}</span>
        <span class="block truncate text-base text-text-muted">{{ user.role }}</span>
      </span>
    </template>

    <!-- Sessions, two-factor and the audit journal are real pages with real tests, and until
         this menu existed the only way to reach any of them was to type its URL: a screen
         nothing links to is a screen nobody has. -->
    <UiDropdownItem @select="go('sessions')">{{ t('app.shell.menu.sessions') }}</UiDropdownItem>
    <UiDropdownItem @select="go('two-factor')">{{ t('app.shell.menu.twoFactor') }}</UiDropdownItem>
    <!-- Hidden from a customer because the journal is an administrator's page and a link that
         only ever answers 403 is a worse answer than no link. This is presentation, not
         authorization: the endpoint refuses a customer whatever this menu shows. -->
    <UiDropdownItem v-if="isAdmin" @select="go('audit')">{{ t('app.shell.menu.audit') }}</UiDropdownItem>
    <UiDropdownItem destructive @select="signOut">{{ t('app.auth.signOut') }}</UiDropdownItem>
  </UiDropdown>

  <!-- Nobody signed in: there is no account to offer a menu for, so the block is
       plain text beside a generic mark. -->
  <template v-else>
    <span
      class="grid h-6 w-6 shrink-0 place-items-center rounded-full border border-border-strong bg-surface-3 text-text-secondary"
      aria-hidden="true"
    >
      <UiIcon name="user" size="md" />
    </span>
    <span class="min-w-0 flex-1 truncate text-base text-text-muted">
      {{ t('app.shell.signedOut') }}
    </span>
  </template>
</template>
