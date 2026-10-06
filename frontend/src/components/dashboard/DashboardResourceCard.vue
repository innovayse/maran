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
 * **Every bar carries its label and its figure as visible text.** `UiMeter` is a bar and nothing
 * else — its `label` and `valueText` reach assistive technology through `aria-label` and
 * `aria-valuetext` and are invisible on screen — so a card that passed them and stopped would show
 * an operator three unlabelled stripes. The monitoring screen's disk table pairs the same primitive
 * with its own visible figures for the same reason.
 *
 * **The network counters the payload carries are deliberately not shown.** They are totals since
 * boot, not rates, and "4.2 TB received" on a landing page reads as traffic while meaning uptime.
 * A rate needs two readings and the panel does not offer one here, so this card shows nothing
 * rather than something that would be misread.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
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

/** Memory in use beside what is installed. */
const memoryText: ComputedRef<string> = computed(() => {
  return t('app.status.resources.ratio', {
    used: formatBytes(props.resources.memoryUsedBytes),
    total: formatBytes(props.resources.memoryTotalBytes),
  })
})

/** Disk in use on the root filesystem beside its capacity. */
const diskText: ComputedRef<string> = computed(() => {
  return t('app.status.resources.ratio', {
    used: formatBytes(props.resources.diskUsedBytes),
    total: formatBytes(props.resources.diskTotalBytes),
  })
})

/** One bar: what it measures, how far along it is, and the figure a reader sees. */
interface Reading {
  /** Stable key for the list. */
  key: string
  /** The measure's name, already translated. */
  label: string
  /** How much is used, in the same unit as {@link Reading.max}. */
  value: number
  /** The allowance, in the same unit as {@link Reading.value}. */
  max: number
  /** The figure as a reader sees it, and as assistive technology announces it. */
  text: string
}

/**
 * The three bars, in the order an operator triages them: the processor says what is happening now,
 * memory says whether it can continue, and the disk says whether it can continue tomorrow.
 */
const readings: ComputedRef<Reading[]> = computed(() => {
  return [
    {
      key: 'cpu',
      label: t('monitoring.charts.cpu'),
      value: props.resources.cpuPercent,

      // 100 explicitly: a percentage carries no total of its own, and passing one of the byte
      // figures here would draw a bar at a ratio that means nothing.
      max: 100,
      text: cpuText.value,
    },
    {
      key: 'memory',
      label: t('monitoring.charts.memory'),
      value: props.resources.memoryUsedBytes,
      max: props.resources.memoryTotalBytes,
      text: memoryText.value,
    },
    {
      key: 'disk',
      label: t('monitoring.charts.disk'),
      value: props.resources.diskUsedBytes,
      max: props.resources.diskTotalBytes,
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
  <UiCard>
    <UiSectionHeading class="mb-3" :title="t('app.status.resources.title')" />

    <div class="grid gap-4 sm:grid-cols-3">
      <div
        v-for="reading in readings"
        :key="reading.key"
        class="flex flex-col gap-1"
      >
        <div class="flex items-baseline justify-between gap-2">
          <span class="text-sm text-[var(--t2)]">{{ reading.label }}</span>
          <span class="text-sm font-medium text-[var(--t1)]">{{ reading.text }}</span>
        </div>
        <UiMeter
          :value="reading.value"
          :max="reading.max"
          :label="reading.label"
          :value-text="reading.text"
        />
      </div>
    </div>

    <UiDescriptionList class="mt-4">
      <UiDescriptionItem :term="t('app.status.resources.load')" mono>{{ loadText }}</UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
