<script setup lang="ts">
/**
 * The published demo logins, shown on the sign-in screen. The credentials are public by design and
 * appear nowhere else: the marketing site does not copy them, so they cannot go stale in two
 * places.
 *
 * Each login has a button that hands it to the sign-in form (the parent owns the fields), so a
 * visitor does not have to retype a password they are looking at.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useDemoStore } from '../../stores/demo'
import type { DemoIdentity } from '../../types/demo'

/** Events emitted by {@link DemoCredentials}. */
const emit = defineEmits<{
  /** The visitor chose a login: the sign-in form should be filled with it. */
  use: [identity: DemoIdentity]
}>()

const { t } = useI18n()
const demoStore = useDemoStore()

onMounted(async (): Promise<void> => {
  await demoStore.load()
})
</script>

<template>
  <UiCard
    v-if="demoStore.status !== null"
    role="region"
    :aria-label="t('demo.credentials.region')"
    data-testid="demo-credentials"
  >
    <UiSectionHeading :title="t('demo.credentials.title')" :subtitle="t('demo.credentials.hint')" />

    <div class="mt-4 flex flex-col gap-4">
      <div
        v-for="identity in demoStore.status.identities"
        :key="identity.role"
        class="flex flex-col gap-2"
        :data-testid="`demo-identity-${identity.role}`"
      >
        <p class="text-sm font-semibold text-text-primary">{{ t(`demo.credentials.roles.${identity.role}`) }}</p>
        <UiDescriptionList>
          <UiDescriptionItem :term="t('demo.credentials.username')" mono>
            <span data-testid="demo-username">{{ identity.username }}</span>
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('demo.credentials.password')" mono>
            <span data-testid="demo-password">{{ identity.password }}</span>
          </UiDescriptionItem>
        </UiDescriptionList>
        <UiButton variant="secondary" class="justify-center" @click="emit('use', identity)">
          {{ t('demo.credentials.use', { role: t(`demo.credentials.roles.${identity.role}`) }) }}
        </UiButton>
      </div>
    </div>
  </UiCard>
</template>
