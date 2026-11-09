<script setup lang="ts">
/**
 * The root filesystem, live: bytes in use as a chart whose ceiling is the filesystem's size, with
 * the figure as used out of size.
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
      key: 'disk',
      label: t('liveDashboard.disk.title'),
      points: liveSeriesOf(props.points, (point) => {
        return point.disk.used
      }),
    },
  ]
})

/** The filesystem's size: the chart's ceiling. */
const ceiling: ComputedRef<number | undefined> = computed(() => {
  return props.latest === null || props.latest.disk.total <= 0 ? undefined : props.latest.disk.total
})

/** The figure, as used out of size. */
const figure: ComputedRef<string | null> = computed(() => {
  if (props.latest === null) {
    return null
  }
  const { used, total } = props.latest.disk
  // A reading without the used figure still knows the size; it is never drawn as zero used.
  return used === null || used === undefined
    ? t('liveDashboard.disk.sizeOnly', { total: formatBytes(total) })
    : t('app.status.resources.ratio', { used: formatBytes(used), total: formatBytes(total) })
})
</script>

<template>
  <DashboardMetricCard
    metric="disk"
    :title="t('liveDashboard.disk.title')"
    icon="hardDrive"
    :figure="figure"
    :detail="null"
    :series="series"
    :format-value="formatBytes"
    :ceiling="ceiling"
    :links-to-monitoring="linksToMonitoring"
  />
</template>
