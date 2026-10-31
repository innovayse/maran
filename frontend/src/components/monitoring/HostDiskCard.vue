<script setup lang="ts">
/**
 * The root filesystem right now, on the monitoring screen: "used / size" as the figure, and under it
 * the three parts that sum to the size — used (`df`'s Used), reserved for the system (the
 * filesystem's root reserve, 0 on xfs) and available (`df`'s Avail) — drawn by
 * {@link UiStackedMeter} with each part's label and figure as text (issue #77).
 *
 * The parts come from `useDiskSegments`, the composable the dashboard's disk tile uses too, so the
 * two screens describe the disk in the same words and colours.
 *
 * The reading is the LIVE one the store asks the agent for, not a stored sample, and it is loaded on
 * its own: this card has its own loading region, and a refused reading is an alert inside this card
 * only — the charts and the services beside it are untouched by it.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiCard from '../ui/UiCard.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import UiStackedMeter from '../ui/UiStackedMeter.vue'
import { useDiskSegments } from '../../composables/useDiskSegments'
import { formatBytes } from '../../utils/formatBytes'
import type { LoadingState } from '../../types/loading'
import type { HostMetrics } from '../../types/monitoring'

/** Props accepted by {@link HostDiskCard}. */
const props = defineProps<{
  /** The live reading, or `null` before one has been answered. */
  metrics: HostMetrics | null
  /** Where the live reading stands. */
  state: LoadingState
  /** The panel's own message for a refused reading, or `null` when none arrived. */
  errorMessage: string | null
}>()

const { t } = useI18n()
const disk = useDiskSegments(() => {
  return props.metrics ?? undefined
})

/** The figure: used beside the size, or empty before a reading has arrived. */
const figure: ComputedRef<string> = computed(() => {
  if (props.metrics === null) {
    return ''
  }
  return `${formatBytes(props.metrics.diskUsedBytes)} / ${formatBytes(props.metrics.diskTotalBytes)}`
})
</script>

<template>
  <UiCard data-testid="host-disk-card">
    <UiSectionHeading class="mb-4" :title="t('monitoring.hostDisk.title')" />
    <UiLoadingRegion :state="state">
      <template #error>
        <UiAlert variant="error">{{ errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <div v-if="state === 'loading'" class="flex flex-col gap-3">
        <UiSkeleton shape="line" size="lg" />
        <UiSkeleton v-for="row in 3" :key="row" shape="line" size="sm" />
      </div>
      <div v-else class="flex flex-col gap-3">
        <p class="m-0 font-display text-2xl leading-none font-black">{{ figure }}</p>
        <UiStackedMeter :segments="disk.segments.value" :label="disk.summary.value" />
      </div>
    </UiLoadingRegion>
  </UiCard>
</template>
