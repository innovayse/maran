<script setup lang="ts">
/**
 * The shell for unauthenticated screens: sign-in, second factor, first-run setup.
 * No navigation and no module catalogue — a visitor who cannot sign in must be
 * able to render this even though the panel will tell them nothing else. Owns the
 * document's single `<main>` landmark, matching {@link ./DefaultLayout.vue}.
 *
 * Two panels on a wide screen, one on a narrow one. The left panel is not
 * decoration for its own sake: a control panel's sign-in page is the first thing
 * an operator sees on a server they may have just bought, and a bare form on a
 * black field says nothing about what they have arrived at. It states what the
 * product is and what protects the account, and it is the only place in the app
 * where that is worth saying.
 *
 * The design canvas has no sign-in screen, so this is assembled from the panel's
 * own vocabulary rather than invented: the sidebar's accent brand square, the
 * `--s1` surface on a `--b1` border that every panel uses, and the accent wash
 * (`--acs`) already used behind active navigation. Below `lg` the left panel is
 * dropped rather than stacked — on a phone it would push the form under the fold,
 * and the form is the only thing there to do.
 */
import { useI18n } from 'vue-i18n'
import UiIcon, { type UiIconName } from '../components/ui/UiIcon.vue'

/** The three assurances the left panel makes, each true of what the panel actually does. */
const highlights: readonly { icon: UiIconName; key: string }[] = [
  { icon: 'server', key: 'sites' },
  { icon: 'pulse', key: 'audit' },
  { icon: 'user', key: 'twoFactor' },
]

const { t } = useI18n()
</script>

<template>
  <div class="grid min-h-screen bg-page text-text-primary lg:grid-cols-2">
    <!-- Presentational, and hidden from assistive technology on purpose: it repeats
         nothing the form needs, and reading it out before the fields would put a
         paragraph of prose between a screen-reader user and the only control here. -->
    <section
      class="relative hidden flex-col justify-between overflow-hidden border-r border-border-subtle bg-surface-1 p-10 lg:flex"
      aria-hidden="true"
    >
      <!-- The accent wash the sidebar already uses behind an active row, at page scale. -->
      <div
        class="pointer-events-none absolute -top-32 -left-32 h-96 w-96 rounded-full bg-accent/15 blur-3xl"
      ></div>
      <div
        class="pointer-events-none absolute -right-40 -bottom-40 h-[28rem] w-[28rem] rounded-full bg-violet/10 blur-3xl"
      ></div>

      <div class="relative flex items-center gap-2.5">
        <span
          class="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-accent text-lg font-bold text-white"
        >
          {{ t('app.brandInitial') }}
        </span>
        <span class="text-xl font-semibold tracking-title">{{ t('app.title') }}</span>
      </div>

      <div class="relative">
        <p class="max-w-md text-4xl leading-tight font-semibold tracking-title">
          {{ t('app.auth.panelStatement') }}
        </p>
        <p class="mt-3 max-w-md text-lg text-text-secondary">{{ t('app.auth.panelSubStatement') }}</p>
      </div>

      <ul class="relative flex flex-col gap-3">
        <li v-for="highlight in highlights" :key="highlight.key" class="flex items-start gap-3">
          <span class="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-accent-soft text-accent">
            <UiIcon :name="highlight.icon" :size="15" />
          </span>
          <span class="text-base text-text-secondary">{{ t(`app.auth.highlights.${highlight.key}`) }}</span>
        </li>
      </ul>
    </section>

    <section class="flex items-center justify-center px-5 py-12">
      <div class="w-full max-w-sm">
        <!-- The brand repeats on narrow screens, where the left panel is gone and this
             would otherwise be a form with no indication of what it belongs to. -->
        <div class="mb-6 flex items-center gap-2.5 lg:hidden">
          <span class="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-accent font-bold text-white">
            {{ t('app.brandInitial') }}
          </span>
          <h1 class="text-xl font-semibold tracking-title">{{ t('app.title') }}</h1>
        </div>

        <main>
          <!-- RouterView, not a slot: this layout is a route component with child routes, so the
               page for the active child renders here. A <slot/> would silently render nothing. -->
          <RouterView />
        </main>
      </div>
    </section>
  </div>
</template>
