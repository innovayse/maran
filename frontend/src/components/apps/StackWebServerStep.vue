<script setup lang="ts">
/**
 * The wizard's first step: choose the site web server, the one required choice. Exactly one web
 * server serves a host's sites, so the choice is a radio group. The options are whatever the
 * host's package sources offer; this bundle carries no list of web servers.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiRadioGroup, { type RadioOption } from '../ui/UiRadioGroup.vue'
import type { InstallableHostApp } from '../../types/hostApp'

/** Props accepted by {@link StackWebServerStep}. */
const props = defineProps<{
  /** The web servers the host can install, as the panel listed them. */
  choices: readonly InstallableHostApp[]
}>()

/** The chosen web server's catalogue id, or the empty string before a choice. Two-way bound. */
const webServer = defineModel<string>({ required: true })

const { t } = useI18n()

/** The choices as radio options, labelled with the panel's name for each app. */
const options: ComputedRef<RadioOption[]> = computed(() => {
  return props.choices.map((app) => {
    return { value: app.appId, label: app.name }
  })
})
</script>

<template>
  <div class="flex flex-col gap-3">
    <UiAlert v-if="choices.length === 0" variant="warning">{{ t('apps.setup.webServer.none') }}</UiAlert>
    <UiRadioGroup v-else v-model="webServer" :legend="t('apps.setup.webServer.legend')" :options="options" required />
    <UiFieldHint>{{ t('apps.setup.webServer.hint') }}</UiFieldHint>
  </div>
</template>
