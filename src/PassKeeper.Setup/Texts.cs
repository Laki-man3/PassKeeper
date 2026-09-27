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
        };

        public static string T(string key)
        {
            string[] pair;
            if (!Table.TryGetValue(key, out pair)) return key;
            return Language == "ru" ? pair[0] : pair[1];
        }
    }
}
