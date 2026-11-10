<script setup lang="ts">
/**
 * The connection test of a remote server: the control that runs it, how the last one ended, and
 * what the panel learned from the server when it worked (system, login, disk, database and web
 * server, whether mail storage exists). A failure is named in the panel's own words for its kind,
 * from the locale table keyed by the closed set of failures the panel reports.
 *
 * The test needs a pinned host key; until there is one the control is off and says why. Props in,
 * events out: the page owns the store.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { RemoteServerAction, RemoteServerDetail } from '../../types/migrationSource'

/** Props accepted by {@link ConnectionTestResult}. */
const props = defineProps<{
  /** The server whose test is shown. */
  server: RemoteServerDetail
  /** Whether a host key is pinned, which is what a test needs. */
  pinned: boolean
  /** The action of the open server in flight, or `null`. */
  busy: RemoteServerAction | null
}>()

/** Events emitted by {@link ConnectionTestResult}. */
const emit = defineEmits<{
  /** The administrator asked for the connection to be tested. */
  (e: 'test'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** Whether the server has facts to show: only a successful test records them. */
const hasFacts: ComputedRef<boolean> = computed(() => {
  return props.server.state === 'ready' && props.server.isRoot !== null && props.server.isRoot !== undefined
})

/** The home filesystem's free and total space, or `null` when the test could not read it. */
const homeSpace: ComputedRef<string | null> = computed(() => {
  const free = props.server.homeFreeBytes
  const total = props.server.homeTotalBytes
  if (free === null || free === undefined || total === null || total === undefined) {
    return null
  }
  return t('migration.test.facts.homeSpaceValue', { free: formatBytes(free), total: formatBytes(total) })
})

/**
 * Asks for a test.
 * @returns Nothing; the event is emitted synchronously.
 */
const runTest = (): void => {
  emit('test')
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('migration.test.title')">
      <template #action>
        <UiButton :disabled="!pinned || busy !== null" @click="runTest">
          {{ busy === 'test' ? t('migration.test.running') : t('migration.test.runAction') }}
        </UiButton>
      </template>
    </UiSectionHeading>

    <p v-if="!pinned" class="text-sm text-text-muted">{{ t('migration.test.needsKey') }}</p>
    <p v-else-if="!server.lastTestedAt" class="text-sm text-text-muted">{{ t('migration.test.neverTested') }}</p>
    <p v-else class="text-sm text-text-secondary">
      {{ t('migration.test.lastTested') }}: {{ formatIsoTimestamp(server.lastTestedAt, localeStore.current) }}
    </p>

    <UiAlert v-if="server.state === 'ready'" variant="success">{{ t('migration.test.readyNotice') }}</UiAlert>
    <UiAlert v-else-if="server.lastFailure" variant="error">
      <strong>{{ t('migration.test.failureTitle') }}</strong>
      {{ t(`migration.test.failure.${server.lastFailure}`) }}
    </UiAlert>

    <template v-if="hasFacts">
      <UiSectionHeading :title="t('migration.test.facts.title')" />
      <UiDescriptionList>
        <UiDescriptionItem v-if="server.systemRelease" :term="t('migration.test.facts.release')">
          {{ server.systemRelease }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="server.kind" :term="t('migration.test.facts.kind')">
          {{ t(`migration.kinds.${server.kind}`) }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="server.maranVersion" :term="t('migration.test.facts.maranVersion')" mono>
          {{ server.maranVersion }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('migration.test.facts.login')">
          {{ server.isRoot ? t('migration.test.facts.loginRoot') : t('migration.test.facts.loginUser') }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="homeSpace !== null" :term="t('migration.test.facts.homeSpace')">
          {{ homeSpace }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="server.databaseServer" :term="t('migration.test.facts.databaseServer')">
          {{ t(`migration.test.databaseServers.${server.databaseServer}`) }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="server.webServer" :term="t('migration.test.facts.webServer')">
          {{ t(`migration.test.webServers.${server.webServer}`) }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('migration.test.facts.mail')">
          {{ server.mailFound ? t('migration.test.facts.mailFound') : t('migration.test.facts.mailNotFound') }}
        </UiDescriptionItem>
      </UiDescriptionList>
    </template>
  </div>
</template>
