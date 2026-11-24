using System;
using System.Collections.Generic;

namespace Fd32.ModUI
{
    // Запрос окна ввода значений от клиента (ModUI.Ask, DESIGN.md раздел 9): значения по порядку (ключи "0", "1"…),
    // шаблон подписи, проверка на «ОК», ответ. Булево приходит выбором из двух вариантов ("true" / "false").
    public class FormRequest
    {
        public const int Protocol = 1;
        public const string LabelKey = "#label";
        public const int LabelMax = 9;              // MyToolbarItemActions.TOOLBAR_LABEL_MAX_CHARS

        public string Owner, Caption, Template;
        public bool AskLabel;
        public List<Item> Values;                   // только число, выбор, текст
        public List<Item> Items;                    // что рисует окно: значения + разделитель и поле подписи
        public Func<Dictionary<string, string>, string[]> Check;
        public Action<Dictionary<string, string>, string> Ok;
        public Action Cancel;
        bool answered;

        // { "ModUI.Ask", протокол, кто (для лога), заголовок, значения object[], шаблон или null, спрашивать подпись,
        //   проверка Func<Dictionary, string[]> или null, ответ Action<Dictionary, string>, отмена Action }
        public static FormRequest Parse(object[] a, out string error)
        {
            error = null;
            if (a.Length < 10 || !(a[1] is int))
            {
                error = "неверный формат (" + a.Length + " полей)";
                return null;
            }
            if ((int)a[1] > Protocol)
            {
                error = "протокол окна ввода " + a[1] + " новее центра (" + Protocol + ") — нужен новый Mod UI";
                return null;
            }
            var f = new FormRequest
            {
                Owner = a[2] as string ?? "?",
                Caption = a[3] as string ?? "",
                Template = a[5] as string,
                AskLabel = a[6] is bool && (bool)a[6],
                Check = a[7] as Func<Dictionary<string, string>, string[]>,
                Ok = a[8] as Action<Dictionary<string, string>, string>,
                Cancel = a[9] as Action,
            };
            if (f.Ok == null)
            {
                error = "нет ответа (Ok)";
                return null;
            }
            var list = Item.ParseList(a[4] as object[], out error);
            if (list == null)
                return null;
            f.Values = list.FindAll(x => x.Kind == "number" || x.Kind == "choice" || x.Kind == "text");
            var keys = new HashSet<string>();
            foreach (var it in f.Values)
                if (!keys.Add(it.Key))
                {
                    error = "ключ \"" + it.Key + "\" повторяется";
                    return null;
                }
            f.Items = new List<Item>(f.Values);
            if (f.AskLabel)
            {
                if (f.Items.Count > 0)
                    f.Items.Add(new Item { Kind = "separator" });
                f.Items.Add(new Item { Kind = "text", Key = LabelKey, Label = Loc.ToolbarLabel, Tip = Loc.ToolbarLabelTip, MaxLength = LabelMax, DefStr = "" });
            }
            return f;
        }

        // Окно настроек рисует форму как меню из одной закладки.
        public MenuEntry AsMenu()
        {
            return new MenuEntry
            {
                Id = Owner,
                Name = Caption,
                Version = "",
                Tabs = new List<Tab> { new Tab { Items = Items } },
            };
        }

        public void SafeOk(Dictionary<string, string> values, string label)
        {
            if (answered)
                return;
            answered = true;
            try
            {
                Ok(values, label);
            }
            catch (Exception e)
            {
                Log.Write("ответ окна ввода " + Owner + ": " + e);
            }
        }

        public void SafeCancel()
        {
            if (answered)
                return;
            answered = true;
            try
            {
                if (Cancel != null)
                    Cancel();
            }
            catch (Exception e)
            {
                Log.Write("отмена окна ввода " + Owner + ": " + e);
            }
        }
    }
}
