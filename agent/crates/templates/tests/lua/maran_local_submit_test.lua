-- Unit tests of the pure rule logic, run with a plain Lua interpreter from
-- agent/crates/templates: lua tests/lua/maran_local_submit_test.lua
-- No Rspamd globals exist here, so the rule file returns before any binding.
local M = dofile('templates/rspamd/lua/maran_local_submit.lua')

local failures = 0
local function check(name, condition)
  if not condition then
    failures = failures + 1
    print('FAIL: ' .. name)
  end
end

local PICKUP = 'by host.example.test (Postfix, from userid 1001)\n\tid ABC; Tue, 1 Jan 2030 00:00:00 +0000'
local FORGED = 'by evil.example.test (Postfix, from userid 0)'

local function maps()
  return {
    accounts = M.parse_pairs('1001 alice\n1002 bob\n'),
    domains = M.parse_sets('alice a.test\nalice b.test\nbob c.test\n'),
    limits = M.parse_pairs('alice 2\nbob 5\n'),
    mailboxes = M.parse_pairs_lower_keys('info@a.test alice\n'),
  }
end

-- A table constructor drops a nil value, so a field the case wants ABSENT is named with this marker.
local ABSENT = {}

local function ctx(overrides)
  local base = {
    daemon_name = 'MARAN_LOCAL',
    received = { PICKUP },
    recipient_count = 1,
    max_recipients = 100,
    envelope_domain = 'a.test',
    from_domains = { 'a.test' },
    exempt_uids = { ['0'] = true },
  }
  for key, value in pairs(overrides or {}) do
    if value == ABSENT then
      base[key] = nil
    else
      base[key] = value
    end
  end
  return base
end

-- parsing
check('rows skip comments and blanks', #M.parse_rows('# c\n\nx y\n') == 1)
check('sets keep every domain of an account', M.parse_sets('a x.test\na y.test')['a']['y.test'])
check('uid from pickup header', M.uid_from_received(PICKUP) == '1001')
check('other shapes have no uid', M.uid_from_received('from a (b [1.2.3.4]) by c') == nil)
check('only the topmost header counts', M.topmost_uid({ PICKUP, FORGED }) == '1001')
check('a forged second header is ignored', M.topmost_uid({ PICKUP, FORGED }) ~= '0')
check('a forged topmost shape is refused', M.topmost_uid({ 'by x (Postfix, from userid abc)' }) == nil)
check('no header, no uid', M.topmost_uid({}) == nil)

-- rule 1: the road is told by the server-side daemon name, first
check('unknown uid is discarded', M.decide(ctx({ received = { 'by h (Postfix, from userid 4242)' } }), maps()).action == 'discard')
check('no pickup header is discarded', M.decide(ctx({ received = {} }), maps()).action == 'discard')
check('root is left alone', M.decide(ctx({ received = { 'by h (Postfix, from userid 0)' } }), maps()).action == 'none')
check('inbound smtp is not a submission', M.decide(ctx({ daemon_name = 'INBOUND' }), maps()).action == 'none')
-- The #518 review defect: Postfix presents local mail as a connection from
-- 127.0.0.1, so a client address must never take a message off the local road.
local with_ip = ctx({ has_client_ip = true, client_ip = '127.0.0.1', received = { 'by h (Postfix, from userid 4242)' } })
check('a client ip does not take a local message off the local road', M.decide(with_ip, maps()).action == 'discard')
check('a client ip on the local road still counts an owned message', M.decide(ctx({ has_client_ip = true }), maps()).action == 'count')
-- Forged SMTP attributes carried by a hand-built queue file.
check('a forged login does not take a local message off the local road',
  M.decide(ctx({ user = 'info@a.test', received = { 'by h (Postfix, from userid 4242)' } }), maps()).action == 'discard')
check('an unknown daemon name is refused, not passed', M.decide(ctx({ daemon_name = 'other' }), maps()).action == 'soft reject')
check('a missing daemon name is refused, not passed', M.decide(ctx({ daemon_name = ABSENT }), maps()).action == 'soft reject')
check('smtpd daemon names never reach the local rule',
  M.decide(ctx({ daemon_name = 'INBOUND', received = { 'by h (Postfix, from userid 4242)' } }), maps()).action == 'none')

-- rule 2
local owned = M.decide(ctx(), maps())
check('owned envelope and From are counted', owned.action == 'count' and owned.account == 'alice' and owned.limit == 2)
check('second owned domain accepted', M.decide(ctx({ envelope_domain = 'b.test', from_domains = { 'b.test' } }), maps()).action == 'count')
check('another account domain in envelope is discarded (no backscatter)', M.decide(ctx({ envelope_domain = 'c.test' }), maps()).action == 'discard')
check('another account domain in From is rejected', M.decide(ctx({ from_domains = { 'a.test', 'c.test' } }), maps()).action == 'reject')
check('missing From is rejected', M.decide(ctx({ from_domains = {} }), maps()).action == 'reject')
check('no envelope domain is discarded', M.decide(ctx({ envelope_domain = ABSENT }), maps()).action == 'discard')
check('refusal text is the contract text', M.decide(ctx({ envelope_domain = 'c.test' }), maps()).message == '5.7.1 sender not owned by account')
check('a From refusal keeps the contract text', M.decide(ctx({ from_domains = { 'c.test' } }), maps()).message == '5.7.1 sender not owned by account')

-- rule 3
check('authenticated mailbox shares the account bucket', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), maps()).account == 'alice')
-- An authenticated login the map does not know fails closed (publication lag, a
-- stale map), where it used to mean "no limit".
check('unknown mailbox is deferred, not unlimited', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'x@z.test' }), maps()).action == 'soft reject')
check('unknown mailbox text', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'x@z.test' }), maps()).message == '4.4.3 mailbox not bound to an account')
-- Inverse control: a known login is still counted.
check('known mailbox is counted', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), maps()).action == 'count')
check('login case does not escape the bucket', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'Info@A.Test' }), maps()).account == 'alice')
check('map keys are lower-cased', M.parse_pairs_lower_keys('Info@A.Test alice')['info@a.test'] == 'alice')
check('unauthenticated inbound is still not a submission', M.decide(ctx({ daemon_name = 'INBOUND', user = ABSENT }), maps()).action == 'none')
check('bucket key', M.bucket_key('alice') == 'maran:send:alice')
check('at the limit is allowed', M.finish(owned, 2).action == 'none')
check('over the limit is deferred 4.7.1', M.finish(owned, 3).action == 'soft reject' and M.finish(owned, 3).message:sub(1, 5) == '4.7.1')
check('a reject passes through finish', M.finish({ action = 'reject', message = 'x' }, 99).action == 'reject')
check('a discard passes through finish', M.finish({ action = 'discard', message = 'x' }, 99).action == 'discard')

-- recipients: counted per recipient, capped per local message
check('one message costs one per recipient', M.decide(ctx({ recipient_count = 7 }), maps()).cost == 7)
check('no recipients still cost one', M.decide(ctx({ recipient_count = 0 }), maps()).cost == 1)
check('authenticated mail costs per recipient too', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test', recipient_count = 4 }), maps()).cost == 4)
check('the cap itself is allowed', M.decide(ctx({ recipient_count = 100 }), maps()).action == 'count')
check('over the cap is rejected 5.5.3', M.decide(ctx({ recipient_count = 101 }), maps()).message == '5.5.3 too many recipients')
check('over the cap is a reject', M.decide(ctx({ recipient_count = 101 }), maps()).action == 'reject')
check('the cap does not hide a forged sender (discard first)', M.decide(ctx({ recipient_count = 101, envelope_domain = 'c.test' }), maps()).action == 'discard')
check('a batch over the remaining allowance is deferred', M.finish(M.decide(ctx({ recipient_count = 3 }), maps()), 3).action == 'soft reject')

-- idempotent counting: keyed on the maildrop id pickup wrote, never the cleanup queue id
check('maildrop id from the pickup header', M.id_from_received(PICKUP) == 'ABC')
check('no id after the shape', M.id_from_received('by h (Postfix, from userid 1001)') == nil)
check('a foreign header has no id', M.id_from_received('from a (b) by c id XYZ; date') == nil)
check('identity is the topmost id', M.message_identity({ daemon_name = 'MARAN_LOCAL', received = { PICKUP } }) == 'id:ABC')
check('a forged second header does not change the identity',
  M.message_identity({ daemon_name = 'MARAN_LOCAL', received = { PICKUP, 'by x (Postfix, from userid 1001)\n\tid FORGED;' } }) == 'id:ABC')
check('the same message offered again keeps its identity',
  M.message_identity({ daemon_name = 'MARAN_LOCAL', received = { PICKUP } }) == M.message_identity({ daemon_name = 'MARAN_LOCAL', received = { PICKUP } }))
check('another maildrop file has another identity',
  M.message_identity({ daemon_name = 'MARAN_LOCAL', received = { 'by h (Postfix, from userid 1001)\n\tid OTHER;' } }) ~= 'id:ABC')
check('a Message-ID header is never an identity', M.message_identity({ daemon_name = 'MARAN_LOCAL', received = {}, message_id = '<a@b>' }) == nil)
check('the authenticated road has no identity', M.message_identity({ daemon_name = 'ORIGINATING', received = { PICKUP } }) == nil)
check('the verdict carries the identity', M.decide(ctx(), maps()).identity == 'id:ABC')
check('seen key joins account and identity', M.seen_key('alice', 'id:ABC') == 'maran:send:seen:alice:id:ABC')
check('no identity, no seen key', M.seen_key('alice', nil) == '' and M.seen_key('alice', '') == '')
check('seen keys differ by identity', M.seen_key('alice', 'id:A') ~= M.seen_key('alice', 'id:B'))

-- a rule that is installed but not configured
check('unconfigured: the local road is refused', M.decide_unconfigured('MARAN_LOCAL').action == 'soft reject')
check('unconfigured: an unidentified path is refused', M.decide_unconfigured(nil).action == 'soft reject')
check('unconfigured: inbound mail is left alone', M.decide_unconfigured('INBOUND').action == 'none')
check('unconfigured: submission mail is left alone', M.decide_unconfigured('ORIGINATING').action == 'none')

-- rule 4
local no_domains = maps(); no_domains.domains = nil
check('missing domains map defers', M.decide(ctx(), no_domains).action == 'soft reject')
local no_accounts = maps(); no_accounts.accounts = nil
check('missing accounts map defers', M.decide(ctx(), no_accounts).action == 'soft reject')
local no_limits = maps(); no_limits.limits = nil
check('missing limits map defers', M.decide(ctx(), no_limits).action == 'soft reject')
local no_mailboxes = maps(); no_mailboxes.mailboxes = nil
check('missing mailbox map defers authenticated mail', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), no_mailboxes).action == 'soft reject')
local no_entry = maps(); no_entry.limits = M.parse_pairs('bob 5')
check('an account without a limit defers', M.decide(ctx(), no_entry).action == 'soft reject')
check('root needs no map', M.decide(ctx({ received = { 'by h (Postfix, from userid 0)' } }), {}).action == 'none')

if failures > 0 then
  os.exit(1)
end
print('ok')
