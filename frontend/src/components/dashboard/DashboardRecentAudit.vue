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
 */
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
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
    <UiSectionHeading class="mb-3" :title="t('app.status.audit.title')" />

    <!-- Empty is an ordinary answer on a server nobody has touched yet, and a different one from a
         journal that could not be read — which the store reports as an error instead. -->
    <p v-if="entries.length === 0" class="text-sm text-[var(--t2)]">{{ t('app.status.audit.empty') }}</p>

    <ul v-else class="flex flex-col gap-2">
      <li v-for="entry in entries" :key="entry.id" class="flex items-baseline gap-2 text-sm">
        <span class="shrink-0 font-mono text-xs text-[var(--t2)]">{{ formatIsoTimestamp(entry.occurredAt, localeStore.current) }}</span>
        <span class="text-[var(--t1)]">{{ entry.actionName }}</span>
        <UiBadge v-if="!entry.succeeded" variant="danger">{{ t('app.status.audit.failed') }}</UiBadge>
      </li>
    </ul>

    <RouterLink to="/settings/audit" class="mt-3 inline-block text-sm">{{ t('app.status.audit.all') }}</RouterLink>
  </UiCard>
</template>
