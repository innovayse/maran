<script setup lang="ts">
/**
 * The row of controls above a listing: a search field, a filter trigger, a toggle, a refresh
 * button. It lays them on ONE line with a shared centre line (`items-center`), wrapping onto
 * further lines when the width runs out, and it is the only thing a screen uses for that row.
 *
 * **Why a primitive.** Every control's height comes from the kit's one control scale
 * (`--d-control`: 34px from tablet up, 44px on a phone; `UiButton`, `UiSearchInput`, `UiSelect`
 * and `UiInput` are `min-h-control`, an icon button is `size-control`). Inside a toolbar the
 * choice rows of `UiSwitch` and `UiCheckbox` take the same height (`--d-choice` is set to
 * `--d-control` here), so every direct child is the same height and centred on the same line.
 * The earlier row (`flex items-end` in each page) bottom-aligned controls of different heights,
 * and a field's stacked label above the input made the field taller than the button beside it.
 *
 * **Labels.** A field inside a toolbar hides its label visually and keeps it as its accessible
 * name (`UiSearchInput` and `UiSelect` read the flag this provides), so the label never stacks
 * above the input. Outside a toolbar a field draws its label as before.
 *
 * It is a plain group of controls, not an ARIA `toolbar`: that role promises arrow-key
 * navigation between its children, which these controls do not implement.
 */
import { provide } from 'vue'
import { TOOLBAR_KEY } from '../../utils/toolbarKey'

provide(TOOLBAR_KEY, true)
</script>

<template>
  <!-- `[--d-choice:var(--d-control)]` puts a switch or checkbox row on the control height; custom
       properties inherit, so it reaches the choice rows below without a prop on each. -->
  <div class="flex flex-wrap items-center gap-3 [--d-choice:var(--d-control)]" data-testid="toolbar">
    <slot />
  </div>
</template>
