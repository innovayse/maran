<script setup lang="ts">
/**
 * The window's traffic day by day, as two plots: requests and bytes. Each is a {@link UiChart}, the
 * kit's single chart primitive; nothing here draws.
 *
 * A day is plotted at local noon rather than at midnight UTC: the chart prints each point's instant
 * in the reader's time zone, and a midnight UTC point would read as the evening before west of
 * Greenwich. The summary's days are whole UTC dates, so a date, not an instant, is what each bucket
 * means.
 */
import { parseISO } from 'date-fns'
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiChart from '../ui/UiChart.vue'
import { useLocaleStore } from '../../stores/locale'
import type { ChartPoint } from '../../utils/chartGeometry'
import { formatBytes } from '../../utils/formatBytes'
import type { TrafficDay } from '../../types/siteTraffic'

/** Props accepted by {@link TrafficDayChart}. */
const props = defineProps<{
  /** The days to plot, oldest first. */
  perDay: TrafficDay[]
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** Requests per day. */
const requestSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((day) => {
    return day.requests
  })
})

/** Response body bytes per day. */
const byteSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((day) => {
    return day.bytes
  })
})

/**
 * Builds one series out of the days.
 *
 * Declared below the computeds that call it: a `computed` body does not run at declaration time,
 * so nothing reads this binding before the `const` is initialised.
 * @param select Reads the plotted figure from one day.
 * @returns The points to plot, one per day.
 */
const seriesOf = (select: (day: TrafficDay) => number): ChartPoint[] => {
  return props.perDay.map((day) => {
    return { at: parseISO(`${day.date}T12:00:00`).getTime(), value: select(day) }
  })
}

/**
 * Formats a request count for an axis or the readout: whole numbers in the interface language's
 * grouping, a fraction only where the axis scale lands on one.
 * @param value The raw count.
 * @returns The count as text.
 */
const formatCount = (value: number): string => {
  return Number.isInteger(value)
    ? value.toLocaleString(localeStore.current)
    : value.toLocaleString(localeStore.current, { maximumFractionDigits: 1 })
}
</script>

<template>
  <div class="grid min-w-0 grid-cols-1 gap-4 lg:grid-cols-2">
    <UiCard class="min-w-0">
      <UiChart :series="requestSeries" :label="t('siteTraffic.chart.requests')" :format-value="formatCount" />
    </UiCard>
    <UiCard class="min-w-0">
      <UiChart :series="byteSeries" :label="t('siteTraffic.chart.bytes')" :format-value="formatBytes" />
    </UiCard>
  </div>
</template>
