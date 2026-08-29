<script setup lang="ts">
/**
 * Bare, centred shell for unauthenticated screens (sign-in, password reset).
 * No navigation, no module catalogue dependency — a locked-out or
 * not-yet-authenticated user must be able to render this layout even if the
 * module catalogue request itself would require a session. Owns the
 * document's single `<main>` landmark, matching {@link ../layouts/DefaultLayout.vue}.
 */
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <div class="flex min-h-screen items-center justify-center bg-slate-50 px-4 text-slate-900">
    <div class="w-full max-w-sm">
      <h1 class="mb-6 text-center text-lg font-semibold">{{ t('app.title') }}</h1>
      <main>
        <!-- RouterView, not a slot: this layout is a route component with child routes, so the
             page for the active child renders here. A <slot/> would silently render nothing. -->
        <RouterView />
      </main>
    </div>
  </div>
</template>
