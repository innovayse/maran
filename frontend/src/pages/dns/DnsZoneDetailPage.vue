<script setup lang="ts">
/**
 * One zone, in up to three panes: what it is, its records, and its DNSSEC signing. The panes are separate
 * components rendered as-is; this page owns the zone's read, the tab, and the delete. The DNSSEC
 * pane is offered only when this host can sign or the zone is already signed, so a host that
 * cannot sign shows nothing it cannot do.
 *
 * Deleting asks first and says what it does: every record goes with the zone and the name server
 * stops answering for the name. It leaves for the list afterwards, because the page it was on no
 * longer describes anything. A signed zone's confirmation adds the warning about the DS record
 * the registrar still holds. A zone another account owns, or one that never existed, is the
 * not-found state, not an alert: the panel answered, and what it said is that no such zone exists
 * for the caller.
 *
 * While the zone loads, the heading and the overview are drawn already, with their real terms and
 * a placeholder in place of each value. Renders a `<section>`, not a `<main>`.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSegmentedControl from '../../components/ui/UiSegmentedControl.vue'
import DnsDnssecTab from './DnsDnssecTab.vue'
import DnsOverviewTab from './DnsOverviewTab.vue'
import DnsRecordsTab from './DnsRecordsTab.vue'
import { useDnsDnssecStore } from '../../stores/dnsDnssec'
import { useDnsZonesStore } from '../../stores/dnsZones'
import { flashNoticeState, takeFlashNotice } from '../../utils/flashNotice'
import type { DnsZone } from '../../types/dns'
import type { LoadingState } from '../../types/loading'

/** Which pane of the detail page is on screen. */
type DetailTab = 'overview' | 'records' | 'dnssec'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The zone's identity, from `/dns/zones/:id`. */
  id: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = useDnsZonesStore()
const dnssecStore = useDnsDnssecStore()

/** The tab currently shown. */
const tab: Ref<DetailTab> = ref('overview')

/** Whether the delete confirmation is open. */
const confirmingDelete: Ref<boolean> = ref(false)

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/**
 * The zone on screen once the panel has answered with it; `null` in every other state the store
 * reports (loading, not found, failed), which the template renders from the store's `detail`.
 */
const zone: ComputedRef<DnsZone | null> = computed(() => {
  return store.detail.state === 'found' ? store.detail.zone : null
})

/** Whether the zone is still on its way, so the overview draws placeholders. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return store.detail.state === 'loading'
})

/** Where the zone's read stands, for the loading region: not found and failed both leave nothing to show. */
const regionState: ComputedRef<LoadingState> = computed(() => {
  switch (store.detail.state) {
    case 'loading':
      return 'loading'
    case 'found':
      return 'loaded'
    default:
      return 'error'
  }
})

/**
 * Whether the DNSSEC pane is offered: this host can sign, or the zone is signed (or being
 * unsigned) already and its DS record must stay reachable. A host that cannot sign shows no pane.
 */
const showDnssecTab: ComputedRef<boolean> = computed(() => {
  if (zone.value !== null && zone.value.dnssecState !== 'off') {
    return true
  }
  return dnssecStore.detail.state === 'loaded' && dnssecStore.detail.dnssec.available
})

/** The pane on screen: the chosen one, unless it is the DNSSEC pane and that is no longer offered. */
const activeTab: ComputedRef<DetailTab> = computed(() => {
  return tab.value === 'dnssec' && !showDnssecTab.value ? 'overview' : tab.value
})

/** The panes, in the order the design lists them. */
const tabOptions: ComputedRef<{ value: string; label: string }[]> = computed(() => {
  const options = [
    { value: 'overview', label: t('dns.detail.tabs.overview') },
    { value: 'records', label: t('dns.detail.tabs.records') },
  ]
  if (showDnssecTab.value) {
    options.push({ value: 'dnssec', label: t('dns.detail.tabs.dnssec') })
  }
  return options
})

/** The delete confirmation's consequence; a signed zone's also warns about the DS record at the registrar. */
const deleteQuestion: ComputedRef<string> = computed(() => {
  return zone.value !== null && zone.value.dnssecState !== 'off'
    ? t('dns.zones.confirmDeleteSigned')
    : t('dns.zones.confirmDelete')
})

/**
 * Switches pane.
 * @param value The tab the control reported.
 * @returns Nothing.
 */
const onTabChange = (value: string): void => {
  tab.value = value as DetailTab
}

/**
 * Reads the zone again after a change in a pane moved its figures, without blanking the page.
 * @returns Resolves once the request has settled.
 */
const refresh = async (): Promise<void> => {
  await store.refresh(props.id)
}

/**
 * Opens the delete confirmation.
 * @returns Nothing.
 */
const askDelete = (): void => {
  confirmingDelete.value = true
}

/**
 * Abandons the delete confirmation.
 * @returns Nothing.
 */
const cancelDelete = (): void => {
  confirmingDelete.value = false
}

/**
 * Deletes the zone and leaves for the list, which no longer holds it.
 * @returns Resolves once the request has settled.
 */
const confirmDelete = async (): Promise<void> => {
  const name = zone.value?.name ?? ''
  const deleted = await store.remove(props.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  confirmingDelete.value = false
  if (deleted) {
    await router.push({ name: 'dns-zones', state: flashNoticeState(t('dns.zones.deleted', { name })) })
  }
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(async () => {
  store.clearErrors()
  notice.value = takeFlashNotice()
  dnssecStore.reset()
  // Both reads start together: the pane's visibility needs the signing, the heading needs the zone.
  await Promise.all([store.loadOne(props.id), dnssecStore.load(props.id)])
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="regionState">
      <!-- A 404 is the not-found state, never the alert: the panel answered, and what it said is
           that no such zone exists for the caller. -->
      <template #error>
        <UiAlert v-if="store.detail.state === 'failed'" variant="error">
          {{ store.detail.message ?? t('common.list.unreachable') }}
        </UiAlert>
        <UiEmptyState
          v-else
          :title="t('dns.detail.notFoundTitle')"
          :description="t('dns.detail.notFoundDescription')"
        />
      </template>

      <div class="flex min-w-0 flex-col gap-4">
        <UiPageHeading mono :title="zone?.name ?? ''" :subtitle="t('dns.detail.subtitle')" :loading="isLoading">
          <template #actions>
            <UiLink :to="{ name: 'dns-zones' }" icon="chevronLeft">{{ t('dns.detail.backToList') }}</UiLink>
            <UiButton v-if="zone !== null" variant="destructive" @click="askDelete">
              {{ t('common.delete') }}
            </UiButton>
          </template>
        </UiPageHeading>

        <UiAlert v-if="!isLoading && store.actionErrorMessage !== null" variant="error">
          {{ store.actionErrorMessage }}
        </UiAlert>

        <UiSegmentedControl
          :model-value="activeTab"
          :options="tabOptions"
          :label="t('dns.detail.heading')"
          @update:model-value="onTabChange"
        />

        <DnsOverviewTab v-if="activeTab === 'overview'" :zone="zone" />
        <DnsRecordsTab v-else-if="activeTab === 'records'" :zone-id="props.id" @changed="refresh" />
        <DnsDnssecTab v-else :zone-id="props.id" :zone-name="zone?.name ?? ''" @changed="refresh" />
      </div>
    </UiLoadingRegion>

    <UiConfirm
      :open="confirmingDelete"
      :title="t('dns.zones.confirmDeleteTitle', { name: zone?.name ?? '' })"
      :question="deleteQuestion"
      :confirm-label="t('dns.zones.confirmDeleteAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('common.saving')"
      @close="cancelDelete"
      @confirm="confirmDelete"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
