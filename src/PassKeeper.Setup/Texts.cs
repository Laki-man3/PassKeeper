using System.Collections.Generic;
using System.Globalization;

namespace PassKeeper.Setup
{
    internal static class Texts
    {
        public static string Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "Title", new[] { "Установка PassKeeper", "PassKeeper Setup" } },
            { "Version", new[] { "Версия {0} · офлайн менеджер паролей", "Version {0} · offline password manager" } },
            { "Scope", new[] { "Для кого установить", "Install for" } },
            { "ScopeUser", new[] { "Только для меня", "Just me" } },
            { "ScopeUserDesc", new[] { "Права администратора не нужны", "No administrator rights needed" } },
            { "ScopeAll", new[] { "Для всех пользователей", "All users" } },
            { "ScopeAllDesc", new[] { "Требуются права администратора", "Requires administrator rights" } },
            { "Folder", new[] { "Папка установки", "Install folder" } },
            { "Change", new[] { "Изменить…", "Change…" } },
            { "Desktop", new[] { "Создать ярлык на рабочем столе", "Create a desktop shortcut" } },
            { "Autostart", new[] { "Запускать при входе в Windows", "Start when signing in to Windows" } },
            { "AutostartAll", new[] { "Запускать при входе в Windows (для всех)", "Start when signing in to Windows (all users)" } },
            { "Install", new[] { "Установить", "Install" } },
            { "Cancel", new[] { "Отмена", "Cancel" } },
            { "Installing", new[] { "Установка…", "Installing…" } },
            { "StepStopping", new[] { "Завершение запущенной копии…", "Closing the running copy…" } },
            { "StepCopying", new[] { "Копирование файлов…", "Copying files…" } },
            { "StepShortcuts", new[] { "Создание ярлыков…", "Creating shortcuts…" } },
            { "StepRegistry", new[] { "Регистрация программы…", "Registering…" } },
            { "StepDone", new[] { "Готово", "Done" } },
            { "Done", new[] { "PassKeeper установлен", "PassKeeper is installed" } },
            { "DoneText", new[] { "При первом запуске создайте локальный профиль и PIN‑код. Программа работает полностью без сети.", "On first start create a local profile and a PIN. The app works completely offline." } },
            { "LaunchNow", new[] { "Запустить PassKeeper", "Launch PassKeeper" } },
            { "Finish", new[] { "Готово", "Finish" } },
            { "Close", new[] { "Закрыть", "Close" } },
            { "Failed", new[] { "Не удалось установить PassKeeper", "PassKeeper could not be installed" } },
            { "Cancelled", new[] { "Установка отменена: права администратора не предоставлены.", "Setup was cancelled: administrator rights were not granted." } },
            { "NoPayload", new[] { "Установщик повреждён: отсутствуют файлы программы.", "The installer is damaged: application files are missing." } },
            { "ShortcutDescription", new[] { "PassKeeper — менеджер паролей", "PassKeeper password manager" } },
            { "Offline", new[] { "Установка не требует подключения к сети", "No network connection required" } },
            { "UninstallShortcut", new[] { "Удалить PassKeeper", "Uninstall PassKeeper" } },

            // ---- uninstaller
            { "UnTitle", new[] { "Удаление PassKeeper", "Uninstall PassKeeper" } },
            { "UnVersion", new[] { "Версия {0}", "Version {0}" } },
            { "UnScopeUser", new[] { "Установлен для текущего пользователя", "Installed for the current user" } },
            { "UnScopeAll", new[] { "Установлен для всех пользователей", "Installed for all users" } },
            { "UnText", new[] { "Будут удалены файлы программы, ярлыки, автозапуск и записи в реестре.", "Program files, shortcuts, autostart and registry entries will be removed." } },
            { "UnRemoveData", new[] { "Удалить также мои данные", "Also remove my data" } },
            { "UnRemoveDataDesc", new[] { "Хранилище паролей, PIN‑код, настройки и резервные копии:", "Password vault, PIN, settings and backups:" } },
            { "UnKeepDataNote", new[] { "Данные останутся на компьютере: при повторной установке хранилище откроется с тем же мастер‑паролем.", "Your data stays on this computer: after reinstalling, the vault opens with the same master password." } },
            { "UnDataWarning", new[] { "Хранилище будет удалено безвозвратно. Если пароли ещё нужны, сначала сделайте экспорт или резервную копию.", "The vault will be deleted permanently. If you still need the passwords, export them or make a backup first." } },
            { "UnNoData", new[] { "Данных текущего пользователя не найдено.", "No data of the current user was found." } },
            { "UnButton", new[] { "Удалить", "Uninstall" } },
            { "UnRemoving", new[] { "Удаление…", "Uninstalling…" } },
            { "UnStepStopping", new[] { "Завершение PassKeeper…", "Closing PassKeeper…" } },
            { "UnStepFiles", new[] { "Удаление программы…", "Removing the program…" } },
            { "UnStepData", new[] { "Удаление данных пользователя…", "Removing user data…" } },
            { "UnDone", new[] { "PassKeeper удалён", "PassKeeper was uninstalled" } },
            { "UnDoneKept", new[] { "Ваши данные сохранены в папке:", "Your data is kept in:" } },
            { "UnDoneRemoved", new[] { "Программа и ваши данные удалены.", "The program and your data were removed." } },
            { "UnFailed", new[] { "Не удалось удалить PassKeeper", "PassKeeper could not be uninstalled" } },
            { "UnCancelled", new[] { "Удаление отменено: права администратора не предоставлены.", "Uninstall was cancelled: administrator rights were not granted." } },
            { "UnNotInstalled", new[] { "PassKeeper не найден в этой папке. Удалите программу через «Параметры → Приложения».", "PassKeeper is not installed in this folder. Remove it in Settings → Apps." } },
            { "UnDataBusy", new[] { "Часть файлов данных не удалось удалить: {0}", "Some data files could not be removed: {0}" } },
        };

        public static string T(string key)
        {
            string[] pair;
            if (!Table.TryGetValue(key, out pair)) return key;
            return Language == "ru" ? pair[0] : pair[1];
        }
    }
}
