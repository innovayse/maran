<script setup lang="ts">
/**
 * The journal's tamper evidence: how many entries are linked into the chain, how many still wait
 * to be (the maintenance run links them within the hour), where retention has purged to, and a
 * button that asks the panel to check the whole chain now.
 *
 * Nothing here judges the chain. The verdict is the panel's: intact, or broken with the position
 * of the first entry that did not match and the panel's machine name for what did not match. That
 * name is printed as a code, beside the position, and is never looked up as text — it exists so an
 * operator can quote it. A verification the panel could not run shows its own sentence instead.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useAuditIntegrityStore } from '../../stores/auditIntegrity'

const { t } = useI18n()
const store = useAuditIntegrityStore()

/**
 * Asks the panel to verify the whole chain.
 * @returns Resolves once the panel answered.
 */
const verify = async (): Promise<void> => {
  await store.verifyChain()
}

onMounted(async () => {
  await store.loadChain()
})
</script>

<template>
  <UiCard class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('app.audit.integrity.heading')" />

    <UiLoadingRegion :state="store.chainLoadState">
      <template #error>
        <UiAlert variant="error">{{ store.chainLoadMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiDescriptionList :loading="store.chain === null">
        <UiDescriptionItem :term="t('app.audit.integrity.sealed')" figure>
          {{ store.chain?.sealedRows }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('app.audit.integrity.unsealed')" figure>
          {{ store.chain?.unsealedRows }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('app.audit.integrity.purgedThrough')" figure>
          {{ store.chain?.purgedThroughIndex }}
        </UiDescriptionItem>
      </UiDescriptionList>
    </UiLoadingRegion>

    <UiAlert v-if="store.verification.state === 'failed'" variant="error">
      {{ store.verificationMessage ?? t('common.list.unreachable') }}
    </UiAlert>
    <UiAlert v-else-if="store.verdict !== null && store.verdict.intact" variant="success">
      {{ t('app.audit.integrity.intact') }}
    </UiAlert>
    <UiAlert v-else-if="store.verdict !== null" variant="error">
      {{ t('app.audit.integrity.broken') }}
      <span class="font-mono">{{ store.verdict.break }} #{{ store.verdict.firstBrokenChainIndex }}</span>
    </UiAlert>

    <div>
      <UiButton variant="secondary" :disabled="store.verifying" @click="verify">
        {{ store.verifying ? t('common.loading.busy') : t('app.audit.integrity.verify') }}
      </UiButton>
    </div>
  </UiCard>
</template>
