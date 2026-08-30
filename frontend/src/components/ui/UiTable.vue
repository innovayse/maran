<script setup lang="ts">
/**
 * The panel's only table primitive. Renders a real `<table>` with a visible
 * `<caption>` (screen-reader and sighted users both get the table's purpose
 * announced) plus `<thead>`/`<tbody>` structure; callers supply the header
 * row via the `head` slot and body rows via the default slot, keeping the
 * primitive column-agnostic (rules/vue.md: "UI comes from components/ui").
 * The caption is visually hidden by default — most screens already show a
 * heading above the table — but stays in the accessibility tree.
 */

/** Props accepted by {@link UiTable}. */
defineProps<{
  /** Accessible name for the table, already translated by the caller. Visually hidden by default. */
  caption: string
}>()
</script>

<template>
  <!-- The design frames every table in the same panel — raised surface, subtle
       border, 10px radius — and clips the corners so the header band's fill
       stops at the rounding. `overflow-hidden` is what does the clipping, so
       the horizontal scroll lives on the inner element. -->
  <div class="overflow-hidden rounded-xl border border-border-subtle bg-surface-1">
    <div class="overflow-x-auto">
      <table class="w-full border-collapse text-left text-xs">
        <caption class="sr-only">
          {{
            caption
          }}
        </caption>
        <thead class="bg-surface-2">
          <slot name="head" />
        </thead>
        <tbody>
          <slot />
        </tbody>
      </table>
    </div>
  </div>
</template>
