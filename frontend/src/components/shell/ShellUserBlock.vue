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
  /**
   * The role this person holds, as a display string.
   *
   * This one is translated by the SPA, not by the backend: the panel reports a
   * machine role (`admin`, `customer`) because the router and this menu branch
   * on it, and the contract carries no localized label beside it. So the two
   * words live in `app.auth.role.*` and are looked up here. That is a
   * documented exception to "the backend owns the text of anything server-side"
   * (rules/vue.md), and it is the SPA's own chrome only for as long as the role
   * set stays these two words; a role the locale files do not know renders as
   * its key. Closing it properly means the panel reporting a display name
   * alongside the machine role — a backend change, noted rather than guessed at
   * here. This comment previously claimed the backend had already localized it,
   * which was never true of any version of this file.
   */
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

/**
 * Whether to offer the administrator-only entries.
 *
 * Asks whether the person is NOT a customer, rather than whether they ARE an
 * administrator, and the difference is the whole point. This menu is
 * presentation and never a gate — the endpoints refuse a customer whatever it
 * shows — so on a role it does not recognise it must err PERMISSIVE and offer
 * the entry (rules/architecture.md: the SPA is never the boundary). The
 * `=== 'admin'` form errs the other way: the day the panel reports a role above
 * administrator (`owner`, `superadmin`), every one of those people silently
 * loses the audit journal, the security policy and the SMTP settings from their
 * menu, with nothing on screen to say why and the backend perfectly willing to
 * serve them. A customer who is wrongly offered a link reads one refusal; an
 * owner who is wrongly denied one cannot find the page at all.
 */
const isAdmin: ComputedRef<boolean> = computed(() => {
  return authStore.user !== null && authStore.user.role !== 'customer'
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
    <!-- Administrator-only for the same presentational reason as the journal above:
         both endpoints refuse a customer whatever this menu shows. -->
    <UiDropdownItem v-if="isAdmin" @select="go('security-policy')">
      {{ t('app.shell.menu.securityPolicy') }}
    </UiDropdownItem>
    <UiDropdownItem v-if="isAdmin" @select="go('smtp-settings')">
      {{ t('app.shell.menu.smtpSettings') }}
    </UiDropdownItem>
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
