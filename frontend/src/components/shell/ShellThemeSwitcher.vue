<script setup lang="ts">
/**
 * The theme control: one labelled menu button showing the mode in force (an icon and its name),
 * opening a menu of Light, Dark and System (issue #217). The same component sits in the app bar
 * on the desktop and in the navigation drawer's head on a phone, so the two places cannot drift.
 *
 * **The accessible name states the setting and its value** ("Theme: Light"), because "Light" on
 * its own tells a screen-reader user nothing about what the button is for. The visible mode is a
 * part of that name, so a voice-control user saying the words on screen still hits it (WCAG 2.5.3).
 * The menu items are `menuitemradio`, checked on the current preference, and the kit dropdown
 * opens on the checked one, so a keyboard user lands on what is set rather than on Light.
 *
 * **System is a mode of its own**, not a third palette: it follows the operating system's
 * preference and keeps following it. Its icon is a monitor, so the trigger says "following the
 * system" rather than showing whichever palette that currently resolves to.
 *
 * "Where there is room" for the visible mode is judged twice in the app bar: by the window (`xl`)
 * and by the bar the control sits in (a container query, 76rem), because a large text size narrows
 * the bar while the window stays wide; the icon carries the meaning when the word steps aside.
 * `labelled` turns both tests off and always draws the word: the drawer passes it.
 *
 * The trigger is the kit dropdown's own button in the `bar` geometry, the language picker's twin,
 * so nothing about its box is restated here.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon, { type UiIconName } from '../ui/UiIcon.vue'
import { useThemeStore } from '../../stores/theme'
import { THEME_PREFERENCES, type ThemePreference } from '../../types/theme'

/** Props accepted by {@link ShellThemeSwitcher}. */
const props = defineProps<{
  /** Always draw the mode's name beside the icon, whatever the width (the phone drawer). */
  labelled?: boolean
}>()

/** The glyph of each mode: the palette's own for the two palettes, a screen for following the system. */
const MODE_ICONS: Record<ThemePreference, UiIconName> = {
  light: 'sun',
  dark: 'moon',
  system: 'monitor',
}

const { t } = useI18n()
const themeStore = useThemeStore()

/** The name of the mode in force, as the trigger shows it. */
const modeName: ComputedRef<string> = computed(() => {
  return t(`app.shell.theme.modes.${themeStore.preference}`)
})

/** Classes on the visible mode: always drawn when `labelled`, else dropped where room is short. */
const modeClass: ComputedRef<string> = computed(() => {
  return props.labelled ? 'whitespace-nowrap' : 'whitespace-nowrap max-xl:hidden @max-[76rem]:hidden'
})

/**
 * Applies the mode the reader chose.
 * @param mode The chosen preference.
 * @returns Nothing; the store repaints synchronously.
 */
const select = (mode: ThemePreference): void => {
  themeStore.setPreference(mode)
}
</script>

<template>
  <UiDropdown
    :align="labelled ? 'start' : 'end'"
    trigger-variant="ghost"
    trigger-size="bar"
    :label="modeName"
    :aria-label="t('app.shell.theme.label', { mode: modeName })"
    :chevron="false"
  >
    <template #leading>
      <UiIcon :name="MODE_ICONS[themeStore.preference]" size="lg" />
    </template>
    <template #trigger>
      <span :class="modeClass">{{ modeName }}</span>
    </template>

    <UiDropdownItem
      v-for="mode in THEME_PREFERENCES"
      :key="mode"
      :checked="themeStore.preference === mode"
      @select="select(mode)"
    >
      {{ t(`app.shell.theme.modes.${mode}`) }}
    </UiDropdownItem>
  </UiDropdown>
</template>
