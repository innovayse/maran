<script setup lang="ts">
/**
 * The state of a migration job or of one of its accounts as a badge: the text always names the state, the tone
 * only repeats it, so colour is never the sole carrier of meaning. The words are the panel's, in the caller's language; the SPA keeps none of its own.
 */
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'

/** Props accepted by {@link MigrationStateBadge}. */
defineProps<{
  /** The state, which picks the tone and never the text, as the backend spells it. */
  state: string
  /** The state as an operator reads it, worded by the panel in the caller's language. */
  name: string
}>()

/** The tone of each state; a state this build does not know is neutral. */
const TONES: Readonly<Record<string, BadgeVariant>> = {
  Draft: 'neutral',
  Pending: 'neutral',
  Running: 'info',
  CuttingOver: 'info',
  Paused: 'warning',
  Synced: 'warning',
  Completed: 'success',
  Failed: 'danger',
  RolledBack: 'danger',
  RolledBackIncomplete: 'danger',
  Cancelled: 'neutral',
}
</script>

<template>
  <UiBadge :variant="(TONES[state] ?? 'neutral')">{{ name }}</UiBadge>
</template>
