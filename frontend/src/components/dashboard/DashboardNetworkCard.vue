<script setup lang="ts">
/**
 * Network throughput, live: bytes per second received and sent, summed over the physical
 * interfaces, as two lines in one unit. The unit is chosen from the largest rate held (a quiet
 * server moves kilobytes, and drawn in megabytes every tick would read zero), by the same scale
 * the monitoring screen uses. A reading with no rate (the first, which has nothing to measure
 * against) is a gap in the line, never a second of silence.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { LiveMetricsPoint } from '../../types/liveDashboard'
import type { UiLiveChartSeries } from '../ui/UiLiveChart.vue'
import { byteRateScale, type ByteRateScale } from '../../utils/byteRateScale'
import { liveSeriesOf } from '../../utils/liveSeriesOf'
import DashboardMetricCard from './DashboardMetricCard.vue'

const props = defineProps<{
  /** The readings held, oldest first. */
  points: readonly LiveMetricsPoint[]
  /** The newest reading, or `null` before the first. */
  latest: LiveMetricsPoint | null
  /** Whether the monitoring screen can be opened. */
  linksToMonitoring: boolean
}>()

const { t } = useI18n()

/** The unit and divisor the series are drawn in, from the largest rate held. */
const scale: ComputedRef<ByteRateScale> = computed(() => {
  return byteRateScale(
    props.points.flatMap((point) => {
      return [point.network.rxBps ?? 0, point.network.txBps ?? 0]
    }),
  )
})

/** The two lines, already divided into the chosen unit. */
const series: ComputedRef<UiLiveChartSeries[]> = computed(() => {
  const divisor = scale.value.divisor
  return [
    {
      key: 'rx',
      label: t('liveDashboard.network.received'),
      points: liveSeriesOf(props.points, (point) => {
        return point.network.rxBps === null ? null : point.network.rxBps / divisor
      }),
    },
    {
      key: 'tx',
      label: t('liveDashboard.network.sent'),
      points: liveSeriesOf(props.points, (point) => {
        return point.network.txBps === null ? null : point.network.txBps / divisor
      }),
    },
  ]
})

/** The newest rates as one sentence, or the placeholder while the first rate is still being measured. */
const figure: ComputedRef<string | null> = computed(() => {
  if (props.latest === null) {
    return null
  }
  const { rxBps, txBps } = props.latest.network
  return rxBps === null || txBps === null
    ? t('common.emptyValue')
    : t('liveDashboard.network.figure', { received: formatRate(rxBps), sent: formatRate(txBps) })
})

/**
 * Formats a rate in the unit the axis uses.
 * @param bytesPerSecond A rate in bytes per second.
 * @returns For example `3.4 KiB/s`.
 */
const formatRate = (bytesPerSecond: number): string => {
  return `${(bytesPerSecond / scale.value.divisor).toFixed(1)} ${scale.value.unit}`
}

/**
 * Formats a value already divided into the axis unit.
 * @param value A rate in the chosen unit.
 * @returns For example `3.4 KiB/s`.
 */
const formatAxis = (value: number): string => {
  return `${value.toFixed(1)} ${scale.value.unit}`
}
</script>

<template>
  <DashboardMetricCard
    metric="network"
    :title="t('liveDashboard.network.title')"
    icon="globe"
    :figure="figure"
    :detail="null"
    :series="series"
    :format-value="formatAxis"
    :links-to-monitoring="linksToMonitoring"
  />
</template>
