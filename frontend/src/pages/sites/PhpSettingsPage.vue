<script setup lang="ts">
/**
 * The customer's PHP settings (issue #138): the memory limit, upload size, execution time, input
 * variables and timezone their account's PHP runs with, within the bounds the plan sets. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. State comes exclusively from the PHP-settings store; the page never touches the API
 * layer (rules/vue.md: API composables are called from stores only).
 *
 * **The settings belong to the account, not to a site.** One pool serves every site of the account
 * on a PHP version, so the page says so, with the number of sites the panel reports
 * (`sitesAffected`), and offers no per-site choice.
 *
 * A refusal (a value above the plan, an unreachable agent) is the backend's own sentence, shown as
 * it arrives. A saved change is confirmed with a toast.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import PhpSettingsForm from '../../components/sites/PhpSettingsForm.vue'
import { useReloadOnLocale } from '../../composables/useReloadOnLocale'
import { usePhpSettingsStore } from '../../stores/phpSettings'
import type { PhpSettingValue } from '../../types/phpSetting'

const { t } = useI18n()
const store = usePhpSettingsStore()

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/**
 * Whether the first read is still on its way, when there is no form to draw placeholders under:
 * the settings are the panel's, so until they arrive the page has no fields, and stands in shapes.
 * A re-read (a language switch) keeps the fields it has and is not this.
 */
const firstLoad: ComputedRef<boolean> = computed(() => {
  return store.loading && store.items.length === 0
})

/**
 * Saves the settings the form reports and confirms it.
 * @param settings The settings to keep; any other is removed.
 * @returns Resolves once the request has settled.
 */
const save = async (settings: PhpSettingValue[]): Promise<void> => {
  notice.value = null
  if (await store.save({ settings })) {
    notice.value = t('phpSettings.saved')
  }
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

// The labels, units and zone names are the panel's, in the interface language, so the settings are
// read when the screen opens; the shell remounts the page on a language switch, which reads again.
useReloadOnLocale(store.load)
</script>

<template>
  <section class="flex w-full max-w-4xl flex-col gap-4">
    <UiPageHeading :title="t('phpSettings.heading')" :subtitle="t('phpSettings.subtitle')" />

    <UiAlert v-if="store.loadState === 'loaded' && store.error !== null" variant="error">{{ store.error }}</UiAlert>

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.error ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <UiCard v-if="firstLoad">
        <div class="grid gap-6 sm:grid-cols-2">
          <UiSkeleton v-for="slot in 4" :key="slot" shape="block" control="field" />
        </div>
      </UiCard>
      <UiCard v-else>
        <p class="mb-4 text-sm text-text-secondary">
          {{ t('phpSettings.appliesTo', { count: store.sitesAffected }, store.sitesAffected) }}
        </p>
        <PhpSettingsForm
          :items="store.items"
          :saving="store.saving"
          :loading="store.loadState === 'loading'"
          @submit="save"
        />
      </UiCard>
    </UiLoadingRegion>

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
