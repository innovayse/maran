import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { UiStackedMeterSegment } from '../components/ui/UiStackedMeter.vue'
import { formatBytes } from '../utils/formatBytes'
import type { HostMetrics } from '../types/monitoring'

/** What {@link useDiskSegments} gives a disk card. */
export interface DiskSegments {
  /** Used, reserved for the system, and available, in that order; empty before a reading arrives. */
  segments: ComputedRef<UiStackedMeterSegment[]>
  /** All three figures as one translated sentence, the bar's accessible name; empty before a reading. */
  summary: ComputedRef<string>
}

/**
 * Turns a live host reading into the three parts of the root filesystem a disk card draws: what is
 * used (`df`'s Used), what only root may still write (the filesystem's reserve, 0 on xfs), and what
 * everyone else can still write (`df`'s Avail). The three sum to the size (issue #77).
 *
 * One composable for the dashboard's disk tile and the monitoring screen's disk card, so the two
 * screens cannot label or colour the same three figures differently — and neither feature imports
 * the other's folder (rules/vue.md). The labels live under `monitoring.hostDisk.*`, the one place
 * both screens read them from. A reserve of zero is kept as a segment, so its row still reads "0 B".
 * @param reading Returns the reading to describe, or `undefined` while it is on its way.
 * @returns The segments for `UiStackedMeter` and the sentence that names them.
 */
export const useDiskSegments = (reading: () => HostMetrics | undefined): DiskSegments => {
  const { t } = useI18n()

  /** The three parts, in the order the bar draws them; empty while the reading is on its way. */
  const segments: ComputedRef<UiStackedMeterSegment[]> = computed(() => {
    const metrics = reading()
    if (metrics === undefined) {
      return []
    }
    return [
      {
        key: 'used',
        label: t('monitoring.hostDisk.used'),
        value: metrics.diskUsedBytes,
        valueText: formatBytes(metrics.diskUsedBytes),
        tone: 'accent',
      },
      {
        key: 'reserved',
        label: t('monitoring.hostDisk.reserved'),
        value: metrics.diskReservedBytes,
        valueText: formatBytes(metrics.diskReservedBytes),
        tone: 'warning',
      },
      {
        key: 'available',
        label: t('monitoring.hostDisk.available'),
        value: metrics.diskAvailableBytes,
        valueText: formatBytes(metrics.diskAvailableBytes),
        tone: 'neutral',
      },
    ]
  })

  /** The bar's accessible name: every figure in one sentence, the size included. */
  const summary: ComputedRef<string> = computed(() => {
    const metrics = reading()
    if (metrics === undefined) {
      return ''
    }
    return t('monitoring.hostDisk.summary', {
      used: formatBytes(metrics.diskUsedBytes),
      reserved: formatBytes(metrics.diskReservedBytes),
      available: formatBytes(metrics.diskAvailableBytes),
      total: formatBytes(metrics.diskTotalBytes),
    })
  })

  return { segments, summary }
}
