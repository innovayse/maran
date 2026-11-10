<script setup lang="ts">
/**
 * What a remote server is: the address the panel connects to, the login, its state, what kind of
 * server it turned out to be, the address limit on the panel's key, when it was added, and the
 * pinned host key (algorithm, fingerprint, when it was confirmed) with the control that reviews
 * it again. Props in, events out: the page owns the store.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import RemoteServerStateBadge from './RemoteServerStateBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { RemoteServerAction, RemoteServerDetail } from '../../types/migrationSource'

/** Props accepted by {@link RemoteServerOverview}. */
defineProps<{
  /** The server to describe. */
  server: RemoteServerDetail
  /** Whether the host key step is already on screen, which makes the review control pointless. */
  reviewing: boolean
  /** The action of the open server in flight, or `null`. */
  busy: RemoteServerAction | null
}>()

/** Events emitted by {@link RemoteServerOverview}. */
const emit = defineEmits<{
  /** The administrator asked to review the pinned host key against a fresh scan. */
  (e: 'review'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * Asks for the pinned host key to be reviewed.
 * @returns Nothing; the event is emitted synchronously.
 */
const review = (): void => {
  emit('review')
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('migration.detail.overview.title')" />
    <UiDescriptionList>
      <UiDescriptionItem :term="t('migration.detail.overview.address')" mono>
        {{ server.host }}:{{ server.port }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('migration.detail.overview.sshUser')" mono>{{ server.sshUser }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('migration.detail.overview.state')">
        <RemoteServerStateBadge :state="server.state" />
      </UiDescriptionItem>
      <UiDescriptionItem v-if="server.kind" :term="t('migration.detail.overview.kind')">
        {{ t(`migration.kinds.${server.kind}`) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('migration.detail.overview.allowedFrom')" mono>
        {{ server.allowedFrom ? server.allowedFrom : t('migration.detail.overview.noLimit') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('migration.detail.overview.createdAt')">
        {{ formatIsoTimestamp(server.createdAt, localeStore.current) }}
      </UiDescriptionItem>
    </UiDescriptionList>

    <template v-if="server.hostKeyFingerprint">
      <UiSectionHeading :title="t('migration.hostKey.pinnedTitle')">
        <template v-if="!reviewing" #action>
          <UiButton variant="secondary" :disabled="busy !== null" @click="review">
            {{ busy === 'rescan' ? t('migration.hostKey.rescanning') : t('migration.hostKey.changeAction') }}
          </UiButton>
        </template>
      </UiSectionHeading>
      <UiDescriptionList>
        <UiDescriptionItem v-if="server.hostKeyAlgorithm" :term="t('migration.hostKey.pinnedAlgorithm')" mono>
          {{ server.hostKeyAlgorithm }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('migration.hostKey.pinnedFingerprint')" mono class="sm:col-span-2">
          <span data-testid="pinned-fingerprint">{{ server.hostKeyFingerprint }}</span>
        </UiDescriptionItem>
        <UiDescriptionItem v-if="server.hostKeyConfirmedAt" :term="t('migration.hostKey.pinnedAt')">
          {{ formatIsoTimestamp(server.hostKeyConfirmedAt, localeStore.current) }}
        </UiDescriptionItem>
      </UiDescriptionList>
    </template>
  </div>
</template>
