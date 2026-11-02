<script setup lang="ts">
/**
 * The host clock's synchronisation on the monitoring screen (issue #150): a badge naming the state,
 * and the backend's own sentence saying what it means for this server.
 *
 * Two-factor codes, sign-in sessions and certificates all read this clock, so a clock that is not
 * synchronised is drawn as a warning notice rather than as a quiet line: the sentence is the
 * backend's, shown verbatim (rules/vue.md, "the backend owns their text"), and only the badge's
 * short label is this SPA's chrome, the way the services card labels a unit's state.
 *
 * The reading is the LIVE one the store asks the agent for, loaded on its own: a refused reading is
 * an alert inside this card only, and it says the panel could not find out — never that the clock
 * is wrong.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import type { LoadingState } from '../../types/loading'
import type { ClockSync, ClockSyncState } from '../../types/monitoring'

/** Props accepted by {@link ClockSyncCard}. */
const props = defineProps<{
  /** The clock reading, or `null` before one has been answered. */
  clock: ClockSync | null
  /** Where the clock reading stands. */
  state: LoadingState
  /** The panel's own message for a refused reading, or `null` when none arrived. */
  errorMessage: string | null
}>()

const { t } = useI18n()

/** Whether the reading arrived and found the clock synchronised. */
const isSynchronized: ComputedRef<boolean> = computed(() => {
  return props.clock?.state === 'synchronized'
})

/**
 * The badge tone for a state: green only for a synchronised clock, amber for both ways of not being.
 * @param state The state the panel reported.
 * @returns The badge variant.
 */
const toneOf = (state: ClockSyncState): BadgeVariant => {
  return state === 'synchronized' ? 'success' : 'warning'
}

/**
 * The translated label for a state; a machine token, so this SPA owns its short wording.
 * @param state The state the panel reported.
 * @returns The label.
 */
const labelOf = (state: ClockSyncState): string => {
  switch (state) {
    case 'synchronized':
      return t('monitoring.clock.synchronized')
    case 'notSynchronized':
      return t('monitoring.clock.notSynchronized')
    default:
      return t('monitoring.clock.syncServiceDisabled')
  }
}
</script>

<template>
  <UiCard data-testid="clock-sync-card">
    <UiSectionHeading class="mb-4" :title="t('monitoring.clock.title')" />
    <UiLoadingRegion :state="state">
      <template #error>
        <UiAlert variant="error">{{ errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <UiSkeleton v-if="state === 'loading' || clock === null" shape="line" size="lg" />
      <div v-else class="flex flex-col gap-3">
        <div>
          <UiBadge :variant="toneOf(clock.state)">{{ labelOf(clock.state) }}</UiBadge>
        </div>
        <p v-if="isSynchronized" class="m-0 text-sm text-text-muted">{{ clock.description }}</p>
        <UiAlert v-else variant="warning">{{ clock.description }}</UiAlert>
      </div>
    </UiLoadingRegion>
  </UiCard>
</template>
