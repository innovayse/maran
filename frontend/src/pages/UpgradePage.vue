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
 * Drawn calmly, as two cards under the page header: what is missing (the module
 * sentence and, when the backend named one, the tier) and how to get it (a plain
 * sentence and the way back). Nothing here is a warning or a sales banner — nothing
 * is broken, the licence simply does not cover this module — so the lock tile is
 * the accent wash, never amber or red. The second card says only what is true of
 * every panel: the licence is held by whoever administers the server. It names no
 * price, no address and no tier of its own; the tier words come from the backend.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../components/ui/UiButton.vue'
import UiCard from '../components/ui/UiCard.vue'
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
 * The module as an operator reads it: the backend-localized `displayName` when the catalogue
 * carries one, and the machine name only when it does not (an unknown module, or a panel older
 * than the field). The SPA never translates it — a module is a server-side concept and a
 * marketplace module is unknown when this bundle is built (rules/vue.md).
 */
const moduleName: ComputedRef<string> = computed(() => {
  return catalogueEntry.value?.displayName ?? props.module
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

    <div class="grid gap-4 lg:grid-cols-3">
      <UiCard class="lg:col-span-2">
        <div class="flex items-start gap-4">
          <UiIconTile name="lock" size="lg" />
          <div class="min-w-0">
            <p class="font-display text-lg font-bold tracking-tight text-text-primary wrap-anywhere">
              {{ t('app.upgrade.module', { module: moduleName }) }}
            </p>
            <p v-if="tierSentence !== undefined" class="mt-2 text-base text-text-secondary wrap-anywhere">
              {{ tierSentence }}
            </p>
          </div>
        </div>
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-3" :title="t('app.upgrade.how.heading')" />
        <p class="mb-4 text-sm text-text-secondary">{{ t('app.upgrade.how.body') }}</p>
        <UiButton variant="secondary" :to="{ name: 'system-status' }">{{ t('app.upgrade.backHome') }}</UiButton>
      </UiCard>
    </div>
  </section>
</template>
