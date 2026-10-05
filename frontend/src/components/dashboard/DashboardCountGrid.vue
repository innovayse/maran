<script setup lang="ts">
/**
 * What this server holds: accounts, sites, databases — each a link to the screen that owns it.
 *
 * Numbers and links rather than tables. The landing page's job is to say how much there is and get
 * the reader to the right screen in one click; three embedded tables would be three screens badly,
 * and all three already exist and are better.
 *
 * **A zero renders as a zero**, never as an empty state. A freshly installed server has no accounts,
 * and that is a true and useful answer — a blank panel there reads as "failed to load" on the one
 * screen where it matters most that it does not.
 *
 * Scheduled tasks are absent, and the backend's `DashboardCountsDto` carries the reason: the Cron
 * module keeps no table, so a server-wide count would be one privileged agent call per account.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DashboardCounts } from '../../types/dashboard'

/** One tile: a count, what it counts, and where the detail lives. */
interface CountTile {
  /** Stable key for the list, and the i18n leaf under `app.status.counts`. */
  key: string
  /** The number itself. */
  value: number
  /** The route the tile links to. */
  to: string
}

const props = defineProps<{
  /** The counts, as the panel answered them. */
  counts: DashboardCounts
}>()

const { t } = useI18n()

/**
 * The tiles in the order a reader builds the server up in their head: accounts hold sites, sites
 * hold databases.
 */
const tiles: ComputedRef<CountTile[]> = computed(() => {
  return [
    { key: 'accounts', value: props.counts.accounts, to: '/accounts' },
    { key: 'sites', value: props.counts.sites, to: '/sites' },
    { key: 'databases', value: props.counts.databases, to: '/databases' },
  ]
})
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-3" :title="t('app.status.counts.title')" />

    <div class="grid gap-3 sm:grid-cols-3">
      <RouterLink
        v-for="tile in tiles"
        :key="tile.key"
        :to="tile.to"
        class="rounded-md border border-[var(--b1)] bg-[var(--s2)] px-4 py-3 no-underline transition-colors hover:border-[var(--b2)]"
      >
        <span class="block text-2xl font-semibold text-[var(--t1)]">{{ tile.value }}</span>
        <span class="block text-sm text-[var(--t2)]">{{ t(`app.status.counts.${tile.key}`) }}</span>
      </RouterLink>
    </div>
  </UiCard>
</template>
