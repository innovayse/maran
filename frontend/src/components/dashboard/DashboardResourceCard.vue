<script setup lang="ts">
/**
 * What the machine is doing right now: processor, memory, disk and the three load averages.
 *
 * A LIVE reading, not the newest stored sample — the panel asks the agent for it, so this card says
 * something on a server whose sampler has not yet run, where the monitoring charts are still empty.
 *
 * The processor and memory bars are {@link UiMeter}, the kit primitive, so the accessible name and
 * the announced value are decided once for the whole panel rather than per screen. The processor is
 * a PERCENTAGE and memory a byte pair, so the processor's bar is given a max of 100 explicitly
 * rather than being passed a total it does not have.
 *
 * **The disk tile's bar has three parts, not one** (issue #77): used (`df`'s Used), reserved for the
 * system (the filesystem's root reserve, 0 on xfs) and available (`df`'s Avail), which sum to the
 * size. Its big figure stays "used / size"; under it a {@link UiStackedMeter} prints all three with
 * their labels, built by `useDiskSegments` — the same composable the monitoring screen's disk card
 * uses, so the two screens cannot describe the disk differently.
 *
 * **Every bar carries its label and its figure as visible text.** A bar's `label` and `valueText`
 * reach assistive technology through `aria-label` and `aria-valuetext` and are invisible on screen,
 * so a card that passed them and stopped would show an operator three unlabelled stripes. The tile
 * prints the label and the reading itself, and tells `UiMeter` not to print its own percentage
 * (`:show-value="false"`) so the figure is not drawn twice.
 *
 * **The card is three figure tiles under one heading, not one panel with three stripes.** Each tile
 * is a `UiCard` (its own `p-5` padding is left alone — a second padding class on the card would lose to it) in the redesign's figure-card form: an accent icon square, the reading in the display
 * face, a small label and the bar. The tiles are NOT links: the monitoring screen belongs
 * to a licensed module, and a figure that led to the upgrade page would be a worse answer than one
 * that is simply a figure. The bars sit at the bottom of their tiles so the three line up whatever
 * the tiles above them hold.
 *
 * **The network counters the payload carries are deliberately not shown.** They are totals since
 * boot, not rates, and "4.2 TB received" on a landing page reads as traffic while meaning uptime.
 * A rate needs two readings and the panel does not offer one here, so this card shows nothing
 * rather than something that would be misread.
 *
 * **While the reading is on its way** (`loading`, no `resources`), the three tiles are drawn with
 * their real icons and labels, and each figure, the load averages and each bar draw a placeholder
 * in their own place, so the reading lands without moving anything.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiMeter from '../ui/UiMeter.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import UiStackedMeter from '../ui/UiStackedMeter.vue'
import { useDiskSegments } from '../../composables/useDiskSegments'
import { formatBytes } from '../../utils/formatBytes'
import type { HostMetrics } from '../../types/monitoring'

const props = defineProps<{
  /** The host's live reading, as the panel answered it; absent only while `loading`. */
  resources?: HostMetrics
  /** Whether the reading is still on its way; see the component note. */
  loading?: boolean
}>()

const { t } = useI18n()
const disk = useDiskSegments(() => {
  return props.resources
})

/**
 * The processor figure as a reader sees it, to one decimal.
 *
 * `toFixed`, like every other decimal in this SPA (`formatBytes`, `UiChart`, `MonitoringCharts`):
 * these are technical readings an operator compares against `top`, and a locale-grouped percentage
 * would stop matching the tool it is being checked against.
 */
const cpuText: ComputedRef<string> = computed(() => {
  if (props.resources === undefined) {
    return ''
  }
  return `${props.resources.cpuPercent.toFixed(1)}${t('monitoring.units.percent')}`
})

/** Memory in use beside what is installed, as one sentence for assistive technology. */
const memoryText: ComputedRef<string> = computed(() => {
  if (props.resources === undefined) {
    return ''
  }
  return t('app.status.resources.ratio', {
    used: formatBytes(props.resources.memoryUsedBytes),
    total: formatBytes(props.resources.memoryTotalBytes),
  })
})

/** What a single-figure tile's {@link UiMeter} draws. */
interface ReadingMeter {
  /** How much is used, in the same unit as {@link ReadingMeter.max}. */
  value: number
  /** The allowance, in the same unit as {@link ReadingMeter.value}. */
  max: number
  /** The whole reading as one sentence, announced by the bar. */
  text: string
}

/** One tile: what it measures, the figure a reader sees, and how its bar is drawn. */
interface Reading {
  /** Stable key for the list. */
  key: string
  /** The glyph in the tile's icon square. */
  icon: UiIconName
  /** The measure's name, already translated. */
  label: string
  /** The large figure: the reading itself. */
  figure: string
  /** The small figure beside it — what it is out of — or `null` when the reading stands alone. */
  of: string | null
  /**
   * The one-part bar under the figure, or `null` for the disk tile, which draws its three parts
   * (used, reserved for the system, available) with {@link UiStackedMeter} instead.
   */
  meter: ReadingMeter | null
}

/**
 * The three tiles, in the order an operator triages them: the processor says what is happening now,
 * memory says whether it can continue, and the disk says whether it can continue tomorrow.
 */
const readings: ComputedRef<Reading[]> = computed(() => {
  // Before the reading arrives every tile still exists, labelled, with nothing measured: the
  // template draws placeholders for the figures and bars while `loading`.
  const resources = props.resources
  return [
    {
      key: 'cpu',
      icon: 'cpu',
      label: t('monitoring.charts.cpu'),
      figure: cpuText.value,
      of: null,
      meter: {
        value: resources?.cpuPercent ?? 0,

        // 100 explicitly: a percentage carries no total of its own, and passing one of the byte
        // figures here would draw a bar at a ratio that means nothing.
        max: 100,
        text: cpuText.value,
      },
    },
    {
      key: 'memory',
      icon: 'memoryStick',
      label: t('monitoring.charts.memory'),
      figure: resources === undefined ? '' : formatBytes(resources.memoryUsedBytes),
      of: resources === undefined ? null : `/ ${formatBytes(resources.memoryTotalBytes)}`,
      meter: {
        value: resources?.memoryUsedBytes ?? 0,
        max: resources?.memoryTotalBytes ?? 0,
        text: memoryText.value,
      },
    },
    {
      key: 'disk',
      icon: 'hardDrive',
      label: t('monitoring.charts.disk'),
      figure: resources === undefined ? '' : formatBytes(resources.diskUsedBytes),
      of: resources === undefined ? null : `/ ${formatBytes(resources.diskTotalBytes)}`,
      meter: null,
    },
  ]
})

/**
 * The three load averages as one line.
 *
 * All three and not only the first: one minute alone cannot tell a spike from a trend, and the
 * trend is the thing an operator glancing at a landing page actually wants.
 */
const loadText: ComputedRef<string> = computed(() => {
  if (props.resources === undefined) {
    return ''
  }
  return [
    props.resources.loadAverage1m,
    props.resources.loadAverage5m,
    props.resources.loadAverage15m,
  ]
    .map((average) => {
      return average.toFixed(2)
    })
    .join(' · ')
})
</script>

<template>
  <section>
    <UiSectionHeading class="mb-3" :title="t('app.status.resources.title')" />

    <!-- Three tiles side by side only from `@xl` (36rem of CONTAINER width). The tiles' own
         layout switches below read the same container, so the grid and the tile never disagree:
         under a large system font the content panel is narrow on a wide viewport, and a viewport
         `sm:` here put three tiles in it, spilling "37.4%" past its tile. -->
    <div class="@container">
    <div class="grid gap-4 @xl:grid-cols-3">
      <UiCard v-for="reading in readings" :key="reading.key" class="flex flex-col">
        <!-- Under `@xl` the icon sits BESIDE the reading so a one-column tile is two short rows
             (reading, bar) rather than four stacked ones; from `@xl` up the tile is narrow, and the
             icon goes back above the reading. -->
        <div class="flex items-center gap-3 @xl:block">
          <UiIconTile :name="reading.icon" />

          <div class="min-w-0 @xl:mt-3.5">
            <!-- While loading, the placeholder's box is the figure's line height (`leading-none`:
                 the font size, 24px, and 30px from `@xl`), so the row keeps its height. -->
            <p class="m-0 flex flex-wrap items-baseline gap-x-1.5 font-display text-2xl leading-none font-black @xl:text-3xl">
              <span v-if="loading" class="flex h-6 w-20 items-center @xl:h-7.5"><UiSkeleton shape="line" size="lg" /></span>
              <span v-else>{{ reading.figure }}</span>
              <small v-if="reading.of !== null" class="text-sm font-extrabold whitespace-nowrap text-text-muted">{{ reading.of }}</small>
            </p>
            <!-- `text-sm` on a phone: the display face at `text-xs`, and in Armenian
                 above all, reads as a smudge beside the figure. The narrow three-up tiles from
                 `sm` up keep `text-xs`, where a larger label would wrap in every language. -->
            <p class="m-0 mt-2 font-display text-sm font-extrabold text-text-secondary break-words @xl:text-xs">{{ reading.label }}</p>
          </div>
        </div>

        <!-- The processor tile also carries the load averages, so the three tiles stay three and
             the trend sits beside the instant it explains. -->
        <p v-if="reading.key === 'cpu'" class="m-0 mt-2 text-xs text-text-muted">
          <span>{{ t('app.status.resources.load') }}</span>
          <span v-if="loading" class="ml-1.5 inline-flex w-24 align-middle"><UiSkeleton shape="line" size="sm" /></span>
          <span v-else class="ml-1.5 font-mono text-text-secondary tabular-nums">{{ loadText }}</span>
        </p>

        <div class="mt-auto pt-3.5">
          <!-- The placeholder is clipped to the bar's own track height; the disk tile also holds
               a line for each of its three legend rows, so the reading lands without moving. -->
          <template v-if="loading">
            <span class="block h-1 overflow-hidden rounded-full"><UiSkeleton shape="line" size="sm" /></span>
            <div v-if="reading.meter === null" class="mt-3 flex flex-col gap-1.5">
              <UiSkeleton v-for="row in 3" :key="row" shape="line" size="sm" />
            </div>
          </template>
          <UiMeter
            v-else-if="reading.meter !== null"
            :value="reading.meter.value"
            :max="reading.meter.max"
            :label="reading.label"
            :value-text="reading.meter.text"
            :show-value="false"
          />
          <UiStackedMeter v-else :segments="disk.segments.value" :label="disk.summary.value" />
        </div>
      </UiCard>
    </div>
    </div>
  </section>
</template>
