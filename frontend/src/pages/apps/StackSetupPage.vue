<script setup lang="ts">
/**
 * First-run wizard (issue #517): a host with no site web server cannot have sites, and this is the
 * one screen the stack guard lets an administrator use until that is fixed. Four steps: the web
 * server (required), the runtimes (PHP and Node.js versions, or none), a review, and the install
 * with live progress. The installs run one at a time and the page stays until the panel reports a
 * web server installed; only then does it offer the way into the panel. After a reload it picks up
 * an install that is still running and shows its progress, instead of starting over.
 *
 * Renders a `<section>`, not a `<main>` (the layout owns the landmark). The page reads state from
 * the stores through `useStackWizard`; it never touches the API layer.
 */
import { computed, onBeforeUnmount, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import StackInstallStep from '../../components/apps/StackInstallStep.vue'
import StackReviewStep from '../../components/apps/StackReviewStep.vue'
import StackRuntimesStep from '../../components/apps/StackRuntimesStep.vue'
import StackWebServerStep from '../../components/apps/StackWebServerStep.vue'
import { STACK_WIZARD_STEPS, useStackWizard } from '../../composables/useStackWizard'
import { useHostAppsStore } from '../../stores/hostApps'

const { t } = useI18n()
const store = useHostAppsStore()
const wizard = useStackWizard()

/** The 1-based number of the step on screen. */
const stepNumber: ComputedRef<number> = computed(() => {
  return STACK_WIZARD_STEPS.indexOf(wizard.step.value) + 1
})

/** Whether the person can still walk back: not on the first step, and not once the install began. */
const canGoBack: ComputedRef<boolean> = computed(() => {
  return stepNumber.value > 1 && wizard.step.value !== 'install'
})

/** Whether any app of the queue failed, so the retry button is offered. */
const hasFailure: ComputedRef<boolean> = computed(() => {
  return wizard.queue.value.some((item) => {
    return item.state === 'failed'
  })
})

/**
 * Starts the installs from the review step.
 * @returns Resolves once the queue has settled.
 */
const startInstall = async (): Promise<void> => {
  wizard.next()
  await wizard.run()
}

/**
 * Reads the catalogue and picks up any install the panel is already running.
 * @returns Resolves once both reads settled.
 */
const start = async (): Promise<void> => {
  await Promise.all([store.loadInstallable(), wizard.resume()])
}

onMounted(start)
onBeforeUnmount(store.stopAll)
</script>

<template>
  <section class="flex w-full max-w-3xl min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('apps.setup.heading')" :subtitle="t('apps.setup.subtitle')" />

    <UiLoadingRegion :state="store.installableState">
      <template #error>
        <UiAlert variant="error">
          {{ store.installableErrorMessage ?? t('apps.setup.loadFailed') }}
          <template #actions>
            <UiButton variant="secondary" @click="store.loadInstallable">{{ t('apps.setup.retryLoad') }}</UiButton>
          </template>
        </UiAlert>
      </template>

      <UiCard>
        <div class="flex flex-col gap-5">
          <UiSectionHeading
            :title="t(`apps.setup.steps.${wizard.step.value}`)"
            :subtitle="t('apps.setup.stepOf', { n: stepNumber, total: STACK_WIZARD_STEPS.length })"
          />

          <StackWebServerStep
            v-if="wizard.step.value === 'webServer'"
            v-model="wizard.webServer.value"
            :choices="wizard.webServerChoices.value"
          />
          <StackRuntimesStep
            v-else-if="wizard.step.value === 'runtimes'"
            :php-choices="wizard.phpChoices.value"
            :node-choices="wizard.nodeChoices.value"
            :php-ids="wizard.phpIds.value"
            :node-ids="wizard.nodeIds.value"
            :node-unavailable="store.nodejsUnavailable"
            :node-unavailable-message="store.nodejsUnavailableMessage"
            @toggle="wizard.toggle"
          />
          <StackReviewStep v-else-if="wizard.step.value === 'review'" :chosen="wizard.chosen.value" />
          <template v-else>
            <UiAlert v-if="wizard.installing.value" variant="info">{{ t('apps.setup.installingNotice') }}</UiAlert>
            <StackInstallStep :queue="wizard.queue.value" :progress-of="wizard.progressOf" />
            <UiAlert v-if="wizard.settled.value && wizard.webServerReady.value" variant="success">
              <p class="font-semibold">{{ t('apps.setup.doneTitle') }}</p>
              <p>{{ t('apps.setup.doneDescription') }}</p>
            </UiAlert>
            <UiAlert v-else-if="wizard.settled.value" variant="error">{{ t('apps.setup.needWebServer') }}</UiAlert>
          </template>

          <div class="flex flex-col-reverse gap-2 border-t border-border-subtle pt-5 sm:flex-row sm:justify-end">
            <UiButton v-if="canGoBack" variant="secondary" @click="wizard.back">{{ t('apps.setup.back') }}</UiButton>
            <UiButton
              v-if="wizard.step.value === 'webServer' || wizard.step.value === 'runtimes'"
              :disabled="!wizard.canAdvance.value"
              @click="wizard.next"
            >
              {{ t('apps.setup.next') }}
            </UiButton>
            <UiButton v-else-if="wizard.step.value === 'review'" @click="startInstall">{{ t('apps.setup.install') }}</UiButton>
            <template v-else>
              <UiButton
                v-if="hasFailure && wizard.settled.value"
                variant="secondary"
                :disabled="wizard.installing.value"
                @click="wizard.run"
              >
                {{ t('apps.setup.retry') }}
              </UiButton>
              <UiButton v-if="wizard.settled.value && wizard.webServerReady.value" :to="{ name: 'system-status' }">
                {{ t('apps.setup.openPanel') }}
              </UiButton>
            </template>
          </div>
        </div>
      </UiCard>
    </UiLoadingRegion>
  </section>
</template>
