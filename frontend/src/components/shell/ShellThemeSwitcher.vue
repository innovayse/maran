<script setup lang="ts">
/**
 * The header's theme picker.
 *
 * Two options shown side by side rather than one toggle: a toggle only says
 * what will happen next, so the user has to reason backwards to learn which
 * theme is active. The design draws both, and so do we.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiSegmentedControl, { type SegmentOption } from '../ui/UiSegmentedControl.vue'
import UiIcon from '../ui/UiIcon.vue'
import { useThemeStore } from '../../stores/theme'
import { SUPPORTED_THEMES, type AppTheme } from '../../types/theme'

const { t } = useI18n()
const themeStore = useThemeStore()

/** The offered themes, dark first — this panel's baseline, not its alternative. */
const options: ComputedRef<readonly SegmentOption[]> = computed(() => {
  return SUPPORTED_THEMES.map((theme: AppTheme) => {
    return {
      value: theme,
      label: t(`app.shell.themes.${theme}`),
    }
  })
})

/**
 * Applies the chosen theme.
 * @param value The chosen theme, as the control's machine value.
 * @returns Nothing; the store updates the document synchronously.
 */
const select = (value: string): void => {
  themeStore.setTheme(value as AppTheme)
}

/**
 * Icon for a theme option: the moon for dark, the sun for light.
 * @param value The option's machine value.
 * @returns The shell icon name to draw beside its label.
 */
const iconFor = (value: string): 'moon' | 'sun' => {
  return value === 'dark' ? 'moon' : 'sun'
}
</script>

<template>
  <UiSegmentedControl
    :model-value="themeStore.current"
    :options="options"
    :label="t('app.shell.themeSwitcherLabel')"
    @update:model-value="select"
  >
    <template #icon="{ option }">
      <UiIcon :name="iconFor(option.value)" :size="12" />
    </template>
  </UiSegmentedControl>
</template>
