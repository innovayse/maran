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

-- What one message costs the bucket: one per envelope recipient, at least one.
function M.cost(recipient_count)
  local count = tonumber(recipient_count) or 0
  if count < 1 then
    return 1
  end
  return count
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

local UNAVAILABLE = '4.4.3 local submission maps unavailable'

-- Counts one message against the account's bucket, or defers it when the account
-- has no limit.
local function counted(ctx, maps, account)
  local limit = tonumber(maps.limits[account])
  if limit == nil then
    return verdict('soft reject', UNAVAILABLE)
  end
  return verdict('count', nil, {
    account = account,
    limit = limit,
    cost = M.cost(ctx.recipient_count),
    identity = M.message_identity(ctx),
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
    return verdict('discard', '5.7.1 local submitter unknown')
  end
  if ctx.exempt_uids and ctx.exempt_uids[uid] then
    return verdict('none')
  end
  if maps_missing(maps, { 'accounts', 'domains', 'limits' }) then
    return verdict('soft reject', UNAVAILABLE)
  end
  local account = maps.accounts[uid]
  if account == nil then
    return verdict('discard', '5.7.1 local submitter unknown')
  end

  local owned = maps.domains[account] or {}
  if ctx.envelope_domain == nil or not owned[ctx.envelope_domain] then
    return verdict('discard', '5.7.1 sender not owned by account')
  end
  local from_domains = ctx.from_domains or {}
  if #from_domains == 0 then
    return verdict('reject', '5.7.1 sender not owned by account')
  end
  for _, domain in ipairs(from_domains) do
    if not owned[domain] then
      return verdict('reject', '5.7.1 sender not owned by account')
    end
  end

  -- smtpd_recipient_limit does not apply to pickup, so the cap is this rule's.
  if ctx.max_recipients and (tonumber(ctx.recipient_count) or 0) > ctx.max_recipients then
    return verdict('reject', '5.5.3 too many recipients')
  end
  return counted(ctx, maps, account)
end

-- Decides one message. `ctx`:
--   daemon_name       the MTA-Name Postfix reported (see M.LOCAL_DAEMON)
--   user              authenticated login or nil
--   received          Received header values, topmost first
--   envelope_domain, from_domains   lowercase sender domains
--   recipient_count   number of envelope recipients
--   max_recipients    cap per locally submitted message
--   exempt_uids       set of uids (as strings) the rule leaves alone
-- `maps`: accounts, domains, limits, mailboxes; a map that never loaded is nil.
-- Returns { action = 'none'|'reject'|'discard'|'soft reject'|'count', message,
-- account, limit, cost }.
--
-- The daemon name is decided FIRST. Postfix presents a locally submitted message
-- to the milter as a connection from 127.0.0.1, so a client address or a login
-- says nothing about which road it came by.
function M.decide(ctx, maps)
  if ctx.daemon_name == M.LOCAL_DAEMON then
    return decide_local(ctx, maps)
  end
  if not M.SMTP_DAEMONS[ctx.daemon_name or ''] then
    return verdict('soft reject', '4.4.3 mail path unidentified')
  end
  if not ctx.user then
    -- Unauthenticated SMTP (inbound mail): not a submission.
    return verdict('none')
  end
  -- Authenticated SMTP: Postfix already bound the sender; only the shared bucket
  -- is the rule's business.
  if maps_missing(maps, { 'mailboxes', 'limits' }) then
    return verdict('soft reject', UNAVAILABLE)
  end
  -- Every login that reaches a submission port is a mailbox of some account, so a
  -- login the map does not know is a publication lag or a stale map, never "no
  -- limit": fail closed. The map keys are lower-case; so is the lookup.
  local login = tostring(ctx.user):lower()
  local account = maps.mailboxes[login]
  if account == nil then
    return verdict('soft reject', '4.4.3 mailbox not bound to an account')
  end
  return counted(ctx, maps, account)
end

-- The answer when the rule's settings section is missing or incomplete (the rule is
-- installed but not configured). The local road and an unidentified path are
-- refused loudly; the smtpd roads are not this rule's business without maps, and
-- refusing them would stop inbound mail for a settings mistake.
function M.decide_unconfigured(daemon_name)
  if M.SMTP_DAEMONS[daemon_name or ''] then
    return verdict('none')
  end
  return verdict('soft reject', '4.4.3 local submission rule not configured')
end

-- Turns the verdict plus the counter's value into the final answer.
function M.finish(decision, count)
  if decision.action ~= 'count' then
    return decision
  end
  if M.over_limit(count, decision.limit) then
    return verdict('soft reject', '4.7.1 hourly send limit reached')
  end
  return verdict('none')
end

-- ---------------------------------------------------------------------------
-- Rspamd binding. UNVERIFIED against a live Rspamd (docker/polygon/asserts/parts/
-- mail-local-submit.sh is the check): rspamd_config:add_map with type 'callback',
-- the symbol registration with type 'prefilter' and its priority order,
-- task:set_pre_result (including the 'discard' action), task:has_pre_result,
-- task:get_request_header('MTA-Name'), task:get_recipients,
-- task:get_header_full ordering, lua_redis.request and lua_redis.parse_redis_server.
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

local function domain_of(address)
  if address and address.domain and address.domain ~= '' then
    return address.domain:lower()
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

-- Sets the answer. A refusal that did not take (an action this Rspamd does not
-- know) falls back to a hard reject: a refusal never degrades to acceptance.
local function apply(task, answer)
  if answer.action == 'none' then
    return
  end
  if answer.action == 'discard' then
    rspamd_logger.infox(task, 'maran_local_submit discards: %s', answer.message)
  end
  task:set_pre_result(answer.action, answer.message, MODULE)
  if not task:has_pre_result() then
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
local REQUIRED_OPTIONS = { 'accounts_map', 'sender_domains_map', 'send_limits_map', 'mailbox_accounts_map' }
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
    local domain = domain_of(address)
    if domain then
      from_domains[#from_domains + 1] = domain
    end
  end
  local received = {}
  for _, header in ipairs(task:get_header_full('Received') or {}) do
    received[#received + 1] = header.decoded or header.value
  end
  local envelope = task:get_from('smtp')
  local decision = M.decide({
    daemon_name = request_header(task, 'MTA-Name'),
    user = task:get_user(),
    received = received,
    envelope_domain = envelope and envelope[1] and domain_of(envelope[1]) or nil,
    from_domains = from_domains,
    recipient_count = #(task:get_recipients('smtp') or {}),
    max_recipients = max_recipients,
    exempt_uids = exempt,
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
