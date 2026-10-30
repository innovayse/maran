<script setup lang="ts">
/**
 * A link that leaves the panel: a real `<a>` to another site, opened in a new tab.
 *
 * `rel="noopener noreferrer"` keeps the opened page from reaching back into the panel through
 * `window.opener` and from learning the panel's address. The new tab is announced to assistive
 * technology by a screen-reader-only suffix, because a link that silently opens a tab moves the
 * reader somewhere they did not expect.
 *
 * It reads as a link everywhere it sits: the inherited text colour gets a semibold weight, and
 * the accent on hover. The focus ring is the kit's `shadow-focus`.
 */
import { useI18n } from 'vue-i18n'

/** Props accepted by {@link UiExternalLink}. */
defineProps<{
  /** The absolute address the link opens. */
  href: string
}>()

const { t } = useI18n()
</script>

<template>
  <a
    :href="href"
    target="_blank"
    rel="noopener noreferrer"
    class="rounded-inset font-semibold text-text-secondary transition-colors hover:text-accent focus-visible:shadow-focus focus-visible:outline-none"
  >
    <slot /><span class="sr-only"> ({{ t('app.link.opensInNewTab') }})</span>
  </a>
</template>
