<script setup lang="ts">
/**
 * Small coloured label rendering a reseller's lifecycle state, so the state is scannable in a
 * column without reading it closely. The colour carries no meaning on its own: the text label is
 * always present. An active reseller is the green dot; a suspended one is the amber dot, because
 * its login is blocked and its writes are refused, which an operator should notice in a column of
 * green.
 *
 * The rendering belongs to `UiBadge`; this component only maps one domain state onto a tone and a
 * label.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { ResellerStatus } from '../../types/reseller'

/** Props accepted by {@link ResellerStatusBadge}. */
const props = defineProps<{
  /** The reseller's current lifecycle state. */
  status: ResellerStatus
}>()

const { t } = useI18n()

/** Badge tone for the current status. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  return props.status === 'active' ? 'success' : 'warning'
})
</script>

<template>
  <UiBadge :variant="variant">{{ t(`resellers.status.${status}`) }}</UiBadge>
</template>
