<script setup lang="ts">
/**
 * One label/value pair inside a {@link UiDescriptionList}.
 *
 * The term is a prop rather than a slot because it is always a single already translated string,
 * and a prop is what makes that obvious at the call site; the value is a slot, because it is as
 * often a badge or a formatted date as it is text.
 *
 * The wrapping `<div>` keeps the `<dt>` and its `<dd>` in ONE grid cell. Without it the two become
 * separate grid children and a two-column list interleaves them, so a value ends up beside a label
 * it does not belong to. It also carries `min-w-0`: a grid child will not shrink below its content,
 * so one long unbroken value would otherwise push the whole panel into a horizontal scroll.
 *
 * The type sizes live here and nowhere else, which is the point: the label was `text-sm` on two
 * screens and `text-base` on a third for the same role, and nothing on either screen showed the
 * mismatch. The label is the design's small uppercase tag (`text-xs`, display face, wide tracking,
 * `text-text-muted` — 5.36:1 on a panel, 5.03:1 on the raised surface, 4.62:1 on the third step in
 * dark; 5.41 / 4.91 / 4.57 in light). Uppercase labels wrap anywhere: a long Armenian or Russian
 * label would otherwise be wider than a phone-width column. The value sits one step above it in
 * `text-sm`, primary colour, and wraps anywhere too, for the same reason (a domain or a path has no
 * spaces to break at).
 */

/** What this pair holds. */
const props = withDefaults(
  defineProps<{
    /** The label, already translated by the caller — the kit holds no copy. */
    term: string
    /**
     * Whether the value is machine text (a domain, a path, a version, an identifier) and must be
     * set in the monospace face, where a zero is distinguishable from an O and a column of values
     * lines up.
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
  return props.mono ? 'font-mono tabular-nums' : ''
}
</script>

<template>
  <div class="min-w-0">
    <dt class="font-display text-xs font-extrabold tracking-caps text-text-muted uppercase wrap-anywhere">
      {{ term }}
    </dt>
    <dd class="mt-1 text-sm text-text-primary wrap-anywhere" :class="valueClass()">
      <slot />
    </dd>
  </div>
</template>
