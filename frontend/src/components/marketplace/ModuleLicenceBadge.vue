<script setup lang="ts">
/**
 * A module's licence standing as one badge: the panel's own words for the state, in a tone picked
 * from the machine state. The tone is presentation only; whether the module may be installed is the
 * panel's `canInstall`, never read from this badge.
 */
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { MarketplaceEntitlement, MarketplaceEntitlementState } from '../../types/marketplace'

/** Props accepted by {@link ModuleLicenceBadge}. */
defineProps<{
  /** The module's licence standing, with the state's name localized by the panel. */
  entitlement: MarketplaceEntitlement
}>()

/** The tone each machine state is drawn in. */
const TONES: Record<MarketplaceEntitlementState, BadgeVariant> = {
  entitled: 'success',
  notRequired: 'neutral',
  notPurchased: 'warning',
  leaseExpired: 'warning',
  unknown: 'neutral',
}
</script>

<template>
  <UiBadge :variant="TONES[entitlement.state]" data-testid="module-licence-badge">{{
    entitlement.stateDisplayName
  }}</UiBadge>
</template>
