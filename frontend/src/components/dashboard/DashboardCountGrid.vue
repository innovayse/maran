<script setup lang="ts">
/**
 * What this server holds: accounts, sites, databases — each a link to the screen that owns it.
 *
 * Numbers and links rather than tables. The landing page's job is to say how much there is and get
 * the reader to the right screen in one click; three embedded tables would be three screens badly,
 * and all three already exist and are better.
 *
 * Each tile is the redesign's figure card: an accent icon square, the number in the display face and
 * a small uppercase label. The tile is a LINK, so it is drawn here as a panel (lifting 2px and taking
 * an accent border on hover) rather than as a `UiCard`, which is a `div` and cannot be one. Its
 * accessible name is the number followed by the noun ("3 Accounts"), which is also what tells it
 * apart from the sidebar's own link to the same screen. The label is uppercase by CSS and wraps
 * anywhere, so a long Armenian noun stays inside a two-column tile at 390px.
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
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
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
  /** The glyph in the tile's icon square. */
  icon: UiIconName
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
    { key: 'accounts', value: props.counts.accounts, to: '/accounts', icon: 'users' },
    { key: 'sites', value: props.counts.sites, to: '/sites', icon: 'earth' },
    { key: 'databases', value: props.counts.databases, to: '/databases', icon: 'database' },
  ]
})
</script>

<template>
  <section>
    <UiSectionHeading class="mb-3" :title="t('app.status.counts.title')" />

    <!-- Two columns in a narrow content panel, three from `@xl` (36rem of CONTAINER width, not
         viewport: a large system font squeezes the panel on a wide screen, and three tiles in it
         broke their labels mid-word). The third tile spans the narrow second row so an odd count
         does not leave a hole beside it. -->
    <div class="@container">
    <div class="grid grid-cols-2 gap-4 @xl:grid-cols-3">
      <RouterLink
        v-for="tile in tiles"
        :key="tile.key"
        :to="tile.to"
        class="block min-w-0 rounded-panel border border-border-subtle bg-surface-1 p-4 text-text-primary transition duration-300 last:col-span-2 hover:-translate-y-0.5 hover:border-accent/45 focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none motion-reduce:hover:translate-y-0 @xl:p-5 @xl:last:col-span-1"
      >
        <UiIconTile :name="tile.icon" />
        <span class="mt-3.5 block font-display text-2xl leading-none font-black @xl:text-3xl">{{ tile.value }}</span>
        <span class="mt-2 block font-display text-xs font-extrabold tracking-caps text-text-secondary uppercase break-words">{{ t(`app.status.counts.${tile.key}`) }}</span>
      </RouterLink>
    </div>
    </div>
  </section>
</template>
