<script setup lang="ts">
/**
 * Small colored label rendering a hosting account's lifecycle state. Used by
 * the accounts list so status is scannable without reading a text column
 * closely; the color carries no meaning on its own (text label is always
 * present) so it stays accessible without relying on color alone.
 *
 * The rendering itself belongs to `UiBadge` — this component's only job is
 * mapping one domain state onto a badge tone and a label.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { AccountStatus } from '../../types/account'

/** Props accepted by {@link AccountStatusBadge}. */
const props = defineProps<{
  /** The account's current lifecycle state. */
  status: AccountStatus
}>()

const { t } = useI18n()

/** Badge tone for the current status; the label below is what actually carries the meaning. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  return props.status === 'active' ? 'success' : 'neutral'
})

/** i18n-resolved label for the current status. */
const label: ComputedRef<string> = computed(() => {
  return t(`accounts.status.${props.status}`)
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
