<script setup lang="ts">
/**
 * What on this server is asking to be looked at: certificates near expiry, backups that failed,
 * addresses the firewall is refusing, panel tasks that failed.
 *
 * **Only the rows that are not zero are shown, and when none of them is, the card says so in a
 * sentence.** That is the opposite of {@link DashboardCountGrid}, where every tile is always drawn,
 * and the difference is deliberate: a count of accounts is information at any value, while a list of
 * four permanent zeros trains a reader to stop looking at the one card whose whole purpose is to be
 * noticed when it changes.
 *
 * Each row links to the screen that holds the detail. A number on a landing page is only useful if
 * the next click is obvious.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiBadge from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DashboardAttention } from '../../types/dashboard'

/** One row: how many, of what, and where to look. */
interface AttentionRow {
  /** Stable key for the list, and the i18n leaf under `app.status.attention`. */
  key: string
  /** How many there are. */
  value: number
  /** The route the row links to. */
  to: string
}

const props = defineProps<{
  /** The attention counts, as the panel answered them. */
  attention: DashboardAttention
}>()

const { t } = useI18n()

/** Every row the panel reported, zeros included, in the order they are judged below. */
const allRows: ComputedRef<AttentionRow[]> = computed(() => {
  return [
    { key: 'certificates', value: props.attention.certificatesExpiringSoon, to: '/sites' },
    { key: 'backups', value: props.attention.failedBackups, to: '/backups' },
    { key: 'tasks', value: props.attention.failedTasks, to: '/tasks' },
    { key: 'bans', value: props.attention.bannedAddresses, to: '/firewall' },
  ]
})

/** The rows worth showing: the ones that are not zero. */
const rows: ComputedRef<AttentionRow[]> = computed(() => {
  return allRows.value.filter((row) => {
    return row.value > 0
  })
})
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-3" :title="t('app.status.attention.title')" />

    <p v-if="rows.length === 0" class="text-sm text-[var(--t2)]">{{ t('app.status.attention.allClear') }}</p>

    <ul v-else class="flex flex-col gap-2">
      <li v-for="row in rows" :key="row.key">
        <RouterLink :to="row.to" class="flex items-center gap-2 text-sm text-[var(--t1)]">
          <UiBadge variant="warning">{{ row.value }}</UiBadge>
          <span>{{ t(`app.status.attention.${row.key}`) }}</span>
        </RouterLink>
      </li>
    </ul>
  </UiCard>
</template>
