<script setup lang="ts">
/**
 * Small coloured label for what an app's service is doing. The text is the panel's
 * (`serviceStatusName`), printed as given; the colour only helps scanning a column: running is
 * green, stopped is neutral, failed is red, and an app with no service, or whose probe could not tell, is neutral too.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { HostAppServiceStatus } from '../../types/hostApp'

/** Props accepted by {@link HostAppServiceBadge}. */
const props = defineProps<{
  /** What the service is doing. */
  status: HostAppServiceStatus
  /** The status as a reader reads it, already in their language; the badge's text. */
  label: string
}>()

/** Badge tone for the current status. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.status) {
    case 'active':
      return 'success'
    case 'failed':
      return 'danger'
    case 'inactive':
    case 'none':
    case 'unknown':
    default:
      return 'neutral'
  }
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
