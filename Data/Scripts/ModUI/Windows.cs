using System;
using System.Collections.Generic;

namespace Fd32.ModUI
{
    // Кнопка внизу общего окна мода.
    public class FooterItem
    {
        public string Key, Label, Tip;
        public bool Left, Default, Cancel;
    }

    // Общее окно мода (ModUI.Window, DESIGN.md раздел 11): мод описывает окно целиком — заголовок, раскладку, контролы,
    // нижние кнопки. Значения живые: каждое изменение сразу уходит моду, значения мода окно перечитывает несколько раз в секунду.
    public class WindowRequest
    {
        public const int Protocol = 2;

        public string Owner, Caption;
        public bool Wide;
        public object Info;                          // string, Func<string> или null
        public List<Tab> Tabs;
        public List<FooterItem> Footer;
        public Func<object[]> Describe;
        public Func<Dictionary<string, string>> Values;
        public Func<Dictionary<string, string>, string, object[]> Edit;
        public Func<string, bool> Command;

        // { "ModUI.Window", протокол, заголовок (для лога), describe Func<object[]>, values Func<Dictionary>,
        //   edit Func<Dictionary, string, object[]>, command Func<string, bool> }
        public static WindowRequest Parse(object[] a, out string error)
        {
            error = null;
            if (a.Length < 7 || !(a[1] is int))
            {
                error = "неверный формат (" + a.Length + " полей)";
                return null;
            }
            if ((int)a[1] > Protocol)
            {
                error = "протокол окна " + a[1] + " новее центра (" + Protocol + ") — нужен новый Mod UI";
                return null;
            }
            var w = new WindowRequest
            {
                Owner = a[2] as string ?? "?",
                Describe = a[3] as Func<object[]>,
                Values = a[4] as Func<Dictionary<string, string>>,
                Edit = a[5] as Func<Dictionary<string, string>, string, object[]>,
                Command = a[6] as Func<string, bool>,
            };
            if (w.Describe == null || w.Edit == null || w.Command == null)
            {
                error = "нет обратных вызовов";
                return null;
            }
            return w.Load(out error) ? w : null;
        }

        // Взять описание у мода заново (открытие и Rebuild).
        public bool Load(out string error)
        {
            error = null;
            object[] d;
            try
            {
                d = Describe();
            }
            catch (Exception e)
            {
                error = "описание: " + e.Message;
                return false;
            }
            if (d == null || d.Length < 5)
            {
                error = "неверное описание окна";
                return false;
            }
            var tabs = Tab.ParseTabs(d[3] as object[], out error);
            if (tabs == null)
                return false;
            var footer = new List<FooterItem>();
            var fa = d[4] as object[];
            if (fa != null)
                foreach (var o in fa)
                {
                    var f = o as object[];
                    if (f == null || f.Length < 7 || (f[0] as string) != "footer")
                        continue;
                    footer.Add(new FooterItem
                    {
                        Key = f[1] as string, Label = f[2] as string ?? "", Tip = f[3] as string,
                        Left = f[4] is bool && (bool)f[4], Default = f[5] is bool && (bool)f[5], Cancel = f[6] is bool && (bool)f[6],
                    });
                }
            var keys = new HashSet<string>();
            var menu = new MenuEntry { Tabs = tabs };
            foreach (var it in menu.All())
                if (it.Key != null && !keys.Add(it.Key))
                {
                    error = "ключ \"" + it.Key + "\" повторяется";
                    return false;
                }
            Caption = d[0] as string ?? "";
            Wide = d[1] is bool && (bool)d[1];
            Info = d[2];
            Tabs = tabs;
            Footer = footer;
            return true;
        }

        // Окно настроек рисует общее окно как меню. В узком окне закладки идут группами одна под другой.
        public MenuEntry AsMenu()
        {
            var tabs = Tabs;
            if (!Wide && Tabs.Count > 1)
            {
                var items = new List<Item>();
                foreach (var t in Tabs)
                {
                    if (t.Title == null)
                        items.AddRange(t.Items);
                    else
                        items.Add(new Item { Kind = "group", Label = t.Title, Children = t.Items });
                }
                tabs = new List<Tab> { new Tab { Items = items } };
            }
            var self = this;
            return new MenuEntry
            {
                Id = Owner,
                Name = Caption,
                Version = "",
                Tabs = tabs,
                Info = Wide ? Info : null,
                GetValues = Values,
                Edit = Edit,
                Command = c => self.Command(c),
            };
        }

        // Команда моду: true — мод просит закрыть окно.
        public bool SafeCommand(string command)
        {
            try
            {
                return Command(command);
            }
            catch (Exception e)
            {
                Log.Write("команда " + command + " окну " + Owner + ": " + e.Message);
                return false;
            }
        }
    }
}
