<script setup lang="ts">
/**
 * Small colored label rendering whether a site serves its own content or a suspension
 * response. Used by the sites list and the site detail header so the serving state is
 * scannable without reading a column closely; the tone never carries the meaning alone —
 * the text label is always present.
 *
 * The rendering itself belongs to `UiBadge` — this component's only job is mapping one
 * domain state onto a badge tone and a label.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { SiteStatus } from '../../types/site'

/** Props accepted by {@link SiteStatusBadge}. */
const props = defineProps<{
  /** Whether the site currently serves its own content. */
  status: SiteStatus
}>()

const { t } = useI18n()

/** Badge tone for the current status; the label below is what actually carries the meaning. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  return props.status === 'enabled' ? 'success' : 'neutral'
})

/** i18n-resolved label for the current status. */
const label: ComputedRef<string> = computed(() => {
  return t(`sites.status.${props.status}`)
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
