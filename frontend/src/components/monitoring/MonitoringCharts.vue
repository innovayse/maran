<script setup lang="ts">
/**
 * The six plots the monitoring screen is made of: processor, memory, disk, the two network rates
 * and the one-minute load average. One component rather than six call sites in the page, because
 * every one of them is the same transformation of the same bucket list — pick a field, choose a
 * unit — and six near-identical blocks in a page is six places for one of them to be edited alone.
 *
 * Each plot is a {@link UiChart}, the kit's existing chart primitive and the panel's single
 * non-icon SVG site (rules/vue.md). Nothing here draws.
 *
 * The series are typed as `ChartPoint` from `utils/chartGeometry`, which is the same shape
 * `UiChart`'s own `UiChartPoint` declares. Deliberately not that one: a type exported from an SFC
 * is opaque to the type-aware lint pass, so every series built against it lands as `any[]` and
 * `@typescript-eslint/no-unsafe-return` rejects the file — a real loss of checking, not a lint
 * quirk to suppress.
 *
 * **The two network series drop their null buckets rather than plotting zeros.** A rate is derived
 * on the server from the difference between two counter readings divided by the seconds actually
 * elapsed, so the first bucket of any chart has nothing to measure against and arrives as `null`.
 * Plotting that as 0 would draw a period of no traffic that never happened. Dropping it leaves the
 * series unevenly spaced, which the chart already handles: it places buckets by index and labels
 * them by their own timestamps.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiChart from '../ui/UiChart.vue'
import type { ChartPoint } from '../../utils/chartGeometry'
import { bytesToGibibytes, bytesToMebibytes } from '../../utils/formatBytes'
import type { MetricBucket } from '../../types/monitoring'

/** Props accepted by {@link MonitoringCharts}. */
const props = defineProps<{
  /** The buckets the panel answered with, oldest first. Empty draws six empty states. */
  buckets: MetricBucket[]
}>()

const { t } = useI18n()

/** Processor utilisation, already a percentage on the wire. */
const cpuSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((bucket) => {
    return bucket.cpuPercent
  })
})

/** Memory in use, in GiB — the unit a person reads a server's memory in. */
const memorySeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((bucket) => {
    return bytesToGibibytes(bucket.memoryUsedBytes)
  })
})

/** Disk space in use on the root filesystem, in GiB. */
const diskSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((bucket) => {
    return bytesToGibibytes(bucket.diskUsedBytes)
  })
})

/** Bytes received per second, in MiB/s. */
const networkReceiveSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((bucket) => {
    const rate = bucket.networkReceivedBytesPerSecond
    return rate === null ? null : bytesToMebibytes(rate)
  })
})

/** Bytes sent per second, in MiB/s. */
const networkTransmitSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((bucket) => {
    const rate = bucket.networkSentBytesPerSecond
    return rate === null ? null : bytesToMebibytes(rate)
  })
})

/** The one-minute load average, a bare number with no unit of its own. */
const loadSeries: ComputedRef<ChartPoint[]> = computed(() => {
  return seriesOf((bucket) => {
    return bucket.loadAverage1m
  })
})

/**
 * Builds one series out of the buckets, keeping only those the chosen metric has a value for.
 *
 * Declared below the computeds that call it, per the member order rules/vue.md fixes — safe because
 * a `computed`'s body does not run at declaration time, so nothing reads this binding before the
 * `const` is initialised.
 * @param select Reads the metric from one bucket; returns `null` for a bucket that has none.
 * @returns The points to plot, oldest first.
 */
const seriesOf = (select: (bucket: MetricBucket) => number | null): ChartPoint[] => {
  return props.buckets.flatMap((bucket): ChartPoint[] => {
    const value = select(bucket)
    // `Date.parse` on the panel's ISO-8601 offset string: the chart's `at` is a Unix epoch in
    // milliseconds, and the offset is carried in the text, so no local timezone is assumed here.
    return value === null ? [] : [{ at: Date.parse(bucket.at), value }]
  })
}

/**
 * Two decimals, for the series whose interesting values are small: a load average of 0.42 and a
 * network rate of 0.03 MiB/s both round to nothing at the chart's own one-decimal default.
 * @param value The raw value to format.
 * @returns The value with two decimal places.
 */
const formatFine = (value: number): string => {
  return value.toFixed(2)
}
</script>

<template>
  <div class="grid grid-cols-1 gap-4 lg:grid-cols-2" data-testid="monitoring-charts">
    <UiChart :series="cpuSeries" :label="t('monitoring.charts.cpu')" :unit="t('monitoring.units.percent')" />
    <UiChart :series="memorySeries" :label="t('monitoring.charts.memory')" :unit="t('monitoring.units.gibibytes')" />
    <UiChart :series="diskSeries" :label="t('monitoring.charts.disk')" :unit="t('monitoring.units.gibibytes')" />
    <UiChart
      :series="networkReceiveSeries"
      :label="t('monitoring.charts.networkReceive')"
      :unit="t('monitoring.units.mebibytesPerSecond')"
      :format-value="formatFine"
    />
    <UiChart
      :series="networkTransmitSeries"
      :label="t('monitoring.charts.networkTransmit')"
      :unit="t('monitoring.units.mebibytesPerSecond')"
      :format-value="formatFine"
    />
    <UiChart
      :series="loadSeries"
      :label="t('monitoring.charts.load')"
      :unit="t('monitoring.units.load')"
      :format-value="formatFine"
    />
  </div>
</template>
