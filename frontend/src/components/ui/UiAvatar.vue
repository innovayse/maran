<script setup lang="ts">
/**
 * A round initials badge: the single place the panel draws one, for the shell's identity card, the
 * accounts table and the audit journal alike, so the three can never drift apart in colour, size or
 * the letters they show.
 *
 * **White on `bg-accent-fill`** (5.14:1) is the only text-on-accent pairing this panel allows; the
 * badge never uses `bg-accent`, which is 3.89:1 under white.
 *
 * **Decorative.** Every place that draws it also prints the name beside it (or names the control
 * around it), so it is hidden from assistive technology instead of reading the initials out as a
 * stray word. That is also why `name` is the only input and there is no label prop.
 *
 * The letters come from {@link initialsOf}, which survives Armenian and Cyrillic names, names with
 * no letters and names that grow when upper-cased, and never returns more than two characters.
 * A caller with no name to show (nobody signed in) passes the default slot instead: it replaces the
 * initials inside the same disc, so a placeholder glyph is the same size and colour as a person.
 */
import { computed, type ComputedRef } from 'vue'
import { initialsOf } from '../../utils/initialsOf'

/** Props accepted by {@link UiAvatar}. */
const props = withDefaults(
  defineProps<{
    /** The text the initials are derived from: a login, an account name. */
    name: string
    /** `md` (36px) beside a name in a card, `sm` (32px) in a bar or a table row. */
    size?: 'sm' | 'md'
  }>(),
  { size: 'md' },
)

/** The letters to draw, derived once per name. */
const initials: ComputedRef<string> = computed(() => {
  return initialsOf(props.name)
})
</script>

<template>
  <span
    class="grid shrink-0 place-items-center rounded-full bg-accent-fill text-sm leading-none font-bold text-white select-none"
    :class="size === 'sm' ? 'size-8' : 'size-9'"
    aria-hidden="true"
  >
    <slot>{{ initials }}</slot>
  </span>
</template>
