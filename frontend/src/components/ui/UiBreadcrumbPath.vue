<script setup lang="ts">
/**
 * A path bar: the folder a person is in, drawn as one link per segment from the root down, so any
 * ancestor is one press away. The last segment is the current folder: it is plain text with
 * `aria-current="page"`, not a link to where the person already is.
 *
 * It is a labelled `<nav>` holding an ordered list, so a screen-reader user gets a landmark that is
 * told apart from the sidebar and the page breadcrumb by its required `label`, and hears the
 * position of each segment. Every segment is a real link (`RouterLink`), so it is reachable by Tab,
 * opens with Enter, and keeps middle-click and "open in new tab".
 *
 * The component knows nothing about routes: the caller says where a folder's link goes with
 * `target`. It also knows nothing about the segments' meaning, so it makes no claim that a path is
 * safe or inside anything; it only draws text. Long names wrap (`wrap-anywhere`) rather than widen
 * the page, which matters in Armenian at 390px.
 *
 * **Look.** Links are `text-accent-text` (the accent for text on the dark surfaces) at the body
 * step, with the shared `shadow-focus` ring on keyboard focus; the current segment is primary
 * text, semibold, and the separators are decorative `chevronRight` glyphs (`aria-hidden`).
 */
import { computed, type ComputedRef } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import UiIcon from './UiIcon.vue'
import { pathTrail } from '../../utils/filePath'

/** Props accepted by {@link UiBreadcrumbPath}. */
const props = defineProps<{
  /** Accessible name of the path bar's navigation landmark, already translated ("Folder path"). */
  label: string
  /** The name of the root segment, already translated ("Home"). */
  rootLabel: string
  /** The current folder, relative to the root, `/`-separated; empty for the root. */
  path: string
  /** Where the link for a folder goes; called with that folder's path (empty for the root). */
  target: (path: string) => RouteLocationRaw
}>()

/** One drawn segment of the bar. */
interface BreadcrumbSegment {
  /** The text drawn: the root label or the folder's own name. */
  name: string
  /** The folder's path, which the link goes to. */
  path: string
  /** Whether this is the current folder. */
  current: boolean
}

/** The segments from the root to the current folder. */
const segments: ComputedRef<BreadcrumbSegment[]> = computed(() => {
  const trail = [{ name: props.rootLabel, path: '' }, ...pathTrail(props.path)]
  return trail.map((segment, index) => {
    return { ...segment, current: index === trail.length - 1 }
  })
})
</script>

<template>
  <nav :aria-label="label">
    <ol class="flex flex-wrap items-center gap-x-1 gap-y-1 text-sm">
      <li v-for="(segment, index) in segments" :key="segment.path" class="flex items-center gap-1">
        <UiIcon v-if="index > 0" name="chevronRight" size="sm" class="text-text-muted" />
        <span v-if="segment.current" aria-current="page" class="font-semibold text-text-primary wrap-anywhere">{{
          segment.name
        }}</span>
        <RouterLink
          v-else
          :to="target(segment.path)"
          class="rounded-row px-1 text-accent-text underline underline-offset-4 wrap-anywhere hover:no-underline focus-visible:shadow-focus focus-visible:outline-none"
          >{{ segment.name }}</RouterLink
        >
      </li>
    </ol>
  </nav>
</template>
