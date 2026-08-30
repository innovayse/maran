<script setup lang="ts">
/**
 * Single-path line icon used by the application shell.
 *
 * The paths are copied verbatim from the design canvas's `ICON` map — only
 * the entries the shell actually draws — and are rendered exactly as the
 * design draws them: a 24×24 viewBox, `fill:none`, `stroke:currentColor`,
 * `stroke-width:1.7`, round caps and joins.
 *
 * It lives beside the layouts rather than in `src/components/ui/` because it
 * is chrome for this shell alone and carries no interactive behaviour; the
 * moment a second area needs it, it moves down into the kit.
 *
 * Always decorative: an icon here sits next to its own text label, so it is
 * hidden from assistive technology rather than given a duplicate name
 * (rules/vue.md: "a decorative icon is declared rather than omitted").
 */

/** Name of a shell icon; each maps to one SVG path in {@link ICON_PATHS}. */
export type UiIconName =
  /** Activity trace — the shell's own system status entry. */
  | 'pulse'
  /** Four tiles — a module reported by the panel's catalogue. */
  | 'grid'
  /** Stacked racks — the server picker. */
  | 'server'
  /** Magnifier — the command/search trigger. */
  | 'search'
  /** Crescent — the theme toggle. */
  | 'moon'
  | 'sun'
  | 'globe'
  | 'sparkle'
  | 'bell'
  | 'user'
  /** Collapse the sidebar into the rail. */
  | 'chevronLeft'
  /** Expand the rail back into the sidebar. */
  | 'chevronRight'
  /** Opens a picker. */
  | 'chevronDown'

/**
 * The design canvas's `ICON` map, restricted to the glyphs this shell draws.
 * `search` is the one composed entry: the design draws it as a circle plus a
 * line, expressed here as the equivalent single path so every icon in the
 * shell renders through one code path.
 */
const ICON_PATHS: Record<UiIconName, string> = {
  pulse: 'M3 12h4l2-6 3 12 2-6h5',
  grid: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z',
  server: 'M4 5h16v5H4zM4 14h16v5H4zM7 7.5h.01M7 16.5h.01',
  search: 'M18 11a7 7 0 11-14 0 7 7 0 0114 0zM20 20l-4-4',
  moon: 'M12 3a9 9 0 109 9 7 7 0 01-9-9z',
  sparkle: 'M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z',
  bell: 'M18 15V10a6 6 0 10-12 0v5l-1.5 3h15zM10 21h4',
  user: 'M12 12a4 4 0 100-8 4 4 0 000 8zM4 21c0-4 3.6-6 8-6s8 2 8 6',
  globe: 'M12 3a9 9 0 100 18 9 9 0 000-18zM3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18',
  sun: 'M12 8a4 4 0 100 8 4 4 0 000-8zM12 2v2M12 20v2M4 12H2M22 12h-2M5.6 5.6L4.2 4.2M19.8 19.8l-1.4-1.4M5.6 18.4L4.2 19.8M19.8 4.2l-1.4 1.4',
  chevronLeft: 'M15 6l-6 6 6 6',
  chevronRight: 'M9 6l6 6-6 6',
  chevronDown: 'M6 9l6 6 6-6',
}

/** Props accepted by {@link UiIcon}. */
withDefaults(
  defineProps<{
    /** Which glyph to draw. */
    name: UiIconName
    /** Edge length in CSS pixels; the design draws shell icons at 15 or 16. */
    size?: number
  }>(),
  { size: 15 },
)
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
    class="shrink-0"
  >
    <path :d="ICON_PATHS[name]" />
  </svg>
</template>
