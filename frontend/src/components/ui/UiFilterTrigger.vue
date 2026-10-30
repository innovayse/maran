<script setup lang="ts">
/**
 * The trigger that opens a list's filters: a secondary button with the filter glyph, the caller's
 * label, and an accent dot while any filter is applied.
 *
 * It only reports the press. Where the filters open — a modal on a desktop, a bottom sheet on a
 * phone — is the caller's decision, so this component holds no open state and renders no panel.
 *
 * **Accessible name.** The visible `label` names the control. While filters are active, the
 * caller's `activeLabel` ("2 filters applied", already translated and pluralised) is appended to
 * the name as visually hidden text, and also sits in a polite live region, so a screen-reader user
 * hears the count both when they reach the trigger and when it changes. The dot is decoration
 * (`aria-hidden`); the count in words is what carries the state, never the colour.
 *
 * **Look.** `UiButton`'s `secondary` variant. The dot is `bg-accent` (`--ac`, graphics only,
 * 4.52:1 on the `--s2` button fill: over the 3:1 graphics need), 8px, at the top-right corner of
 * the glyph.
 */
import { computed, type ComputedRef } from 'vue'
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'

/** Props accepted by {@link UiFilterTrigger}. */
const props = withDefaults(
  defineProps<{
    /** The trigger's visible text and accessible name, already translated ("Filters"). */
    label: string
    /** How many filters are applied; the dot and the announced count show only above zero. */
    activeCount?: number
    /** The count in words, already translated and pluralised ("2 filters applied"). */
    activeLabel?: string
  }>(),
  { activeCount: 0, activeLabel: '' },
)

/** Events emitted by {@link UiFilterTrigger}. */
const emit = defineEmits<{
  /** Fired when the trigger is pressed; the caller opens its filter modal or sheet. */
  (e: 'click', payload: MouseEvent): void
}>()

/** Whether any filter is applied. */
const active: ComputedRef<boolean> = computed(() => {
  return props.activeCount > 0
})

/**
 * Forwards the press to the caller.
 * @param event The native mouse event.
 * @returns Nothing; re-emits synchronously.
 */
const onClick = (event: MouseEvent): void => {
  emit('click', event)
}
</script>

<template>
  <span class="inline-flex">
    <UiButton variant="secondary" aria-haspopup="dialog" data-testid="filter-trigger" @click="onClick">
      <span class="relative inline-flex">
        <UiIcon name="filter" size="sm" />
        <span
          v-if="active"
          class="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-accent"
          data-testid="filter-trigger-dot"
          aria-hidden="true"
        />
      </span>
      <span>{{ label }}</span>
      <span v-if="active" class="sr-only">{{ activeLabel }}</span>
    </UiButton>
    <!-- Announces a change of the count; the button's own name repeats it on arrival. -->
    <span class="sr-only" aria-live="polite" data-testid="filter-trigger-status">{{
      active ? activeLabel : ''
    }}</span>
  </span>
</template>
