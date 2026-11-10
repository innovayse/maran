<script setup lang="ts">
/**
 * One packaged module's screen, running in an isolated frame and brokered by the shell.
 *
 * The component mounts the sandboxed frame (`UiSandboxFrame`) at the module's panel-built address
 * with a fresh nonce in its fragment, runs the handshake and the message bridge
 * (`useModuleBridge`), and draws what the bridge reports: a spinner until the module is ready,
 * the frame once it is, `ModuleUnavailable` with a retry if the module never starts, fails to
 * load or misbehaves. The rest of the panel is never involved: a module that throws, hangs or
 * floods only loses its own screen.
 *
 * What the frame asks for is done here and nowhere else: `resize` sets the frame's height,
 * `navigate` is handed to the page (which maps it to a shell route), `toast` is shown as text, and
 * `api` goes through the module-bridge store, which sends it with the shell's session and the
 * module's name. The shell tells the frame its language, theme and role name, and nothing about
 * the person.
 *
 * Keyed by the page on the route and the frame address: a navigation inside the module or a new
 * version of it mounts a new frame with a new handshake, so a retry and a change of screen are the
 * same operation.
 */
import {
  computed,
  onBeforeUnmount,
  onMounted,
  ref,
  shallowRef,
  useTemplateRef,
  watch,
  type ComputedRef,
  type Ref,
  type ShallowRef,
} from 'vue'
import { useI18n } from 'vue-i18n'
import UiPinnedToast from '../ui/UiPinnedToast.vue'
import UiSandboxFrame from '../ui/UiSandboxFrame.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import ModuleUnavailable from './ModuleUnavailable.vue'
import { useCallerRole } from '../../composables/useCallerRole'
import { useModuleBridge } from '../../composables/useModuleBridge'
import { useLocaleStore } from '../../stores/locale'
import { useModuleBridgeStore } from '../../stores/moduleBridge'
import { useThemeStore } from '../../stores/theme'
import { mintBridgeNonce } from '../../utils/mintBridgeNonce'
import {
  MODULE_FRAME_INITIAL_HEIGHT,
  type ModuleBridge,
  type ModuleMountState,
  type SandboxFrameHandle,
} from '../../types/moduleBridge'
import type { PanelModule } from '../../types/module'
import type { AppTheme } from '../../types/theme'

/** Props accepted by {@link ModuleFrame}. */
const props = defineProps<{
  /** The packaged module; it must carry `spa`, which is what the routes registered for it require. */
  module: PanelModule
  /** The module-relative path of the screen to draw, as `init` tells the frame. */
  path: string
}>()

/** Events emitted by {@link ModuleFrame}. */
const emit = defineEmits<{
  /** The frame asked for another of its module's screens; the path is one the module declared. */
  (e: 'navigate', path: string): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
const themeStore = useThemeStore()
const bridgeStore = useModuleBridgeStore()
const { role } = useCallerRole()

/** The nonce minted for the current attempt. */
const nonce: Ref<string> = ref(mintBridgeNonce())

/** The height the frame asked for, already clamped. */
const frameHeight: Ref<number> = ref(MODULE_FRAME_INITIAL_HEIGHT)

/** The text of the toast the frame asked for, or `null` when none is shown. */
const notice: Ref<string | null> = ref(null)

/** The running bridge of the current attempt; replaced on a retry. */
const bridge: ShallowRef<ModuleBridge | null> = shallowRef(null)

/** The sandboxed frame component, whose exposed element gives the frame's window. */
const sandbox = useTemplateRef<SandboxFrameHandle>('sandbox')

/** The module's name for the person: the localized display name, else the machine name. */
const moduleName: ComputedRef<string> = computed(() => {
  return props.module.displayName ?? props.module.name
})

/** The frame document's address with this attempt's nonce in the fragment. */
const frameSrc: ComputedRef<string> = computed(() => {
  return `${props.module.spa?.frameUrl ?? ''}#${nonce.value}`
})

/** What the current attempt is doing. */
const mountState: ComputedRef<ModuleMountState> = computed(() => {
  return bridge.value?.state.value ?? { state: 'loading' }
})

/**
 * Starts an attempt: a fresh nonce, a fresh bridge, a frame that has not told its height yet.
 * @returns Nothing.
 */
const start = (): void => {
  bridge.value?.close()
  nonce.value = mintBridgeNonce()
  frameHeight.value = MODULE_FRAME_INITIAL_HEIGHT
  const next = useModuleBridge({
    moduleId: props.module.name,
    nonce: nonce.value,
    routePaths: (props.module.routes ?? []).map((route): string => {
      return route.path
    }),
    frameWindow: (): Window | null => {
      return sandbox.value?.element?.contentWindow ?? null
    },
    init: () => {
      return {
        locale: localeStore.current,
        theme: themeStore.current,
        roleName: role.value ?? '',
        route: props.path,
      }
    },
    relay: (request) => {
      return bridgeStore.relay(props.module.name, request)
    },
    onResize: (height: number): void => {
      frameHeight.value = height
    },
    onNavigate: (path: string): void => {
      emit('navigate', path)
    },
    onToast: (text: string): void => {
      notice.value = text
    },
    onUnavailable: (failure): void => {
      bridgeStore.reportFailure(props.module.name, failure)
    },
    onReady: (): void => {
      bridgeStore.clearFailure(props.module.name)
    },
  })
  bridge.value = next
  next.open()
}

/**
 * Hides the toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

/**
 * Forwards the iframe's load error to the bridge.
 * @returns Nothing.
 */
const onFrameError = (): void => {
  bridge.value?.fail()
}

onMounted(start)

onBeforeUnmount((): void => {
  bridge.value?.close()
})

watch(
  (): string => {
    return localeStore.current
  },
  (locale: string): void => {
    bridge.value?.sendLocale(locale)
  },
)

watch(
  (): AppTheme => {
    return themeStore.current
  },
  (theme: AppTheme): void => {
    bridge.value?.sendTheme(theme)
  },
)
</script>

<template>
  <div class="min-w-0">
    <ModuleUnavailable
      v-if="mountState.state === 'unavailable'"
      :reason="mountState.reason"
      :module-name="moduleName"
      @retry="start"
    />
    <template v-else>
      <UiSpinner
        v-if="mountState.state === 'loading'"
        class="mb-3"
        :label="t('app.modules.loading', { module: moduleName })"
      />
      <UiSandboxFrame
        ref="sandbox"
        :src="frameSrc"
        :title="t('app.modules.frameTitle', { module: moduleName })"
        :height="frameHeight"
        @error="onFrameError"
      />
    </template>
    <UiPinnedToast :message="notice" variant="info" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </div>
</template>
