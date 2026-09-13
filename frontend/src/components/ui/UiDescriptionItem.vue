<script setup lang="ts">
/**
 * One label/value pair inside a {@link UiDescriptionList}.
 *
 * The term is a prop rather than a slot because it is always a single already
 * translated string, and a prop is what makes that obvious at the call site;
 * the value is a slot, because it is as often a badge or a formatted date as
 * it is text.
 *
 * The wrapping `<div>` keeps the `<dt>` and its `<dd>` in ONE grid cell. Without
 * it the two become separate grid children and a two-column list interleaves
 * them, so a value ends up beside a label it does not belong to.
 *
 * The type sizes live here and nowhere else, which is the point: the label was
 * `text-sm` on two screens and `text-base` on a third for the same role, and
 * nothing on either screen showed the mismatch — you had to open both files.
 * `text-sm` for the label is the body step for a muted secondary line, and the
 * value sits one step above it so the eye lands on the value first
 * (rules/vue.md "Text sizes are Tailwind's own steps").
 */

/**
 * What this pair holds.
 */
const props = withDefaults(
  defineProps<{
    /** The label, already translated by the caller — the kit holds no copy. */
    term: string
    /**
     * Whether the value is machine text (a domain, a path, a version, an
     * identifier) and must be set in the monospace face, where a zero is
     * distinguishable from an O and a column of values lines up.
     */
    mono?: boolean
  }>(),
  { mono: false },
)

/**
 * The typography classes for the value.
 * @returns The class string for this item's `<dd>`.
 */
const valueClass = (): string => {
  return props.mono
    ? 'font-mono text-base text-text-primary'
    : 'text-base text-text-primary'
}
</script>

<template>
  <div>
    <dt class="text-sm text-text-secondary">{{ term }}</dt>
    <dd :class="valueClass()"><slot /></dd>
  </div>
</template>
