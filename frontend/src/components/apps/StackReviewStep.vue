<script setup lang="ts">
/**
 * The wizard's third step: what will be installed, before anything is. The web server is named
 * first, then the runtimes, or a sentence saying there are none. Nothing here changes the choice;
 * "Back" does.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import type { InstallableHostApp } from '../../types/hostApp'
import { hostAppFamily } from '../../utils/hostAppFamily'

/** Props accepted by {@link StackReviewStep}. */
const props = defineProps<{
  /** The chosen apps in install order: the web server first. */
  chosen: readonly InstallableHostApp[]
}>()

const { t } = useI18n()

/** The chosen web server's name, or the empty string when none is chosen yet. */
const webServerName: ComputedRef<string> = computed(() => {
  return props.chosen.find((app) => {
    return hostAppFamily(app.appId) === 'webServer'
  })?.name ?? ''
})

/** The chosen runtimes' names, joined for one line. */
const runtimeNames: ComputedRef<string> = computed(() => {
  return props.chosen
    .filter((app) => {
      return hostAppFamily(app.appId) !== 'webServer'
    })
    .map((app) => {
      return app.name
    })
    .join(', ')
})
</script>

<template>
  <UiDescriptionList>
    <UiDescriptionItem :term="t('apps.setup.review.webServer')">{{ webServerName }}</UiDescriptionItem>
    <UiDescriptionItem :term="t('apps.setup.review.runtimes')">
      {{ runtimeNames === '' ? t('apps.setup.review.noRuntimes') : runtimeNames }}
    </UiDescriptionItem>
  </UiDescriptionList>
</template>
