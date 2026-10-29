<script setup lang="ts">
/**
 * What on this server is asking to be looked at: certificates near expiry, backups that failed,
 * addresses the firewall is refusing, panel tasks that failed.
 *
 * **Only the rows that are not zero are shown, and when none of them is, the notice says so in a
 * sentence.** That is the opposite of {@link DashboardCountGrid}, where every tile is always drawn,
 * and the difference is deliberate: a count of accounts is information at any value, while a list of
 * four permanent zeros trains a reader to stop looking at the one card whose whole purpose is to be
 * noticed when it changes.
 *
 * Each row links to the screen that holds the detail. A number on a landing page is only useful if
 * the next click is obvious.
 *
 * **This is the page's notice, and it is drawn first.** The one thing that needs attention goes
 * above everything else as an amber `UiAlert` (a warning is amber, never blue); when nothing does,
 * the same slot says so in a green one, so the reader learns the answer from the same place either
 * way. The rows live inside the notice and are drawn as links, not as a second card, so the sentence
 * and its next click are one object.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiAlert from '../ui/UiAlert.vue'
import UiIcon from '../ui/UiIcon.vue'
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
  <UiAlert v-if="rows.length > 0" variant="warning">
    <p class="font-semibold">{{ t('app.status.attention.title') }}</p>

    <ul class="mt-1.5 flex flex-wrap gap-x-6 gap-y-1">
      <li v-for="row in rows" :key="row.key">
        <RouterLink
          :to="row.to"
          class="inline-flex items-center gap-2 rounded-control text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >
          <span class="font-mono font-semibold text-text-primary tabular-nums">{{ row.value }}</span>
          <span>{{ t(`app.status.attention.${row.key}`) }}</span>
          <UiIcon name="arrowRight" size="sm" />
        </RouterLink>
      </li>
    </ul>
  </UiAlert>

  <UiAlert v-else variant="success">{{ t('app.status.attention.allClear') }}</UiAlert>
</template>
