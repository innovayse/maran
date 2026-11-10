<script setup lang="ts">
/**
 * The DNSSEC pane of a zone: where the signing stands, the DS record to publish at the registrar,
 * and the three transitions (enable, withdraw, finish the withdrawal). The zone page mounts it
 * only when this host can sign or the zone is already signed; the signing itself is read by the
 * page so the tab can be hidden, and held by the DNSSEC store.
 *
 * Every transition is confirmed. Withdrawing and finishing carry the warning that matters: the
 * DS record must be removed at the registrar FIRST, because a DS record that outlives the signing
 * makes validating resolvers reject the zone and the domain stops resolving for them. Finishing
 * also needs the explicit statement that the DS record was removed, and is disabled until the
 * earliest instant the panel stated. That wait is the server's rule; the control only reflects it,
 * and a refusal the server still sends is shown as it was sent.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCheckbox from '../../components/ui/UiCheckbox.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import DnssecDsRecord from '../../components/dns/DnssecDsRecord.vue'
import DnssecStatePanel from '../../components/dns/DnssecStatePanel.vue'
import { useDnsDnssecStore } from '../../stores/dnsDnssec'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { LoadingState } from '../../types/loading'
import type { ZoneDnssec } from '../../types/dnsDnssec'

/** Which transition the confirmation dialog is asking about. */
type Transition = 'enable' | 'withdraw' | 'finish'

/** The words of one confirmation: its title, the consequence and the button that performs it. */
interface ConfirmText {
  /** The dialog's accessible name. */
  title: string
  /** The consequence the person is asked to weigh. */
  question: string
  /** The button that performs the transition. */
  action: string
}

/** Props accepted by {@link DnsDnssecTab}. */
const props = defineProps<{
  /** The zone whose signing is shown. */
  zoneId: string
  /** The zone's apex name, for the confirmation titles. */
  zoneName: string
}>()

/** Events emitted by {@link DnsDnssecTab}. */
const emit = defineEmits<{
  /** A transition was accepted, so the zone's own state (`dnssecState`) moved and the page reads it again. */
  (e: 'changed'): void
}>()

const { t } = useI18n()
const store = useDnsDnssecStore()
const localeStore = useLocaleStore()

/** The transition awaiting confirmation, or `null` while none is. */
const confirming: Ref<Transition | null> = ref(null)

/** Whether the person stated that the DS record was removed at the registrar. */
const dsRemoved: Ref<boolean> = ref(false)

/** The signing once the panel has answered with it; `null` in every other state of the read. */
const dnssec: ComputedRef<ZoneDnssec | null> = computed(() => {
  return store.detail.state === 'loaded' ? store.detail.dnssec : null
})

/** Where the read stands, for the loading region. */
const regionState: ComputedRef<LoadingState> = computed(() => {
  switch (store.detail.state) {
    case 'loading':
      return 'loading'
    case 'loaded':
      return 'loaded'
    default:
      return 'error'
  }
})

/** The panel's reason the read failed, or the unreachable text when it did not answer. */
const loadFailure: ComputedRef<string> = computed(() => {
  return store.detail.state === 'failed' ? (store.detail.message ?? t('common.list.unreachable')) : t('common.list.unreachable')
})

/**
 * Whether the earliest instant the panel stated for finishing the withdrawal has passed. The panel
 * enforces it regardless; this only keeps the control from offering what would be refused.
 */
const earliestReached: ComputedRef<boolean> = computed(() => {
  const earliest = dnssec.value?.withdrawalEarliestFinishAt ?? null
  return earliest === null || Date.parse(earliest) <= Date.now()
})

/** Whether the finish control can be pressed: the wait is over and the DS removal was stated. */
const canFinish: ComputedRef<boolean> = computed(() => {
  return earliestReached.value && dsRemoved.value
})

/** The words of the open confirmation; empty text while none is open. */
const confirmText: ComputedRef<ConfirmText> = computed(() => {
  const name = props.zoneName
  switch (confirming.value) {
    case 'enable':
      return {
        title: t('dns.dnssec.enable.confirmTitle', { name }),
        question: t('dns.dnssec.enable.confirm'),
        action: t('dns.dnssec.enable.action'),
      }
    case 'withdraw':
      return {
        title: t('dns.dnssec.withdraw.confirmTitle', { name }),
        question: t('dns.dnssec.withdraw.confirm'),
        action: t('dns.dnssec.withdraw.action'),
      }
    case 'finish':
      return {
        title: t('dns.dnssec.finish.confirmTitle', { name }),
        question: t('dns.dnssec.finish.confirm'),
        action: t('dns.dnssec.finish.action'),
      }
    default:
      return { title: '', question: '', action: '' }
  }
})

/**
 * Opens the confirmation of a transition.
 * @param transition The transition to confirm.
 * @returns Nothing.
 */
const ask = (transition: Transition): void => {
  store.clearActionError()
  confirming.value = transition
}

/**
 * Abandons the open confirmation.
 * @returns Nothing.
 */
const cancel = (): void => {
  confirming.value = null
}

/**
 * Performs the confirmed transition, closes the dialog once the request settled (it holds the
 * spinner meanwhile) and tells the page when the panel accepted, so the zone is read again.
 * @returns Resolves once the request has settled.
 */
const confirmTransition = async (): Promise<void> => {
  const transition = confirming.value
  let accepted = false
  if (transition === 'enable') {
    accepted = await store.enable(props.zoneId)
  } else if (transition === 'withdraw') {
    accepted = await store.withdraw(props.zoneId)
  } else if (transition === 'finish') {
    accepted = await store.finishWithdrawal(props.zoneId)
  }
  confirming.value = null
  if (accepted) {
    dsRemoved.value = false
    emit('changed')
  }
}
</script>

<template>
  <UiLoadingRegion :state="regionState">
    <template #error>
      <UiAlert variant="error">{{ loadFailure }}</UiAlert>
    </template>

    <div v-if="dnssec !== null" class="flex min-w-0 max-w-3xl flex-col gap-4">
      <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

      <DnssecStatePanel :dnssec="dnssec" />

      <!-- Off: nothing is signed, so there is nothing to publish; the only step is to start. -->
      <div v-if="dnssec.state === 'off'" class="flex flex-col gap-3">
        <UiSectionHeading :title="t('dns.dnssec.enable.title')" :subtitle="t('dns.dnssec.enable.subtitle')" />
        <div>
          <UiButton :disabled="!dnssec.available" @click="ask('enable')">{{ t('dns.dnssec.enable.action') }}</UiButton>
        </div>
      </div>

      <template v-else>
        <div v-if="dnssec.state === 'signing'" class="flex flex-col gap-2">
          <UiSectionHeading :title="t('dns.dnssec.registrar.title')" />
          <p class="text-sm">{{ t('dns.dnssec.registrar.instructions') }}</p>
        </div>
        <div v-else class="flex flex-col gap-2">
          <UiSectionHeading :title="t('dns.dnssec.registrar.removeTitle')" />
          <p class="text-sm">{{ t('dns.dnssec.registrar.removeInstructions') }}</p>
        </div>

        <DnssecDsRecord v-if="dnssec.ds !== null" :ds="dnssec.ds" :dnskey="dnssec.dnskey" />

        <div v-if="dnssec.state === 'signing'" class="flex flex-col gap-3">
          <UiSectionHeading :title="t('dns.dnssec.withdraw.title')" :subtitle="t('dns.dnssec.withdraw.subtitle')" />
          <div>
            <UiButton variant="destructive" @click="ask('withdraw')">{{ t('dns.dnssec.withdraw.action') }}</UiButton>
          </div>
        </div>

        <div v-else class="flex flex-col gap-3">
          <UiSectionHeading :title="t('dns.dnssec.finish.title')" :subtitle="t('dns.dnssec.finish.subtitle')" />
          <p v-if="!earliestReached && dnssec.withdrawalEarliestFinishAt !== null" class="text-sm" data-testid="dnssec-earliest">
            {{ t('dns.dnssec.finish.earliest', { when: formatIsoTimestamp(dnssec.withdrawalEarliestFinishAt, localeStore.current) }) }}
          </p>
          <UiCheckbox v-model="dsRemoved" :label="t('dns.dnssec.finish.dsRemoved')" />
          <div>
            <UiButton variant="destructive" :disabled="!canFinish" @click="ask('finish')">
              {{ t('dns.dnssec.finish.action') }}
            </UiButton>
          </div>
        </div>
      </template>
    </div>
  </UiLoadingRegion>

  <UiConfirm
    :open="confirming !== null"
    :title="confirmText.title"
    :question="confirmText.question"
    :confirm-label="confirmText.action"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="store.acting"
    :acting-label="t('common.saving')"
    :destructive="confirming !== 'enable'"
    @close="cancel"
    @confirm="confirmTransition"
  />
</template>
