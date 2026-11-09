<script setup lang="ts">
/**
 * The first-run checklist card: what a new server still needs before it earns its keep, one row per
 * step with whether it is done and where to do it.
 *
 * **The panel decides everything but the words.** Which steps exist, in which order, whether each
 * is done and whether the whole list is complete come from the panel (derived from what exists, so
 * there is no skipping and nothing to go stale). The card draws nothing at all until the panel has
 * answered, and nothing once it says `complete`; this component never works out "done" itself.
 *
 * **A step the panel could not check is shown as that**, never as "to do": a row that claimed a
 * step was open when its source was merely unreadable would send the operator to redo finished
 * work. Each state is a word in a badge, so colour and icon are never the only signal.
 *
 * **Destinations are a closed set.** The panel sends a name, this component maps it to a route the
 * SPA has, and a step whose page does not exist yet (the site certificates screen is planned)
 * draws the row without a link; the SPA invents no destination.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { useFirstRunStore } from '../../stores/firstRun'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiIcon, { type UiIconName } from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { FirstRunStep } from '../../types/firstRun'

/** Which stage of a step the row shows. */
type StepStage = 'done' | 'todo' | 'unavailable'

/** The step keys this bundle has wording for, in the order the panel lists them. */
const KNOWN_STEPS: readonly string[] = ['plan', 'account', 'site', 'backupDestination', 'certificate']

/**
 * The route each destination name the panel sends opens. `null` is a step whose page is not built
 * yet; a name missing here is not offered either.
 */
const DESTINATIONS: Readonly<Record<string, string | null>> = {
  plans: 'plans',
  accounts: 'accounts',
  sites: 'sites',
  backupDestinations: 'backup-destinations',
  certificates: null,
}

/** The glyph, badge tone and icon colour of each stage. */
const STAGES: Record<StepStage, { icon: UiIconName; badge: BadgeVariant; tone: string }> = {
  done: { icon: 'circleCheck', badge: 'success', tone: 'text-success' },
  todo: { icon: 'circleDashed', badge: 'neutral', tone: 'text-text-muted' },
  unavailable: { icon: 'triangleAlert', badge: 'warning', tone: 'text-warning' },
}

const { t } = useI18n()
const router = useRouter()
const store = useFirstRunStore()

/** The checklist to draw, or `null` while unread, unreadable, or complete. */
const open: ComputedRef<{ steps: FirstRunStep[]; done: number } | null> = computed(() => {
  const checklist = store.checklist
  if (checklist === null || checklist.complete) {
    return null
  }
  const steps = checklist.steps.filter((step) => {
    return KNOWN_STEPS.includes(step.key)
  })
  return { steps, done: steps.filter((step) => {return stageOf(step) === 'done'}).length }
})

/**
 * Reads which stage a step is at.
 * @param step The step.
 * @returns `done`, `todo`, or `unavailable` when the panel could not check it.
 */
const stageOf = (step: FirstRunStep): StepStage => {
  if (step.done.state === 'unavailable') {
    return 'unavailable'
  }
  return step.done.data ? 'done' : 'todo'
}

/**
 * Maps a step's destination name to a route this bundle has.
 * @param step The step.
 * @returns The route, or `null` when the step has no page to open.
 */
const destinationOf = (step: FirstRunStep): RouteLocationRaw | null => {
  const name = DESTINATIONS[step.routeName]
  return name !== undefined && name !== null && router.hasRoute(name) ? { name } : null
}

onMounted(store.load)
</script>

<template>
  <UiCard v-if="open !== null" data-testid="first-run-checklist">
    <div class="flex flex-col gap-4">
      <UiSectionHeading :title="t('firstRun.title')" :subtitle="t('firstRun.subtitle')" />
      <p class="text-sm text-text-secondary">{{ t('firstRun.progress', { done: open.done, total: open.steps.length }) }}</p>
      <ul class="flex flex-col" :aria-label="t('firstRun.listLabel')">
        <li
          v-for="step in open.steps"
          :key="step.key"
          class="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-border-subtle py-3 first:pt-0 last:border-b-0 last:pb-0"
          :data-step="step.key"
        >
          <UiIcon :name="STAGES[stageOf(step)].icon" size="lg" :class="STAGES[stageOf(step)].tone" />
          <div class="flex min-w-0 flex-1 flex-col gap-0.5">
            <p class="text-sm font-semibold text-text-primary">{{ t(`firstRun.steps.${step.key}.title`) }}</p>
            <p class="text-sm text-text-secondary">{{ t(`firstRun.steps.${step.key}.description`) }}</p>
          </div>
          <UiBadge :variant="STAGES[stageOf(step)].badge">{{ t(`firstRun.stage.${stageOf(step)}`) }}</UiBadge>
          <UiButton
            v-if="stageOf(step) !== 'done' && destinationOf(step) !== null"
            :to="destinationOf(step) ?? undefined"
            variant="secondary"
          >
            {{ t(`firstRun.steps.${step.key}.action`) }}
            <UiIcon name="arrowRight" size="sm" />
          </UiButton>
        </li>
      </ul>
    </div>
  </UiCard>
</template>
