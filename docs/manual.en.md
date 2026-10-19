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
| Folders | your folders: **New folder** creates one ("Work", "Study"; "Work/Mail" is a folder inside a folder), entries are moved into a folder by dragging them, a right click on a folder renames or deletes it (its entries are kept) |
| Trash | deleted entries; restore them or delete them permanently |

The section is chosen in the entry editor. Entries of older versions and imported entries get a section automatically: an entry with a VPN client goes to "VPN and remote access", one with program windows to "Programs", one with a web address to "Websites".

Search (Ctrl+F) looks in the title, login, addresses, e-mail, phone, notes, folder and visible custom fields.

The borders between the left pane, the list and the entry card can be dragged with the mouse; the widths are remembered. In a small window the panes narrow by themselves (the entry card keeps its minimum width), and fields and buttons in a narrow pane are placed one under another; widening the window brings the widths back.

### Several entries at once {#bulk}

- **One by one:** point at an entry's icon and it turns into a round check; a click on it starts selecting. From then on a plain click on any entry adds it to the selection or takes it out. Selecting ends when nothing is selected any more (or **Clear selection**, Esc).
- **All at once:** the box left of the section title above the list (**Select all**, Ctrl+A) selects every entry in the list, for example all entries of "VPN and remote access" or those found by a search.
- Ctrl+click and Shift+click work too.
- The right pane offers actions for all of them: **Add to favorites**, **Remove from favorites**, **Delete** (to the trash, after a confirmation); in the trash **Restore** and **Delete forever**. The context menu has the same actions; Delete removes the selected entries, Esc clears the selection.
- **Dragging:** entries (one, or all selected ones) can be dragged with the mouse onto a folder in the left pane, onto **Favorites** or into the **Trash**; onto **New folder** to create a folder and move them into it at once.

## Adding and editing entries {#entries}

**+** (Ctrl+N) creates an entry in the current section. The section at the top of the editor decides which fields are shown.

### Website

Title, login, password, website address, e-mail, phone, key, 2FA code (TOTP), folder, notes, custom fields. The website address is all the browser suggestions need. **Sign in automatically** presses Enter after the sign-in form is filled in, see [Automatic sign-in to websites](#autologin-web).

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

- **⋯ → Duplicate** (or right-click an entry) opens a copy marked "(copy)" in the editor. The sites where the original is filled in by itself stay with the original.
- **⋯ → Use for a VPN client or program…**: choose the client and a new entry for it opens with the login and password of the original. Add what is missing (PIN, server address) in the editor.
- **⋯ → Use for a website…** (on VPN and program entries) is the other direction: a new site entry with the same login and password; only the address is left to fill in.
- **Take login and password from another entry…** in the editor copies the login, password, e-mail and 2FA secret of the chosen entry.

### Changing a password in several entries {#password-sync}

When a saved password changes and the old one is still used in other entries (for example a domain account for a site and a VPN), PassKeeper offers to update them as well.

### Password generator

The key button next to the password or **Password generator** in the left pane: length, upper and lower case letters, digits, symbols, excluding look-alike characters (I, l, 1, O, 0). The strength estimate is shown in the generator under the created password.

## Autofill in browsers {#browser}

**Automatically.** When a sign-in page opens, PassKeeper finds its form: if the cursor is not in a field, it puts it into the login field (with a click when the browser ignores the request, and only when nothing covers the field) and fills in the login and password. Each page is filled once; fields the browser or you already filled are left alone. Enter is pressed only with **Press Enter after filling** on or with **Sign in automatically** on in the entry ([Automatic sign-in to websites](#autologin-web)). Turned off in **Settings → Autofill → Fill in sign-in forms automatically**.

**Which entry is filled in.**

| Situation | What happens |
|---|---|
| One entry is saved for the page address | it is filled in, even if the domain has other entries (the company's other services on `*.corp.local` do not interfere) |
| Only one entry of the domain matches | it is filled in |
| Two or more entries for this address, or only several entries of the domain | nothing is typed: a list appears next to the login field, the entry used last on top |
| You chose an entry in the list | it becomes the account for this site and is filled in by itself next time, even if other accounts are saved for the site |
| No entry | the login or password field of a sign-in form offers **Create an entry for the site** (see below) or **Choose an entry…**: any entry will do, and with **Remember the choice** ticked the address is added to it |

**Suggestion.** When the cursor enters a login, password, e-mail, phone, one-time code or key field, a PassKeeper suggestion with matching entries appears next to it. Clicking an entry fills the field; for a login or password field both are filled. If you went to another page or window, the suggestion closes and types nothing.

**Browsers.** Chrome, Edge, Firefox, Yandex Browser and other Chromium- and Firefox-based browsers. Yandex Browser does not report which field has the cursor: PassKeeper watches its pages itself and puts the cursor into fields with a click.

- An entry matches when the page address is the entry's address or one of its **Other addresses of this site**; entries of other subdomains of the same company match more weakly.
- A site is its name (`sso.example.com`), not the full link: a sign-in page with a fresh one-time link on every sign-in (single sign-on, Keycloak) is the same site. PassKeeper also notices such a page by its changed address when the title stays the same.
- A site is filled in by itself at most twice in 3 minutes: a page that comes back with a new link after a failed sign-in is not filled over and over.
- The entry card shows where the entry is filled in by itself (**Filled in by itself on**); to undo, use the **…** menu → **Do not fill in automatically**. Removing the site's address from the entry undoes the choice for that site as well.
- A VPN or program entry can be the chosen account of a site too (a domain account on a single sign-on page, for example): the site's address is added to it and kept when the entry is edited.
- A form below the visible part of the page is found as well: PassKeeper scrolls to the field before typing.
- A field without a label of its own is recognised by the text next to it ("Enter your phone number" above the field). A phone field where the site already put the country code ("+7") counts as empty; only an entry with a phone number is filled into it by itself.
- While the vault is locked the suggestion offers to unlock it with the PIN.
- Suggestions are turned off in **Settings → Autofill → Suggestions at login fields**.

### A new site {#new-site}

**Create an entry from the sign-in page.** On a site without entries, the login or password field of a sign-in form offers **Create an entry for …**. A new entry opens with the site's address and a suggested password; the fields of the form found (login, password, sign-in in two steps) make its typing sequence. After saving, PassKeeper goes back to the page and fills in the form.

**Save a sign-in done by hand.** When you signed in to such a site yourself, without PassKeeper, it offers **Save…** once the next page of the site opens. Nothing is read from the page: the entry that opens has only the address, and you enter the login and password. If another entry already has the same login and password (a VPN domain account, for example), PassKeeper offers to use that entry for the site or to save a new entry with the same data. **Never for this site** stops the offer there; turned off in **Settings → Autofill → Offer to save sign-ins to websites**.

## Auto-type hotkey {#hotkey}

In a sign-in window (browser or program) press **Ctrl+Alt+A**:

1. PassKeeper identifies the window: the page address, or the program by process name and title.
2. With one matching entry it is typed at once; with several a list appears (Enter: type, ↑↓: select, Esc: cancel).
3. Fields are filled one by one: a login the client remembered is not retyped; a password, PIN or code field gets just that value. If the cursor is not in the page's form, PassKeeper finds the form and puts the cursor into the login field.
4. If the entry has a typing order of keys, that is typed; an order of fields only fills those fields of the form.

The hotkey is changed in **Settings → Autofill**. The keyboard button in an entry card minimizes PassKeeper and types into the entry's client window or the previous window.

## Client detection {#detect}

No need to add windows by hand:

- **Ctrl+Alt+A in the window of a client that has no entry yet** opens a new entry with the client, its windows and its form fields (login, password, PIN, code) already recognised. Fill in the rest or take the login and password from a site entry: after saving, PassKeeper returns to the window and signs in.
- **The suggestion at a login field** of such a client offers "Create an entry for …".
- **Choose…** in the editor and **Find clients on this computer** in empty sections list running and installed clients.

## Automatic sign-in {#autologin}

### VPN clients and programs

For entries with **Sign in automatically** on: when the client's sign-in window appears, PassKeeper fills the fields and presses Enter.

- If the vault is locked, PassKeeper asks for the PIN right away and signs in after unlocking.
- Each window is handled once. An entry is tried at most 2 times in 10 minutes, so a wrong password cannot lock the account; a notification tells when the limit is reached.
- If a client has several entries, the one whose server address is in the window title is used; otherwise automatic sign-in is skipped and the suggestion is shown.
- Main switch: **Settings → Autofill → Automatic sign-in to VPN clients and programs**.
- A sign-in window the client hid and shows again (after a connection error, for example) counts as new.

**Without automatic sign-in.** When exactly one entry belongs to a sign-in window (by the client's windows), the fields are filled in by themselves as soon as the cursor is in an empty login or password field, without pressing Enter. A login box without a label in a known client's window (Check Point and others) is recognised as the login.

### Websites {#autologin-web}

For website entries with **Sign in automatically** on: when PassKeeper has filled in the sign-in form by itself (or you chose the entry in the list at the field), it presses Enter.

- Attempts that typed a password are counted: at most 2 per entry in 10 minutes. After that the form is only filled in, Enter is not pressed, and a notification appears.
- Sign-in in two steps (login, then password): Enter is pressed after the login as well; if the password is asked for on a new page, PassKeeper fills that page too.
- Works together with **Fill in sign-in forms automatically**. Main switch: **Settings → Autofill → Automatic sign-in to websites**.
- A VPN or program entry chosen on a site only fills in the form there: its switch is about the client.

## Token and smart-card PIN {#pin}

A PIN field ("PIN:", "Enter PIN", "Token PIN") is recognised separately from a password. It gets the entry's **Token PIN**, or the password if no PIN is set. In a sequence the PIN is `{PIN}`.

Rutoken and CryptoPro CSP PIN prompts are in the client catalog; they use the sequence `{PIN}{ENTER}` by default.

## Program windows, other addresses and typing order {#autotype}

Usually nothing here needs changing: a site needs only its address, a program only the chosen client.

### Program windows

The window list is filled by **Choose…** in the **Client or program** block. To change it by hand, press **Set the windows by hand** under the block. One per line:

- `csc_ui.exe`: a program's process name;
- `Cisco Secure Client*`: a window title, `*` matches any text;
- plain text without `*` is looked up as part of the title.

### Other addresses of this site

The link under the site address. If the same account also opens on other addresses (a single sign-on page, for example), list them one per line:

```
mail.example.com
id.example.com
```

An address is added by itself when you choose the entry in the list at a field on another page of the domain, or tick **Remember the choice**.

### Typing order

The folded **Typing order** block at the bottom of the editor. The sequence is shown as elements (`{USERNAME}`, `{TAB}`, `{PASSWORD}`, `{ENTER}`), each with a cross that removes it. Elements from the list below are added at the end by clicking them, right-click copies; **Edit as text** opens the whole sequence.

- **Made automatically.** With **Sign in automatically** on, PassKeeper makes the order itself: from the sign-in form's fields found when the entry was created (otherwise a login and a password), with Enter at the end; for sign-in in two steps with Enter after the login too. Without automatic sign-in Enter is not pressed.
- **Fields only** (`{USERNAME}`, `{PASSWORD}`, `{PIN}`, `{TOTP}`, `{EMAIL}`, `{PHONE}`, `{KEY}` separated by `{TAB}` or `{ENTER}`). PassKeeper fills these fields of the form in order, as reliably as without a sequence, and presses Enter after the field it follows. For example `{PASSWORD}{ENTER}`: a login the program remembers is left alone. Removing an element of the automatic sequence with its cross makes it your own.
- **Keystrokes** (with pauses, text and other keys), for terminals and custom-drawn windows: PassKeeper types them where the cursor is, as if you typed them.

Text in braces is replaced by the entry's data or a key press; everything else is typed as is.

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
| Autofill | hotkey, suggestions at fields, automatic filling of sign-in forms, autofill log, automatic sign-in to VPN clients and programs, automatic sign-in to websites, offer to save sign-ins, Enter after filling, compatibility typing mode, delay between keystrokes |
| Profile | sign out, delete the user |
| Data | vault folder, backup |
| About | version, installation mode, uninstall, the author's contacts for remarks and suggestions (Telegram, e-mail, GitHub) |

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
| Click an entry's icon | start selecting several entries |
| Drag an entry | to a folder, to favorites or to the trash |
| Ctrl+A | select all entries in the list |
| Ctrl+click, Shift+click | add an entry to the selection, select a range |
| Delete | move to trash (several selected entries after a confirmation) |
| Ctrl+L | lock |
| Esc or a click next to the dialog | close a dialog (settings, help, generator and others); in the list, clear the selection |

## Troubleshooting {#faq}

**No suggestion in a program.** Check that the client is chosen (**Client or program**) and suggestions are on. If the program's fields are not recognised, press Ctrl+Alt+A in the login field or set a custom typing order.

**A site is not filled in by itself.** Choose the entry in the list at the field once: from then on it is filled in by itself. If there is no list, turn on **Settings → Autofill → Autofill log**, open the sign-in page again and look at the log (**Open the log**): it shows which page and fields PassKeeper found, which entries matched and why the form was not filled. Logins, passwords and full addresses are never written to it. Ctrl+Alt+A always works.

**Text goes to the wrong place or is cut.** Increase **Delay between keystrokes** or turn on **Compatibility typing mode**; `{DELAY 500}` can be added to a sequence.

**The program runs as administrator.** See [Programs running as administrator](#elevated).

**Several accounts of one VPN.** Set **Server address** in each entry: automatic sign-in and suggestions use the entry whose address is in the window title.

**Automatic sign-in is paused.** There were 2 attempts in 10 minutes. Check the password in the entry; automatic sign-in is available again after 10 minutes, and Ctrl+Alt+A types the data meanwhile. On a website the form is filled in without Enter meanwhile.

**A VPN client behaves differently while PassKeeper runs.** In the background PassKeeper only receives focus notifications and does not query program windows until you ask (suggestion, Ctrl+Alt+A, automatic sign-in). If the problem remains, turn off **Suggestions at login fields** and report the error text.

**Forgotten PIN.** Sign in with the master password (**Use master password** on the lock screen) and set a new PIN in Settings.

**Forgotten master password.** It cannot be recovered: the data is encrypted with it.
