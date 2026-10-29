<script setup lang="ts">
/**
 * The shell for unauthenticated screens: sign-in, second factor, first-run setup, password reset,
 * invitation. No navigation and no module catalogue — a visitor who cannot sign in must be able
 * to render this even though the panel will tell them nothing else. Owns the document's single
 * `<main>` landmark, matching {@link ./DefaultLayout.vue}.
 *
 * It is a full-screen page with **no shell chrome**: no app bar, no sidebar, no tab bar, so a
 * spec that asserts the enrolment screen shows no navigation landmark finds none.
 *
 * **Two columns from `lg` (1024px), one below.**
 *
 * - **Left, an information panel.** A sign-in page is the first thing an operator sees on a server
 *   they may have just bought, and a bare form says nothing about what they have arrived at. The
 *   panel names the product (the keystone and the display wordmark), states what it is for, and
 *   lists the three assurances the panel actually keeps. It sits on the panel surface with two
 *   accent blooms, the redesign's look at page scale. It is **presentational and `aria-hidden`**:
 *   it repeats nothing the form needs, and reading prose out before the fields would stand
 *   between a screen-reader user and the only control on the screen. It is not rendered below
 *   `lg`, where it would push the form under the fold and the form is the only thing to do.
 * - **Right, the form.** One `max-w-md` column centred in the remaining half: the brand, the card,
 *   a small credit line, and the language switcher pinned top-right. A visitor who cannot read the
 *   screen must be able to change its language before signing in, and the switcher is the same
 *   component the app bar uses, so the locale store is still the one source of truth.
 *
 * **One `h1`, and it lives in the right column.** The screen has exactly one top-level heading:
 * the wordmark above the card, so the outline reads "Maran › Sign in" and no page repeats the
 * brand (pages open with their own `h2`). From `lg` the left panel already draws the brand
 * visually, so the right column's wordmark turns `sr-only` there rather than being drawn twice;
 * the left panel's copy of the wordmark is a plain decorative element, because a heading inside an
 * `aria-hidden` region would not be in the outline at all. (The authenticated shell differs on
 * purpose: there the page's own title is the `h1` and its wordmark is a plain link.)
 *
 * **The backdrop belongs to the right column.** Its blooms are `--ac` graphics, never under text
 * (the text is on the card's own `--s1`, above the backdrop at `z-10`). It is clipped by its own
 * `overflow-hidden` wrapper, not by the column, because a blurred circle larger than its box
 * otherwise creates scrollbars while the column must stay un-clipped for the language menu. The
 * credit line is plain text, never a link out: an unauthenticated screen of a control panel has no
 * business sending its visitor to another site.
 *
 * Content is centred in a `min-h-dvh` flex container with 16px of padding (24px from `sm`):
 * centred on a tall desktop, scrollable on a short phone held sideways. `dvh` so a phone's
 * collapsing address bar does not leave the form under the fold. Long Armenian words wrap
 * (`wrap-anywhere`) rather than widen the page.
 */
import ShellLocaleSwitcher from '../components/shell/ShellLocaleSwitcher.vue'
import { useI18n } from 'vue-i18n'
import UiBrandMark from '../components/ui/UiBrandMark.vue'
import UiIconTile from '../components/ui/UiIconTile.vue'
import type { UiIconName } from '../components/ui/UiIcon.vue'

/** The three assurances the left panel makes, each true of what the panel actually does. */
const highlights: readonly { icon: UiIconName; key: string }[] = [
  { icon: 'server', key: 'sites' },
  { icon: 'pulse', key: 'audit' },
  { icon: 'shieldCheck', key: 'twoFactor' },
]

const { t } = useI18n()

/** The year printed in the credit line, taken once from the visitor's clock. */
const year: number = new Date().getFullYear()
</script>

<template>
  <div class="grid min-h-dvh bg-page text-text-primary lg:grid-cols-2">
    <aside
      class="relative hidden flex-col justify-between gap-10 overflow-hidden border-r border-border-subtle bg-surface-1 p-12 lg:flex xl:p-16"
      aria-hidden="true"
    >
      <div
        class="pointer-events-none absolute -top-32 -left-32 size-[28rem] rounded-full bg-accent/15 blur-3xl"
      ></div>
      <div
        class="pointer-events-none absolute -right-40 -bottom-40 size-[30rem] rounded-full bg-accent/10 blur-3xl"
      ></div>

      <div class="relative flex items-center gap-3">
        <UiBrandMark :size="40" />
        <span class="font-display text-2xl font-black tracking-caps uppercase">{{ t('app.title') }}</span>
      </div>

      <div class="relative">
        <p
          class="max-w-lg font-display text-4xl leading-tight font-extrabold tracking-title wrap-anywhere xl:text-5xl"
        >
          {{ t('app.auth.panelStatement') }}
        </p>
        <p class="mt-4 max-w-md text-lg text-text-secondary wrap-anywhere">
          {{ t('app.auth.panelSubStatement') }}
        </p>
      </div>

      <ul class="relative flex max-w-md flex-col gap-4">
        <li v-for="highlight in highlights" :key="highlight.key" class="flex items-start gap-4">
          <UiIconTile :name="highlight.icon" />
          <span class="min-w-0 pt-2 text-base text-text-secondary wrap-anywhere">{{
            t(`app.auth.highlights.${highlight.key}`)
          }}</span>
        </li>
      </ul>
    </aside>

    <div class="relative min-w-0">
      <div class="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden="true">
        <div
          class="absolute top-1/2 left-1/2 size-[80vmin] -translate-x-1/2 -translate-y-1/2 rounded-full bg-accent/15 blur-3xl"
        ></div>
        <div class="absolute -top-32 -right-24 size-96 rounded-full bg-accent/10 blur-3xl"></div>
        <div class="absolute -bottom-24 -left-20 size-80 rounded-full bg-accent/10 blur-3xl"></div>
      </div>

      <div class="absolute top-4 right-4 z-20 sm:top-6 sm:right-6">
        <ShellLocaleSwitcher />
      </div>

      <main class="relative z-10 flex min-h-dvh items-center justify-center p-4 sm:p-6">
        <div class="w-full max-w-md">
          <!-- The margin sits on this outer box so the inner one can go sr-only (which sets its own
               margin) without leaving an empty 32px gap above the card from lg up. -->
          <div class="mb-8 lg:mb-0">
            <div class="flex items-center justify-center gap-3 lg:sr-only">
              <UiBrandMark :size="40" />
              <h1 class="font-display text-2xl font-black tracking-caps uppercase wrap-anywhere">
                {{ t('app.title') }}
              </h1>
            </div>
          </div>

          <div class="rounded-panel border border-border-subtle bg-surface-1 p-6 sm:p-8">
            <!-- RouterView, not a slot: this layout is a route component with child routes, so the
                 page for the active child renders here. A <slot/> would silently render nothing. -->
            <RouterView />
          </div>

          <p class="mt-6 text-center text-xs text-text-muted wrap-anywhere">
            {{ t('app.shell.authCredit', { year }) }}
          </p>
        </div>
      </main>
    </div>
  </div>
</template>
