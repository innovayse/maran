<script setup lang="ts">
/**
 * Authenticated shell: header (app title, locale switcher), a `<nav>`
 * sidebar built from the module catalogue, and the document's single
 * `<main>` landmark wrapping the routed page (rules/vue.md: "Exactly one
 * `<main>` per document" — this layout is the one place that renders it;
 * pages render `<section>`). Every route that belongs to the authenticated
 * area nests under this layout in the router.
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../components/ui/UiButton.vue'
import UiNavLink from '../components/ui/UiNavLink.vue'
import { onMounted } from 'vue'
import { useNavigation } from '../composables/useNavigation'
import { useModulesStore } from '../stores/modules'
import { useLocaleStore } from '../stores/locale'
import { SUPPORTED_LOCALES } from '../i18n'
import type { AppLocale } from '../i18n'

const { t } = useI18n()
const localeStore = useLocaleStore()
const entries = useNavigation()
const modulesStore = useModulesStore()

/**
 * Ensures the module catalogue is loaded, since the navigation is built from it. The router guard
 * only fetches it for gated routes, so without this the sidebar would stay empty on every ungated
 * page — which is every page until a module ships one.
 * @returns Resolves once the catalogue request has settled.
 */
const ensureCatalogueLoaded = async (): Promise<void> => {
  if (!modulesStore.isLoaded) {
    await modulesStore.load()
  }
}

onMounted(ensureCatalogueLoaded)

/** Locales offered by the switcher, in menu order. */
const locales: ComputedRef<readonly AppLocale[]> = computed(() => SUPPORTED_LOCALES)

/**
 * Switches the interface language. The locale store is the single source of
 * truth (rules/vue.md "One locale, one source of truth") — `main.ts` keeps
 * `vue-i18n` and `<html lang>` in step with it, and `useApi` reads it for
 * `Accept-Language`, so changing it here is enough to change everything.
 * @param locale The language to switch to.
 * @returns Nothing; the store updates synchronously.
 */
const selectLocale = (locale: AppLocale): void => {
  localeStore.setLocale(locale)
}
</script>

<template>
  <div class="min-h-screen bg-slate-50 text-slate-900">
    <header class="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-4">
      <h1 class="text-lg font-semibold">{{ t('app.title') }}</h1>
      <div class="flex items-center gap-1" role="group" :aria-label="t('app.locale.switcherLabel')">
        <UiButton
          v-for="locale in locales"
          :key="locale"
          :variant="localeStore.current === locale ? 'secondary' : 'ghost'"
          :aria-pressed="localeStore.current === locale"
          @click="selectLocale(locale)"
        >
          {{ t(`app.locale.names.${locale}`) }}
        </UiButton>
      </div>
    </header>
    <div class="mx-auto flex max-w-6xl gap-6 px-6 py-6">
      <nav class="w-56 shrink-0" :aria-label="t('app.nav.ariaLabel')">
        <ul class="flex flex-col gap-1">
          <li v-for="entry in entries" :key="entry.key">
            <UiNavLink :to="entry.target" :locked="entry.locked">
              {{ entry.labelKey === null ? entry.label : t(entry.labelKey) }}
              <template v-if="entry.locked" #badge>
                <span class="rounded bg-slate-200 px-1.5 py-0.5 text-xs text-slate-600">{{
                  t('app.nav.lockedBadge')
                }}</span>
              </template>
            </UiNavLink>
          </li>
        </ul>
      </nav>
      <main class="min-w-0 flex-1">
        <!-- RouterView, not a slot: this layout is a route component with child routes, so the
             page for the active child renders here. A <slot/> would silently render nothing. -->
        <RouterView />
      </main>
    </div>
  </div>
</template>
