<script setup lang="ts">
/**
 * What a licence-locked module shows: which module was requested and which
 * tier unlocks it, with no marketing copy (rules/architecture.md: the SPA
 * only hides what the licence does not include, it never dresses that up).
 * Reached either by clicking a locked navigation entry or by the router
 * guard redirecting a deep link into a module the licence does not permit.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark
 * belongs to the layout this page is nested under.
 *
 * Drawn calmly, as one card under the page header: what is missing (the module
 * sentence and, when the backend named one, the tier) and how to get it (a plain
 * sentence and the way back). Nothing here is a warning or a sales banner — nothing
 * is broken, the licence simply does not cover this module — so the lock tile is
 * the accent wash, never amber or red. The "how to get it" part says only what is true of
 * every panel: the licence is held by whoever administers the server. It names no
 * price, no address and no tier of its own; the tier words come from the backend.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiCard from '../components/ui/UiCard.vue'
import UiIcon from '../components/ui/UiIcon.vue'
import UiIconTile from '../components/ui/UiIconTile.vue'
import UiPageHeading from '../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../components/ui/UiSectionHeading.vue'
import { useModulesStore } from '../stores/modules'
import type { PanelModule } from '../types/module'

/** Props accepted by {@link UpgradePage}. */
const props = defineProps<{
  /** Machine name of the module the user tried to reach, from the `:module` route param. */
  module: string
}>()

const { t } = useI18n()
const modulesStore = useModulesStore()

/**
 * The catalogue entry for the requested module, if the panel knows it.
 * `undefined` when the catalogue has not loaded yet or the name is unknown
 * (e.g. a stale bookmark to a module that no longer exists) — the template
 * falls back to the raw module name in that case.
 */
const catalogueEntry: ComputedRef<PanelModule | undefined> = computed(() => {
  return modulesStore.modules.find((module) => {
    return module.name === props.module
  })
})

/**
 * The sentence naming what is locked. A module the catalogue knows is named by its display name,
 * already localized by the panel; one it does not know (a deep link to a module this build does not
 * ship, `/upgrade/mail`) gets a sentence without a name, because its machine id is not a name a
 * person should read (#218).
 */
const moduleSentence: ComputedRef<string> = computed(() => {
  const displayName = catalogueEntry.value?.displayName
  return displayName === undefined
    ? t('app.upgrade.moduleUnnamed')
    : t('app.upgrade.module', { module: displayName })
})

/**
 * The sentence naming the licence tier, or `undefined` when there is none to name.
 *
 * It is built from `tierDisplayName` — the backend's own words — and NOT from `tier`, which is the
 * machine constant the panel keys on: interpolating that produced "Он доступен в тарифе addOn." on
 * a Russian panel. A catalogue that carries no `tierDisplayName` yields no sentence at all, because
 * showing nothing is honest and showing the constant is not.
 */
const tierSentence: ComputedRef<string | undefined> = computed(() => {
  const tierName = catalogueEntry.value?.tierDisplayName

  return tierName === undefined ? undefined : t('app.upgrade.tier', { tier: tierName })
})
</script>

<template>
  <section class="w-full">
    <UiPageHeading
      class="mb-4"
      :eyebrow="t('app.upgrade.eyebrow')"
      :title="t('app.upgrade.heading')"
      :subtitle="t('app.upgrade.subtitle')"
    />

    <!-- One card: what is locked, then how to get it. Two cards side by side left the first one a
         single line in a mostly empty box. -->
    <UiCard>
      <div class="flex items-start gap-4">
        <UiIconTile name="lock" size="lg" />
        <div class="min-w-0">
          <p class="font-display text-lg font-bold tracking-tight text-text-primary wrap-anywhere">
            {{ moduleSentence }}
          </p>
          <p v-if="tierSentence !== undefined" class="mt-2 text-sm text-text-secondary wrap-anywhere">
            {{ tierSentence }}
          </p>
        </div>
      </div>

      <div class="mt-5 border-t border-border-subtle pt-4">
        <UiSectionHeading class="mb-3" :title="t('app.upgrade.how.heading')" />
        <p class="mb-4 max-w-prose text-sm text-text-secondary">{{ t('app.upgrade.how.body') }}</p>
        <!-- A way back is navigation, so it looks like a link, not a button (#218). -->
        <RouterLink
          :to="{ name: 'system-status' }"
          class="inline-flex items-center gap-1 rounded-control text-sm font-medium text-accent-text underline underline-offset-4 hover:no-underline focus-visible:shadow-focus focus-visible:outline-none"
        >
          <UiIcon name="chevronLeft" size="sm" />
          {{ t('app.upgrade.backHome') }}
        </RouterLink>
      </div>
    </UiCard>
  </section>
</template>
