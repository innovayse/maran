<script setup lang="ts">
/**
 * How one run started by hand ended, as a badge: succeeded, failed, or stopped by one of the agent's
 * ceilings. Shared by the run history and the result dialog so the two cannot colour an outcome
 * differently.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { CronRunOutcome } from '../../types/cronRun'

/** Props accepted by {@link CronRunOutcomeBadge}. */
const props = defineProps<{
  /** The outcome to show. */
  outcome: CronRunOutcome
}>()

const { t } = useI18n()

/** The badge's tone: a stopped run is a warning, not a failure of the program itself. */
const variant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.outcome) {
    case 'succeeded':
      return 'success'
    case 'failed':
      return 'danger'
    default:
      return 'warning'
  }
})
</script>

<template>
  <UiBadge :variant="variant">{{ t(`cron.runs.outcome.${outcome}`) }}</UiBadge>
</template>
