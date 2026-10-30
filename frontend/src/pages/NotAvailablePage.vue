<script setup lang="ts">
/**
 * The screen a customer lands on when they reach a page that is not part of their area: an
 * administrator's screen (the role guard), or a module their catalogue does not list (the module
 * access guard). Issue #202.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark belongs to the layout. The
 * page heading is the document's one `<h1>`.
 *
 * The wording is fixed, translated chrome and deliberately says nothing about who may use the page
 * or why: the guard that sent the caller here is advice, the endpoints are the boundary, and the
 * requested path is never echoed (it is caller-controlled text). The way back leads to the system
 * status screen, the one route every signed-in caller can open.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../components/ui/UiButton.vue'
import UiCard from '../components/ui/UiCard.vue'
import UiIcon from '../components/ui/UiIcon.vue'
import UiPageHeading from '../components/ui/UiPageHeading.vue'

const { t } = useI18n()
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading :title="t('app.notAvailable.heading')" :subtitle="t('app.notAvailable.description')" />
    <UiCard class="flex flex-col items-start gap-4">
      <p class="text-sm leading-normal text-text-secondary wrap-anywhere">
        {{ t('app.notAvailable.hint') }}
      </p>
      <UiButton class="max-w-full" :to="{ name: 'system-status' }">
        <UiIcon name="house" aria-hidden="true" />
        <span class="wrap-anywhere">{{ t('app.notAvailable.backHome') }}</span>
      </UiButton>
    </UiCard>
  </section>
</template>
