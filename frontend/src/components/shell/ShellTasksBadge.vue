<script setup lang="ts">
/**
 * The app bar's running-tasks badge: how much background work the panel has in flight
 * right now, on every screen.
 *
 * **It is a view of the tasks store, not a second source of truth.** The count is a computed over
 * the store's running tasks, which every stream frame merges into — the same frames that move the
 * tasks page's rows — so the number rises while the operator is somewhere else entirely, with no
 * navigation and no reload.
 *
 * The stream itself is not opened here. The shell opens it once, and only for the roles that may call
 * it, through `useShellLiveReads` (issue #415): a badge that opened its own stream would ask for it
 * on every screen whatever the role, and the panel refuses it to some of them.
 *
 * Nothing is drawn when nothing is running. A badge reading "0" is a control that is never right
 * about anything, and the sidebar already carries the way in to the feed.
 *
 * **The link's text is the number and nothing else.** The tasks spec asserts the link's text is
 * exactly the count, so the explanation ("N background tasks running") lives in the accessible
 * name, never in visible or screen-reader-only text. The count is white on `--acf` (5.14:1).
 *
 * A read that fails needs no handling here: the store keeps nothing to count, and the badge is
 * silent rather than an error shouted on every screen.
 */
import { useI18n } from 'vue-i18n'
import UiIcon from '../ui/UiIcon.vue'
import { useTasksStore } from '../../stores/tasks'

const { t } = useI18n()
const store = useTasksStore()
</script>

<template>
  <RouterLink
    v-if="store.runningCount > 0"
    :to="{ name: 'tasks' }"
    class="flex h-control shrink-0 items-center gap-2 rounded-full px-3 text-text-secondary transition-colors hover:bg-surface-2 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
    :aria-label="t('tasks.badge.label', { count: store.runningCount }, store.runningCount)"
  >
    <UiIcon name="listChecks" size="lg" />
    <span
      class="min-w-6 rounded-full bg-accent-fill px-1.5 text-center text-xs font-bold text-white tabular-nums"
    >{{ store.runningCount }}</span>
  </RouterLink>
</template>
