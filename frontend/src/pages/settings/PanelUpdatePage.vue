<script setup lang="ts">
/**
 * The panel's release state and the action that updates it (issue #382): the running version, the
 * channel, the newer release the last check found, and the previous version a rollback would
 * restore. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout.
 *
 * The button is advice for administrators only; the endpoint refuses everyone else regardless.
 * Progress and the result (queued, running, succeeded, failed, rolled back) are the task list's:
 * this screen only says where to look. "Check for updates" (issue #516) is the one action with no
 * task: the screen follows it by re-reading the state while the server reports one in progress, and
 * says whether it recorded a result. The release-notes address is signed text a remote party
 * wrote, so it is shown as a plain link and only when it is an https address.
 */
import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue'
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
  return state !== null && state.canStart && state.available !== null && !state.updateInProgress && !updateStore.checking
})

/** Whether "Check for updates" is offered: not while a check runs, and not while an update holds the lock. */
const canCheck: ComputedRef<boolean> = computed(() => {
  const state = updateStore.state
  return state !== null && state.canStart && !state.updateInProgress && !updateStore.checking
})

/**
 * What the "Available version" row says. "Up to date" is only true after a check: with none on
 * record the row says so instead of claiming it.
 */
const availableText: ComputedRef<string> = computed(() => {
  const state = updateStore.state
  if (state === null) {
    return ''
  }
  if (state.available !== null) {
    return state.available.version
  }
  return state.lastCheckedAt === null ? t('panelUpdate.neverChecked') : t('panelUpdate.upToDate')
})

/**
 * Why the action cannot be used right now, or `null` when it can. Three honest states: the host has
 * never checked (so nothing is known, and "up to date" would be a claim without a check behind it),
 * the last check found nothing newer, or an update is already on its way (which has its own alert).
 */
const blockedReason: ComputedRef<string | null> = computed(() => {
  const state = updateStore.state
  if (state === null || state.available !== null || state.updateInProgress) {
    return null
  }
  return state.lastCheckedAt === null ? t('panelUpdate.blockedNeverChecked') : t('panelUpdate.blockedUpToDate')
})

/**
 * Opens the confirmation dialog.
 * @returns Nothing.
 */
const ask = (): void => {
  confirming.value = true
}

/**
 * Asks the server to check for updates; a refusal is shown by the page.
 * @returns Resolves once the request has been accepted and the check followed, or refused.
 */
const checkForUpdates = async (): Promise<void> => {
  await updateStore.check()
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
  void updateStore.load().then(() => {
    // A check started before this screen opened (or before a reload) is followed to its end.
    updateStore.follow()
  })
})

onBeforeUnmount(() => {
  updateStore.stopFollowing()
})
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading :title="t('panelUpdate.heading')" :subtitle="t('panelUpdate.subtitle')" />

    <!-- A refused request; a refused read is the region's own error below. -->
    <UiAlert v-if="updateStore.startMessage !== null" variant="error">
      {{ updateStore.startMessage }}
    </UiAlert>

    <UiAlert v-if="updateStore.checkMessage !== null" variant="error">
      {{ updateStore.checkMessage }}
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
            {{ availableText }}
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

        <UiAlert v-if="updateStore.checking" class="mt-4" variant="info" data-testid="panel-update-checking">
          {{ t('panelUpdate.checking') }}
        </UiAlert>
        <UiAlert v-else-if="updateStore.checkOutcome === 'recorded'" class="mt-4" variant="success">
          {{ t('panelUpdate.checkRecorded') }}
        </UiAlert>
        <UiAlert v-else-if="updateStore.checkOutcome === 'notRecorded'" class="mt-4" variant="warning">
          {{ t('panelUpdate.checkNotRecorded') }}
        </UiAlert>

        <div v-if="updateStore.state.canStart" class="mt-4 flex flex-col gap-2">
          <p class="text-sm text-text-muted">{{ t('panelUpdate.hint') }}</p>
          <p v-if="blockedReason !== null" class="text-sm text-text-muted" data-testid="panel-update-blocked">
            {{ blockedReason }}
          </p>
          <div class="flex flex-wrap justify-end gap-2">
            <UiButton variant="secondary" :disabled="!canCheck" @click="checkForUpdates">
              {{ t('panelUpdate.checkAction') }}
            </UiButton>
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
