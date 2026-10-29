<script setup lang="ts">
/**
 * The app bar's language picker.
 *
 * Its own component rather than markup inside the app bar, because switching
 * language here changes three things at once — the interface copy, the
 * `<html lang>` attribute and the `Accept-Language` header the panel sends —
 * and that knowledge belongs in one place. The locale store is the single
 * source of truth (rules/vue.md); this only drives it.
 *
 * A menu rather than a row of buttons: the list of languages will grow, and a
 * segmented control that grows stops being readable and starts eating the
 * bar. The globe carries the meaning, so the trigger needs only the current
 * language beside it — and the trigger's accessible name says "language" out
 * loud, because "English" on its own tells a screen-reader user nothing.
 *
 * **The language name is dropped below `lg`**, leaving the globe alone: at 390px the
 * bar holds a menu button, the mark, search, theme and this, and a word here is the
 * difference between all of them fitting and the row pushing the page sideways. It is also
 * dropped when the app bar it sits in is narrower than 62rem, whatever the window: a large text
 * size makes a wide window a narrow bar, and a window query cannot see that. With no container
 * around it (the sign-in screen) only the window test applies. The accessible name does not
 * change with the width.
 *
 * The trigger is the kit dropdown's own button, so its look comes in through the dropdown's
 * `trigger-variant` and `trigger-size` props (a ghost `bar` button, the theme switch's twin)
 * rather than through descendant selectors reaching into markup this component does not own.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import { useLocaleStore } from '../../stores/locale'
import { SUPPORTED_LOCALES, type AppLocale } from '../../types/app'

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The offered languages, in menu order. */
const locales: ComputedRef<readonly AppLocale[]> = computed(() => {
  return SUPPORTED_LOCALES
})

/**
 * Switches the interface language.
 * @param locale The language the user chose.
 * @returns Nothing; the store updates synchronously.
 */
const select = (locale: AppLocale): void => {
  localeStore.setLocale(locale)
}
</script>

<template>
  <UiDropdown
    align="end"
    trigger-variant="ghost"
    trigger-size="bar"
    :label="t(`app.locale.names.${localeStore.current}`)"
    :aria-label="t('app.locale.switcherLabel')"
    :chevron="false"
  >
    <template #leading>
      <UiIcon name="globe" size="lg" />
    </template>
    <template #trigger>
      <span class="max-lg:hidden @max-[62rem]:hidden">{{ t(`app.locale.names.${localeStore.current}`) }}</span>
    </template>

    <UiDropdownItem
      v-for="locale in locales"
      :key="locale"
      :checked="localeStore.current === locale"
      @select="select(locale)"
    >
      {{ t(`app.locale.names.${locale}`) }}
    </UiDropdownItem>
  </UiDropdown>
</template>
