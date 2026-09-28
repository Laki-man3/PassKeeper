# PassKeeper

Offline password manager for Windows. No network access, no cloud, no telemetry. Per-user installation without administrator rights or machine-wide installation for all users.

[English](#english) · [Русский](#русский)

---

## English

![PassKeeper](docs/screenshots/en/vault.png)

| | |
|---|---|
| ![Unlock](docs/screenshots/en/unlock.png) | ![Settings: auto-lock dial](docs/screenshots/en/settings.png) |
| ![Entry editor](docs/screenshots/en/editor.png) | ![Autofill suggestion](docs/screenshots/en/autofill.png) |
| ![Installer](docs/screenshots/en/installer.png) | ![Uninstaller](docs/screenshots/en/uninstaller.png) |

### Specifications

| Parameter | Value |
|---|---|
| OS | Windows 10 (1903+), Windows 11, x64 |
| Runtime | self-contained .NET 10; installer and uninstaller run on the built-in .NET Framework 4.8 |
| Network | none |
| Package size | installer 67 MB, installed 161 MB |
| Vault encryption | AES-256-GCM, random 256-bit vault key |
| Key derivation | Argon2id, 64 MiB, 3 iterations, 4 lanes, 256-bit salt |
| Integrity | file header authenticated as AAD; any modification is detected |
| Quick unlock | PIN 4–12 digits, Argon2id + Windows DPAPI binding, 5 attempts, then master password |
| Auto-lock | after 8 h of inactivity by default; any period from 10 s to 24 h, set to the second on a dial; optionally on Windows lock |
| Clipboard | cleared after 30 s (10–60 s / never), excluded from clipboard history and cloud sync |
| Backups | previous version + daily copies for 14 days (encrypted) |
| Autofill | UI Automation / MSAA field detection, SendInput typing; browsers (verified in Chrome 153, Firefox 156) and desktop sign-in clients: VPN, VDI, RDP, token PIN prompts |
| Auto-type hotkey | Ctrl+Alt+A (configurable) |
| Import | 25+ sources, see below |
| Export | 13 formats |
| UI languages | English, Russian |
| Tests | 92 unit tests |

### Features

- Entry fields: title, login, password, URLs, e-mail, phone, key/token, TOTP, notes, folders, favorites, custom (hidden) fields, password history, trash.
- First run: local profile with master password, then mandatory PIN. Later starts and inactivity locks ask for the PIN only.
- Autofill: a suggestion appears next to a focused login, password, e-mail, phone, one-time code, PIN or key field in any browser or desktop application. Hotkey auto-type matches the active site or window (title / process name, wildcards). Sequences: `{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{PIN}`, `{EMAIL}`, `{PHONE}`, `{KEY}`, `{TOTP}`, `{DELAY n}`, `{S:field}`.
- Password generator, strength estimate, weak/reused password overview.
- Tray, autostart (no administrator rights), dark/light/system theme.

### Desktop sign-in clients

In the entry editor, **Application windows → Pick a client** adds the window patterns of a known client:

| Group | Clients |
|---|---|
| VPN | Cisco Secure Client (AnyConnect), Check Point Endpoint Security VPN, FortiClient, Palo Alto GlobalProtect, OpenVPN GUI / Connect, Континент-АП, ViPNet Client |
| VDI / remote desktop | Базис.WorkPlace (ВРМ), Citrix Workspace, Omnissa / VMware Horizon, Termidesk, Remote Desktop / Windows Security |
| Tokens | Рутокен and КриптоПро CSP PIN prompts |
| Other | 1С:Предприятие, SAP GUI, PuTTY / KiTTY, WinSCP |

- Fields are filled one by one: a login the client remembered is kept (or replaced if it differs), a password, PIN or code field gets only its value. The hotkey works the same way; a custom sequence overrides it.
- A text box without a label followed by a password box is taken as the login field.
- Token / smart-card PIN: the entry's custom field `PIN` (otherwise the password).
- Clients running as administrator or SYSTEM: Windows blocks simulated input (UIPI). PassKeeper puts the password or PIN on the clipboard (cleared as configured) and shows a notification.
- Custom-drawn clients without UI Automation: the hotkey types the entry's sequence into the focused field.

### Import / export

| Import | |
|---|---|
| Browsers (direct) | Chrome, Edge, Yandex Browser, Opera, Opera GX, Brave, Vivaldi, Chromium, Atom, Firefox, Waterfox, LibreWolf, Floorp, Zen, Thunderbird |
| Password managers | KeePass/KeePassXC (KDBX 3.1/4 with password and key file, XML, CSV), Bitwarden (JSON, CSV), 1Password (1PUX, CSV), LastPass, Dashlane, NordPass, Proton Pass, RoboForm, Keeper, Kaspersky Password Manager |
| Other | Windows Credential Manager, any CSV (UTF-8/UTF-16/Windows-1251, `,` `;` Tab), PassKeeper PKX/JSON |

Export: PassKeeper PKX (encrypted), KeePass KDBX 4 (encrypted), CSV for Chromium browsers, Firefox, Bitwarden, 1Password, LastPass, KeePassXC, full CSV, full JSON, Bitwarden JSON, KeePass XML, HTML. Every export requires the master password.

Chrome 127+ protects newly saved passwords with App-Bound Encryption, which third-party applications cannot read. Such entries are listed in the import report; export them from Chrome (Settings → Password Manager → Export) and import the CSV.

### Installation

```bat
PassKeeper-Setup-1.1.0.exe
```

| Mode | Location | Rights |
|---|---|---|
| Just me | `%LOCALAPPDATA%\Programs\PassKeeper` | user |
| All users | `%ProgramFiles%\PassKeeper` | administrator |
| Portable (zip) | any folder, data in `.\Data` | user |

The autostart and language chosen in the installer are used on the first run.

Silent install: `/S`, `/allusers` or `/currentuser`, `/desktop`, `/autostart` or `/noautostart`, `/nolaunch`, `"/dir=path"`, `/lang=en|ru`. Exit codes: 0 ok, 1 error, 740 elevation required, 1602 cancelled.

```bat
PassKeeper-Setup-1.1.0.exe /S /allusers /desktop /autostart
```

### Uninstall

`Uninstall.exe` in the installation folder, Start menu → PassKeeper → Uninstall PassKeeper, Settings → Apps, or PassKeeper → Settings → About. Program files, shortcuts, autostart and registry entries are removed; the user's data in `%APPDATA%\PassKeeper` is removed only when **Also remove my data** is checked.

Silent: `Uninstall.exe /S` (data kept), `Uninstall.exe /S /removedata`.

Data: `%APPDATA%\PassKeeper` — `vault.pkv`, `pin.dat` (DPAPI), `settings.json`, `Backups\`. Override with `--data <dir>`.

### Build

Requires .NET 10 SDK.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output in `dist\`: installer, portable zip, `SHA256SUMS.txt`. Tests: `dotnet test tests/PassKeeper.Tests`.

| Path | Contents |
|---|---|
| `src/PassKeeper.Core` | cryptography, vault, PIN, import/export, KDBX, TOTP, matching, field classification |
| `src/PassKeeper` | WPF application |
| `src/PassKeeper.Setup` | installer and uninstaller (.NET Framework 4.8) |
| `tests/PassKeeper.Tests` | unit tests |

### Limitations

- Simulated input cannot reach applications running as SYSTEM; applications running as administrator need PassKeeper started as administrator too. In both cases the clipboard fallback is used.
- Sign-in clients were checked against dialogs with the same structure (Cisco and Check Point login forms, token PIN prompt); vendor builds may differ.
- Binaries are not code-signed.
- The master password cannot be recovered.

---

## Русский

![PassKeeper](docs/screenshots/ru/vault.png)

| | |
|---|---|
| ![Вход](docs/screenshots/ru/unlock.png) | ![Настройки: циферблат автоблокировки](docs/screenshots/ru/settings.png) |
| ![Редактор записи](docs/screenshots/ru/editor.png) | ![Подсказка автозаполнения](docs/screenshots/ru/autofill.png) |
| ![Установщик](docs/screenshots/ru/installer.png) | ![Деинсталлятор](docs/screenshots/ru/uninstaller.png) |

### Характеристики

| Параметр | Значение |
|---|---|
| ОС | Windows 10 (1903+), Windows 11, x64 |
| Среда выполнения | встроенная .NET 10; установщик и деинсталлятор работают на штатном .NET Framework 4.8 |
| Сеть | не используется |
| Размер | установщик 67 МБ, после установки 161 МБ |
| Шифрование хранилища | AES-256-GCM, случайный 256-битный ключ |
| Формирование ключа | Argon2id, 64 МиБ, 3 прохода, 4 потока, соль 256 бит |
| Целостность | заголовок файла аутентифицируется (AAD), любое изменение обнаруживается |
| Быстрый вход | PIN 4–12 цифр, Argon2id + привязка к учётной записи Windows (DPAPI), 5 попыток, затем мастер-пароль |
| Автоблокировка | по умолчанию через 8 ч бездействия; любой срок от 10 с до 24 ч с точностью до секунды на циферблате; опционально при блокировке Windows |
| Буфер обмена | очистка через 30 с (10–60 с / никогда), исключение из журнала и облачной синхронизации |
| Резервные копии | предыдущая версия + ежедневные копии за 14 дней (зашифрованы) |
| Автозаполнение | поиск полей через UI Automation / MSAA, ввод через SendInput; браузеры (проверено в Chrome 153, Firefox 156) и клиенты входа: VPN, VDI, RDP, запросы PIN токенов |
| Горячая клавиша автоввода | Ctrl+Alt+A (настраивается) |
| Импорт | 25+ источников, см. ниже |
| Экспорт | 13 форматов |
| Языки интерфейса | русский, английский |
| Тесты | 92 модульных теста |

### Возможности

- Поля записи: название, логин, пароль, адреса сайтов, e-mail, телефон, ключ/токен, TOTP, заметки, папки, избранное, дополнительные (скрытые) поля, история паролей, корзина.
- Первый запуск: локальный профиль с мастер-паролем, затем обязательный PIN. При следующих запусках и после автоблокировки запрашивается только PIN.
- Автозаполнение: подсказка появляется рядом с полем логина, пароля, e-mail, телефона, одноразового кода, PIN или ключа в любом браузере и программе. Автоввод по горячей клавише подбирает запись по сайту или окну (заголовок / имя процесса, маски `*`). Последовательности: `{USERNAME}{TAB}{PASSWORD}{ENTER}`, `{PIN}`, `{EMAIL}`, `{PHONE}`, `{KEY}`, `{TOTP}`, `{DELAY n}`, `{S:поле}`.
- Генератор паролей, оценка стойкости, обзор слабых и повторяющихся паролей.
- Трей, автозапуск (без прав администратора), тёмная/светлая/системная тема.

### Клиенты входа (VPN, VDI, RDP)

В редакторе записи **Окна приложений → Клиент из списка** добавляет шаблоны окон известного клиента:

| Группа | Клиенты |
|---|---|
| VPN | Cisco Secure Client (AnyConnect), Check Point Endpoint Security VPN, FortiClient, Palo Alto GlobalProtect, OpenVPN GUI / Connect, Континент-АП, ViPNet Client |
| VDI / удалённый рабочий стол | Базис.WorkPlace (ВРМ), Citrix Workspace, Omnissa / VMware Horizon, Termidesk, Удалённый рабочий стол / Безопасность Windows |
| Токены | запросы PIN Рутокен и КриптоПро CSP |
| Прочее | 1С:Предприятие, SAP GUI, PuTTY / KiTTY, WinSCP |

- Поля заполняются по отдельности: логин, запомненный клиентом, сохраняется (или заменяется, если отличается), в поле пароля, PIN или кода вводится только это значение. Горячая клавиша работает так же; своя последовательность в записи имеет приоритет.
- Текстовое поле без подписи, за которым идёт поле пароля, считается полем логина.
- PIN токена / смарт-карты: дополнительное поле записи `PIN` (если его нет — пароль).
- Клиенты, запущенные от имени администратора или SYSTEM: Windows блокирует эмулированный ввод (UIPI). PassKeeper помещает пароль или PIN в буфер обмена (с очисткой по настройке) и показывает уведомление.
- Клиенты с собственной отрисовкой без UI Automation: горячая клавиша вводит последовательность записи в поле с фокусом.

### Импорт и экспорт

| Импорт | |
|---|---|
| Браузеры (напрямую) | Chrome, Edge, Яндекс Браузер, Opera, Opera GX, Brave, Vivaldi, Chromium, Atom, Firefox, Waterfox, LibreWolf, Floorp, Zen, Thunderbird |
| Менеджеры паролей | KeePass/KeePassXC (KDBX 3.1/4 с паролем и файлом-ключом, XML, CSV), Bitwarden (JSON, CSV), 1Password (1PUX, CSV), LastPass, Dashlane, NordPass, Proton Pass, RoboForm, Keeper, Kaspersky Password Manager |
| Прочее | диспетчер учётных данных Windows, любой CSV (UTF-8/UTF-16/Windows-1251, `,` `;` Tab), PassKeeper PKX/JSON |

Экспорт: PassKeeper PKX (зашифрованный), KeePass KDBX 4 (зашифрованный), CSV для Chromium-браузеров, Firefox, Bitwarden, 1Password, LastPass, KeePassXC, полный CSV, полный JSON, Bitwarden JSON, KeePass XML, HTML. Экспорт требует мастер-пароль.

Chrome 127+ шифрует новые пароли App-Bound Encryption, недоступной сторонним программам. Такие записи отмечаются в отчёте импорта: выгрузите их из Chrome (Настройки → Менеджер паролей → Экспорт) и импортируйте CSV.

### Установка

```bat
PassKeeper-Setup-1.1.0.exe
```

| Режим | Папка | Права |
|---|---|---|
| Только для меня | `%LOCALAPPDATA%\Programs\PassKeeper` | пользователь |
| Для всех пользователей | `%ProgramFiles%\PassKeeper` | администратор |
| Портативный (zip) | любая папка, данные в `.\Data` | пользователь |

Автозапуск и язык, выбранные в установщике, применяются при первом запуске.

Тихая установка: `/S`, `/allusers` или `/currentuser`, `/desktop`, `/autostart` или `/noautostart`, `/nolaunch`, `"/dir=путь"`, `/lang=ru|en`. Коды возврата: 0 — успех, 1 — ошибка, 740 — нужны права администратора, 1602 — отменено.

```bat
PassKeeper-Setup-1.1.0.exe /S /allusers /desktop /autostart
```

### Удаление

`Uninstall.exe` в папке программы, Пуск → PassKeeper → Удалить PassKeeper, Параметры → Приложения или PassKeeper → Настройки → О программе. Удаляются файлы программы, ярлыки, автозапуск и записи реестра; данные пользователя в `%APPDATA%\PassKeeper` удаляются, только если отмечено **Удалить также мои данные**.

Тихо: `Uninstall.exe /S` (данные сохраняются), `Uninstall.exe /S /removedata`.

Данные: `%APPDATA%\PassKeeper` — `vault.pkv`, `pin.dat` (DPAPI), `settings.json`, `Backups\`. Другая папка: `--data <папка>`.

### Сборка

Нужен .NET 10 SDK.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Результат в `dist\`: установщик, портативный zip, `SHA256SUMS.txt`. Тесты: `dotnet test tests/PassKeeper.Tests`.

| Путь | Содержимое |
|---|---|
| `src/PassKeeper.Core` | криптография, хранилище, PIN, импорт/экспорт, KDBX, TOTP, сопоставление, распознавание полей |
| `src/PassKeeper` | приложение WPF |
| `src/PassKeeper.Setup` | установщик и деинсталлятор (.NET Framework 4.8) |
| `tests/PassKeeper.Tests` | модульные тесты |

### Ограничения

- Эмулированный ввод не доходит до программ, запущенных от имени SYSTEM; для программ, запущенных от имени администратора, PassKeeper тоже нужно запустить от имени администратора. В обоих случаях используется передача через буфер обмена.
- Клиенты входа проверены на диалогах той же структуры (формы входа Cisco и Check Point, запрос PIN токена); сборки производителей могут отличаться.
- Исполняемые файлы не подписаны цифровой подписью.
- Мастер-пароль не восстанавливается.
