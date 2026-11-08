import type { DashboardAttention } from '../types/dashboard'

/**
 * Whether every attention source was read and none of them asks to be looked at.
 *
 * The one definition of "all clear", shared by the attention notice and the status screen's
 * heading, so the two can never disagree: an unread source is not a zero (issue #248), a count above
 * zero needs attention, a clock that is anything but synchronised needs attention (issue #150), and so does an alert channel whose last delivery failed (issue #142).
 * @param attention The attention sections, as the panel answered them.
 * @returns True only when all of them were read and none needs attention.
 */
export const attentionIsClear = (attention: DashboardAttention): boolean => {
  const counts = [
    attention.certificatesExpiringSoon,
    attention.failedBackups,
    attention.failedTasks,
    attention.bannedAddresses,
    attention.sitesOnEndOfLifePhp,
  ]
  const countsClear = counts.every((section) => {
    return section.state === 'loaded' && section.data === 0
  })
  const clockClear = attention.clock.state === 'loaded' && attention.clock.data.state === 'synchronized'
  const deliveryClear =
    attention.alertDelivery.state === 'loaded' &&
    attention.alertDelivery.data.mail.state !== 'failing' &&
    attention.alertDelivery.data.webhook.state !== 'failing'
  return countsClear && clockClear && deliveryClear
}
