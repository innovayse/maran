<script setup lang="ts">
/**
 * The header's theme control: one button that flips the theme.
 *
 * It used to draw both options side by side, and the reasoning then was that a
 * toggle "only says what will happen next, so the user has to reason backwards
 * to learn which theme is active". That is a real objection, and it is answered
 * here rather than ignored: the button shows the theme that is ACTIVE, by icon
 * and by name, and flipping is what clicking it does. Nothing has to be reasoned
 * backwards.
 *
 * One control rather than two also ends a duplication the shell carried: the
 * sidebar's footer already flips the theme through the same store, so the panel
 * offered the same setting in two different shapes on the same screen.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import { useThemeStore } from '../../stores/theme'

const { t } = useI18n()
const themeStore = useThemeStore()

/** The icon of the theme in force: the moon for dark, the sun for light. */
const icon: ComputedRef<'moon' | 'sun'> = computed(() => {
  return themeStore.isDark ? 'moon' : 'sun'
})

/** The name of the theme in force, so the button states rather than promises. */
const label: ComputedRef<string> = computed(() => {
  return t(`app.shell.themes.${themeStore.current}`)
})

/**
 * Flips the theme.
 * @returns Nothing; the store applies it to `<html data-theme>` synchronously.
 */
const flip = (): void => {
  themeStore.toggle()
}
</script>

<template>
  <UiButton class="shell-header-theme" :aria-label="t('app.shell.toggleTheme')" @click="flip">
    <UiIcon :name="icon" size="md" />
    <span>{{ label }}</span>
  </UiButton>
</template>
