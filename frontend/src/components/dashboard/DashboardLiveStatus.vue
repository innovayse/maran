<script setup lang="ts">
/**
 * Says whether the figures below are arriving: live, connecting, reconnecting, paused (the tab was
 * hidden, so the connection was closed on purpose) or off (nothing open, or the panel refused the
 * stream, in which case the one-shot read is what is drawn).
 *
 * The state is a WORD in a badge, never a colour alone, and the text sits in a polite live region
 * so a screen-reader user hears "Reconnecting" when it happens without being interrupted by every
 * two-second tick (the ticks are not announced at all). The live region is always present, empty
 * text included, because a region inserted together with its text is often not announced.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { LiveStatus } from '../../types/liveDashboard'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'

const props = defineProps<{
  /** Where the connection stands. */
  status: LiveStatus
}>()

const { t } = useI18n()

/** The badge tone of each state. */
const TONES: Record<LiveStatus, BadgeVariant> = {
  live: 'success',
  connecting: 'info',
  reconnecting: 'warning',
  paused: 'neutral',
  stopped: 'neutral',
}

/** The state in words. */
const text: ComputedRef<string> = computed(() => {
  return t(`liveDashboard.status.${props.status}`)
})
</script>

<template>
  <div aria-live="polite" data-testid="live-status" :data-status="status">
    <UiBadge :variant="TONES[status]">{{ text }}</UiBadge>
  </div>
</template>
