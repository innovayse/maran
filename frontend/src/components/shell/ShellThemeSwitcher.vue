<script setup lang="ts">
/**
 * The theme control: one button that flips the theme, and says what clicking it will do.
 *
 * The light theme is KEPT (main.css carries measured light values), so the control stays. Its
 * name is the ACTION ("Switch to light"), not the theme in force (issue #217): a button reading
 * "Light" in the light theme left the reader guessing whether it reported a state or offered one.
 * The icon shows the theme it switches TO, so the glyph and the words agree.
 *
 * **The visible text is the accessible name.** There is no separate `aria-label`: when the text is
 * hidden for want of room it becomes screen-reader-only (`sr-only`) rather than `display: none`,
 * so a voice-control user saying the words on screen always hits the control (WCAG 2.5.3).
 *
 * "Where there is room" is judged twice in the app bar: by the window (`xl`) and by the bar the
 * control sits in (a container query, 76rem), because a large text size narrows the bar while the
 * window stays wide. `labelled` turns both tests off and always draws the words: the navigation
 * drawer on a phone passes it, since there the switch is otherwise an unexplained sun or moon.
 *
 * The geometry is the kit button's `bar` size: a 40px square that a visible label widens,
 * the same box the language picker next to it takes. Nothing is restated here.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import { useThemeStore } from '../../stores/theme'

/** Props accepted by {@link ShellThemeSwitcher}. */
const props = defineProps<{
  /** Always draw the label, whatever the width (the phone drawer). */
  labelled?: boolean
}>()

const { t } = useI18n()
const themeStore = useThemeStore()

/** The icon of the theme a click switches to: the sun from dark, the moon from light. */
const icon: ComputedRef<'moon' | 'sun'> = computed(() => {
  return themeStore.isDark ? 'sun' : 'moon'
})

/** What clicking does, which is also the control's accessible name. */
const label: ComputedRef<string> = computed(() => {
  return t(`app.shell.switchTheme.${themeStore.isDark ? 'light' : 'dark'}`)
})

/** Classes on the label: always drawn when `labelled`, else screen-reader-only where room is short. */
const labelClass: ComputedRef<string> = computed(() => {
  return props.labelled ? 'whitespace-nowrap' : 'max-xl:sr-only @max-[76rem]:sr-only'
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
  <UiButton variant="ghost" size="bar" :title="label" @click="flip">
    <UiIcon :name="icon" size="lg" />
    <span :class="labelClass">{{ label }}</span>
  </UiButton>
</template>
