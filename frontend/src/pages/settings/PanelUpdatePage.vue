<script setup lang="ts">
/**
 * The panel's release state and the action that updates it (issue #382): the running version, the
 * channel, the newer release the last check found, and the previous version a rollback would
 * restore. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout.
 *
 * The button is advice for administrators only; the endpoint refuses everyone else regardless.
 * Progress and the result (queued, running, succeeded, failed, rolled back) are the task list's:
 * this screen only says where to look. The release-notes address is signed text a remote party
 * wrote, so it is shown as a plain link and only when it is an https address.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiExternalLink from '../../components/ui/UiExternalLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { usePanelUpdateStore } from '../../stores/panelUpdate'
import { formatDate } from '../../utils/formatDate'

/** The only scheme a release-notes link may have; anything else is shown as no link at all. */
const HTTPS_PREFIX = 'https://'

const { t } = useI18n()
const updateStore = usePanelUpdateStore()
const localeStore = useLocaleStore()

/** Whether the confirmation dialog is open. */
const confirming: Ref<boolean> = ref(false)

/** The release-notes address when it is safe to link, otherwise `null`. */
const notesUrl: ComputedRef<string | null> = computed(() => {
  const url = updateStore.state?.available?.notesUrl ?? null
  return url !== null && url.startsWith(HTTPS_PREFIX) ? url : null
})

/** Whether the action is offered: an administrator, with a release to install and none running. */
const canUpdate: ComputedRef<boolean> = computed(() => {
  const state = updateStore.state
  return state !== null && state.canStart && state.available !== null && !state.updateInProgress
})

/**
 * Opens the confirmation dialog.
 * @returns Nothing.
 */
const ask = (): void => {
  confirming.value = true
}

/**
 * Closes the confirmation dialog without acting.
 * @returns Nothing.
 */
const dismiss = (): void => {
  confirming.value = false
}

/**
 * Requests the update and closes the dialog once the request has settled; a refusal is shown by the page.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  await updateStore.start()
  confirming.value = false
}

onMounted(() => {
  void updateStore.load()
})
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading :title="t('panelUpdate.heading')" :subtitle="t('panelUpdate.subtitle')" />

    <!-- A refused request; a refused read is the region's own error below. -->
    <UiAlert v-if="updateStore.startMessage !== null" variant="error">
      {{ updateStore.startMessage }}
    </UiAlert>

    <UiAlert v-if="updateStore.started" variant="success">
      {{ t('panelUpdate.started') }}
      <UiButton :to="{ name: 'tasks' }" variant="secondary" class="ml-2 whitespace-nowrap">
        {{ t('panelUpdate.openTasks') }}
      </UiButton>
    </UiAlert>

    <UiLoadingRegion :state="updateStore.loadState">
      <template #error>
        <UiAlert variant="error">{{ updateStore.loadMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <UiCard v-if="updateStore.state !== null">
        <UiSectionHeading class="mb-4" :title="t('panelUpdate.versionTitle')" />

        <UiDescriptionList>
          <UiDescriptionItem :term="t('panelUpdate.currentLabel')" mono>
            {{ updateStore.state.currentVersion }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('panelUpdate.channelLabel')" mono>
            {{ updateStore.state.channel }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('panelUpdate.lastCheckedLabel')">
            {{
              updateStore.state.lastCheckedAt === null
                ? t('panelUpdate.neverChecked')
                : formatDate(updateStore.state.lastCheckedAt, localeStore.current)
            }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('panelUpdate.availableLabel')" mono>
            {{ updateStore.state.available === null ? t('panelUpdate.upToDate') : updateStore.state.available.version }}
          </UiDescriptionItem>
          <UiDescriptionItem v-if="updateStore.state.available?.releasedAt" :term="t('panelUpdate.releasedLabel')">
            {{ formatDate(updateStore.state.available.releasedAt, localeStore.current) }}
          </UiDescriptionItem>
          <UiDescriptionItem v-if="updateStore.state.available !== null" :term="t('panelUpdate.notesLabel')">
            <UiExternalLink v-if="notesUrl !== null" :href="notesUrl">{{ t('panelUpdate.notesLink') }}</UiExternalLink>
            <template v-else>{{ t('panelUpdate.noNotes') }}</template>
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('panelUpdate.previousLabel')" mono>
            {{ updateStore.state.previousVersion ?? t('panelUpdate.noPrevious') }}
          </UiDescriptionItem>
        </UiDescriptionList>

        <UiAlert v-if="updateStore.state.updateInProgress" class="mt-4" variant="info">
          {{ t('panelUpdate.inProgress') }}
        </UiAlert>

        <div v-if="updateStore.state.canStart" class="mt-4 flex flex-col gap-2">
          <p class="text-sm text-text-muted">{{ t('panelUpdate.hint') }}</p>
          <div class="flex justify-end">
            <UiButton :disabled="!canUpdate || updateStore.starting" @click="ask">
              {{ t('panelUpdate.action') }}
            </UiButton>
          </div>
        </div>
      </UiCard>
    </UiLoadingRegion>

    <UiConfirm
      :open="confirming"
      :title="t('panelUpdate.confirmTitle')"
      :question="t('panelUpdate.confirmQuestion')"
      :confirm-label="t('panelUpdate.confirmAction')"
      :cancel-label="t('panelUpdate.cancel')"
      :close-label="t('panelUpdate.close')"
      :acting="updateStore.starting"
      :acting-label="t('panelUpdate.working')"
      :destructive="false"
      @close="dismiss"
      @confirm="confirm"
    />
  </section>
</template>
