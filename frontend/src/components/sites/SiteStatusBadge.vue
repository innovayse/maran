<script setup lang="ts">
/**
 * Small colored label rendering whether a site serves its own content or a suspension
 * response. Used by the sites list and the site detail header so the serving state is
 * scannable without reading a column closely; the tone never carries the meaning alone —
 * the text label is always present.
 *
 * An enabled site is green. A disabled one is amber, not grey: it is a site that is not serving its
 * visitors, which is a state somebody chose and may need to undo, and a neutral pill would file it
 * with the things that need no attention. The badge draws the leading status dot itself, so the
 * shape of the pill still differs from a plain label.
 *
 * The text is the backend's (`statusName` on the row), printed as given.
 *
 * The rendering itself belongs to `UiBadge` — this component's only job is mapping one
 * domain state onto a badge tone and a label.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { SiteStatus } from '../../types/site'

/** Props accepted by {@link SiteStatusBadge}. */
const props = defineProps<{
  /** Whether the site currently serves its own content; picks the tone. */
  status: SiteStatus
  /** The serving state as the operator reads it, already in their language; the badge's text. */
  label: string
}>()

/** Badge tone for the current status; the label below is what actually carries the meaning. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  return props.status === 'enabled' ? 'success' : 'warning'
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
