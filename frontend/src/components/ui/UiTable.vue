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
  <div class="overflow-x-auto">
    <table class="w-full border-collapse text-left text-sm">
      <caption class="sr-only">
        {{
          caption
        }}
      </caption>
      <thead class="border-b border-slate-200 text-slate-600">
        <slot name="head" />
      </thead>
      <tbody class="divide-y divide-slate-100">
        <slot />
      </tbody>
    </table>
  </div>
</template>
