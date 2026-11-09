<script setup lang="ts">
/**
 * The processor, live: utilisation across all cores as a chart that grows as readings arrive, the
 * current figure to one decimal (like `top`, which an operator checks it against), and the three
 * load averages under it. The ceiling is fixed at 100 so a quiet host and a busy one are drawn on
 * the same scale.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { LiveMetricsPoint } from '../../types/liveDashboard'
import type { UiLiveChartSeries } from '../ui/UiLiveChart.vue'
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

/** The one line: utilisation. */
const series: ComputedRef<UiLiveChartSeries[]> = computed(() => {
  return [
    {
      key: 'cpu',
      label: t('liveDashboard.cpu.title'),
      points: liveSeriesOf(props.points, (point) => {
        return point.cpu.total
      }),
    },
  ]
})

/** The figure, as `0.0 %`. */
const figure: ComputedRef<string | null> = computed(() => {
  return props.latest === null ? null : formatPercent(props.latest.cpu.total)
})

/** The load averages as one sentence. */
const detail: ComputedRef<string | null> = computed(() => {
  if (props.latest === null) {
    return null
  }
  const [one, five, fifteen] = props.latest.cpu.load
  return t('liveDashboard.cpu.load', { one: formatLoad(one), five: formatLoad(five), fifteen: formatLoad(fifteen) })
})

/**
 * Formats one load average; a figure the reading does not carry is the placeholder, never zero.
 * @param value The load average, or nothing.
 * @returns For example `0.21`.
 */
const formatLoad = (value: number | null | undefined): string => {
  return value === null || value === undefined ? t('common.emptyValue') : value.toFixed(2)
}

/**
 * Formats a percentage to one decimal, with the panel's unit.
 * @param value The percentage, 0-100.
 * @returns For example `45.5 %`.
 */
const formatPercent = (value: number): string => {
  return `${value.toFixed(1)}${t('monitoring.units.percent')}`
}
</script>

<template>
  <DashboardMetricCard
    metric="cpu"
    :title="t('liveDashboard.cpu.title')"
    icon="cpu"
    :figure="figure"
    :detail="detail"
    :series="series"
    :format-value="formatPercent"
    :ceiling="100"
    :links-to-monitoring="linksToMonitoring"
  />
</template>
