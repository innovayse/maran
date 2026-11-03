<script setup lang="ts">
/**
 * A task's status as a badge: running, completed, or failed.
 *
 * The three are the module's whole `PanelTaskStatus` enum (the panel lists them; the status arrives as
 * text), and the mapping onto the kit's badge tones is stated here once so a row, a live pane and
 * anything added later cannot colour them differently: running is the accent (work in flight is not a verdict), completed is green, failed
 * is red. The tone is never the only carrier — the badge always prints the status as a word.
 *
 * The TEXT is the backend's: the row carries the status as the operator reads it (`statusName`) and
 * this component prints it as given, so the bundle holds no copy of the vocabulary. The machine
 * status only picks the tone.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'

/** Props accepted by {@link TaskStatusBadge}. */
const props = defineProps<{
  /** The status to draw, which picks the tone. */
  status: string
  /** The status as the operator reads it, already in their language; the badge's text. */
  label: string
}>()

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
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
