<script setup lang="ts">
/**
 * The status of one backup, drawn as a {@link UiBadge} in the tone its meaning deserves.
 *
 * Its own component rather than a `variant` function on the page, because the mapping is a
 * judgement about the domain — which states an operator must not mistake for one another — and it
 * will be needed by every screen that shows a backup, of which there will be more than one.
 *
 * `running` is `info` and NOT `warning`: a backup in progress is the normal case, and a tone that
 * says "look at this" beside every row taken in the last minute teaches an operator to ignore the
 * colour. `failed` is `danger` because a failed backup is a copy that does not exist, which is
 * exactly the fact a list of backups can most dangerously imply the opposite of.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { BackupStatus } from '../../types/backup'

/** Props accepted by {@link BackupStatusBadge}. */
const props = defineProps<{
  /** The backup's status, exactly as the panel reported it. */
  status: BackupStatus
}>()

const { t } = useI18n()

/** The tone the badge is drawn in. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.status) {
    case 'completed':
      return 'success'
    case 'failed':
      return 'danger'
    case 'running':
    default:
      return 'info'
  }
})

/**
 * The label for the status.
 *
 * This is the SPA's own chrome and not a server outcome, so it is translated here: the three
 * values are a closed set this bundle was compiled against, not text the panel sends.
 */
const label: ComputedRef<string> = computed(() => {
  return t(`backups.status.${props.status}`)
})
</script>

<template>
  <UiBadge :variant="variant">{{ label }}</UiBadge>
</template>
