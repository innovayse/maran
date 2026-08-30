<script setup lang="ts">
/**
 * The sidebar footer's identity block: an avatar, the signed-in person's name
 * and their role, laid out as the design canvas draws it — a 24px circle on
 * `--s3` inside a `--b2` border with 10px/600 initials, the name at 12px/500
 * truncating beside it, and the role beneath at 10px in `--t3`.
 *
 * The person comes from the auth store, which holds what the backend reported at
 * sign-in. The canvas's "Dana Keller / Owner" is invented sample data and is not
 * used: a fictional name in front of a real customer is worse than no name
 * (rules/vue.md: the SPA never invents domain data). When nobody is signed in the
 * block says so, which on this shell only happens for the moment before the
 * session is restored.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiButton from '../ui/UiButton.vue'
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
  <!-- The design's 24px avatar circle: raised surface, stronger border, and the
       initials only when there genuinely are initials to draw. -->
  <span
    class="grid h-6 w-6 shrink-0 place-items-center rounded-full border border-border-strong bg-surface-3 text-sm font-semibold text-text-secondary"
    aria-hidden="true"
  >
    <template v-if="user !== null">{{ user.initials }}</template>
    <UiIcon v-else name="user" :size="13" />
  </span>

  <span v-if="user !== null" class="min-w-0 flex-1">
    <span class="block truncate text-base font-medium text-text-primary">{{ user.name }}</span>
    <span class="block text-base text-text-muted">{{ user.role }}</span>
  </span>

  <span v-else class="min-w-0 flex-1 truncate text-base text-text-muted">
    {{ t('app.shell.signedOut') }}
  </span>

  <UiButton
    v-if="user !== null"
    variant="ghost"
    :aria-label="t('app.auth.signOut')"
    :title="t('app.auth.signOut')"
    @click="signOut"
  >
    <UiIcon name="logOut" :size="15" />
  </UiButton>
</template>
