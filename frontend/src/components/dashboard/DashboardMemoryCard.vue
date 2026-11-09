<script setup lang="ts">
/**
 * Memory, live: bytes in use as a chart whose ceiling is the installed memory, so the line's
 * height is the share used and a nearly full host is visibly near the top. The figure is the pair
 * the resource card on the one-shot read uses ("used of total").
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { LiveMetricsPoint } from '../../types/liveDashboard'
import type { UiLiveChartSeries } from '../ui/UiLiveChart.vue'
import { formatBytes } from '../../utils/formatBytes'
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

/** The one line: bytes in use. */
const series: ComputedRef<UiLiveChartSeries[]> = computed(() => {
  return [
    {
      key: 'memory',
      label: t('liveDashboard.memory.title'),
      points: liveSeriesOf(props.points, (point) => {
        return point.memory.used
      }),
    },
  ]
})

/** The installed memory: the chart's ceiling, which never moves on a running host. */
const ceiling: ComputedRef<number | undefined> = computed(() => {
  return props.latest === null || props.latest.memory.total <= 0 ? undefined : props.latest.memory.total
})

/** The figure, as used out of total. */
const figure: ComputedRef<string | null> = computed(() => {
  return props.latest === null
    ? null
    : t('app.status.resources.ratio', {
        used: formatBytes(props.latest.memory.used),
        total: formatBytes(props.latest.memory.total),
      })
})
</script>

<template>
  <DashboardMetricCard
    metric="memory"
    :title="t('liveDashboard.memory.title')"
    icon="memoryStick"
    :figure="figure"
    :detail="null"
    :series="series"
    :format-value="formatBytes"
    :ceiling="ceiling"
    :links-to-monitoring="linksToMonitoring"
  />
</template>
