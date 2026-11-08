<script setup lang="ts">
/**
 * The DNS server settings screen (administrators only): the name server's state and switch, and
 * the values the panel writes into every zone it serves. Renders a `<section>`, not a `<main>`.
 * State comes from the DNS settings store; the page never touches the API layer (rules/vue.md).
 *
 * **Turning the name server on opens port 53 to the internet**, and turning it off closes it, so
 * both ask first and say which. The setting and the host's answer are two facts and are shown as
 * two (see {@link DnsServerPanel}).
 *
 * Every refusal is the server's own localized sentence, shown as it was sent. `adminOnly` on the
 * route is advice; the endpoints refuse every other role on their own (rules/security.md).
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import DnsNameserversForm from '../../components/dns/DnsNameserversForm.vue'
import DnsServerPanel from '../../components/dns/DnsServerPanel.vue'
import { useDnsCatalogStore } from '../../stores/dnsCatalog'
import { useDnsSettingsStore } from '../../stores/dnsSettings'
import type { SaveDnsSettingsRequest } from '../../types/dnsSettings'
import type { LoadingState } from '../../types/loading'

const { t } = useI18n()
const store = useDnsSettingsStore()
const catalogStore = useDnsCatalogStore()

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Which way the name server is about to be switched, or `null` when no switch awaits confirmation. */
const pendingSwitch: Ref<boolean | null> = ref(null)

/** Where the read stands, for the loading region: a refused or unanswered read with nothing held is its error. */
const regionState: ComputedRef<LoadingState> = computed(() => {
  if (store.loading) {
    return 'loading'
  }
  return store.settings === null || store.server === null ? 'error' : 'loaded'
})

/**
 * Saves the settings the form collected and says so.
 * @param settings The complete settings, already read by the form's client-side checks.
 * @returns Resolves once the request has settled.
 */
const save = async (settings: SaveDnsSettingsRequest): Promise<void> => {
  if (await store.save(settings)) {
    notice.value = t('dns.nameservers.saved')
  }
}

/**
 * Starts a switch of the name server, which then waits for confirmation.
 * @param on True to turn it on, false to turn it off.
 * @returns Nothing.
 */
const askSwitch = (on: boolean): void => {
  pendingSwitch.value = on
}

/**
 * Abandons a pending switch.
 * @returns Nothing.
 */
const cancelSwitch = (): void => {
  pendingSwitch.value = null
}

/**
 * Carries out the confirmed switch.
 * @returns Resolves once the request has settled.
 */
const confirmSwitch = async (): Promise<void> => {
  const on = pendingSwitch.value
  if (on === null) {
    return
  }
  const switched = await store.setServerEnabled(on)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  pendingSwitch.value = null
  if (switched) {
    notice.value = on ? t('dns.server.turnedOn') : t('dns.server.turnedOff')
  }
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(() => {
  // The catalogue bounds the TTL the form checks; it fills without holding the page back.
  void catalogStore.load()
  void store.load()
})
</script>

<template>
  <section class="flex w-full max-w-3xl min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('dns.nameservers.heading')" :subtitle="t('dns.nameservers.subtitle')" />

    <UiAlert v-if="store.switchErrorMessage !== null" variant="error">{{ store.switchErrorMessage }}</UiAlert>
    <UiAlert v-if="store.saveErrorMessage !== null" variant="error">{{ store.saveErrorMessage }}</UiAlert>

    <UiLoadingRegion :state="regionState">
      <template #error>
        <UiAlert variant="error">{{ store.loadErrorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <div v-if="store.settings !== null && store.server !== null" class="flex min-w-0 flex-col gap-4">
        <UiAlert v-if="!store.settings.complete" variant="info">{{ t('dns.nameservers.incomplete') }}</UiAlert>
        <DnsServerPanel :server="store.server" :acting="store.switching" @switch="askSwitch" />
        <DnsNameserversForm
          :settings="store.settings"
          :catalog="catalogStore.catalog"
          :saving="store.saving"
          @submit="save"
        />
      </div>
      <UiCard v-else>
        <UiSkeleton shape="block" />
      </UiCard>
    </UiLoadingRegion>

    <UiConfirm
      :open="pendingSwitch !== null"
      :title="pendingSwitch === true ? t('dns.server.confirmOnTitle') : t('dns.server.confirmOffTitle')"
      :question="pendingSwitch === true ? t('dns.server.confirmOn') : t('dns.server.confirmOff')"
      :confirm-label="pendingSwitch === true ? t('dns.server.turnOn') : t('dns.server.turnOff')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.switching"
      :acting-label="t('common.saving')"
      :destructive="pendingSwitch !== true"
      @close="cancelSwitch"
      @confirm="confirmSwitch"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
