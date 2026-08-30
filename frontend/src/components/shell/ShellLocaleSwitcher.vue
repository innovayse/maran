<script setup lang="ts">
/**
 * The header's language picker.
 *
 * Its own component rather than markup inside the header, because switching
 * language here changes three things at once — the interface copy, the
 * `<html lang>` attribute and the `Accept-Language` header the panel sends —
 * and that knowledge belongs in one place. The locale store is the single
 * source of truth (rules/vue.md); this only drives it.
 *
 * A menu rather than a row of buttons: the list of languages will grow, and a
 * segmented control that grows stops being readable and starts eating the
 * header. The globe carries the meaning, so the trigger needs only the current
 * code beside it — and the trigger's accessible name says "language" out loud,
 * because "EN" on its own tells a screen-reader user nothing.
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
    class="shell-locale"
    align="end"
    :label="t(`app.locale.names.${localeStore.current}`)"
    :aria-label="t('app.locale.switcherLabel')"
    :chevron="false"
  >
    <template #leading>
      <UiIcon name="globe" :size="13" />
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

<style scoped>
/* The header's own control geometry, as the design draws its picker: 4px 8px on
   a 6px radius, not the kit button's roomier default for a labelled action. */
.shell-locale :deep(button) {
  gap: 6px;
  padding: 4px 8px;
  border-radius: 6px;
  font-size: var(--text-base);
  font-weight: 400;
  color: var(--t2);
}

.shell-locale :deep(button:focus-visible) {
  border-color: var(--ac);
}

/* The kit's menu is 192px wide because most menus hold sentences; this one holds
   three two-letter codes, so it sizes to its content instead. */
.shell-locale :deep(ul) {
  min-width: 0;
  width: max-content;
}
</style>
