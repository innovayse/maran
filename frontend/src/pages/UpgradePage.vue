<script setup lang="ts">
/**
 * What a licence-locked module shows: which module was requested and which
 * tier unlocks it, with no marketing copy (rules/architecture.md: the SPA
 * only hides what the licence does not include, it never dresses that up).
 * Reached either by clicking a locked navigation entry or by the router
 * guard redirecting a deep link into a module the licence does not permit.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark
 * belongs to the layout this page is nested under.
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../components/ui/UiCard.vue'
import UiNavLink from '../components/ui/UiNavLink.vue'
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
const catalogueEntry: ComputedRef<PanelModule | undefined> = computed(() =>
  modulesStore.modules.find((module) => module.name === props.module),
)
</script>

<template>
  <section class="mx-auto max-w-xl">
    <h1 class="mb-4 text-2xl font-semibold">{{ t('app.upgrade.heading') }}</h1>
    <UiCard>
      <p class="mb-2">{{ t('app.upgrade.module', { module }) }}</p>
      <p v-if="catalogueEntry" class="mb-4">{{ t('app.upgrade.tier', { tier: catalogueEntry.tier }) }}</p>
      <UiNavLink :to="{ name: 'system-status' }">{{ t('app.upgrade.backHome') }}</UiNavLink>
    </UiCard>
  </section>
</template>
