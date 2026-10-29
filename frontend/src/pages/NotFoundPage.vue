<script setup lang="ts">
/**
 * Catch-all 404 page for any path the router does not recognize. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark belongs to the
 * layout this page is nested under.
 *
 * The screen is a single `UiCard` panel: a large display "404", the page's one
 * `<h1>`, one plain sentence, and the way back. Nothing else.
 *
 * - The `<h1>` is the page's only title. Without it this was the one routed
 *   screen whose content contributed no heading to the document outline, so
 *   the title stays a real heading and the big "404" is decoration only: it
 *   is `aria-hidden`, because a screen reader would otherwise announce the
 *   number twice (once as the figure, once inside the heading's meaning).
 * - The requested path is NEVER echoed. A path is attacker-controlled text; it
 *   is not needed to say "this page does not exist", and rendering it would
 *   invite exactly the injection this page has no reason to risk. The copy is
 *   fixed, translated text only.
 * - The way back goes to the system status screen, the one route every signed-in
 *   caller can open, so the escape never leads to another dead end.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../components/ui/UiButton.vue'
import UiCard from '../components/ui/UiCard.vue'
import UiIcon from '../components/ui/UiIcon.vue'

const { t } = useI18n()

/** The HTTP status drawn as the display figure: a number, not a translatable phrase. */
const statusCode = '404'
</script>

<template>
  <section class="w-full">
    <UiCard class="flex flex-col items-center gap-4 px-6 py-16 text-center">
      <!-- Decoration: the heading below carries the meaning. -->
      <p
        class="font-display text-8xl leading-none font-black tracking-tight text-accent-text select-none sm:text-9xl"
        aria-hidden="true"
      >
        {{ statusCode }}
      </p>
      <h1 class="font-display text-2xl font-extrabold tracking-caps text-text-primary uppercase wrap-anywhere">
        {{ t('app.notFound.heading') }}
      </h1>
      <p class="max-w-100 text-sm leading-normal text-text-secondary wrap-anywhere">
        {{ t('app.notFound.description') }}
      </p>
      <UiButton class="mt-2 max-w-full" :to="{ name: 'system-status' }">
        <UiIcon name="house" aria-hidden="true" />
        <span class="wrap-anywhere">{{ t('app.notFound.backHome') }}</span>
      </UiButton>
    </UiCard>
  </section>
</template>
