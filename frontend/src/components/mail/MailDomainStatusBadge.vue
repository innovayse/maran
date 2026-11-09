<script setup lang="ts">
/**
 * Small coloured label for whether a mail domain is receiving and sending. An active domain is
 * green; a suspended one is amber, because it is a domain that is not delivering and somebody may
 * need to undo it. The tone never carries the meaning alone: the text is the panel's own `statusName`,
 * printed as given.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { MailDomainStatus } from '../../types/mailDomain'

/** Props accepted by {@link MailDomainStatusBadge}. */
const props = defineProps<{
  /** Whether the domain is delivering; picks the tone. */
  status: MailDomainStatus
  /** The state as the reader sees it, already in their language; the badge's text. */
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
