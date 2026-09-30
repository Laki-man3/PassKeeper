# PassKeeper user guide

PassKeeper keeps passwords encrypted on this computer only and never uses the network. This guide covers every feature; the same text opens in the program with **Help** in the left pane.

## First run {#first-run}

1. Enter a user name and a master password (at least 8 characters). The master password cannot be recovered: without it the passwords are lost.
2. Tick **Start PassKeeper when I sign in to Windows** if needed (it starts as chosen in the installer).
3. Choose a PIN of 4–12 digits. From then on only the PIN is asked at start-up and after inactivity.

> After 5 wrong PINs in a row the PIN is reset: sign in with the master password and set a new PIN.

The interface language is switched with "Русский / English" in the top right corner of the first-run screen and in **Settings → General**.

## Users: sign out, sign in, delete {#users}

One computer can have several local users, each with a vault, PIN and backups of their own.

- **Sign out**: click the name at the bottom of the left pane, the link on the PIN screen, or **Settings → Profile**. The PIN is deleted; the passwords stay on the computer.
- **Sign in**: the sign-in screen asks for the user name (click it in the list) and the master password. A new PIN is set afterwards.
- **Create a new user**: the link on the sign-in screen.
- **Delete the user**: **Settings → Profile → Delete user…**, confirmed with the master password. This user's vault, PIN and backups are erased permanently; other users are not affected.

## Vault and sections {#sections}

Left pane:

| Item | Shows |
|---|---|
| All entries | every entry except the trash |
| Favorites | starred entries |
| Websites | site accounts |
| VPN and remote access | VPN clients, VDI, remote desktop |
| Programs | desktop programs: 1C, SAP, PuTTY, token PIN prompts |
| Other | Wi-Fi, licences, notes and everything else |
| Folders | your folders (shown once entries have a folder) |
| Trash | deleted entries; restore them or delete them permanently |

The section is chosen in the entry editor. Entries of older versions and imported entries get a section automatically: an entry with a VPN client goes to "VPN and remote access", one with program windows to "Programs", one with a web address to "Websites".

Search (Ctrl+F) looks in the title, login, addresses, e-mail, phone, notes, folder and visible custom fields.

The borders between the left pane, the list and the entry card can be dragged with the mouse; the widths are remembered.

## Adding and editing entries {#entries}

**+** (Ctrl+N) creates an entry in the current section. The section at the top of the editor decides which fields are shown.

### Website

Title, login, password, website address, e-mail, phone, key, 2FA code (TOTP), folder, notes, custom fields. The website address is all the browser suggestions need.

### VPN and remote access

1. In **Client or program** press **Choose…**. PassKeeper finds running and installed clients itself and lists them first; other open windows and the whole catalog follow.
2. Fill in the login and password or press **Take login and password from another entry…**.
3. If the client asks for a token PIN, fill in **Token PIN**.
4. **Server address** (optional) tells several entries of one client apart: when the window title shows the server, the entry with that address is used.
5. **Sign in automatically**: see [Automatic sign-in](#autologin).

### Program

The same as VPN, without the server address: 1C, SAP GUI, PuTTY, WinSCP, Rutoken and CryptoPro PIN prompts, or any window picked from the open ones.

### Other

Login, password, notes and custom fields for Wi-Fi, licences and the like.

### Duplicating and copying to a VPN entry {#duplicate}

- **⋯ → Duplicate** (or right-click an entry) opens a copy marked "(copy)" in the editor.
- **⋯ → Use for a VPN client or program…**: choose the client and a new entry for it opens with the login and password of the original. Add what is missing (PIN, server address) in the editor.
- **⋯ → Use for a website…** (on VPN and program entries) is the other direction: a new site entry with the same login and password; only the address is left to fill in.
- **Take login and password from another entry…** in the editor copies the login, password, e-mail and 2FA secret of the chosen entry.

### Changing a password in several entries {#password-sync}

When a saved password changes and the old one is still used in other entries (for example a domain account for a site and a VPN), PassKeeper offers to update them as well.

### Password generator

The key button next to the password or **Password generator** in the left pane: length, upper and lower case letters, digits, symbols, excluding look-alike characters (I, l, 1, O, 0). The strength estimate is shown under the password.

## Autofill in browsers {#browser}

**Automatically.** When a sign-in page puts the cursor into an empty login or password field and exactly one entry matches the site, the login and password are filled in by themselves. Each page is filled once; fields the browser or you already filled are left alone. Enter is pressed only with **Press Enter after filling** on. Turned off in **Settings → Autofill → Fill in sites automatically**.

**Several accounts on one site.** When two or more entries match, nothing is typed automatically: a list appears next to the field and you choose. The entry used last is on top. Ctrl+Alt+A opens the chooser in this case.

**Suggestion.** When the cursor enters a login, password, e-mail, phone, one-time code or key field, a PassKeeper suggestion with matching entries appears next to it. Clicking an entry fills the field; for a login or password field both are filled.

**A site without an entry.** The password field offers **Choose an entry…**: any entry will do, for example a VPN account for the company portal. With **Remember the choice for …** ticked, the site's address is added to that entry, so next time it is filled in by itself. The Ctrl+Alt+A chooser offers the same.

- An entry matches when the page address is the entry's address or one of its **Other addresses of this site**.
- While the vault is locked the suggestion offers to unlock it with the PIN.
- Suggestions are turned off in **Settings → Autofill → Suggestions at login fields**.

## Auto-type hotkey {#hotkey}

In a sign-in window (browser or program) press **Ctrl+Alt+A**:

1. PassKeeper identifies the window: the page address, or the program by process name and title.
2. With one matching entry it is typed at once; with several a list appears (Enter: type, ↑↓: select, Esc: cancel).
3. Fields are filled one by one: a login the client remembered is not retyped; a password, PIN or code field gets just that value.
4. If the entry has its own sequence, that sequence is typed.

The hotkey is changed in **Settings → Autofill**. The keyboard button in an entry card minimizes PassKeeper and types into the entry's client window or the previous window.

## Client detection {#detect}

No need to add windows by hand:

- **Ctrl+Alt+A in the window of a client that has no entry yet** opens a new entry with the client, its windows and its form fields (login, password, PIN, code) already recognised. Fill in the rest or take the login and password from a site entry: after saving, PassKeeper returns to the window and signs in.
- **The suggestion at a login field** of such a client offers "Create an entry for …".
- **Choose…** in the editor and **Find clients on this computer** in empty sections list running and installed clients.

## Automatic sign-in {#autologin}

For entries with **Sign in automatically** on: when the client's sign-in window appears, PassKeeper fills the fields and presses Enter.

- If the vault is locked, PassKeeper asks for the PIN right away and signs in after unlocking.
- Each window is handled once. An entry is tried at most 2 times in 10 minutes, so a wrong password cannot lock the account; a notification tells when the limit is reached.
- If a client has several entries, the one whose server address is in the window title is used; otherwise automatic sign-in is skipped and the suggestion is shown.
- Main switch: **Settings → Autofill → Automatic sign-in to programs**.

## Token and smart-card PIN {#pin}

A PIN field ("PIN:", "Enter PIN", "Token PIN") is recognised separately from a password. It gets the entry's **Token PIN**, or the password if no PIN is set. In a sequence the PIN is `{PIN}`.

Rutoken and CryptoPro CSP PIN prompts are in the client catalog; they use the sequence `{PIN}{ENTER}` by default.

## Application windows and auto-type sequence {#autotype}

These settings are in the folded **Auto-type: windows and sequence** block at the bottom of the editor. Usually they need no changes.

### Application windows

One per line:

- `csc_ui.exe`: a program's process name;
- `Cisco Secure Client*`: a window title, `*` matches any text;
- plain text without `*` is looked up as part of the title.

**Choose…** fills in the lines for you.

### Other addresses of this site

The browser suggestion appears when the page address matches the entry's address. If the same account also opens on other addresses, list them here, one per line:

```
mail.example.com
id.example.com
```

### Auto-type sequence

An empty sequence fills the window's fields (recommended). A sequence of your own is needed when fields are not recognised: terminals, custom-drawn windows, multi-step forms. PassKeeper types it where the cursor is, as if you typed it.

Text in braces is replaced by the entry's data or a key press; everything else is typed as is. In the editor, click a placeholder to insert it; right-click to copy.

| Placeholder | Typed |
|---|---|
| `{USERNAME}` | login (e-mail if empty) |
| `{PASSWORD}` | password |
| `{PIN}` | token PIN (the password if not set) |
| `{EMAIL}`, `{PHONE}`, `{KEY}` | the entry's e-mail, phone, key |
| `{TOTP}` | current 2FA code |
| `{TITLE}`, `{URL}`, `{NOTES}` | title, address, notes |
| `{S:Field name}` | value of a custom field |
| `{TAB}`, `{ENTER}`, `{ESC}`, `{SPACE}` | keys |
| `{UP}`, `{DOWN}`, `{LEFT}`, `{RIGHT}`, `{HOME}`, `{END}` | arrows and navigation |
| `{BACKSPACE}`, `{DELETE}`, `{F1}`…`{F24}` | other keys |
| `{TAB 3}` | key pressed three times |
| `{DELAY 500}` | 500 ms pause |
| `{DELAY=30}` | 30 ms between keystrokes for the rest of the sequence |
| `{CLEARFIELD}` | clear the field |
| `{{}`, `{}}` | literal braces |

Examples:

```
{USERNAME}{TAB}{PASSWORD}{ENTER}
```

```
{PASSWORD}{ENTER}
```

```
{PIN}{ENTER}
```

```
{USERNAME}{ENTER}{DELAY 800}{PASSWORD}{ENTER}
```

```
{PASSWORD}{ENTER}{DELAY 1500}{TOTP}{ENTER}
```

## Supported clients {#clients}

| Group | Clients |
|---|---|
| VPN | Cisco Secure Client (AnyConnect), Check Point Endpoint Security VPN, FortiClient, Palo Alto GlobalProtect, OpenVPN GUI / Connect, Континент-АП, ViPNet Client |
| VDI and remote access | Базис.WorkPlace (ВРМ), Citrix Workspace, Omnissa / VMware Horizon, Termidesk, Remote Desktop and the Windows Security dialog |
| Tokens | Rutoken and CryptoPro CSP PIN prompts |
| Programs | 1C:Enterprise, SAP GUI, PuTTY / KiTTY, WinSCP |

Any other program is added by choosing its open window.

## Programs running as administrator {#elevated}

Windows blocks simulated input into programs running as administrator or as the system (UIPI). PassKeeper then puts the password or PIN on the clipboard and shows a notification with the login: paste the value with Ctrl+V. With PassKeeper itself started as administrator, typing into such programs (except system ones) works as usual.

## Auto-lock and PIN {#lock}

- **Settings → Security → Auto-lock after inactivity**: a dial with an hh:mm:ss readout. Click hours, minutes or seconds and set the value with the dial's hand, the mouse wheel, the arrow keys or digits. The buttons below the readout are shortcuts. Any period from 10 seconds to 24 hours, 8 hours by default.
- **Lock when Windows is locked (Win+L)** locks together with the session.
- Ctrl+L or the lock icon at the bottom of the left pane locks at once.
- **Change PIN** and **Change master password** are in the same section.

## Clipboard {#clipboard}

Copied passwords, PINs and codes are cleared after 30 seconds (10–60 seconds or never, **Settings → Security**) and are kept out of the Windows clipboard history and cloud sync.

## Import {#import}

**Import** in the left pane:

- directly from browsers: Chrome, Edge, Yandex Browser, Opera, Opera GX, Brave, Vivaldi, Chromium, Firefox, Waterfox, LibreWolf, Floorp, Zen, Thunderbird;
- from files: KeePass / KeePassXC (KDBX with password and key file, XML, CSV), Bitwarden, 1Password, LastPass, Dashlane, NordPass, Proton Pass, RoboForm, Keeper, Kaspersky Password Manager, any CSV;
- from Windows Credential Manager.

Chrome 127+ encrypts new passwords so that other programs cannot read them. Such entries are listed in the import report: export them from Chrome (Settings → Password Manager → Export) and import the CSV.

## Export {#export}

**Export** in the left pane: PassKeeper PKX and KeePass KDBX (encrypted), CSV for browsers and password managers, JSON, XML, HTML. Export always asks for the master password; keep unencrypted files safe.

## Backups {#backup}

On every save the previous vault is kept as `vault.pkv.bak`, and once a day a copy goes to the `Backups` folder (14 days). **Settings → Data → Save a backup…** saves a copy wherever you choose. Backups are encrypted with the same master password.

## Settings {#settings}

| Section | Options |
|---|---|
| General | language, appearance (dark, light, as Windows), start with Windows, minimize to tray |
| Security | auto-lock, clipboard clearing, lock with Windows, change master password and PIN |
| Autofill | hotkey, suggestions at fields, automatic filling of sites, automatic sign-in, Enter after filling, compatibility typing mode, delay between keystrokes |
| Profile | sign out, delete the user |
| Data | vault folder, backup |
| About | version, installation mode, uninstall |

**Compatibility typing mode** types characters through the keyboard layout, for programs that do not accept Unicode input (some terminals and remote desktops).

## Installation and removal {#install}

- **Just me**: no administrator rights, `%LOCALAPPDATA%\Programs\PassKeeper`.
- **All users**: administrator rights, `%ProgramFiles%\PassKeeper`.
- **Portable**: unpack the archive into any folder; data is stored next to it in `Data`.

Uninstall: **Start → PassKeeper → Uninstall PassKeeper**, **Settings → Apps**, **Settings → About** in PassKeeper, or `Uninstall.exe` in the program folder. The user's data (`%APPDATA%\PassKeeper`) is removed only when **Also remove my data** is ticked. Copies of PassKeeper started from the program folder are closed automatically, and the program is removed from startup as well.

Silent install and uninstall:

```
PassKeeper-Setup.exe /S /currentuser /noautostart
```

```
Uninstall.exe /S
```

## Keyboard shortcuts {#shortcuts}

| Keys | Action |
|---|---|
| Ctrl+Alt+A | auto-type into the active window (from any program) |
| Ctrl+F | search |
| Ctrl+N | new entry |
| Ctrl+E | edit entry |
| Ctrl+C | copy password |
| Ctrl+B | copy login |
| Double-click an entry | copy password |
| Delete | move to trash |
| Ctrl+L | lock |
| Esc | close a dialog, clear the search |

## Troubleshooting {#faq}

**No suggestion in a program.** Check that the client is chosen (**Client or program**) and suggestions are on. If the program's fields are not recognised, press Ctrl+Alt+A in the login field or set a sequence.

**Text goes to the wrong place or is cut.** Increase **Delay between keystrokes** or turn on **Compatibility typing mode**; `{DELAY 500}` can be added to a sequence.

**The program runs as administrator.** See [Programs running as administrator](#elevated).

**Several accounts of one VPN.** Set **Server address** in each entry: automatic sign-in and suggestions use the entry whose address is in the window title.

**Automatic sign-in is paused.** There were 2 attempts in 10 minutes. Check the password in the entry; automatic sign-in is available again after 10 minutes, and Ctrl+Alt+A types the data meanwhile.

**A VPN client behaves differently while PassKeeper runs.** In the background PassKeeper only receives focus notifications and does not query program windows until you ask (suggestion, Ctrl+Alt+A, automatic sign-in). If the problem remains, turn off **Suggestions at login fields** and report the error text.

**Forgotten PIN.** Sign in with the master password (**Use master password** on the lock screen) and set a new PIN in Settings.

**Forgotten master password.** It cannot be recovered: the data is encrypted with it.
