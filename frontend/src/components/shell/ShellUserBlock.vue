<script setup lang="ts">
/**
 * The sidebar footer's identity block: an avatar, the signed-in person's name
 * and their role, laid out as the design canvas draws it — a 24px circle on
 * `--s3` inside a `--b2` border with 10px/600 initials, the name at 12px/500
 * truncating beside it, and the role beneath at 10px in `--t3`.
 *
 * It renders a signed-out state today, and that is not a placeholder awaiting a
 * nicer design: this build has no authentication, no session and no `/me`
 * endpoint, so the panel genuinely does not know who is looking at it. The
 * canvas's "Dana Keller / Owner" is invented sample data; shipping it would put
 * a fictional person's name in front of every real customer (rules/vue.md: the
 * SPA never invents domain data).
 */
import { useI18n } from 'vue-i18n'
import UiIcon from '../ui/UiIcon.vue'

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

/** Props accepted by {@link ShellUserBlock}. */
defineProps<{
  /** The signed-in person, or `null` while the panel has no session to report. */
  user: ShellUser | null
}>()

const { t } = useI18n()
</script>

<template>
  <!-- The design's 24px avatar circle: raised surface, stronger border, and the
       initials only when there genuinely are initials to draw. -->
  <span
    class="grid h-6 w-6 shrink-0 place-items-center rounded-full border border-border-strong bg-surface-3 text-2xs font-semibold text-text-secondary"
    aria-hidden="true"
  >
    <template v-if="user !== null">{{ user.initials }}</template>
    <UiIcon v-else name="user" :size="13" />
  </span>

  <span v-if="user !== null" class="min-w-0 flex-1">
    <span class="block truncate text-xs font-medium text-text-primary">{{ user.name }}</span>
    <span class="block text-2xs text-text-muted">{{ user.role }}</span>
  </span>

  <span v-else class="min-w-0 flex-1 truncate text-2xs text-text-muted">
    {{ t('app.shell.signedOut') }}
  </span>
</template>
