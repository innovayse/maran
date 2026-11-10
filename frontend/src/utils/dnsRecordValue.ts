import type { DnsRecordTypeInfo } from '../types/dns'
import type { DnsRecordRequest } from '../types/dnsRecord'

/**
 * Writes a record's data the way a zone file reads it left to right: the numbers and the tag that
 * come before the value, then the value. An MX shows `10 mail.example.com`, an SRV
 * `10 5 5060 sip.example.com`, a CAA `0 issue letsencrypt.org`, and a type with no extra inputs
 * shows its value alone. Which numbers a type takes is the catalogue's answer, not this function's:
 * a priority of zero on an MX is a real preference and must be written, while the zero a record
 * stores for an input its type does not take must not be.
 * @param record The record; its TTL plays no part, so a template's record, whose TTL may be absent, is written too.
 * @param info The catalogue entry of the record's type, or `undefined` while the catalogue has not
 * arrived, in which case only the value is written.
 * @returns The record's data as one line of text.
 */
export const dnsRecordValue = (record: Omit<DnsRecordRequest, 'ttl'>, info: DnsRecordTypeInfo | undefined): string => {
  if (info === undefined) {
    return record.value
  }
  const leading: (number | string)[] = []
  if (info.usesPriority) {
    leading.push(record.priority)
  }
  if (info.usesWeightAndPort) {
    leading.push(record.weight, record.port)
  }
  if (info.usesFlagsAndTag) {
    leading.push(record.flags, record.tag)
  }
  return [...leading, record.value].join(' ')
}
