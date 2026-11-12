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

local PICKUP = '1001 ABC'
local FORGED = '0 EVIL'

local function maps()
  return {
    accounts = M.parse_pairs('1001 alice\n1002 bob\n'),
    domains = M.parse_sets('alice a.test\nalice b.test\nbob c.test\n'),
    limits = M.parse_pairs('alice 2\nbob 5\n'),
    mailboxes = M.parse_pairs_lower_keys('info@a.test alice\n'),
    fanouts = M.parse_pairs_lower_keys('team@a.test 5 alice\nBig@a.test 40 alice\n@b.test 7 alice\n'),
  }
end

-- A table constructor drops a nil value, so a field the case wants ABSENT is named with this marker.
local ABSENT = {}

local function ctx(overrides)
  local base = {
    daemon_name = 'MARAN_LOCAL',
    submitters = { PICKUP },
    recipient_count = 1,
    max_recipients = 100,
    envelope_domain = 'a.test',
    from_domains = { 'a.test' },
    exempt_uids = { ['0'] = true },
    host_mail_name = 'host.test',
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
check('uid from the submitter header', M.parse_submitter(PICKUP) == '1001')
check('other shapes have no uid', M.parse_submitter('from a (b [1.2.3.4]) by c') == nil)
check('a Received line is not a submitter header', M.parse_submitter('by h (Postfix, from userid 1001)\n\tid ABC;') == nil)
check('a header with trailing text is refused', M.parse_submitter('1001 ABC extra') == nil)
check('a non-string value has no uid', M.parse_submitter(nil) == nil)
check('only the topmost header counts', M.topmost_uid({ PICKUP, FORGED }) == '1001')
check('a forged second header is ignored', M.topmost_uid({ PICKUP, FORGED }) ~= '0')
check('a forged topmost shape is refused', M.topmost_uid({ 'abc ABC' }) == nil)
check('no header, no uid', M.topmost_uid({}) == nil)

-- rule 1: the road is told by the server-side daemon name, first
check('unknown uid is discarded', M.decide(ctx({ submitters = { '4242 H' } }), maps()).action == 'discard')
check('no pickup header is discarded', M.decide(ctx({ submitters = {} }), maps()).action == 'discard')
check('root is left alone', M.decide(ctx({ submitters = { '0 H' } }), maps()).action == 'none')
check('inbound smtp is not a submission', M.decide(ctx({ daemon_name = 'INBOUND' }), maps()).action == 'none')
-- The #518 review defect: Postfix presents local mail as a connection from
-- 127.0.0.1, so a client address must never take a message off the local road.
local with_ip = ctx({ has_client_ip = true, client_ip = '127.0.0.1', submitters = { '4242 H' } })
check('a client ip does not take a local message off the local road', M.decide(with_ip, maps()).action == 'discard')
check('a client ip on the local road still counts an owned message', M.decide(ctx({ has_client_ip = true }), maps()).action == 'count')
-- Forged SMTP attributes carried by a hand-built queue file.
check('a forged login does not take a local message off the local road',
  M.decide(ctx({ user = 'info@a.test', submitters = { '4242 H' } }), maps()).action == 'discard')
check('an unknown daemon name is refused, not passed', M.decide(ctx({ daemon_name = 'other' }), maps()).action == 'soft reject')
check('a missing daemon name is refused, not passed', M.decide(ctx({ daemon_name = ABSENT }), maps()).action == 'soft reject')
check('smtpd daemon names never reach the local rule',
  M.decide(ctx({ daemon_name = 'INBOUND', submitters = { '4242 H' } }), maps()).action == 'none')

-- rule 2
local owned = M.decide(ctx(), maps())
check('owned envelope and From are counted', owned.action == 'count' and owned.account == 'alice' and owned.limit == 2)
check('second owned domain accepted', M.decide(ctx({ envelope_domain = 'b.test', from_domains = { 'b.test' } }), maps()).action == 'count')
check('another account domain in envelope is discarded (no backscatter)', M.decide(ctx({ envelope_domain = 'c.test' }), maps()).action == 'discard')
check('another account domain in From is rejected', M.decide(ctx({ from_domains = { 'a.test', 'c.test' } }), maps()).action == 'reject')
check('missing From is rejected', M.decide(ctx({ from_domains = {} }), maps()).action == 'reject')
check('no envelope domain is discarded', M.decide(ctx({ envelope_domain = ABSENT }), maps()).action == 'discard')
check('refusal text is the contract text', M.decide(ctx({ envelope_domain = 'c.test' }), maps()).message == 'sender not owned by account')
check('a From refusal keeps the contract text', M.decide(ctx({ from_domains = { 'c.test' } }), maps()).message == 'sender not owned by account')

-- rule 3
check('authenticated mailbox shares the account bucket', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), maps()).account == 'alice')
-- An authenticated login the map does not know fails closed (publication lag, a
-- stale map), where it used to mean "no limit".
check('unknown mailbox is deferred, not unlimited', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'x@z.test' }), maps()).action == 'soft reject')
check('unknown mailbox text', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'x@z.test' }), maps()).message == 'mailbox not bound to an account')
-- Inverse control: a known login is still counted.
check('known mailbox is counted', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), maps()).action == 'count')
check('login case does not escape the bucket', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'Info@A.Test' }), maps()).account == 'alice')
check('map keys are lower-cased', M.parse_pairs_lower_keys('Info@A.Test alice')['info@a.test'] == 'alice')
check('unauthenticated inbound is still not a submission', M.decide(ctx({ daemon_name = 'INBOUND', user = ABSENT }), maps()).action == 'none')
check('bucket key', M.bucket_key('alice') == 'maran:send:alice')
check('at the limit is allowed', M.finish(owned, 2).action == 'none')
-- Rspamd's milter prepends the class and enhanced code itself (451 4.7.1 / 554 5.7.1), so the
-- message carries none of its own.
check('over the limit is deferred with the limit text', M.finish(owned, 3).action == 'soft reject' and M.finish(owned, 3).message == 'hourly send limit reached')
check('no reply text repeats an enhanced status code', not M.finish(owned, 3).message:find('%d%.%d%.%d'))
check('a reject passes through finish', M.finish({ action = 'reject', message = 'x' }, 99).action == 'reject')
check('a discard passes through finish', M.finish({ action = 'discard', message = 'x' }, 99).action == 'discard')

-- recipients: counted per recipient, capped per local message
check('one message costs one per recipient', M.decide(ctx({ recipient_count = 7 }), maps()).cost == 7)
check('no recipients still cost one', M.decide(ctx({ recipient_count = 0 }), maps()).cost == 1)
check('authenticated mail costs per recipient too', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test', recipient_count = 4 }), maps()).cost == 4)
check('the cap itself is allowed', M.decide(ctx({ recipient_count = 100 }), maps()).action == 'count')
check('over the cap is rejected with its text', M.decide(ctx({ recipient_count = 101 }), maps()).message == 'too many recipients')
check('over the cap is a reject', M.decide(ctx({ recipient_count = 101 }), maps()).action == 'reject')
check('the cap does not hide a forged sender (discard first)', M.decide(ctx({ recipient_count = 101, envelope_domain = 'c.test' }), maps()).action == 'discard')
check('a batch over the remaining allowance is deferred', M.finish(M.decide(ctx({ recipient_count = 3 }), maps()), 3).action == 'soft reject')

-- idempotent counting: keyed on the maildrop id pickup wrote, never the cleanup queue id
check('maildrop id from the submitter header', M.topmost_id({ PICKUP }) == 'ABC')
check('no id after the uid', M.topmost_id({ '1001' }) == nil)
check('a foreign header has no id', M.topmost_id({ 'from a (b) by c id XYZ; date' }) == nil)
check('only the topmost header names the id', M.topmost_id({ PICKUP, FORGED }) == 'ABC')
check('identity is the topmost id', M.message_identity({ daemon_name = 'MARAN_LOCAL', submitters = { PICKUP } }) == 'id:ABC')
check('a forged second header does not change the identity',
  M.message_identity({ daemon_name = 'MARAN_LOCAL', submitters = { PICKUP, '1001 FORGED' } }) == 'id:ABC')
check('the same message offered again keeps its identity',
  M.message_identity({ daemon_name = 'MARAN_LOCAL', submitters = { PICKUP } }) == M.message_identity({ daemon_name = 'MARAN_LOCAL', submitters = { PICKUP } }))
check('another maildrop file has another identity',
  M.message_identity({ daemon_name = 'MARAN_LOCAL', submitters = { '1001 OTHER' } }) ~= 'id:ABC')
check('a Message-ID header is never an identity', M.message_identity({ daemon_name = 'MARAN_LOCAL', submitters = {}, message_id = '<a@b>' }) == nil)
check('the authenticated road has no identity', M.message_identity({ daemon_name = 'ORIGINATING', submitters = { PICKUP } }) == nil)
check('the verdict carries the identity', M.decide(ctx(), maps()).identity == 'id:ABC')
check('seen key joins account and identity', M.seen_key('alice', 'id:ABC') == 'maran:send:seen:alice:id:ABC')
check('no identity, no seen key', M.seen_key('alice', nil) == '' and M.seen_key('alice', '') == '')
check('seen keys differ by identity', M.seen_key('alice', 'id:A') ~= M.seen_key('alice', 'id:B'))

-- a rule that is installed but not configured
check('unconfigured: the local road is refused', M.decide_unconfigured('MARAN_LOCAL').action == 'soft reject')
check('unconfigured: an unidentified path is refused', M.decide_unconfigured(nil).action == 'soft reject')
check('unconfigured: inbound mail is left alone', M.decide_unconfigured('INBOUND').action == 'none')
check('unconfigured: authenticated submission is refused, not sent unbounded', M.decide_unconfigured('ORIGINATING').action == 'soft reject')

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
check('root needs no map', M.decide(ctx({ submitters = { '0 H' } }), {}).action == 'none')

-- alias fan-out (issue #531): an alias charges one per external target
local team = { 'team@a.test', 'x@gmail.test' }
check('an alias costs its external targets', M.decide(ctx({ recipient_count = 2, recipients = team }), maps()).cost == 6)
check('fan-out keys are case-insensitive', M.decide(ctx({ recipient_count = 1, recipients = { 'big@a.test' } }), maps()).cost == 40)
check('no alias, plain cost per recipient', M.decide(ctx({ recipient_count = 2, recipients = { 'x@y.test', 'z@y.test' } }), maps()).cost == 2)
check('an alias past the limit is deferred', M.finish(M.decide(ctx({ recipient_count = 1, recipients = { 'team@a.test' } }), maps()), 6).action == 'soft reject')
check('the same alias within the limit is allowed', M.finish(M.decide(ctx({ recipient_count = 1, recipients = { 'team@a.test' } }), maps()), 2).action == 'none')
check('cost without a fan-out table is the recipient count', M.cost(3, { 'a@b.test' }, nil) == 3)
check('an alias with a zero count still costs one', M.cost(1, { 'a@b.test' }, { ['a@b.test'] = '0' }) == 1)
local no_fanouts = maps(); no_fanouts.fanouts = nil
check('missing fan-out map defers local mail', M.decide(ctx(), no_fanouts).action == 'soft reject')
check('missing fan-out map text', M.decide(ctx(), no_fanouts).message == 'local submission maps unavailable')
check('authenticated mail needs the fan-out map too (the bucket charges it)', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), no_fanouts).action == 'soft reject')
check('root needs no fan-out map', M.decide(ctx({ submitters = { '0 H' } }), no_fanouts).action == 'none')

-- the bound-domain proof (issue #531): only a passed local message carries one
check('single From domain is the proof candidate', M.single_domain({ 'a.test', 'a.test' }) == 'a.test')
check('several From domains give no proof', M.single_domain({ 'a.test', 'b.test' }) == nil)
check('no From domain gives no proof', M.single_domain({}) == nil)
local counted_message = M.decide(ctx(), maps())
check('a counted local message carries its From domain', counted_message.bound_domain == 'a.test')
check('under the limit the proof survives finish', M.finish(counted_message, 1).bound_domain == 'a.test')
check('over the limit there is no proof', M.finish(counted_message, 3).bound_domain == nil)
check('a refused sender has no proof', M.decide(ctx({ from_domains = { 'c.test' } }), maps()).bound_domain == nil)
check('a discarded envelope has no proof', M.decide(ctx({ envelope_domain = 'c.test' }), maps()).bound_domain == nil)
check('two owned From domains give no proof', M.decide(ctx({ from_domains = { 'a.test', 'b.test' } }), maps()).bound_domain == nil)
check('authenticated mail of its own From domain records the proof', M.decide(ctx({ daemon_name = 'ORIGINATING', user = 'info@a.test' }), maps()).bound_domain == 'a.test')
check('root records the proof of the host mail name', M.decide(ctx({ submitters = { '0 H' }, from_domains = { 'host.test' } }), maps()).bound_domain == 'host.test')
check('inverse: root mail of a tenant domain records no proof', M.decide(ctx({ submitters = { '0 H' } }), maps()).bound_domain == nil)
check('the host mail name matches without regard to case', M.decide(ctx({ submitters = { '0 H' }, from_domains = { 'host.test' }, host_mail_name = 'HOST.test' }), maps()).bound_domain == 'host.test')
check('inverse: without a host mail name root records no proof', M.decide(ctx({ submitters = { '0 H' }, from_domains = { 'host.test' }, host_mail_name = ABSENT }), maps()).bound_domain == nil)
check('inverse: root with two From domains records no proof', M.decide(ctx({ submitters = { '0 H' }, from_domains = { 'host.test', 'b.test' } }), maps()).bound_domain == nil)

-- The authenticated road binds the From: header too (issue #531 review): the
-- envelope was bound by Postfix, the header was not.
local function login(overrides)
  local base = { daemon_name = 'ORIGINATING', user = 'info@a.test', submitters = {}, from_domains = { 'a.test' } }
  for key, value in pairs(overrides or {}) do
    base[key] = value
  end
  return ctx(base)
end
check('a login with its own From domain is counted', M.decide(login(), maps()).action == 'count')
check('a login with another account From domain is rejected', M.decide(login({ from_domains = { 'c.test' } }), maps()).action == 'reject')
check('the foreign From refusal text', M.decide(login({ from_domains = { 'c.test' } }), maps()).message == 'sender not owned by account')
check('one foreign From among owned ones is rejected', M.decide(login({ from_domains = { 'a.test', 'c.test' } }), maps()).action == 'reject')
check('a login with no From domain is rejected', M.decide(login({ from_domains = {} }), maps()).action == 'reject')
check('a foreign From carries no proof', M.decide(login({ from_domains = { 'c.test' } }), maps()).bound_domain == nil)
check('the second owned domain is accepted', M.decide(login({ from_domains = { 'b.test' } }), maps()).bound_domain == 'b.test')
check('two owned From domains give no proof on the login road', M.decide(login({ from_domains = { 'a.test', 'b.test' } }), maps()).bound_domain == nil)
check('a missing domains map defers the login road', M.decide(login(), no_domains).action == 'soft reject')
check('the login road passes finish with its proof', M.finish(M.decide(login(), maps()), 1).bound_domain == 'a.test')
check('inbound mail with a login-less foreign From is still not this rule\'s business', M.decide(ctx({ daemon_name = 'INBOUND', user = ABSENT, from_domains = { 'c.test' } }), maps()).action == 'none')
-- The login road is charged the alias fan-out too: one counter per account.
check('a login mailing an alias is charged its targets', M.decide(login({ recipient_count = 1, recipients = { 'team@a.test' } }), maps()).cost == 5)
check('a login mailing a plain address is charged one', M.decide(login({ recipient_count = 1, recipients = { 'x@y.test' } }), maps()).cost == 1)

-- Postfix resolves an address the way M.deliveries reads it: extension, catch-all.
local fanouts = M.parse_pairs_lower_keys('team@a.test 5 alice\n@b.test 7 alice\n')
local boxes = M.parse_pairs_lower_keys('info@a.test alice\nbox@b.test alice\n')
check('the delimiter is the one main.cf sets', M.RECIPIENT_DELIMITER == '+')
check('an extension does not escape the alias', M.deliveries('team+x@a.test', fanouts, boxes) == 5)
check('an extension with a long tag does not escape the alias', M.deliveries('Team+news+2@A.Test', fanouts, boxes) == 5)
check('a plus sign in the domain is not an extension', M.deliveries('plain@a+b.test', fanouts, boxes) == 1)
check('an address that is no alias is one delivery', M.deliveries('plain@a.test', fanouts, boxes) == 1)
check('a catch-all charges an unknown address of its domain', M.deliveries('anything@b.test', fanouts, boxes) == 7)
check('a catch-all charges an extension of an unknown address', M.deliveries('anything+x@b.test', fanouts, boxes) == 7)
check('a mailbox is one delivery whatever the catch-all says', M.deliveries('box@b.test', fanouts, boxes) == 1)
check('a mailbox with an extension is one delivery', M.deliveries('box+x@b.test', fanouts, boxes) == 1)
check('an explicit alias wins over the catch-all', M.deliveries('team@a.test', M.parse_pairs_lower_keys('team@a.test 5 x\n@a.test 9 x\n'), boxes) == 5)
check('a domain without a catch-all is unaffected', M.deliveries('anything@a.test', fanouts, boxes) == 1)
check('cost sums extension and catch-all recipients', M.cost(3, { 'team+x@a.test', 'zzz@b.test', 'q@y.test' }, fanouts, boxes) == 13)
check('without the mailbox set a catch-all is charged', M.deliveries('box@b.test', fanouts, nil) == 7)
local via_decide = M.decide(ctx({ recipient_count = 2, recipients = { 'team+x@a.test', 'anything@b.test' } }), maps())
check('decide charges the extension and the catch-all', via_decide.cost == 12)

-- N2: an internal alias is a key of its own (count 0), so it never falls through to the
-- catch-all, with or without an extension; an unclaimed address still does.
local with_internal = M.parse_pairs_lower_keys('info@a.test 0 alice\n@a.test 20 alice\n')
check('an internal alias costs one beside a fanning catch-all', M.deliveries('info@a.test', with_internal, boxes) == 0 and M.cost(1, { 'info@a.test' }, with_internal, boxes) == 1)
check('an extension of an internal alias costs one', M.cost(1, { 'info+x@a.test' }, with_internal, boxes) == 1)
check('an unclaimed address still takes the catch-all', M.cost(1, { 'nobody@a.test' }, with_internal, boxes) == 20)
check('inverse: without the alias key the catch-all over-charges (the old map)', M.cost(1, { 'info@a.test' }, M.parse_pairs_lower_keys('@a.test 20 alice\n'), nil) == 20)
check('a mailbox is one even when a catch-all fans out', M.cost(1, { 'info@a.test' }, M.parse_pairs_lower_keys('@a.test 20 alice\n'), M.parse_pairs_lower_keys('info@a.test alice\n')) == 1)

-- N4: send-as of an alias the account owns is a domain the account owns; a foreign domain is not
check('an alias address of an owned domain is an acceptable From on the login road', M.decide(login({ from_domains = { 'a.test' } }), maps()).action == 'count')
check('inverse: an external send-as domain is refused on the login road', M.decide(login({ from_domains = { 'gmail.test' } }), maps()).action == 'reject')

-- N5: root and the Postfix owner keep no ownership check; their mail carries the proof of its From: domain only when that is the host's own mail name
local root = { '0 H' }
check('root mail with one From domain carries that proof', M.decide(ctx({ submitters = root, from_domains = { 'host.test' } }), maps()).bound_domain == 'host.test')
check('root mail with two From domains carries no proof', M.decide(ctx({ submitters = root, from_domains = { 'host.test', 'b.test' } }), maps()).bound_domain == nil)
check('root mail without a From domain carries no proof', M.decide(ctx({ submitters = root, from_domains = {} }), maps()).bound_domain == nil)
check('root mail with an unreadable From domain carries no proof', M.decide(ctx({ submitters = root, from_domains = { '' } }), maps()).bound_domain == nil)
check('inverse: a customer with a foreign From still gets no proof', M.decide(ctx({ from_domains = { 'host.test' } }), maps()).bound_domain == nil)

-- N7: internationalised domains are compared as A-labels
check('ascii domains are only lower-cased', M.to_ascii_domain('A.Test') == 'a.test')
check('a trailing dot is dropped', M.to_ascii_domain('a.test.') == 'a.test')
check('latin domain to punycode', M.to_ascii_domain('b\195\188cher.example') == 'xn--bcher-kva.example')
check('upper-case latin letters fold first', M.to_ascii_domain('B\195\156CHER.example') == 'xn--bcher-kva.example')
check('cyrillic domain to punycode', M.to_ascii_domain('\208\191\209\128\208\184\208\188\208\181\209\128.\208\184\209\129\208\191\209\139\209\130\208\176\208\189\208\184\208\181') == 'xn--e1afmkfd.xn--80akhbyknj4f')
check('an A-label stays as it is', M.to_ascii_domain('xn--bcher-kva.example') == 'xn--bcher-kva.example')
check('malformed utf-8 is refused', M.to_ascii_domain('b\195.example') == nil)
check('an overlong utf-8 form is refused', M.to_ascii_domain('\192\128.example') == nil)
check('a surrogate is refused', M.to_ascii_domain('\237\160\128.example') == nil)
check('an overlong name is refused', M.to_ascii_domain(string.rep('a', 300)) == nil)
check('a non-string is refused', M.to_ascii_domain(nil) == nil)
local idn_maps = maps(); idn_maps.domains = M.parse_sets('alice xn--bcher-kva.example\n')
check('a Unicode From matches its A-label owner entry', M.decide(ctx({ envelope_domain = 'xn--bcher-kva.example', from_domains = { M.to_ascii_domain('b\195\188cher.example') } }), idn_maps).action == 'count')
check('inverse: the raw Unicode spelling never matches the A-label entry', M.decide(ctx({ envelope_domain = 'xn--bcher-kva.example', from_domains = { 'b\195\188cher.example' } }), idn_maps).action == 'reject')
check('an empty (unreadable) From domain is refused beside a readable one', M.decide(ctx({ from_domains = { 'a.test', '' } }), maps()).action == 'reject')

if failures > 0 then
  os.exit(1)
end
print('ok')
