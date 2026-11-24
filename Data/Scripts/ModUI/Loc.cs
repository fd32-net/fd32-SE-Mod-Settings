using Sandbox.ModAPI;
using VRage;

namespace Fd32.ModUI
{
    // Надписи центра: русский при русском языке игры, иначе английский.
    public static class Loc
    {
        static bool Ru
        {
            get
            {
                var s = MyAPIGateway.Session;
                return s != null && s.Config != null && s.Config.Language == MyLanguagesEnum.Russian;
            }
        }

        static string T(string ru, string en) { return Ru ? ru : en; }

        public static string GearTip { get { return T("Настройки модов", "Mod settings"); } }
        public static string NoMods { get { return T("Нет модов с настройками", "No mods with settings"); } }
        public static string Ok { get { return T("ОК", "OK"); } }
        public static string Cancel { get { return T("Отмена", "Cancel"); } }
        public static string Defaults { get { return T("По умолчанию", "Defaults"); } }
        public static string DefaultsTip { get { return T("Вернуть значения по умолчанию во всём моде. Применяются после «ОК».",
                                                           "Reset the whole mod to its defaults. Applied on OK."); } }
        public static string ErrorCaption { get { return T("Ошибка", "Error"); } }
        public static string ModFailed { get { return T("Мод не ответил: ошибка при применении настроек.", "The mod failed to apply the settings."); } }
        public static string MainTab { get { return T("Основные настройки", "General"); } }
        public static string Yes { get { return T("Да", "Yes"); } }
        public static string No { get { return T("Нет", "No"); } }
        public static string ConfirmCaption { get { return T("Подтверждение", "Confirm"); } }
        public static string PressKey { get { return T("Нажмите клавишу...", "Press a key..."); } }
        public static string PressKeyFor { get { return T("Нажмите клавишу или кнопку мыши для", "Press a key or mouse button for"); } }
        public static string PressKeyHint { get { return T("Esc - отмена, Delete - очистить", "Esc - cancel, Delete - clear"); } }
        public static string KeyBusy { get { return T("Эта клавиша занята: ", "This key is also used by: "); } }
        public static string InGame { get { return T("игра", "game"); } }
        public static string ToolbarLabel { get { return T("Подпись на панели", "Toolbar label"); } }
        public static string ToolbarLabelTip { get { return T("Текст под значком на панели действий: не длиннее 9 символов",
                                                               "Text under the icon on the toolbar: up to 9 characters"); } }
        public static string Transparency { get { return T("Прозрачность", "Transparency"); } }
        public static string ColorTip { get { return T("Щелчок — показать или скрыть ползунки", "Click to show or hide the sliders"); } }
        public static string Empty { get { return T("У этого мода нет настроек.", "This mod has no settings."); } }
    }
}
