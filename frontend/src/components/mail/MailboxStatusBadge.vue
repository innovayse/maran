<script setup lang="ts">
/**
 * Small coloured label for whether a mailbox accepts mail and logins. Active is green; suspended
 * by the panel is amber (somebody may need to undo it); switched off by its owner is neutral, a
 * state chosen on purpose. The tone never carries the meaning alone: the text is the panel's own
 * `statusName`, printed as given.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { MailboxStatus } from '../../types/mailbox'

/** Props accepted by {@link MailboxStatusBadge}. */
const props = defineProps<{
  /** Whether the mailbox accepts mail and logins; picks the tone. */
  status: MailboxStatus
  /** The state as the reader sees it, already in their language; the badge's text. */
  label: string
}>()

/** Badge tone for the current status. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.status) {
    case 'active':
      return 'success'
    case 'suspended':
      return 'warning'
    default:
      return 'neutral'
  }
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
