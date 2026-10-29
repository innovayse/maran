<script setup lang="ts">
/**
 * The app bar's theme control: one button that flips the theme.
 *
 * The light theme is KEPT (main.css carries measured light values), so the control
 * stays. It shows the theme that is ACTIVE — by icon, and by name from the `xl`
 * breakpoint where there is room — and flipping is what clicking it does. The
 * objection to a bare toggle ("it only says what will happen next, so the user has
 * to reason backwards") is answered by that icon rather than ignored; the
 * accessible name states the action, as it always did.
 *
 * One control rather than two sets: the sidebar footer used to carry a second copy of
 * this same setting, in a different shape, one of them a moon that showed the moon
 * whichever theme was in force. The app bar is the only home now, on every width.
 *
 * "Where there is room" is judged twice: by the window (`xl`) and by the app bar the control sits in
 * (a container query, 76rem). The second is what a large text size changes: the window is still
 * wide, the bar is not, and a name drawn anyway pushed the language picker off the canvas. Where
 * there is no container around it (the navigation drawer) the second test never applies.
 *
 * The geometry is the kit button's `bar` size: a 40px square that a visible label widens,
 * the same box the language picker next to it takes. Nothing is restated here.
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
  <UiButton
    variant="ghost"
    size="bar"
    :aria-label="t('app.shell.toggleTheme')"
    :title="t('app.shell.toggleTheme')"
    @click="flip"
  >
    <UiIcon :name="icon" size="lg" />
    <span class="max-xl:hidden @max-[76rem]:hidden">{{ label }}</span>
  </UiButton>
</template>
