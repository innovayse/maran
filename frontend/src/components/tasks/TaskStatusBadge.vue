<script setup lang="ts">
/**
 * A task's status as a badge: running, completed, or failed.
 *
 * The three are the module's whole `PanelTaskStatus` enum, and the mapping onto the kit's badge
 * tones is stated here once so a row, a live pane and anything added later cannot colour them
 * differently. The TEXT is this panel's own chrome and comes from the locale files — the status
 * arrives as a machine-stable name (`running`), not as a sentence, which is exactly why it may be
 * translated here without breaking the "the backend owns its text" rule.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { PanelTaskStatus } from '../../types/panelTask'

/** Props accepted by {@link TaskStatusBadge}. */
const props = defineProps<{
  /** The status to draw. */
  status: PanelTaskStatus
}>()

const { t } = useI18n()

/** The badge tone for this status. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.status) {
    case 'completed':
      return 'success'
    case 'failed':
      return 'danger'
    default:
      return 'info'
  }
})

/** The status in the operator's language. */
const label: ComputedRef<string> = computed(() => {
  return t(`tasks.statuses.${props.status}`)
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
