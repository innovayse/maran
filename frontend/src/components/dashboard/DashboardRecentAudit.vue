<script setup lang="ts">
/**
 * The last few things that happened on this server, from the audit journal.
 *
 * The journal's own screen is one click away and is where a reader who wants more belongs; this is
 * the glance that tells an operator whether anything happened at all since they last looked.
 *
 * `actionName` is rendered, never `action`: the backend localizes the readable name and falls back
 * to the machine name itself for an action it has no entry for, so this component holds no copy for
 * a server-side thing (rules/vue.md). A failed entry is marked, because a journal in which failures
 * look like successes is a list rather than a journal.
 *
 * Each row is the redesign's list row: an initials avatar for whoever did it, the readable action with
 * what it was done to underneath, and the time in the mono face on the right. The "open the journal"
 * link sits in the section heading's `action` slot, as "View all" does in the approved mockup.
 */
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiAvatar from '../ui/UiAvatar.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AuditEvent } from '../../types/audit'

defineProps<{
  /** The newest entries, as the panel answered them — already the few this card shows. */
  entries: AuditEvent[]
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('app.status.audit.title')">
      <template #action>
        <RouterLink
          to="/settings/audit"
          class="rounded-control text-accent-text transition-colors hover:underline focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >{{ t('app.status.audit.all') }}</RouterLink>
      </template>
    </UiSectionHeading>

    <!-- Empty is an ordinary answer on a server nobody has touched yet, and a different one from a
         journal that could not be read — which the store reports as an error instead. -->
    <p v-if="entries.length === 0" class="text-sm text-text-secondary">{{ t('app.status.audit.empty') }}</p>

    <ul v-else class="m-0 list-none p-0">
      <li
        v-for="entry in entries"
        :key="entry.id"
        class="flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-border-subtle py-3 first:border-t-0 first:pt-0 last:pb-0"
      >
        <UiAvatar :name="entry.actorUsername" size="sm" />

        <!-- `basis-40` (10rem) is the least width the text is ever given: the row WRAPS, so when the
             time and the badge would squeeze the text below that they drop under it instead of
             forcing the text into a letter-per-line column or off the card's edge. `break-words`
             and not `wrap-anywhere`: the latter makes the minimum width one letter, which is what
             let the flex algorithm starve this column in the first place. -->
        <div class="min-w-0 flex-1 basis-40">
          <p class="m-0 text-sm font-semibold text-text-primary break-words">{{ entry.actionName }}</p>
          <p v-if="entry.subject !== ''" class="m-0 text-xs text-text-muted break-words">{{ entry.subject }}</p>
        </div>

        <div class="ml-auto flex max-w-full flex-wrap items-center justify-end gap-x-2 gap-y-1">
          <time class="font-mono text-xs whitespace-nowrap text-text-muted tabular-nums" :datetime="entry.occurredAt">{{ formatIsoTimestamp(entry.occurredAt, localeStore.current) }}</time>
          <UiBadge v-if="!entry.succeeded" variant="danger">{{ t('app.status.audit.failed') }}</UiBadge>
        </div>
      </li>
    </ul>
  </UiCard>
</template>
