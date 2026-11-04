<script setup lang="ts">
/**
 * The verification state of one backup, drawn as a {@link UiBadge} in the tone its meaning deserves.
 *
 * `notVerified` is `neutral` and NOT `warning`: a backup nobody has re-read yet is the ordinary
 * state of a row taken before verification existed, and a tone that says "look at this" beside it
 * would drown out the one that matters. `verified` is `success`; `failed` is `danger`, because a
 * copy that cannot be read back is the fact a list of backups must never imply the opposite of.
 *
 * The label is the backend's (`verificationStateName` on the row), printed as given.
 */
import { computed, type ComputedRef } from 'vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { BackupVerificationState } from '../../types/backup'

/** Props accepted by {@link BackupVerificationBadge}. */
const props = defineProps<{
  /** The backup's verification state, exactly as the panel reported it. */
  state: BackupVerificationState
  /** The state as the operator reads it, already in their language; the badge's text. */
  label: string
}>()

/** The tone the badge is drawn in. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.state) {
    case 'verified':
      return 'success'
    case 'failed':
      return 'danger'
    case 'notVerified':
    default:
      return 'neutral'
  }
})
</script>

<template>
  <!-- `data-testid` so a spec reads THIS badge's text, not a substring of the row. -->
  <UiBadge :variant="variant" data-testid="backup-verification">{{ label }}</UiBadge>
</template>
