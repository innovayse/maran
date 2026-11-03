<script setup lang="ts">
/**
 * Small coloured label rendering a reseller's lifecycle state, so the state is scannable in a
 * column without reading it closely. The colour carries no meaning on its own: the text label is
 * always present, and it is the backend's (`statusName` on the reseller), printed as given. An active
 * reseller is the green dot; a suspended one is the amber dot, because its login is blocked and its
 * writes are refused, which an operator should notice in a column of green.
 *
 * The rendering belongs to `UiBadge`; this component only maps one domain state onto a tone.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { ResellerStatus } from '../../types/reseller'

/** Props accepted by {@link ResellerStatusBadge}. */
const props = defineProps<{
  /** The reseller's current lifecycle state. */
  status: ResellerStatus
  /** The state as a reader reads it, already in their language; the badge's text. */
  label: string
}>()

/** Badge tone for the current status. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  return props.status === 'active' ? 'success' : 'warning'
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
