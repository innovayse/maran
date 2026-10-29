<script setup lang="ts">
/**
 * The product's mark and name side by side: the one wordmark the shell draws, in the app bar and
 * at the head of the navigation drawer.
 *
 * Both places need the same mark, the same display type and the same gap, so they are one
 * component: two hand-copied spans are how one of them ends up a step off the other. It is plain
 * inline content with no heading semantics and no link — the app bar wraps it in a link to the
 * landing screen, the drawer leaves it as a label — because an authenticated page already has its
 * `<h1>` (the page's own title) and a second one would give the document two.
 *
 * **The name can step aside on the narrowest bars.** With `collapsible`, the name becomes
 * screen-reader-only when the nearest `@container` ancestor is narrower than Tailwind's `xs`
 * (320px). The app bar at a 320px viewport is a 304px container holding the menu, mark, search,
 * tasks badge and language controls, and the name is the one part that is decoration rather than
 * a control. From `sm` of the window up, where the app bar also holds the theme switch, it steps
 * aside in a container under 26rem instead, which is what 768px of window at a 200% text size is.
 * It is hidden by clipping, not `display: none`, so the link around it keeps its
 * accessible name at every width.
 */
import UiBrandMark from '../ui/UiBrandMark.vue'
import { useI18n } from 'vue-i18n'

/** Props accepted by {@link ShellWordmark}. */
defineProps<{
  /** Whether the name steps aside (stays readable to assistive technology) in a container under 320px. */
  collapsible?: boolean
}>()

const { t } = useI18n()
</script>

<template>
  <span class="inline-flex min-w-0 items-center gap-2.5">
    <UiBrandMark :size="30" />
    <span
      class="font-display text-base font-black tracking-caps text-text-primary uppercase"
      :class="collapsible === true ? '@max-xs:sr-only sm:@max-[26rem]:sr-only' : ''"
    >
      {{ t('app.title') }}
    </span>
  </span>
</template>
