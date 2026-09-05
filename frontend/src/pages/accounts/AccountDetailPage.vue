<script setup lang="ts">
/**
 * One hosting account, with the three things that can be done to it: suspend,
 * reactivate, delete. Renders a `<section>`, not a `<main>` — the single `<main>`
 * landmark lives in the layout this page is nested under.
 *
 * Every action asks first. Two of them are visible to the account's customer
 * within seconds (their sites stop serving), and the third destroys a home
 * directory; none should be one stray click away. The confirmation names what
 * will happen rather than asking "are you sure", because the operator is being
 * asked to weigh a consequence, not to repeat themselves.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The account's identity, from `/accounts/:id`. */
  id: string
}>()

/** The lifecycle action awaiting confirmation, or `null` when none is. */
type PendingAction = 'suspend' | 'reactivate' | 'delete' | null

const { t } = useI18n()
const router = useRouter()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** Which action the operator has started and is being asked to confirm. */
const pending: Ref<PendingAction> = ref(null)

/** The sentence shown while an action awaits confirmation. */
const confirmationText: ComputedRef<string> = computed(() => {
  switch (pending.value) {
    case 'suspend':
      return t('accounts.detail.confirmSuspend')
    case 'reactivate':
      return t('accounts.detail.confirmReactivate')
    case 'delete':
      return t('accounts.detail.confirmDelete')
    default:
      return ''
  }
})

/**
 * Starts an action, which then waits for confirmation.
 * @param action The action the operator clicked.
 * @returns Nothing; the page switches to its confirming state.
 */
const ask = (action: Exclude<PendingAction, null>): void => {
  pending.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing; the page returns to its normal state.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed action. A deletion leaves for the list, because the
 * page it was on no longer describes anything.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const action = pending.value
  pending.value = null

  if (action === 'suspend') {
    await accountsStore.suspend(props.id)
  } else if (action === 'reactivate') {
    await accountsStore.reactivate(props.id)
  } else if (action === 'delete' && (await accountsStore.remove(props.id))) {
    await router.push({ name: 'accounts' })
  }
}

onMounted(async () => {
  await accountsStore.loadOne(props.id)
})
</script>

<template>
  <section class="w-full">
    <UiSpinner v-if="accountsStore.loading" :label="t('accounts.detail.loading')" />

    <template v-else-if="accountsStore.selected !== null">
      <div class="mb-4 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-3xl font-semibold tracking-title text-text-primary">
            {{ accountsStore.selected.name }}
          </h1>
          <p class="mt-1 text-base text-text-secondary">{{ accountsStore.selected.primaryDomain }}</p>
        </div>
        <UiButton variant="ghost" @click="router.push({ name: 'accounts' })">
          {{ t('accounts.detail.backToList') }}
        </UiButton>
      </div>

      <UiAlert v-if="accountsStore.errorMessage !== null" variant="error" class="mb-4">
        {{ accountsStore.errorMessage }}
      </UiAlert>

      <UiCard>
        <dl class="grid gap-3 sm:grid-cols-2">
          <div>
            <dt class="text-base text-text-secondary">{{ t('accounts.detail.nameLabel') }}</dt>
            <dd class="text-base text-text-primary">{{ accountsStore.selected.name }}</dd>
          </div>
          <div>
            <dt class="text-base text-text-secondary">{{ t('accounts.detail.primaryDomainLabel') }}</dt>
            <dd class="text-base text-text-primary">{{ accountsStore.selected.primaryDomain }}</dd>
          </div>
          <div>
            <dt class="text-base text-text-secondary">{{ t('accounts.detail.statusLabel') }}</dt>
            <dd>
              <UiBadge :variant="accountsStore.selected.status === 'active' ? 'success' : 'warning'">
                {{ t(`accounts.status.${accountsStore.selected.status}`) }}
              </UiBadge>
            </dd>
          </div>
          <div>
            <dt class="text-base text-text-secondary">{{ t('accounts.detail.createdAtLabel') }}</dt>
            <dd class="text-base text-text-primary">
              {{ formatDate(accountsStore.selected.createdAt, localeStore.current) }}
            </dd>
          </div>
        </dl>
      </UiCard>

      <div class="mt-4 flex flex-wrap items-center gap-2">
        <template v-if="pending !== null">
          <span class="text-base text-text-secondary">{{ confirmationText }}</span>
          <UiButton variant="destructive" :disabled="accountsStore.acting" @click="confirm">
            {{ accountsStore.acting ? t('accounts.detail.working') : t('accounts.detail.confirm') }}
          </UiButton>
          <UiButton variant="secondary" @click="cancel">{{ t('accounts.detail.cancel') }}</UiButton>
        </template>

        <template v-else>
          <UiButton
            v-if="accountsStore.selected.status === 'active'"
            variant="secondary"
            @click="ask('suspend')"
          >
            {{ t('accounts.detail.suspend') }}
          </UiButton>
          <UiButton v-else variant="secondary" @click="ask('reactivate')">
            {{ t('accounts.detail.reactivate') }}
          </UiButton>
          <UiButton variant="destructive" @click="ask('delete')">
            {{ t('accounts.detail.delete') }}
          </UiButton>
        </template>
      </div>
    </template>

    <UiAlert v-else-if="accountsStore.errorMessage !== null" variant="error">
      {{ accountsStore.errorMessage }}
    </UiAlert>

    <UiEmptyState
      v-else
      :title="t('accounts.detail.notFoundTitle')"
      :description="t('accounts.detail.notFoundDescription')"
    />
  </section>
</template>
