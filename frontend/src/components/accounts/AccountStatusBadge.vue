<script setup lang="ts">
/**
 * Small colored label rendering a hosting account's lifecycle state. Used by
 * the accounts list so status is scannable without reading a text column
 * closely; the color carries no meaning on its own (text label is always
 * present) so it stays accessible without relying on color alone. The text is the backend's
 * (`statusName` on the account), printed as given.
 *
 * The rendering itself belongs to `UiBadge` — this component's only job is
 * mapping one domain state onto a badge tone and a label. An active account is the
 * green dot; a suspended one is the amber dot — its sites have stopped serving, which
 * is a state an operator should notice in a column of green, not a neutral grey that
 * recedes (a grey badge draws no dot at all, so it would read as "no status").
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { AccountStatus } from '../../types/account'

/** Props accepted by {@link AccountStatusBadge}. */
const props = defineProps<{
  /** The account's current lifecycle state. */
  status: AccountStatus
  /** The state as a reader reads it, already in their language; the badge's text. */
  label: string
}>()

/** Badge tone for the current status; the label below is what actually carries the meaning. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  return props.status === 'active' ? 'success' : 'warning'
})

</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
