<script setup lang="ts">
/**
 * A real tablist: a row of tabs that switch between panels of one screen (the marketplace's
 * Available, Installed and Updates). `UiSegmentedControl` is a group of toggle buttons and says
 * nothing about panels, so it is the wrong control for this; this one carries the tab pattern's
 * roles and keyboard model.
 *
 * ROLES. The row is `role="tablist"` named by `label`; each tab is a `role="tab"` button with
 * `aria-selected`, and the selected one `aria-controls` the panel. The panel is a `role="tabpanel"`
 * named by its tab (`aria-labelledby`) and is a tab stop itself, so a keyboard user who has chosen
 * a tab with no focusable content in its panel can still reach it.
 *
 * ONE PANEL IN THE DOM. Only the selected tab's panel is rendered, through the slot named after the
 * tab's id (`<template #installed>`). A panel that is not shown therefore runs no setup and makes
 * no request: the marketplace's Updates tab must not call the marketplace until it is opened.
 *
 * KEYBOARD. Roving tab stop: only the selected tab is in the Tab order. Left and Right move focus
 * to the neighbour (wrapping), Home and End to the first and last; moving focus does not choose,
 * Enter or Space does (manual activation, because choosing a tab can start a request and a sweep
 * across the row must not start three).
 *
 * LOOK AND MEASUREMENTS. The same pairings as `UiSegmentedControl`, so no new contrast claim: the
 * well is `bg-surface-2`; the selected tab is `bg-accent-fill` with white text (5.14:1) and 3.43:1
 * against the well; the others are `text-text-secondary` on the well (6.65:1) and rise to
 * `text-text-primary` on `bg-surface-3` under the pointer. Focus is the kit's `shadow-focus`.
 * `aria-selected` reports the choice, so colour is not the only cue. Text is `text-sm`, in sentence
 * case; labels wrap (`wrap-anywhere`) instead of widening the row at 390px.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'

/** One tab offered by a {@link UiTabs}. */
export interface TabOption {
  /** Stable machine id: bound through `v-model` and the name of the slot holding the tab's panel. */
  id: string
  /** Ready-to-render text for the tab; the control holds no copy of its own. */
  label: string
}

/** Props accepted by {@link UiTabs}. */
const props = defineProps<{
  /** The id of the selected tab. */
  modelValue: string
  /** The tabs, in the order they should read. */
  tabs: readonly TabOption[]
  /** Accessible name of the tablist, already translated by the caller. */
  label: string
}>()

/** Events emitted by {@link UiTabs}. */
const emit = defineEmits<{
  /** The user chose a tab. */
  (event: 'update:modelValue', id: string): void
}>()

/** A prefix that keeps this instance's element ids apart from another tablist's on the same page. */
const prefix: string = `ui-tabs-${useId().replace(/[^A-Za-z0-9_-]/g, '_')}`

/** The rendered tab buttons, in document order, for arrow-key navigation. */
const tabButtons: Ref<HTMLButtonElement[]> = ref([])

/** The id of the selected tab's panel. */
const panelId: ComputedRef<string> = computed(() => {
  return `${prefix}-panel-${props.modelValue}`
})

/**
 * The id of one tab button.
 * @param id The tab's id.
 * @returns The element id.
 */
const tabId = (id: string): string => {
  return `${prefix}-tab-${id}`
}

/**
 * Moves focus to another tab without choosing it.
 * @param index Position of the tab the keyboard is on.
 * @param target Where to go: a step (1 or -1, wrapping at the ends), `first` or `last`.
 * @returns Nothing; focus moves synchronously.
 */
const moveFocus = (index: number, target: number | 'first' | 'last'): void => {
  const buttons = tabButtons.value
  if (buttons.length === 0) {
    return
  }

  let next: number
  if (target === 'first') {
    next = 0
  } else if (target === 'last') {
    next = buttons.length - 1
  } else {
    next = (index + target + buttons.length) % buttons.length
  }
  buttons[next]?.focus()
}
</script>

<template>
  <div>
    <div
      class="flex w-fit max-w-full flex-wrap items-center gap-0.5 rounded-control border border-border-subtle bg-surface-2 p-1"
      role="tablist"
      :aria-label="label"
    >
      <button
        v-for="(tab, index) in tabs"
        :id="tabId(tab.id)"
        :key="tab.id"
        ref="tabButtons"
        type="button"
        role="tab"
        class="flex min-h-choice items-center gap-1.5 rounded-inset px-2.5 py-0.5 font-sans text-sm wrap-anywhere transition-colors focus-visible:shadow-focus focus-visible:outline-none"
        :class="
          modelValue === tab.id
            ? 'bg-accent-fill font-semibold text-white'
            : 'font-medium text-text-secondary hover:bg-surface-3 hover:text-text-primary'
        "
        :aria-selected="modelValue === tab.id"
        :aria-controls="modelValue === tab.id ? panelId : undefined"
        :tabindex="modelValue === tab.id ? 0 : -1"
        @click="emit('update:modelValue', tab.id)"
        @keydown.right.prevent="moveFocus(index, 1)"
        @keydown.left.prevent="moveFocus(index, -1)"
        @keydown.home.prevent="moveFocus(index, 'first')"
        @keydown.end.prevent="moveFocus(index, 'last')"
      >
        {{ tab.label }}
      </button>
    </div>
    <div
      :id="panelId"
      role="tabpanel"
      tabindex="0"
      class="mt-4 rounded-control focus-visible:shadow-focus focus-visible:outline-none"
      :aria-labelledby="tabId(modelValue)"
    >
      <slot :name="modelValue"></slot>
    </div>
  </div>
</template>
