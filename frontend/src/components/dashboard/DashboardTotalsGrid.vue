<script setup lang="ts">
/**
 * What this server holds: accounts, sites and databases. Each tile is ONE link to the screen that owns the thing, so a number is never a
 * dead end; a module the licence does not permit draws its tile without the link rather than one
 * that ends at the upgrade page.
 *
 * The numbers arrive on the live `totals` frame and change without a reload. While they are on
 * their way (`counts` is `null`) the tiles are drawn already, with their real icons and labels and a
 * placeholder in the figure's own line, so the figures land without moving anything. A zero renders
 * as a zero (a fresh server has no accounts, and that is a true and useful answer), and a count the
 * panel could not take renders as "Unavailable", never as a zero.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useModulesStore } from '../../stores/modules'
import type { DashboardCounts, DashboardSection } from '../../types/dashboard'
import UiCard from '../ui/UiCard.vue'
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'

/** One tile before any count: what it counts, where the detail lives and which module owns it. */
interface TileSpec {
  /** Which count the tile shows; the list key and the label's key under `app.status.counts`. */
  key: keyof DashboardCounts
  /** The route the tile links to. */
  to: string
  /** The glyph in the tile's icon square. */
  icon: UiIconName
}

const props = defineProps<{
  /** The counts as last answered; `null` is the one state in which they are still on their way. */
  counts: DashboardCounts | null
}>()

/** The tiles in the order a reader builds the server up in their head: accounts hold sites, sites hold databases. */
const TILES: readonly TileSpec[] = [
  { key: 'accounts', to: '/accounts', icon: 'users' },
  { key: 'sites', to: '/sites', icon: 'earth' },
  { key: 'databases', to: '/databases', icon: 'database' },
]

const { t } = useI18n()
const modules = useModulesStore()

/** The tiles with their counts and links. */
const tiles: ComputedRef<(TileSpec & { value: DashboardSection<number> | undefined; linked: boolean })[]> = computed(() => {
  return TILES.map((spec) => {
    return { ...spec, value: props.counts?.[spec.key], linked: modules.isEnabled(spec.key) }
  })
})
</script>

<template>
  <section>
    <UiSectionHeading class="mb-3" :title="t('app.status.counts.title')" />

    <!-- Two columns in a narrow content panel, three from `@xl` (36rem of CONTAINER width). The
         third tile spans the narrow second row so an odd count leaves no hole beside it. -->
    <div class="@container">
      <div class="grid grid-cols-2 gap-4 @xl:grid-cols-3">
        <UiCard
          v-for="tile in tiles"
          :key="tile.key"
          :to="tile.linked ? tile.to : undefined"
          class="last:col-span-2 @xl:p-5 @xl:last:col-span-1"
          :data-testid="`totals-tile-${tile.key}`"
        >
          <UiIconTile :name="tile.icon" />
          <span class="mt-3.5 block font-display text-2xl leading-none font-black tabular-nums @xl:text-3xl">
            <span v-if="counts === null" class="inline-flex w-12 align-middle">
              <UiSkeleton shape="line" size="lg" />
            </span>
            <template v-else-if="tile.value !== undefined">{{
              tile.value.state === 'loaded' ? tile.value.data : t('app.status.counts.unavailable')
            }}</template>
          </span>
          <span class="mt-2 block font-display text-xs font-extrabold text-text-secondary break-words">{{
            t(`app.status.counts.${tile.key}`)
          }}</span>
        </UiCard>
      </div>
    </div>
  </section>
</template>
