<script setup lang="ts">
/**
 * Where a PHP version stands in its support lifecycle: supported, security fixes only, end of life,
 * or not known to the panel.
 *
 * **The state is a word AND a glyph, never a colour alone.** The word is the backend's
 * `statusName`, printed as received: it is server text in the operator's language, so the SPA
 * holds no copy of it. The icon repeats the phase with a shape of its own (a tick, a shield, an
 * alert, a dashed ring), and the badge's tone is the third, redundant carrier. A reader who cannot
 * tell the tones apart still has two others.
 *
 * **`unknown` is its own state.** A version the panel has no row for is drawn neutral with a
 * dashed ring, never as supported: an unreadable lifecycle must not read as a clean bill of health.
 *
 * **Days left are shown only when the panel gave them and the version is still inside its
 * support.** For an end-of-life version the count is meaningless (the date has passed), and for a
 * version the panel has no date for there is nothing to count; either way the badge says no more
 * than its word.
 *
 * The rendering belongs to `UiBadge`; this component maps one lifecycle onto a tone, a glyph and a
 * text. The site's PHP tab renders the same badge.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiIcon, { type UiIconName } from '../ui/UiIcon.vue'
import type { PhpLifecycle, PhpSupportStatus } from '../../types/phpVersion'

/** Props accepted by {@link PhpLifecycleBadge}. */
const props = defineProps<{
  /** The version's lifecycle, as the panel reported it. */
  lifecycle: PhpLifecycle
}>()

/** The tone of each phase; the word beside it is what actually carries the meaning. */
const VARIANTS: Readonly<Record<PhpSupportStatus, BadgeVariant>> = {
  supported: 'success',
  securityOnly: 'warning',
  endOfLife: 'danger',
  unknown: 'neutral',
}

/** The glyph of each phase: a different shape each, so the phase survives without colour. */
const ICONS: Readonly<Record<PhpSupportStatus, UiIconName>> = {
  supported: 'circleCheck',
  securityOnly: 'shieldCheck',
  endOfLife: 'circleAlert',
  unknown: 'circleDashed',
}

const { t } = useI18n()

/** The days left, or `null` when none are to be shown (see the component's doc comment). */
const daysLeft: ComputedRef<number | null> = computed(() => {
  const days = props.lifecycle.daysLeft
  if (props.lifecycle.status === 'endOfLife' || days === null || days < 0) {
    return null
  }
  return days
})
</script>

<template>
  <UiBadge :variant="VARIANTS[lifecycle.status]">
    <UiIcon :name="ICONS[lifecycle.status]" size="sm" />
    <span>{{ lifecycle.statusName }}</span>
    <span v-if="daysLeft !== null">
      {{ t('php.lifecycle.daysLeft', { count: daysLeft }, daysLeft) }}
    </span>
  </UiBadge>
</template>
