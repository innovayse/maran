<script setup lang="ts">
/**
 * Small coloured label for where an app's install stands. The colour carries no meaning on its own:
 * the text is always present and is the panel's (`stateName` on the app), printed as given. A
 * finished install is green, a transient one (installing, removing) is blue, a failed one is red.
 *
 * The rendering belongs to `UiBadge`; this component only maps one domain state onto a tone.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { HostAppState } from '../../types/hostApp'

/** Props accepted by {@link HostAppStateBadge}. */
const props = defineProps<{
  /** Where the app's install stands. */
  state: HostAppState
  /** The state as a reader reads it, already in their language; the badge's text. */
  label: string
}>()

/** Badge tone for the current state. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.state) {
    case 'installed':
      return 'success'
    case 'failed':
      return 'danger'
    case 'installing':
    case 'removing':
    default:
      return 'info'
  }
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
