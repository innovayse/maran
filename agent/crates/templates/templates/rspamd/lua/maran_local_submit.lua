-- Maran: binds mail an account submits locally (sendmail, PHP mail(), cron) to the
-- account's own sender domains and to its hourly send limit (issue #518,
-- docs/mail/local-submission.md). Installed by the Maran agent; hand edits are
-- overwritten on the next apply.
--
-- Everything that decides is a pure function of plain values (M.decide and its
-- helpers) so tests can drive it with a stub; only the block at the bottom touches
-- the Rspamd API, and only when it exists.

local M = {}

M.BUCKET_PREFIX = 'maran:send:'
M.SEEN_PREFIX = 'maran:send:seen:'
M.WINDOW_SECONDS = 3600

-- The character Postfix cuts an address extension at (`recipient_delimiter` in
-- main.cf): `alias+tag@domain` is delivered as `alias@domain`. The recipients are
-- looked up the way Postfix resolves them, so the cost cannot be dodged by naming an
-- extension. A test holds this and main.cf to the same character.
M.RECIPIENT_DELIMITER = '+'

-- The daemon name Postfix reports for each road a message can arrive by
-- (milter_macro_daemon_name in master.cf, a fact of the server and not of the
-- message). LOCAL is the `cleanup_local` service that only `pickup` uses; the
-- other two are the smtpd services. Any other name is refused (fail-closed).
M.LOCAL_DAEMON = 'MARAN_LOCAL'
M.SMTP_DAEMONS = { INBOUND = true, ORIGINATING = true }

-- Splits map text into lines of whitespace separated fields, skipping blanks and
-- comments.
function M.parse_rows(text)
  local rows = {}
  for line in string.gmatch(text or '', '[^\r\n]+') do
    local trimmed = line:match('^%s*(.-)%s*$')
    if trimmed ~= '' and trimmed:sub(1, 1) ~= '#' then
      local fields = {}
      for field in string.gmatch(trimmed, '%S+') do
        fields[#fields + 1] = field
      end
      rows[#rows + 1] = fields
    end
  end
  return rows
end

-- "<key> <value>" lines into key -> value.
function M.parse_pairs(text)
  local map = {}
  for _, fields in ipairs(M.parse_rows(text)) do
    if #fields >= 2 then
      map[fields[1]] = fields[2]
    end
  end
  return map
end

-- Same as parse_pairs with the key lower-cased: a mailbox login is compared
-- case-insensitively, and the agent renders it lower-case.
function M.parse_pairs_lower_keys(text)
  local map = {}
  for _, fields in ipairs(M.parse_rows(text)) do
    if #fields >= 2 then
      map[fields[1]:lower()] = fields[2]
    end
  end
  return map
end

-- "<key> <value>" lines, a key repeating, into key -> set of lowercase values.
function M.parse_sets(text)
  local map = {}
  for _, fields in ipairs(M.parse_rows(text)) do
    if #fields >= 2 then
      map[fields[1]] = map[fields[1]] or {}
      map[fields[1]][fields[2]:lower()] = true
    end
  end
  return map
end

-- The uid Postfix's pickup recorded in one Received header value, or nil when the
-- value is not that exact shape.
function M.uid_from_received(value)
  if type(value) ~= 'string' then
    return nil
  end
  local uid = value:match('^%s*by%s+%S+%s+%(Postfix,%s+from%s+userid%s+(%d+)%)')
  return uid
end

-- Only the topmost Received header is Postfix's own; everything below it was
-- written by the sender and is never read. `values` is in message order.
function M.topmost_uid(values)
  if type(values) ~= 'table' or values[1] == nil then
    return nil
  end
  return M.uid_from_received(values[1])
end

-- The message id Postfix's pickup recorded on its own Received header (the name of
-- the maildrop file, stable across every re-offer of a deferred message), or nil.
-- Only meaningful for the topmost header; M.uid_from_received guards the shape.
function M.id_from_received(value)
  if M.uid_from_received(value) == nil then
    return nil
  end
  return value:match('^%s*by%s+%S+%s+%(Postfix,%s+from%s+userid%s+%d+%)%s+id%s+(%w+)')
end

-- One stable identity per message, used to count it once however often it is
-- offered again: the id Postfix's pickup wrote on the topmost Received header of a
-- locally submitted message, or nil. Nothing else identifies a message safely: a
-- Message-ID header is the sender's own text, so keying on it would let a customer
-- repeat one and send without ever being counted. Nil means the message cannot be
-- remembered and is counted every time, which only ever over-counts (the
-- authenticated road is in that case: a client retry opens a new transaction).
function M.message_identity(ctx)
  if ctx.daemon_name == M.LOCAL_DAEMON and type(ctx.received) == 'table' then
    local id = M.id_from_received(ctx.received[1])
    if id then
      return 'id:' .. id
    end
  end
  return nil
end

-- ---------------------------------------------------------------------------
-- Internationalised domains. The sender-domain map holds the A-label (punycode)
-- spelling of every domain, because that is what the panel stores and what a DKIM
-- selector is looked up by. A From: header can carry the same domain in its Unicode
-- spelling (SMTPUTF8), which would never equal the map entry, so every From: domain
-- is brought to the A-label form before it is compared. Pure Lua 5.1 (LuaJIT, which
-- Rspamd runs): no utf8 library and no integer division operator.
-- ---------------------------------------------------------------------------

-- The longest domain name that is looked at; a longer one is refused outright, which
-- also bounds the work of the quadratic encoder below.
M.MAX_DOMAIN_LENGTH = 253

local PUNY_BASE, PUNY_TMIN, PUNY_TMAX = 36, 1, 26
local PUNY_SKEW, PUNY_DAMP = 38, 700
local PUNY_INITIAL_BIAS, PUNY_INITIAL_N = 72, 128

local function floor_div(a, b)
  return math.floor(a / b)
end

local function puny_adapt(delta, numpoints, first)
  if first then
    delta = floor_div(delta, PUNY_DAMP)
  else
    delta = floor_div(delta, 2)
  end
  delta = delta + floor_div(delta, numpoints)
  local k = 0
  while delta > floor_div((PUNY_BASE - PUNY_TMIN) * PUNY_TMAX, 2) do
    delta = floor_div(delta, PUNY_BASE - PUNY_TMIN)
    k = k + PUNY_BASE
  end
  return k + floor_div((PUNY_BASE - PUNY_TMIN + 1) * delta, delta + PUNY_SKEW)
end

local function puny_digit(d)
  if d < 26 then
    return string.char(97 + d)
  end
  return string.char(22 + d)
end

-- RFC 3492 encoding of a list of code points (without the `xn--` prefix).
function M.punycode_encode(cps)
  local out = {}
  local basic = 0
  for _, cp in ipairs(cps) do
    if cp < 128 then
      out[#out + 1] = string.char(cp)
      basic = basic + 1
    end
  end
  local handled = basic
  if basic > 0 then
    out[#out + 1] = '-'
  end
  local n, delta, bias = PUNY_INITIAL_N, 0, PUNY_INITIAL_BIAS
  while handled < #cps do
    local m = math.huge
    for _, cp in ipairs(cps) do
      if cp >= n and cp < m then
        m = cp
      end
    end
    delta = delta + (m - n) * (handled + 1)
    n = m
    for _, cp in ipairs(cps) do
      if cp < n then
        delta = delta + 1
      end
      if cp == n then
        local q, k = delta, PUNY_BASE
        while true do
          local t
          if k <= bias then
            t = PUNY_TMIN
          elseif k >= bias + PUNY_TMAX then
            t = PUNY_TMAX
          else
            t = k - bias
          end
          if q < t then
            break
          end
          out[#out + 1] = puny_digit(t + (q - t) % (PUNY_BASE - t))
          q = floor_div(q - t, PUNY_BASE - t)
          k = k + PUNY_BASE
        end
        out[#out + 1] = puny_digit(q)
        bias = puny_adapt(delta, handled + 1, handled == basic)
        delta = 0
        handled = handled + 1
      end
    end
    delta = delta + 1
    n = n + 1
  end
  return table.concat(out)
end

-- The code points of a UTF-8 string, or nil when it is not well-formed UTF-8
-- (overlong forms, surrogates and values above U+10FFFF included).
function M.utf8_codepoints(text)
  local cps = {}
  local i, length = 1, #text
  while i <= length do
    local b = text:byte(i)
    local cp, extra, minimum
    if b < 0x80 then
      cp, extra, minimum = b, 0, 0
    elseif b >= 0xC2 and b <= 0xDF then
      cp, extra, minimum = b - 0xC0, 1, 0x80
    elseif b >= 0xE0 and b <= 0xEF then
      cp, extra, minimum = b - 0xE0, 2, 0x800
    elseif b >= 0xF0 and b <= 0xF4 then
      cp, extra, minimum = b - 0xF0, 3, 0x10000
    else
      return nil
    end
    for offset = 1, extra do
      local c = text:byte(i + offset)
      if c == nil or c < 0x80 or c > 0xBF then
        return nil
      end
      cp = cp * 64 + (c - 0x80)
    end
    if cp < minimum or cp > 0x10FFFF or (cp >= 0xD800 and cp <= 0xDFFF) then
      return nil
    end
    cps[#cps + 1] = cp
    i = i + extra + 1
  end
  return cps
end

-- Simple lower-casing of the scripts a domain realistically uses (Latin-1, Greek,
-- Cyrillic). A letter outside them keeps its case: the comparison then fails and the
-- message is refused, which is the safe side.
local function lower_codepoint(cp)
  if cp >= 0x41 and cp <= 0x5A then
    return cp + 32
  end
  if (cp >= 0xC0 and cp <= 0xDE and cp ~= 0xD7) or (cp >= 0x391 and cp <= 0x3A9 and cp ~= 0x3A2) then
    return cp + 32
  end
  if cp >= 0x410 and cp <= 0x42F then
    return cp + 32
  end
  if cp >= 0x400 and cp <= 0x40F then
    return cp + 80
  end
  return cp
end

-- A domain name in its lowercase A-label form (`bücher.example` becomes
-- `xn--bcher-kva.example`), or nil when it cannot be brought to it: not UTF-8, or too
-- long. An ASCII domain is only lower-cased, and a trailing dot is dropped.
function M.to_ascii_domain(domain)
  if type(domain) ~= 'string' or #domain > M.MAX_DOMAIN_LENGTH then
    return nil
  end
  local name = domain:gsub('%.$', '')
  if not name:find('[\128-\255]') then
    return name:lower()
  end
  local labels = {}
  for label in (name .. '.'):gmatch('([^.]*)%.') do
    if label:find('[\128-\255]') then
      local cps = M.utf8_codepoints(label)
      if cps == nil then
        return nil
      end
      for index, cp in ipairs(cps) do
        cps[index] = lower_codepoint(cp)
      end
      labels[#labels + 1] = 'xn--' .. M.punycode_encode(cps)
    else
      labels[#labels + 1] = label:lower()
    end
  end
  return table.concat(labels, '.')
end

function M.bucket_key(account)
  return M.BUCKET_PREFIX .. account
end

-- The key that remembers a message identity (M.message_identity) was already
-- counted, or '' when the message has none (then it cannot be remembered and is
-- counted every time, which only ever over-counts).
function M.seen_key(account, identity)
  if type(identity) ~= 'string' or identity == '' then
    return ''
  end
  return M.SEEN_PREFIX .. account .. ':' .. identity
end

-- How many deliveries Postfix makes for one recipient, read the way Postfix resolves
-- it: the address itself, then the address without its extension, then the domain's
-- catch-all (`@domain`), and the catch-all only for an address that is neither an
-- alias nor a mailbox. A mailbox of the account is delivered once, whatever the
-- catch-all says. An address that is none of these is one delivery. `fanouts` maps a
-- lowercase alias address, or a domain's `@domain` catch-all, to the number of
-- outside deliveries the whole expansion makes (nested aliases included, counted by
-- the panel). EVERY alias is a key, an alias that stays inside the account with the
-- count 0 (cost one, like a mailbox): that is how an internal alias is told apart
-- from an address nobody claims, which Postfix hands to the catch-all.
-- `mailboxes` is the set of mailbox logins (may be nil).
function M.deliveries(recipient, fanouts, mailboxes)
  local address = tostring(recipient):lower()
  local direct = tonumber(fanouts[address])
  if direct then
    return direct
  end
  local base = address
  local at = address:find('@', 1, true)
  local domain = at and address:sub(at) or nil
  if at then
    local cut = address:find(M.RECIPIENT_DELIMITER, 1, true)
    if cut and cut < at then
      base = address:sub(1, cut - 1) .. domain
      local stripped = tonumber(fanouts[base])
      if stripped then
        return stripped
      end
    end
  end
  if type(mailboxes) == 'table' and (mailboxes[address] or mailboxes[base]) then
    return 1
  end
  if domain then
    local catch_all = tonumber(fanouts[domain])
    if catch_all then
      return catch_all
    end
  end
  return 1
end

-- What one message costs the bucket: one per envelope recipient, at least one.
--
-- When the caller names the recipients and the alias fan-out table, a recipient
-- that Postfix expands outside the server costs one per outside delivery (at least
-- one), because Postfix expands the alias AFTER this rule counted the recipient once
-- and every delivery is a message sent (M.deliveries). Without `fanouts` or
-- `recipients` the cost is the plain recipient count.
function M.cost(recipient_count, recipients, fanouts, mailboxes)
  if type(recipients) == 'table' and type(fanouts) == 'table' then
    local total = 0
    for _, recipient in ipairs(recipients) do
      local targets = M.deliveries(recipient, fanouts, mailboxes)
      if targets < 1 then
        targets = 1
      end
      total = total + targets
    end
    if total < 1 then
      return 1
    end
    return total
  end
  local count = tonumber(recipient_count) or 0
  if count < 1 then
    return 1
  end
  return count
end

-- The one domain every From: address of a message shares, or nil when there is
-- none or more than one. Only a message with exactly one From: domain can be bound
-- to it as the DKIM proof; a message that names several is simply not signed.
function M.single_domain(domains)
  local found = nil
  for _, domain in ipairs(domains or {}) do
    if found == nil then
      found = domain
    elseif found ~= domain then
      return nil
    end
  end
  if found == '' then
    return nil
  end
  return found
end

function M.over_limit(count, limit)
  return count > limit
end

local function verdict(action, message, extra)
  local result = { action = action, message = message }
  for key, value in pairs(extra or {}) do
    result[key] = value
  end
  return result
end

local function maps_missing(maps, names)
  for _, name in ipairs(names) do
    if maps[name] == nil then
      return true
    end
  end
  return false
end

local UNAVAILABLE = 'local submission maps unavailable'

-- Counts one message against the account's bucket, or defers it when the account
-- has no limit. Both roads charge the alias fan-out (`maps.fanouts`): the bucket is
-- one counter per account, and a login that mails an alias of twenty outside
-- targets sends twenty messages. `bound_domain` is the From: domain the message
-- proves once it has passed every check (nil when there is nothing to prove).
local function counted(ctx, maps, account, bound_domain)
  local limit = tonumber(maps.limits[account])
  if limit == nil then
    return verdict('soft reject', UNAVAILABLE)
  end
  return verdict('count', nil, {
    account = account,
    limit = limit,
    cost = M.cost(ctx.recipient_count, ctx.recipients, maps.fanouts, maps.mailboxes),
    identity = M.message_identity(ctx),
    bound_domain = bound_domain,
  })
end

-- The road `pickup` feeds. The submitter is the uid Postfix stamped, never
-- anything the message says about itself.
--
-- A refusal whose bounce would reach an address nobody verified (an unknown
-- submitter, an envelope sender the account does not own) is a `discard`: Postfix
-- would otherwise mail a delivery failure notice to that address, which turns a
-- forged `sendmail -f victim@example.net` into a way to mail the victim. A `From:`
-- refusal with an owned envelope sender is a `reject`: its notice goes to the
-- account itself.
local function decide_local(ctx, maps)
  local uid = M.topmost_uid(ctx.received)
  if uid == nil then
    return verdict('discard', 'local submitter unknown')
  end
  if ctx.exempt_uids and ctx.exempt_uids[uid] then
    -- Root and the Postfix owner are the operator's own: no ownership check and no
    -- limit. Their mail records the proof of its From: domain ONLY when that domain is
    -- the host's own mail name (rendered into the settings), so the signing module can
    -- never be asked to sign a tenant's domain on the strength of a trusted uid: a
    -- compromised daemon running as the Postfix owner, or a root job built from user
    -- data, gets no signature of a customer's domain from here. Any other From: domain
    -- leaves no proof and the message is not signed.
    local single = M.single_domain(ctx.from_domains)
    local host = ctx.host_mail_name and tostring(ctx.host_mail_name):lower() or nil
    local bound = nil
    if single ~= nil and host ~= nil and host ~= '' and single:lower() == host then
      bound = single
    end
    return verdict('none', nil, { bound_domain = bound })
  end
  if maps_missing(maps, { 'accounts', 'domains', 'limits', 'fanouts' }) then
    return verdict('soft reject', UNAVAILABLE)
  end
  local account = maps.accounts[uid]
  if account == nil then
    return verdict('discard', 'local submitter unknown')
  end

  local owned = maps.domains[account] or {}
  if ctx.envelope_domain == nil or not owned[ctx.envelope_domain] then
    return verdict('discard', 'sender not owned by account')
  end
  local from_domains = ctx.from_domains or {}
  if #from_domains == 0 then
    return verdict('reject', 'sender not owned by account')
  end
  for _, domain in ipairs(from_domains) do
    if not owned[domain] then
      return verdict('reject', 'sender not owned by account')
    end
  end

  -- smtpd_recipient_limit does not apply to pickup, so the cap is this rule's.
  if ctx.max_recipients and (tonumber(ctx.recipient_count) or 0) > ctx.max_recipients then
    return verdict('reject', 'too many recipients')
  end
  return counted(ctx, maps, account, M.single_domain(from_domains))
end

-- Decides one message. `ctx`:
--   daemon_name       the MTA-Name Postfix reported (see M.LOCAL_DAEMON)
--   user              authenticated login or nil
--   received          Received header values, topmost first
--   envelope_domain, from_domains   lowercase sender domains
--   recipient_count   number of envelope recipients
--   recipients        the envelope recipient addresses, lowercase (alias fan-out cost)
--   max_recipients    cap per locally submitted message
--   exempt_uids       set of uids (as strings) the rule leaves alone
--   host_mail_name    the host's own mail name: the only From: domain an exempt uid's mail is
--                     proven for (nil leaves it no proof)
-- `maps`: accounts, domains, limits, mailboxes, fanouts; a map that never loaded is
-- nil. Returns { action = 'none'|'reject'|'discard'|'soft reject'|'count', message,
-- account, limit, cost, bound_domain }.
--
-- The daemon name is decided FIRST. Postfix presents a locally submitted message
-- to the milter as a connection from 127.0.0.1, so a client address or a login
-- says nothing about which road it came by.
function M.decide(ctx, maps)
  if ctx.daemon_name == M.LOCAL_DAEMON then
    return decide_local(ctx, maps)
  end
  if not M.SMTP_DAEMONS[ctx.daemon_name or ''] then
    return verdict('soft reject', 'mail path unidentified')
  end
  if not ctx.user then
    -- Unauthenticated SMTP (inbound mail): not a submission.
    return verdict('none')
  end
  -- Authenticated SMTP. Postfix bound the ENVELOPE sender to the login
  -- (reject_sender_login_mismatch); nothing bound the From: header, which is what a
  -- signature and a recipient's mail client show. This rule binds it, the same way
  -- it does on the local road, and records the proof the DKIM condition reads.
  if maps_missing(maps, { 'mailboxes', 'limits', 'domains', 'fanouts' }) then
    return verdict('soft reject', UNAVAILABLE)
  end
  -- Every login that reaches a submission port is a mailbox of some account, so a
  -- login the map does not know is a publication lag or a stale map, never "no
  -- limit": fail closed. The map keys are lower-case; so is the lookup.
  local login = tostring(ctx.user):lower()
  local account = maps.mailboxes[login]
  if account == nil then
    return verdict('soft reject', 'mailbox not bound to an account')
  end
  local owned = maps.domains[account] or {}
  local from_domains = ctx.from_domains or {}
  if #from_domains == 0 then
    return verdict('reject', 'sender not owned by account')
  end
  for _, domain in ipairs(from_domains) do
    if not owned[domain] then
      return verdict('reject', 'sender not owned by account')
    end
  end
  return counted(ctx, maps, account, M.single_domain(from_domains))
end

-- The answer when the rule's settings section is missing or incomplete (the rule is
-- installed but not configured). The local road, an unidentified path and the
-- authenticated submission road are refused: a mailbox login must never send
-- unbounded and unaccounted because a settings file went missing. Only inbound mail
-- (INBOUND) is left alone: it is not this rule's business without maps, and refusing
-- it would stop incoming mail for a settings mistake.
function M.decide_unconfigured(daemon_name)
  if daemon_name == 'INBOUND' then
    return verdict('none')
  end
  return verdict('soft reject', 'local submission rule not configured')
end

-- Turns the verdict plus the counter's value into the final answer.
function M.finish(decision, count)
  if decision.action ~= 'count' then
    return decision
  end
  if M.over_limit(count, decision.limit) then
    return verdict('soft reject', 'hourly send limit reached')
  end
  -- The message passed the envelope, From: and rate checks: only now may its From:
  -- domain be recorded as bound to the submitting account (see apply).
  return verdict('none', nil, { bound_domain = decision.bound_domain })
end

-- ---------------------------------------------------------------------------
-- Rspamd binding. Loaded as a MODULE: the file is installed as
-- <rspamd dir>/plugins.d/maran_local_submit.lua and its settings are included from
-- rspamd.conf.local (Rspamd reads neither lua.local.d nor an unknown local.d file).
-- Verified on a live Rspamd 3.8 to 4.2 (milter path, container): the add_map callback
-- type, the prefilter at priority 100, set_pre_result for 'reject' and 'soft reject'
-- (and for 'discard' only with the custom `discard` action rendered into actions.conf),
-- has_pre_result, MTA-Name from the {daemon_name} macro, topmost-first Received order,
-- lua_redis.request, cache_set/cache_get across modules, the `addr` field of
-- get_recipients. Not verified here: the real Postfix side (docker/polygon).
-- ---------------------------------------------------------------------------
if rspamd_config == nil then
  return M
end

local lua_redis = require 'lua_redis'
local rspamd_logger = require 'rspamd_logger'
local opts = rspamd_config:get_all_opt('maran_local_submit')
if opts and opts.enabled == false then
  -- Switched off on purpose by the rendered settings.
  return M
end

local MODULE = 'maran_local_submit'

-- The A-label spelling of an address's domain (M.to_ascii_domain), or nil.
local function domain_of(address)
  if address and address.domain and address.domain ~= '' then
    return M.to_ascii_domain(tostring(address.domain))
  end
  return nil
end

local function request_header(task, name)
  local value = task:get_request_header(name)
  if value == nil then
    return nil
  end
  return tostring(value)
end

-- Sets the answer. Rspamd RAISES (it does not return a failure) for an action it does
-- not know, so the call is guarded and a refusal that did not take falls back to a
-- hard reject: a refusal never degrades to acceptance, and a `discard` on a host whose
-- actions.conf lacks the custom `discard` action still refuses instead of deferring
-- forever. Rspamd prepends the class and enhanced code to the text itself
-- (451 4.7.1 for a soft reject, 554 5.7.1 for a reject), so the messages carry none.
local function apply(task, answer)
  if answer.action == 'none' then
    -- The proof the DKIM signing condition reads (maran_dkim_condition.lua): this
    -- message's From: domain belongs to the account that submitted it. Written only
    -- by an accepted local message; every other road, and every refusal, leaves
    -- none, and the condition then signs nothing for the local road.
    if answer.bound_domain then
      task:cache_set(opts.bound_domain_key, answer.bound_domain)
    end
    return
  end
  if answer.action == 'discard' then
    rspamd_logger.infox(task, 'maran_local_submit discards: %s', answer.message)
  end
  local ok = pcall(task.set_pre_result, task, answer.action, answer.message, MODULE)
  if not ok or not task:has_pre_result() then
    task:set_pre_result('reject', answer.message, MODULE)
  end
end

local function fail_closed(task, err)
  rspamd_logger.errx(task, 'maran_local_submit failed closed: %s', tostring(err))
  task:set_pre_result('soft reject', UNAVAILABLE, MODULE)
end

-- Registers the rule's prefilter. One place, so the configured rule and the
-- fail-closed stand-in below cannot drift apart.
local function register(callback)
  rspamd_config:register_symbol({
    name = 'MARAN_LOCAL_SUBMIT',
    type = 'prefilter',
    priority = 100,
    callback = callback,
  })
end

-- The settings every rendered file must carry. A section that is missing (the
-- local.d file renamed, an include order that skips it) or incomplete must not
-- leave the local road unexamined: the stand-in below is registered instead and
-- refuses it.
local REQUIRED_OPTIONS = {
  'accounts_map',
  'sender_domains_map',
  'send_limits_map',
  'mailbox_accounts_map',
  'alias_fanout_map',
  'bound_domain_key',
}
local function misconfiguration()
  if not opts then
    return 'settings section maran_local_submit not found'
  end
  for _, name in ipairs(REQUIRED_OPTIONS) do
    if type(opts[name]) ~= 'string' or opts[name] == '' then
      return 'setting ' .. name .. ' missing'
    end
  end
  return nil
end

local problem = misconfiguration()
if problem then
  rspamd_logger.errx(rspamd_config, 'maran_local_submit not configured (%s): refusing local mail', problem)
  register(function(task)
    local ok, err = pcall(function()
      apply(task, M.decide_unconfigured(request_header(task, 'MTA-Name')))
    end)
    if not ok then
      fail_closed(task, err)
    end
  end)
  return M
end

local maps = {}
local function watch(option, name, parser)
  rspamd_config:add_map({
    type = 'callback',
    url = opts[option],
    description = 'Maran local submission ' .. name,
    callback = function(text)
      maps[name] = parser(text)
    end,
  })
end
watch('accounts_map', 'accounts', M.parse_pairs)
watch('sender_domains_map', 'domains', M.parse_sets)
watch('send_limits_map', 'limits', M.parse_pairs)
watch('mailbox_accounts_map', 'mailboxes', M.parse_pairs_lower_keys)
watch('alias_fanout_map', 'fanouts', M.parse_pairs_lower_keys)

-- Exactly the uids the agent rendered (root and the Postfix owner); nothing a
-- customer can influence.
local exempt = {}
for _, uid in ipairs(opts.exempt_uids or {}) do
  exempt[tostring(uid)] = true
end
local max_recipients = tonumber(opts.max_recipients)

local redis_params = lua_redis.parse_redis_server('maran_local_submit')

-- Adds `cost` to the account's bucket unless this message identity was counted
-- already (a message the milter defers is re-offered under a NEW queue id, but
-- with the same maildrop id on its pickup Received header). Returns the
-- bucket's value.
local INCREMENT = "if KEYS[2] ~= '' and not redis.call('SET', KEYS[2], '1', 'NX', 'EX', ARGV[1]) then "
  .. "return tonumber(redis.call('GET', KEYS[1]) or '0') end; "
  .. "local c = redis.call('INCRBY', KEYS[1], ARGV[2]); "
  .. "if redis.call('TTL', KEYS[1]) < 0 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end; return c"

local function check(task)
  local from_domains = {}
  for _, address in ipairs(task:get_from('mime') or {}) do
    -- A From: address whose domain cannot be read is recorded as the empty name, which
    -- no account owns: the message is refused, and the readable address beside it can
    -- never stand for the whole header.
    from_domains[#from_domains + 1] = domain_of(address) or ''
  end
  local received = {}
  for _, header in ipairs(task:get_header_full('Received') or {}) do
    received[#received + 1] = header.decoded or header.value
  end
  local recipients = {}
  for _, address in ipairs(task:get_recipients('smtp') or {}) do
    if address.addr then
      recipients[#recipients + 1] = address.addr:lower()
    end
  end
  local envelope = task:get_from('smtp')
  local decision = M.decide({
    daemon_name = request_header(task, 'MTA-Name'),
    user = task:get_user(),
    received = received,
    envelope_domain = envelope and envelope[1] and domain_of(envelope[1]) or nil,
    from_domains = from_domains,
    recipient_count = #(task:get_recipients('smtp') or {}),
    recipients = recipients,
    max_recipients = max_recipients,
    exempt_uids = exempt,
    host_mail_name = opts.host_mail_name,
  }, maps)

  if decision.action ~= 'count' then
    apply(task, decision)
    return
  end

  local function on_reply(err, data)
    local ok, failure = pcall(function()
      if err or tonumber(data) == nil then
        apply(task, { action = 'soft reject', message = UNAVAILABLE })
        return
      end
      apply(task, M.finish(decision, tonumber(data)))
    end)
    if not ok then
      fail_closed(task, failure)
    end
  end
  local sent = lua_redis.request(redis_params, {
    task = task,
    is_write = true,
    callback = on_reply,
  }, {
    'EVAL',
    INCREMENT,
    2,
    M.bucket_key(decision.account),
    M.seen_key(decision.account, decision.identity),
    tostring(M.WINDOW_SECONDS),
    tostring(decision.cost),
  })
  if not sent then
    apply(task, { action = 'soft reject', message = UNAVAILABLE })
  end
end

-- Any Lua error in the rule is a refusal, never an acceptance: Rspamd carries on
-- scanning (and the mail passes) when a prefilter dies without a verdict.
local function guarded(task)
  local ok, err = pcall(check, task)
  if not ok then
    fail_closed(task, err)
  end
end

-- The highest priority of any prefilter, so no other rule's verdict (a settings or
-- whitelist entry matching 127.0.0.1) is reached before this one.
register(guarded)

return M
