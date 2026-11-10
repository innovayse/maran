<script setup lang="ts">
/**
 * The state of an import as a badge: the text always names the state, the tone only repeats it, so
 * colour is never the sole carrier of meaning. The words are the panel's, in the caller's language; the SPA keeps none of its own.
 */
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { AccountImportState } from '../../types/accountMove'

/** Props accepted by {@link AccountImportStateBadge}. */
defineProps<{
  /** The state, which picks the tone and never the text. */
  state: AccountImportState
  /** The state as an operator reads it, worded by the panel in the caller's language. */
  name: string
}>()

/** The tone of each state: a finished import is green, a running one blue, a waiting one amber, a failed one red. */
const TONES: Readonly<Record<AccountImportState, BadgeVariant>> = {
  Staged: 'warning',
  Running: 'info',
  Completed: 'success',
  RolledBack: 'danger',
  RolledBackIncomplete: 'danger',
  Discarded: 'neutral',
}
</script>

<template>
  <UiBadge :variant="TONES[state]">{{ name }}</UiBadge>
</template>
