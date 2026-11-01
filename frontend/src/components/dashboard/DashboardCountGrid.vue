<script setup lang="ts">
/**
 * What this server holds: accounts, sites, databases — each a link to the screen that owns it.
 *
 * Numbers and links rather than tables. The landing page's job is to say how much there is and get
 * the reader to the right screen in one click; three embedded tables would be three screens badly,
 * and all three already exist and are better.
 *
 * Each tile is the redesign's figure card: an accent icon square, the number in the display face and
 * a small label. The tile is a LINK, so it is drawn here as a panel (lifting 2px and taking
 * an accent border on hover) rather than as a `UiCard`, which is a `div` and cannot be one. Its
 * accessible name is the number followed by the noun ("3 Accounts"), which is also what tells it
 * apart from the sidebar's own link to the same screen. The label wraps
 * anywhere, so a long Armenian noun stays inside a two-column tile at 390px.
 *
 * **A zero renders as a zero**, never as an empty state. A freshly installed server has no accounts,
 * and that is a true and useful answer — a blank panel there reads as "failed to load" on the one
 * screen where it matters most that it does not.
 *
 * Scheduled tasks are absent, and the backend's `DashboardCountsDto` carries the reason: the Cron
 * module keeps no table, so a server-wide count would be one privileged agent call per account.
 *
 * **While the counts are on their way** (`loading`, no `counts`), the tiles are drawn already, one
 * per entry of {@link TILES}, with their real icons, labels and links, and the figure draws a
 * placeholder in its own line, so the counts land without moving anything.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import type { DashboardCounts, DashboardSection } from '../../types/dashboard'

/** One tile as the grid knows it before any count: what it counts and where the detail lives. */
interface TileSpec {
  /** Which count the tile shows; the list's key and the i18n leaf under `app.status.counts`. */
  key: keyof DashboardCounts
  /** The route the tile links to. */
  to: string
  /** The glyph in the tile's icon square. */
  icon: UiIconName
}

/** One tile as drawn: its spec and, once the counts are in, its count. */
interface CountTile extends TileSpec {
  /** The count, or the fact that it could not be taken; absent while the counts are loading. */
  value?: DashboardSection<number>
}

/** Props accepted by {@link DashboardCountGrid}. */
const props = defineProps<{
  /** The counts, as the panel answered them; absent only while `loading`. */
  counts?: DashboardCounts
  /** Whether the counts are still on their way; see the component note. */
  loading?: boolean
}>()

/**
 * The tiles in the order a reader builds the server up in their head: accounts hold sites, sites
 * hold databases. The one list of tiles, loading or loaded.
 */
const TILES: readonly TileSpec[] = [
  { key: 'accounts', to: '/accounts', icon: 'users' },
  { key: 'sites', to: '/sites', icon: 'earth' },
  { key: 'databases', to: '/databases', icon: 'database' },
]

const { t } = useI18n()

/** The tiles with their counts, once the counts are in. */
const tiles: ComputedRef<CountTile[]> = computed(() => {
  return TILES.map((spec) => {
    return { ...spec, value: props.counts?.[spec.key] }
  })
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
        class="block min-w-0 rounded-panel border border-border-subtle bg-surface-1 p-4 text-text-primary transition duration-300 last:col-span-2 hover:-translate-y-0.5 hover:border-accent/45 focus-visible:shadow-focus focus-visible:outline-none @xl:p-5 @xl:last:col-span-1"
      >
        <UiIconTile :name="tile.icon" />
        <!-- While loading, the placeholder sits in the figure's own line, which gives it the
             figure's height. -->
        <span class="mt-3.5 block font-display text-2xl leading-none font-black @xl:text-3xl">
          <span v-if="loading" class="inline-flex w-12 align-middle">
            <UiSkeleton shape="line" size="lg" />
          </span>
          <template v-else-if="tile.value !== undefined">{{
            tile.value.state === 'loaded' ? tile.value.data : t('app.status.counts.unavailable')
          }}</template>
        </span>
        <span class="mt-2 block font-display text-xs font-extrabold text-text-secondary break-words">{{ t(`app.status.counts.${tile.key}`) }}</span>
      </RouterLink>
    </div>
    </div>
  </section>
</template>
