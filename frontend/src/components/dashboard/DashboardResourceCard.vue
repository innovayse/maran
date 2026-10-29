<script setup lang="ts">
/**
 * What the machine is doing right now: processor, memory, disk and the three load averages.
 *
 * A LIVE reading, not the newest stored sample — the panel asks the agent for it, so this card says
 * something on a server whose sampler has not yet run, where the monitoring charts are still empty.
 *
 * The bars are {@link UiMeter}, the kit primitive, so the accessible name and the announced value
 * are decided once for the whole panel rather than per screen. The processor is a PERCENTAGE and the
 * other two are byte pairs, so the processor's bar is given a max of 100 explicitly rather than
 * being passed a total it does not have.
 *
 * **Every bar carries its label and its figure as visible text.** A bar's `label` and `valueText`
 * reach assistive technology through `aria-label` and `aria-valuetext` and are invisible on screen,
 * so a card that passed them and stopped would show an operator three unlabelled stripes. The tile
 * prints the label and the reading itself, and tells `UiMeter` not to print its own percentage
 * (`:show-value="false"`) so the figure is not drawn twice.
 *
 * **The card is three figure tiles under one heading, not one panel with three stripes.** Each tile
 * is a `UiCard` (its own `p-5` padding is left alone — a second padding class on the card would lose to it) in the redesign's figure-card form: an accent icon square, the reading in the display
 * face, a small uppercase label and the bar. The tiles are NOT links: the monitoring screen belongs
 * to a licensed module, and a figure that led to the upgrade page would be a worse answer than one
 * that is simply a figure. The bars sit at the bottom of their tiles so the three line up whatever
 * the tiles above them hold.
 *
 * **The network counters the payload carries are deliberately not shown.** They are totals since
 * boot, not rates, and "4.2 TB received" on a landing page reads as traffic while meaning uptime.
 * A rate needs two readings and the panel does not offer one here, so this card shows nothing
 * rather than something that would be misread.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiMeter from '../ui/UiMeter.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { formatBytes } from '../../utils/formatBytes'
import type { DashboardResources } from '../../types/dashboard'

const props = defineProps<{
  /** The host's live reading, as the panel answered it. */
  resources: DashboardResources
}>()

const { t } = useI18n()

/**
 * The processor figure as a reader sees it, to one decimal.
 *
 * `toFixed`, like every other decimal in this SPA (`formatBytes`, `UiChart`, `MonitoringCharts`):
 * these are technical readings an operator compares against `top`, and a locale-grouped percentage
 * would stop matching the tool it is being checked against.
 */
const cpuText: ComputedRef<string> = computed(() => {
  return `${props.resources.cpuPercent.toFixed(1)}${t('monitoring.units.percent')}`
})

/** Memory in use beside what is installed, as one sentence for assistive technology. */
const memoryText: ComputedRef<string> = computed(() => {
  return t('app.status.resources.ratio', {
    used: formatBytes(props.resources.memoryUsedBytes),
    total: formatBytes(props.resources.memoryTotalBytes),
  })
})

/** Disk in use on the root filesystem beside its capacity, as one sentence for assistive technology. */
const diskText: ComputedRef<string> = computed(() => {
  return t('app.status.resources.ratio', {
    used: formatBytes(props.resources.diskUsedBytes),
    total: formatBytes(props.resources.diskTotalBytes),
  })
})

/** One tile: what it measures, how far along it is, and the figure a reader sees. */
interface Reading {
  /** Stable key for the list. */
  key: string
  /** The glyph in the tile's icon square. */
  icon: UiIconName
  /** The measure's name, already translated. */
  label: string
  /** How much is used, in the same unit as {@link Reading.max}. */
  value: number
  /** The allowance, in the same unit as {@link Reading.value}. */
  max: number
  /** The large figure: the reading itself. */
  figure: string
  /** The small figure beside it — what it is out of — or `null` when the reading stands alone. */
  of: string | null
  /** The whole reading as one sentence, announced by the bar. */
  text: string
}

/**
 * The three tiles, in the order an operator triages them: the processor says what is happening now,
 * memory says whether it can continue, and the disk says whether it can continue tomorrow.
 */
const readings: ComputedRef<Reading[]> = computed(() => {
  return [
    {
      key: 'cpu',
      icon: 'cpu',
      label: t('monitoring.charts.cpu'),
      value: props.resources.cpuPercent,

      // 100 explicitly: a percentage carries no total of its own, and passing one of the byte
      // figures here would draw a bar at a ratio that means nothing.
      max: 100,
      figure: cpuText.value,
      of: null,
      text: cpuText.value,
    },
    {
      key: 'memory',
      icon: 'memoryStick',
      label: t('monitoring.charts.memory'),
      value: props.resources.memoryUsedBytes,
      max: props.resources.memoryTotalBytes,
      figure: formatBytes(props.resources.memoryUsedBytes),
      of: `/ ${formatBytes(props.resources.memoryTotalBytes)}`,
      text: memoryText.value,
    },
    {
      key: 'disk',
      icon: 'hardDrive',
      label: t('monitoring.charts.disk'),
      value: props.resources.diskUsedBytes,
      max: props.resources.diskTotalBytes,
      figure: formatBytes(props.resources.diskUsedBytes),
      of: `/ ${formatBytes(props.resources.diskTotalBytes)}`,
      text: diskText.value,
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
            <p class="m-0 flex flex-wrap items-baseline gap-x-1.5 font-display text-2xl leading-none font-black @xl:text-3xl">
              <span>{{ reading.figure }}</span>
              <small v-if="reading.of !== null" class="text-sm font-extrabold whitespace-nowrap text-text-muted">{{ reading.of }}</small>
            </p>
            <!-- `text-sm` on a phone: the display face at `text-xs` in capitals, and in Armenian
                 above all, reads as a smudge beside the figure. The narrow three-up tiles from
                 `sm` up keep `text-xs`, where a larger label would wrap in every language. -->
            <p class="m-0 mt-2 font-display text-sm font-extrabold tracking-caps text-text-secondary uppercase break-words @xl:text-xs">{{ reading.label }}</p>
          </div>
        </div>

        <!-- The processor tile also carries the load averages, so the three tiles stay three and
             the trend sits beside the instant it explains. -->
        <p v-if="reading.key === 'cpu'" class="m-0 mt-2 text-xs text-text-muted">
          <span>{{ t('app.status.resources.load') }}</span>
          <span class="ml-1.5 font-mono text-text-secondary tabular-nums">{{ loadText }}</span>
        </p>

        <div class="mt-auto pt-3.5">
          <UiMeter
            :value="reading.value"
            :max="reading.max"
            :label="reading.label"
            :value-text="reading.text"
            :show-value="false"
          />
        </div>
      </UiCard>
    </div>
    </div>
  </section>
</template>
