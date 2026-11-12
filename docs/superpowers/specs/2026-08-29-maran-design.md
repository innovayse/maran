# Maran — дизайн-документ v1

- **Дата:** 2026-08-29
- **Статус:** утверждён по секциям в брейншторме, ожидает финального ревью спеки
- **Рабочее имя продукта:** Maran (может смениться; нейминг кода — под него)
- **Компания:** Innovayse LLC
- **Language of amendments:** amendments made on or after 2026-11-12 are written in English (owner decision O-55); the existing Russian text and the earlier Russian amendments are not rewritten.

---

## 1. Продукт и бизнес-модель

Maran — панель управления веб-хостингом: ставится на сервер клиента, управляет сайтами, PHP, базами, FTP, cron, файрволом, бэкапами и мониторингом через веб-интерфейс.

**Модель: открытый код, платный продукт** (модель «открытое ядро + платные модули»):

- Открытое читаемое ядро (лицензия класса BSL 1.1) — даёт доверие, установки и волонтёров-контрибьюторов.
- Платные модули — закрытые скомпилированные бинарники (.NET NativeAOT / Rust), доступны только из маркетплейса Innovayse при валидной лицензии.
- Загрузчик платного кода («PluginLoader») — закрытый C#-компонент в приватном репозитории.
- Реальная защита — облачная сторона: маркетплейс, обновления, базы правил. Ломаная копия не получает ничего. Принято как факт: невзламываемого клиентского кода не существует; цель — «взлом дороже покупки».

**Вертикаль Innovayse:** внешняя биллинговая система продаёт хостинг → через reseller API создаёт аккаунты на серверах с Maran. Maran — панель на серверах в этой цепочке.

## 2. Принципы (директивы владельца продукта)

1. **Всё утверждённое для v1 выходит в v1.** Внутри скоупа ничего не откладывается.
2. **Максимальная безопасность** каждого решения. История уязвимостей хостинг-панелей — наш антипример и продажный аргумент.
3. **Максимально чистый код и архитектура.** Границы модулей защищаются CI, не дисциплиной.
4. **Минимум демонов на сервере клиента:** PgSQL, panel-api, panel-agent — и всё. Никаких брокеров (ни RabbitMQ, ни NATS), никакого Docker в проде.
   - **Amendment (2026-11-12, owner decisions 48, 50, 51).** "And nothing else" holds for the base installation. The optional functions (FTPS, mail, DNS) install their own daemons only when an administrator turns them on, and listen on nothing while off (the packaged daemon's own unit masked, a Maran unit installed disabled and started only by an agent rpc an administrator asked for; rules/architecture.md System boundaries holds the one wording). A managed Fleet node runs the agent alone. No broker and no Docker in production, as before.

## 3. Скоуп v1

**Входит:** аккаунты с лимитами и suspension; кабинеты клиентов (полная мультитенантность); сайты (PHP/статика/reverse-proxy); мульти-PHP 7.4–8.4; SSL (Let's Encrypt + свои сертификаты); MySQL/MariaDB для клиентов; SFTP + FTPS; файловый менеджер; cron; файрвол (nftables); мониторинг с алертами; бэкапы (файлы + БД, локально и S3, восстановление из UI); очередь задач с прогрессом; Provisioning API; лицензирование целиком (клиент + облачная сторона); установщик/обновления/CLI; полный security-набор (2FA, ротация refresh, rate limiting, аудит, API-ключи); i18n en/ru/hy.

**Поправка (2026-11-03, issue #179):** роль Reseller перенесена в v1. Реселлер — именованный бизнес с одним логином (вход по приглашению, двухфакторная аутентификация обязательна), который создаёт собственные тарифы и аккаунты клиентов внутри конверта, заданного администратором: потолки на каждый тариф, максимум тарифов, максимум аккаунтов и общий дисковый пул. Реселлер видит только свои тарифы и аккаунты; чужая строка отвечает 404, как несуществующая. К ресурсам клиента реселлер попадает только через «войти под клиентом» (view-as, один переход на токен: администратор → реселлер, администратор → аккаунт, реселлер → свой аккаунт, без цепочек). Приостановка реселлера закрывает его вход и запись, но не приостанавливает аккаунты его клиентов. Подробности: `docs/accounts/resellers.md`; модель угроз: `docs/superpowers/notes/2026-11-03-reseller-role-threat-note.md`.

**Amendment (2026-11-12, issues #177, #178, #180; owner decisions 48, 50, 51, option a).** The mail module, the DNS module and Fleet mode are v1 scope; they are removed from the list below.

- **Mail (#177).** Customer mail is a function of its own, separate from the panel's own outgoing mail (SMTP settings and alerts stay in Notifications). Stack: Postfix, Dovecot (with Pigeonhole for Sieve) and Rspamd (Redis or Valkey on a unix socket only); ClamAV is optional and off. A mailbox belongs to an account: a Maildir under `/home/<account>/mail`, under the account's uid, so account quotas and backups cover mail. A mail domain is the host name of a site of the same account. Mailbox passwords are stored in the strongest scheme the host's Dovecot supports (rules/security.md item 9); the panel's own logins are always Argon2id. Webmail (#190-#193) is part of the same function and reaches Dovecot and Postfix over unix sockets only. New listening ports: SMTP 25/465/587 and IMAP 143/993, and POP3 110/995 behind a separate switch that is off by default; none of them listens until an administrator turns mail on (units installed disabled, the distribution's own units masked, as for FTPS). The panel reports the ports to open and does not open them. The call list is in rules/security.md item 10. Threat note: `docs/superpowers/notes/2026-11-07-mail-module-threat-note.md`.
- **DNS (#178).** An authoritative DNS server, BIND 9 (a Maran unit installed disabled, the distribution's own unit masked). Zones are files the agent renders from the database, which stays the source of truth; the daemon's zone list is kept with `rndc addzone/delzone`. Port 53 (tcp and udp) opens only after an administrator turns DNS on, by a firewall rule. Forward zones and zone transfer OUT only; acting as a secondary for foreign servers is not in v1. DNSSEC keys (ECDSA P-256) are generated by the panel and stored encrypted in the `dns` schema. A customer creates zones only for host names claimed by their own sites; DNS is administrator-only (neither reseller nor customer manages zones in v1). Threat note: `docs/superpowers/notes/2026-11-07-dns-module-threat-note.md`.
- **Fleet (#180).** Fleet mode is in v1 in a bounded form: a managed node runs the agent only (plus the client stack the installer sets up: nginx, php-fpm, MariaDB, sshd-chroot, vsftpd, nftables, cron) and the root CLI `maran`; there is no `maran-api` and no PostgreSQL on the node, and all node state (accounts, sites, databases, schedules, audit) lives in the primary panel's database. Trust is mutual pinning of fingerprints with no certificate authority: an administrator types the node's fingerprint before the panel trusts it; node certificates last two years and are renewed by enrolling again. The node's agent listens on tcp/8444 (mutual TLS) only after enrolment and only for the panel's address (`--allow-from` is required; `--allow-any` only with a warning). Resellers always land on the default server. Not part of this amendment: moving accounts between servers (depends on #127) and updating nodes remotely. Threat note: `docs/superpowers/notes/2026-11-07-fleet-threat-note.md`.

**Не входит (осознанно отложено):** вебхуки Provisioning API, PgSQL-базы для клиентов, встроенный phpMyAdmin (только опциональный изолированный модуль), Docker-модуль для приложений, Alpine/Arch.

## 4. Поддерживаемые ОС v1

| Семейство | Версии | Пакеты PHP |
|---|---|---|
| Debian | Ubuntu 22.04/24.04 LTS, Debian 12/13 | Sury |
| RHEL | AlmaLinux 9/10, Rocky 9/10 | Remi |
| SUSE | openSUSE Leap 16.0 and 16.1 (16.0 stays until its vendor end-of-support date is confirmed, #559); SLES 16.0 and 16.1 (claimed through Leap 16, its community sibling, as RHEL is claimed through its rebuilds) | the distribution's own repositories (one minor) |

Архитектуры: x86_64, aarch64. Дистро-различия изолированы в слое адаптеров агента (`DistroAdapter`: пакеты, пути, имена сервисов, бэкенд файрвола). Добавление дистрибутива = новый адаптер, не переписывание.

SUSE family (issue #59), limitations by design: the distribution ships ONE PHP minor (Leap 16: 8.4) and the installer adds no third-party repository, so PHP versions are not offered side by side; FTPS is not offered (vsftpd is not packaged for Leap 16; SFTP is unaffected); the mail stack is not offered until it is measured. The default MAC is SELinux, and `firewalld` is stopped, disabled and masked after SSH reachability is confirmed in the agent's nftables table (one firewall owner). Support is UNVERIFIED until the first `leap16` polygon run. Detection admits only the exact ids `opensuse-leap` and `sles`; the other ids that say SUSE in `ID_LIKE` are refused.

Not supported, and why:

- **Fedora**: each release lives about 13 months, so the matrix would churn every six months and a row would be end-of-life by the time the installer is next touched.
- **Alpine**: no systemd (the install polygon and the unit model assume it), and musl has no NSS, which the agent's account resolution deliberately relies on.
- **SLES 15** (all service packs): SP6 is out of general support and SP7 cannot be tested without a subscription; no free sibling exists.
- **openSUSE Leap 15.x**: end of support (15.6 ended 2026-04-30), and its default MAC is AppArmor, for which no step was written.
- **openSUSE Tumbleweed** (a rolling release has no version to pin), **MicroOS** (read-only transactional root), **SLED**, **SLES for SAP**.

## 5. Архитектура системы

Топология «Вариант A»: полная панель на каждом сервере (классическая модель хостинг-панели). Три процесса, строгое разделение власти:

```
                     Innovayse Cloud (лицензии, маркетплейс, обновления)
                                   ▲ https (единственный исходящий вызов)
┌─ Сервер клиента ─────────────────┼─────────────────────────────┐
│  Vue 3 SPA ──REST/SSE──► maran-api (C#, под юзером maran)  │
│  billing  ──REST(reseller API)───┤   EF Core ──► PostgreSQL    │
│                                  ▼ gRPC, unix socket           │
│                     maran-agent (Rust, root)               │
│                                  ▼                             │
│    nginx · php-fpm×N · MySQL · vsftpd/sshd · cron · nftables   │
└────────────────────────────────────────────────────────────────┘
```

- **maran-api (C#, ASP.NET Core, не root):** весь интеллект — auth, бизнес-логика, модульная система, лицензии, Provisioning API, доступ к БД (только он).
- **maran-agent (Rust, root):** единственный root-процесс, исполняет фиксированный набор типизированных команд. Без состояния, без БД.
- **PostgreSQL:** единственная БД, слушает только unix-socket. Истина живёт здесь.
- **Фоновые задачи:** Wolverine с durable inbox/outbox в PostgreSQL. Локальные очереди, ретраи с backoff, отложенные сообщения, саги для многошагового провижининга. Брокера нет; смена транспорта Wolverine конфигом — дверь в Fleet-режим.
- Fleet-режим v2+ подготовлен архитектурно: транспорт агента абстрагирован (unix socket ↔ TCP+mTLS), код операций не меняется.
  - **Amendment (2026-11-12, issue #180).** Fleet is in v1 (section 3): the agent's transport is the unix socket on the primary panel and TCP with mutual TLS on a managed node; the operation code is the same. The licence is checked on the primary panel only.

## 6. Структура репозитория

Монорепо. Ключевая идея — **одна доменная разбивка сквозь весь стек**: фича «sites» существует под одним именем в proto, агенте, бэкенде и фронтенде.

```
Maran/
├── proto/agent/v1/            # контракт C# ↔ Rust: sites.proto, ssl.proto, db.proto,
│                              #   ftp.proto, files.proto, cron.proto, firewall.proto,
│                              #   monitor.proto, backup.proto, system.proto
├── backend/
│   ├── src/
│   │   ├── Maran.Host/                # тонкий хост: DI, auth, middleware, сборка модулей
│   │   ├── Maran.SharedKernel/        # Result, ошибки, ICurrentUser, IClock
│   │   ├── Maran.Agent.Client/        # gRPC-клиент (генерация из proto/)
│   │   ├── Maran.Sdk/                 # контракт модуля: IPanelModule, права, меню, хуки
│   │   └── modules/
│   │       ├── Maran.Modules.Accounts/
│   │       ├── Maran.Modules.Sites/
│   │       ├── Maran.Modules.Ssl/
│   │       ├── Maran.Modules.Databases/
│   │       ├── Maran.Modules.Ftp/
│   │       ├── Maran.Modules.Files/
│   │       ├── Maran.Modules.Cron/
│   │       ├── Maran.Modules.Firewall/
│   │       ├── Maran.Modules.Monitoring/
│   │       ├── Maran.Modules.Backups/
│   │       ├── Maran.Modules.Tasks/
│   │       ├── Maran.Modules.Licensing/
│   │       └── Maran.Modules.Provisioning/
│   ├── tests/                             # зеркалит modules/ + ArchitectureTests
│   ├── Directory.Build.props · Directory.Packages.props · .editorconfig
│   └── Maran.sln
├── agent/
│   ├── Cargo.toml                         # workspace
│   └── crates/
│       ├── agent/                         # bin: tonic gRPC-сервер, диспетчер
│       ├── agent-core/                    # типы команд/результатов, ошибки
│       ├── ops/                           # src/{sites,php,db,ftp,files,cron,firewall,ssl,backup,monitor}/
│       ├── distro/                        # DistroAdapter: debian_family, rhel_family
│       └── templates/                     # askama-шаблоны (nginx, php-fpm, vsftpd)
├── frontend/                              # ОДИН Vue 3 SPA: админка + кабинет клиента
│   └── src/{assets,components,composables,middleware,modules,router,stores,types,utils,views,locales}
│       # modules/ = те же имена: sites/, databases/, ftp/, files/, cron/, firewall/,
│       #            monitoring/, backups/, tasks/; locales/ = en/ ru/ hy/
├── installer/
│   ├── install.sh · uninstall.sh
│   ├── lib/                               # 10-preflight.sh, 20-deps.sh, 30-pgsql.sh, …
│   └── systemd/                           # maran-api.service, maran-agent.service
├── docker/                                # ТОЛЬКО dev: compose (pgsql + контейнеры-полигоны)
├── rules/                                 # нормативные правила архитектуры/стиля (раздел 7)
├── docs/superpowers/specs/
├── .github/workflows/                     # backend.yml, agent.yml, frontend.yml, e2e.yml
├── LICENSE                                # BSL 1.1
└── README.md
```

**Модульный монолит, не слои.** Обоснование: продукт зарабатывает продажей модулей → модуль обязан быть физической единицей кода. Внутренние модули v1 и платные модули будущего имеют одну форму (контракт `Maran.Sdk`). Прецеденты: Orchard Core; Wolverine спроектирован под вертикальные срезы.

**Анатомия модуля** (вертикальные срезы):

```
Maran.Modules.Sites/
├── SitesModule.cs        # регистрация: DI, эндпоинты, права, меню, миграции
├── Features/             # CreateSite/, DeleteSite/, ChangePhpVersion/ …
│                         #   (request + validator + handler + endpoint рядом)
├── Domain/               # Site, доменные правила
└── Data/                 # EF-конфигурации; своя схема PostgreSQL: sites.*
```

Каждый модуль владеет **своей схемой PostgreSQL** (`accounts.*`, `sites.*`, …). Кросс-модульные ссылки запрещены (только Sdk и SharedKernel) и **проверяются архитектурными тестами NetArchTest на CI** — нарушение = красный билд.

Закрытый PluginLoader — в отдельном приватном репозитории; в публичном — только интерфейсы Sdk.

## 7. Кодстайл

- **C#:** 4 пробела, 120 колонок, LF; `var` при очевидном типе; pattern matching; скобки всегда; `_camelCase` приватные поля; суффикс `Async`; XML-доки на публичном API; file-scoped namespaces; `sealed` по умолчанию; records для команд/DTO; `<Nullable>enable</Nullable>`; warnings-as-errors; версии пакетов централизованно в `Directory.Packages.props`.
- **Rust:** rustfmt по умолчанию; clippy = error на CI; крейты kebab-case; ошибки `thiserror` (библиотеки) / `anyhow` (границы); логи `tracing`; tokio + tonic.
- **Vue/TS:** `<script setup lang="ts">`; Pinia; ESLint + oxlint; типы в `types/`; фичи в `modules/`.
- **Proto:** package `maran.agent.v1`; buf-стиль имён; proto — единственный источник правды контракта; изменения только аддитивные.

**Нормативная папка правил `rules/` в корне репозитория — создаётся ДО первой строчки кода** (директива владельца):

```
rules/
├── architecture.md   # границы модулей, разрешённые зависимости, анти-паттерны
├── csharp.md         # стиль C#: обязательные примеры «правильно/неправильно», XML-доки
├── rust.md           # стиль Rust: примеры, /// doc-комменты, ошибки, unsafe-политика
├── vue.md            # стиль Vue/TS: примеры компонентов, stores, composables
├── proto.md          # конвенции контракта агента
├── testing.md        # DoD, виды тестов, именование, где какой тест живёт
├── security.md       # security-чеклист PR: валидация, секреты, IDOR, пути
└── git.md            # ветки, конвенции коммитов, PR-процесс
```

Каждый документ нормативен (MUST/MUST NOT), с живыми примерами кода — включая обязательные doc-комментарии для каждого языка. `.editorconfig`, линтеры CI и `CLAUDE.md` ссылаются на эти правила; ревью PR ведётся по ним. Написание `docs/rules/` — задача №0 плана реализации.

Конвенции фиксируются в `.editorconfig` и `CLAUDE.md` при скаффолде.

## 8. Мультитенантность, роли, лимиты

- **Account** — единица владения (= классический «хостинг-аккаунт»): владеет сайтами, БД, FTP, cron, бэкапами. Каждому Account соответствует системный Linux-юзер: `/home/<account>/`, php-fpm пулы под его UID, SFTP-chroot, ФС-квоты. Изоляция клиентов — правами ОС, модель проверена индустрией.
- **User** — логин в панель. Роли v1: `Admin` (весь сервер), `Customer` (свои Account'ы). Кабинеты клиентов — в v1 (внешняя биллинговая система продаёт shared-хостинг с первого дня).
- **Plan** — пакет лимитов: дисковая квота, число сайтов/БД/FTP-юзеров, лимит php-fpm воркеров. Назначается аккаунту; Provisioning API оперирует планами.
- **Enforcement:** счётные лимиты — проверка в Application при создании; диск — квоты ФС через агента; CPU/процессы — размеры php-fpm пула + systemd-slice юзера.
- **Anti-IDOR архитектурно:** каждая тенантная таблица несёт `AccountId`; EF Core global query filters по контексту Customer — чужая строка не возвращается физически. Обязательные интеграционные тесты «клиент A → ресурс клиента B = 404» на каждый эндпоинт.
- **Suspension** — первоклассное состояние: nginx-заглушка, FTP/SFTP/cron остановлены; включается одним вызовом биллинга.

## 9. Rust-агент: контракт и безопасность

- **Транспорт:** gRPC (tonic) на `/run/maran/agent.sock`; сокет `root:maran 0660` + проверка `SO_PEERCRED` (только UID юзера `maran`). Абстракция транспорта — задел под TCP+mTLS для Fleet (в v1, см. §3: управляемые узлы).
- **Контракт:** proto по модулям, package `maran.agent.v1`, только аддитивные изменения; handshake `GetAgentInfo` (версия, дистрибутив) при коннекте. **Команды «выполнить shell-строку» не существует** — класс RCE-уязвимостей хостинг-панелей устранён конструктивно.
- **Недоверие к вызывающему (defense in depth):** повторная валидация всех входов в агенте — строгие regex имён, канонизация путей, пути только внутри `/home/<account>/`.
- **Файловые операции клиентов — под UID аккаунта** (fork + setuid), не под root: symlink-атака через файловый менеджер не достаёт `/etc/shadow`.
- **Изменения конфигов:** рендер из встроенного askama-шаблона → temp-файл → валидация (`nginx -t`) → атомарный rename → reload → при провале откат и типизированная ошибка. Агент пишет только свои файлы (`/etc/maran/nginx/sites/*.conf` через include), чужие не редактирует.
- Все команды идемпотентны; долгие операции — server-streaming с прогрессом; каждая команда — в аудит.
- ACME-логика в C#; агент только кладёт файлы сертификатов и делает reload.
- **Исходящий SSH к зарегистрированным хостам (поправка, issue #197):** агент — первый сетевой клиент панели: для миграции целого сервера он запускает `ssh`, `ssh-keyscan` и `ssh-keygen` из allow-list дистрибутива, и только к одному хосту, который администратор сам зарегистрировал (один хост, один порт, отпечаток ключа подтверждён администратором и закреплён, `StrictHostKeyChecking=yes`). Адрес разрешается один раз и проверяется уже разрешённым (loopback, link-local, адреса самого сервера отклоняются), соединение идёт на этот адрес, а не на имя. На источнике выполняется только закрытый набор команд на чтение; запись на источник и корневой пароль не предусмотрены. Агент ничего не слушает. Первая фаза — реестр источников и инвентаризация; перенос данных ждёт серверной части #127. Модель угроз: `docs/superpowers/notes/2026-11-07-server-migration-threat-note.md`.
- **Агент не расширяется платным кодом:** все операции (включая почтовые и DNS) компилируются в открытый агент заранее и бездействуют без соответствующего C#-модуля. Root-процесс не грузит внешний код никогда. Платится «мозг», не «руки».

## 10. Безопасность панели

- **Токены:** JWT 15 минут + refresh с ротацией в httpOnly/SameSite=Strict cookie; сессии в PgSQL — видимы и отзываемы («выйти со всех устройств», админ может убить сессии клиента); reuse отработанного refresh = отзыв всей цепочки.
- **2FA:** TOTP + recovery-коды; принудительное требование для админов включаемо.
- **Пароли:** Argon2id; индикатор стойкости; политика настраивается.
- **Антибрутфорс:** rate limiting IP+логин с прогрессирующей блокировкой; упорные баны — на уровне nftables через агента (встроенный fail2ban).
- **Доступ:** только HTTPS (self-signed при установке → LE после указания hostname); порт по умолчанию 8443 (меняется); опциональный IP-allowlist админ-входа.
- **API-ключи** (для внешней биллинговой системы): отдельный механизм, скоупы прав, привязка к IP, в БД — только хеш.
- **Веб-гигиена:** строгий CSP, security headers, CSRF = SameSite + обязательный кастомный заголовок.
- **Аудит:** append-only журнал всех входов и мутаций (кто/когда/что/откуда) с экраном в админке.
- **Секреты:** `/etc/maran/panel.env` `root:maran 0640`; ключи DataProtection с правами `maran`; пароли клиентских сервисов в открытом виде не хранятся.

## 11. Модули v1 — поведение

- **Sites:** домен + алиасы; корень `/home/<acc>/sites/<domain>/`; типы PHP/статика/reverse-proxy; выбор PHP-версии; логи в UI; www/HTTPS-редиректы; вкл/выкл; кастомные nginx-сниппеты (только админ).
- **Multi-PHP:** 7.4–8.4 из Sury/Remi, установка агентом по требованию; пул на «аккаунт × версия» под UID аккаунта; клиенту — безопасное подмножество настроек через pool-overrides.
- **SSL:** LE HTTP-01 (webroot), автопродление за 30 дней (scheduled job), свои сертификаты, self-signed fallback. Wildcard/DNS-01 — с DNS-модулем.
  - **Amendment (2026-11-12, issue #178).** Wildcard and DNS-01 go through Maran's own DNS only (section 3); third-party DNS provider APIs are not used in v1.
- **Databases:** MySQL/MariaDB: БД + юзер + права, лимит по плану, креды показываются один раз. phpMyAdmin — только опциональный модуль на изолированном vhost со входом по одноразовому SSO-токену.
  - **Amendment (2026-11-12, issue #156, owner decision 49).** Remote database access (off by default): an administrator enables the feature in a plan; a customer adds single IPv4/IPv6 addresses for a database login, and the panel opens tcp/3306 for each of them with a firewall rule for that address only, with `MAX_USER_CONNECTIONS` bounded. While no address exists MariaDB listens locally only. Connection encryption is not required in v1, and the interface and the documentation say so in plain words.
- **FTP/SFTP:** SFTP по умолчанию (openssh `internal-sftp`, chroot); FTPS через vsftpd: виртуальные юзеры на UID аккаунта, chroot в подпапку, TLS обязателен.
- **Files:** навигация, chunked-upload, редактор с подсветкой, zip/tar, права, поиск; исполнение агентом под UID аккаунта.
- **Cron:** crontab аккаунта; конструктор + raw; env; вкл/выкл; вывод последнего запуска.
- **Firewall:** nftables: пресеты портов (MySQL наружу закрыт по умолчанию и открывается только на одиночные адреса, которые клиент добавил сам, — см. Databases), правила порт/протокол/источник, whitelist, автобаны антибрутфорса. Только админ.
- **Monitoring:** CPU/RAM/диск/сеть/load; статусы сервисов; графики 24ч/7д (PgSQL + даунсемплинг); e-mail алерты (диск >90%, сервис упал); SMTP-настройки панели; per-account диск против квоты.
- **Backups:** архивы файлов аккаунта + дампы его БД по расписанию; хранение локально и S3-совместимо; восстановление из UI; ретеншн.
  - **Amendment (2026-11-12, issues #129, #160; owner decisions 45 and 14).** The S3 upload is the only outbound call of backups: the destination is set by the operator, https only, only ciphertext leaves the server (encryption is mandatory for a remote destination; the key stays on the server), no identifier is sent, and with no destination configured there is no call (rules/security.md item 10). Partial restore (one database or one site subtree) ships before the remote destination.
- **Mail (amendment, 2026-11-12, issue #177):** domains, mailboxes, aliases, forwarding (with SRS where the package exists on every family), an auto-responder through Sieve (strict grammar), quotas and limits per plan; DKIM, SPF and DMARC records are shown by the panel (defaults: DMARC `p=none`, SPF `~all`); webmail reads and sends plain text. Turned on by an administrator.
- **DNS (amendment, 2026-11-12, issue #178):** zones and records, a default record template, a per-plan zone limit (`MaxDnsZones`), DNSSEC.
- **Tasks:** сквозной UI очереди фоновых задач: прогресс/статусы/логи по SSE.

## 12. Provisioning API

REST `/api/v1/reseller-api/…`; auth — скоупированные API-ключи, принадлежащие резеллеру, + IP; всё в аудит. Семантика - как у серверных провайдеров внешней биллинговой системы (маппинг 1:1):

- `POST /accounts` (план, домен, e-mail → аккаунт + Customer-логин; первый сайт не создаётся)
- `POST /accounts/{id}/suspend` · `/reactivate`
- `DELETE /accounts/{id}` (финальный бэкап → удаление)
- `PUT /accounts/{id}/plan`
- `GET /accounts/{id}/usage` (диск, счётчики, статус — биллинг опрашивает)
- `POST /accounts/{id}/sso` (одноразовый токен → URL автовхода в кабинет)
- `GET /server/info` (версия, ёмкость — выбор сервера при заказе)

Гарантии: `Idempotency-Key` на мутациях; ошибки RFC 7807; изменения аддитивны. Вебхуков в v1 нет — модель опроса (как WHM).

**Amendment (2026-11-12, issue #7, owner decision 62, option a).** The route prefix is `/api/v1/reseller-api/` and replaces `/api/provisioning/v1/`; the controllers live in the Accounts module under `Controllers/External/` and the keys are owned by a reseller (a single-operator server creates one reseller). The prefix cannot be renamed after the first release (additive-only rule), which is why it is decided now. `unsuspend` is `reactivate`, the panel's own word everywhere else. `POST /accounts` creates the account and its Customer login with an invitation and no first site: a site is created by a separate call to Sites. Mutations require an `Idempotency-Key` header.

## 13. Лицензирование и платные модули

- **Лицензия:** подписанный Ed25519 JSON (id, продукт, tier, модули, срок, отпечаток сервера `machine-id` + первичный интерфейс). Публичный ключ вшит — офлайн-проверка. Перенос на другой сервер — self-service в кабинете Innovayse с лимитом частоты.
- **Phone-home:** раз в сутки обмен на подписанный lease (14 дней). Облако недоступно → работа на последнем lease весь grace. После — **деградируют только платные модули; ядро не умирает никогда** (серверы клиентов не кирпичим).
- **PluginLoader** (закрытый C#, приватный репо): проверка lease → скачивание из маркетплейса → расшифровка (ключи в lease) → проверка подписи пакета → загрузка C#-модуля через `AssemblyLoadContext`.
- **Amendment (2026-11-09, issues #186-#189; owner decision: approved, decision list items 46 and 47, option (a), 2026-11-09). The Russian text is unchanged except that "closed Rust" now reads "closed C#" in two places: the open-core model list near the top of the document and the PluginLoader line of this section. The third mention of the loader (the sentence placing it in a private repository) names no language and is unchanged.**
  - **The loader is closed C#, not closed Rust.** The "PluginLoader" loads .NET assemblies into the panel process, which a Rust binary cannot do. It is a closed C# component in a private repository; the public repository holds the interfaces (`Maran.Sdk`: `IModuleLoader`, `IModulePackageSource`, `IVerifiedModulePackageSource`, `ILeaseRequestAuthorizer`), the verification library (`Maran.Packaging`) and the format (`docs/marketplace/ipm-format.md`). The agent still never loads external code. The threat note is `docs/superpowers/notes/2026-11-07-marketplace-loader-threat-note.md`; its second review is outstanding.
  - **Marketplace (outbound call).** The panel contacts the Innovayse marketplace at `api-marketplace-maran.innovayse.com` (configurable with `Marketplace__BaseUrl`; the configured host is the whole allow-list), over HTTPS only, for exactly these purposes: list the modules available for this panel version; obtain the details of one module; ask for a single-use download grant (this requires the lease); download the package; ask whether installed modules have updates or have been revoked. Each call is made by `maran-api`, as the unprivileged system user, on an administrator's action, and only after an administrator has switched the marketplace on (off by default; the switch is a database row with no environment path, and is audited). Nothing is called from a timer, a start-up step or a background job. The panel sends the fields listed in rules/security.md item 10 and nothing else: the panel version, release channel, interface language, the administrator's search text and page cursor, the module id and version in question, the lease credential (only to show entitlements and to ask for a grant, never to fetch the signing keys or the package), and on the update check the id, version and package digest of the installed modules asked about. It sends no host name, account, site, user or customer data.
  - **Verification.** Answers are signed with a pinned marketplace metadata key (ECDSA P-384 / SHA-384, `installer/keys/marketplace-metadata.pub`, supplied by the owner) and are refused when unsigned, older than 24 hours or signed by another key. Packages are signed by the module-signing key (ECDSA P-384 / SHA-384, offline) and verified by the loader before any byte is loaded. There are four keys: licence (Ed25519), release, module-signing and marketplace-metadata (all three P-384). The contract with the service is `docs/marketplace/api-v1.md` and `docs/marketplace/openapi-v1.yaml`.
  - **Activation is a restart.** An installed package is verified and staged, and the module serves after the next panel start; the panel can restart itself on request. An installed module is trusted code in the panel process, not a sandbox.
  - **No-network alternative.** A server that must not reach the internet leaves the marketplace switched off; a package file given directly to the loader needs no call.
  - **Screens.** A packaged module's interface is shown in an isolated frame that the panel builds (decision D3 of the plan), never imported into the panel's own script; the "SPA loads dynamically" wording of the `.ipm` line below is read in that sense. That decision is recorded by the work that implements it, not by this amendment.
- **Формат `.ipm`:** manifest (id, версия, min-panel-version, права, меню) + C#-сборки + frontend-бандл (SPA подгружает динамически). Внутренние и платные модули — одна форма (Sdk = контракт).
- **Облачная сторона v1** (отдельный сервис Innovayse, вне монорепо): выдача/валидация лицензий, реестр модулей с подписями. Входит в объём v1 — без него лицензии не работают.
- Что именно платное — бизнес-решение позже; механика полная в v1. Рекомендация: v1-модули бесплатны, монетизация с почты/DNS/Fleet.

## 14. Установщик, обновления, CLI

**Установка:** `curl -sSL https://get.maran.innovayse.com | sudo bash`; скрипт тонкий — качает версионированные артефакты с проверкой подписи манифеста (ECDSA P-384, SHA-384; лицензии подписаны отдельно, Ed25519 — см. §13). Preflight до первого изменения: ОС/архитектура, RAM/диск, порты, чужие панели → честный отказ. Шаги идемпотентны: deps → PgSQL (unix-socket only) → юзер `maran` → бинарники `/usr/local/maran/` → hardened systemd-юниты (`NoNewPrivileges`, `ProtectSystem=strict`, `PrivateTmp` для api) → nginx-vhost 8443 self-signed → миграции → сервисы. **Первый админ — в браузере по одноразовому setup-токену** (пароль не попадает в логи/history). Полный лог установки; честный `uninstall.sh`; офлайн-tarball для энтерпрайза.

**Обновления:** каналы stable/beta; манифесты подписаны; один клик из UI или `maran update` (`maran update --check` — только проверка версии). `maran update`/`--check` инициирует оператор от root: CLI скачивает подписанный манифест и артефакты по HTTPS, не передавая идентификаторов; вызов не делают ни API, ни агент; безвызовная альтернатива — `--offline-tarball`. Порядок: скачать+проверить всё → автоматический `pg_dump` → blue/green-каталоги + переключение симлинка → рестарт API, затем агента, и только после обоих рестартов — health-gate (окно между рестартами покрывают handshake и аддитивный proto, но ровно на одну ревизию, поэтому готовность проверяется после обоих). `maran rollback` — откат симлинка + дамп как страховка. Автообновление только security-патчей: таймер systemd от root раз в сутки (с 00:00 и случайной задержкой до 4 часов) запускает `maran update --security-only --scheduled`. Вызов тот же, что у `maran update`: подписанный манифест по HTTPS, без идентификаторов, не API и не агент. Применяется только релиз, который подписанный манифест помечает как `security`, в пределах установленной мажорной версии; шаги те же, что у ручного обновления (блокировка, дамп, health-gate, откат кода без восстановления БД); сбой пишется в журнал аудита и уходит письмом оператору. Установка (онлайн и офлайн) только ставит юниты и оставляет таймер выключенным, сообщая об этом; включается `maran update-schedule enable`, отключается `maran update-schedule disable`.

**CLI `maran`** (root-only): `status`, `update`, `rollback`, `admin-reset-password`, `issue-panel-certificate`, `collect-diagnostics`, `panel-backup`, `panel-restore` (disaster recovery of the panel itself: database, keys, licence, configuration — docs/operations/panel-disaster-recovery.md) — аварийный доступ при недоступном вебе.

## 15. Наблюдаемость и ошибки

- Структурные логи: Serilog (C#), `tracing` (Rust) → journald + `/var/log/maran/`; уровни меняются из UI.
- **Сквозной correlation-id:** UI → HTTP → Wolverine → gRPC-метаданные → операция агента.
- `/health` (живость + готовность: БД, handshake агента); самомониторинг через Monitoring-модуль; деградационный баннер + алерт при падении агента.
- `collect-diagnostics` → тарболл с вычищенными секретами.
- **Без скрытой телеметрии:** исходящие вызовы — документированный phone-home и запускаемая оператором загрузка подписанного релиза через `maran update` и ежесуточная проверка таймера security-патчей (§14, отключаемая), а также SSH-подключение агента к хосту, который администратор зарегистрировал источником миграции (§9, только по его действию); отчёты об ошибках opt-in.
  - **Amendment (2026-11-12, owner decisions 2, 44-51).** The list is kept in rules/security.md item 10 and the spec follows it: the licence lease (daily, the licence id only); the signed release fetch (operator-run `maran update`, section 14, and the daily security-patch timer, off until enabled); the agent's migration SSH (an administrator's act); the marketplace calls (section 13); the alert webhook, only if an administrator entered the URL; the backup upload to an S3-compatible store, only to a destination the operator configured; customer mail (section 11); the Fleet node channel (section 9). Destinations an operator configures for a stated purpose (the SMTP server, the ACME directory) send only what that purpose needs. Any other call needs a spec change before code.
- Ошибки: типизированный `Result` в домене; наружу RFC 7807 с кодом и i18n-текстом; enum ошибок в proto; **детализация по роли** (админ — полная причина, клиент — безопасная формулировка).

## 16. Тестирование

Definition of Done фичи: unit + integration + IDOR-тест + аудит-событие + i18n-строки.

- **C#:** unit на хендлеры; NetArchTest (изоляция модулей); integration на Testcontainers-PgSQL, включая IDOR-сценарии; snapshot-тесты контракта Provisioning API.
- **Rust:** unit валидаторов; golden-тесты шаблонов (эталонные nginx-конфиги); cargo-fuzz на валидаторы путей/имён; интеграционные прогоны в контейнерах: PR — Ubuntu 24 + Alma 9 + openSUSE Leap 16 и почтовая линия (Ubuntu 24 + Alma 9), ночью — полная матрица (см. amendment ниже).
  - **Amendment (2026-11-12).** The installer polygon (`nightly.yml`) runs on dispatch and before a release tag, on no schedule (issue #553); a dispatch with scope `weekly` or `both` runs the other twelve images under `docker/polygon/images`. See rules/testing.md "PR and installer matrices".
- **E2E:** Playwright на docker-полигоне: золотой путь (установка → аккаунт → сайт → SSL → файл → БД → cron → suspend) + сценарии лимитов.
- **Установщик:** ночная чистая установка на каждой ОС.
- **Security CI:** cargo audit, NuGet vulnerable, npm audit, CodeQL, скан секретов. Перед 1.0 — внешний пентест (бюджетируется).

## 17. UI

Один Vue 3 + Tailwind SPA, две зоны: админка и кабинет клиента; i18n en/ru/hy с первого дня; фронтенд-модули зеркалят бэкенд-модули; живые данные по SSE.

Визуальный дизайн — источник правды: Claude Design canvas
`https://claude.ai/design/p/1b6fb6ec-ce07-46e3-9206-c297327f2971?file=ServerPanel.dc.html`.
Имя файла канваса (`ServerPanel.dc.html`) — внешний идентификатор в дизайн-инструменте, сохранённый как есть: он не переименовывается вместе с продуктом, иначе ссылка перестанет вести к канвасу. Доступ из рабочей сессии пока не открыт (403); до реализации фронтенда файл канваса кладётся в репозиторий или канвас расшаривается. Секция UI-раскладок дополняется в спеке после этого; блокером для бэкенда/агента не является.

## 18. Облачная сторона Innovayse (v1, отдельный сервис)

Минимальный сервис: выдача и валидация лицензий (lease-цикл), реестр модулей с подписями и выдачей платных пакетов, манифесты обновлений с подписями. Технологии — по стандартам компании (C#/PgSQL); детальный дизайн — отдельным документом, интерфейсы зафиксированы в разделе 13.

## 19. Риски

1. **Ширина скоупа v1** — «всё в v1» на 6 ОС и 2 архитектурах: главный риск сроков. Митигция: модульный монолит позволяет параллелить команды по модулям; матрица ОС покрыта адаптерами и CI с первого дня.
2. **Root-агент** — ошибка = компрометация сервера клиента. Митигция: узкий типизированный контракт, повторная валидация, setuid для клиентских операций, fuzzing, пентест перед 1.0.
3. **Совместимость дистрибутивов** (Sury/Remi, различия systemd/nftables) — ночная матрица CI ловит регрессии до пользователей.
4. **Лицензионное ядро** — будет атаковано. Митигция принята: ценность в облаке, ядро не кирпичится, юридическая защита через торговую марку.

---

*Следующий шаг после утверждения спеки: план реализации (writing-plans), затем скаффолд монорепо по разделу 6.*
