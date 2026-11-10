<script setup lang="ts">
/**
 * The state of a pull grant as a badge: the text always names the state, the tone only repeats it,
 * so colour is never the sole carrier of meaning. The words are the panel's, in the caller's language; the SPA keeps none of its own.
 */
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { ExportGrantState } from '../../types/accountMove'

/** Props accepted by {@link ExportGrantStateBadge}. */
defineProps<{
  /** The state, which picks the tone and never the text. */
  state: ExportGrantState
  /** The state as an operator reads it, worded by the panel in the caller's language. */
  name: string
}>()

/** The tone of each state: a grant that can still be used is blue, a used one green, the others neutral. */
const TONES: Readonly<Record<ExportGrantState, BadgeVariant>> = {
  Live: 'info',
  Used: 'success',
  Revoked: 'neutral',
  Expired: 'neutral',
}
</script>

<template>
  <UiBadge :variant="TONES[state]">{{ name }}</UiBadge>
</template>
