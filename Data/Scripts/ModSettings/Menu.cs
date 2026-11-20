using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using VRage;
using VRage.Input;
using VRage.ModAPI;

namespace ModSettings
{
    // Элемент меню мода, как его описал клиентский файл ModSettings.cs (протокол 1, см. DESIGN.md раздел 6).
    // Описание приходит массивом object[]: { вид, ... }; неизвестные виды пропускаются (клиент новее центра).
    public class Item
    {
        public const int PartSlider = 1, PartField = 2, PartButtons = 4;

        public const int DefaultMaxLength = 256;

        public string Kind, Key, Label, Tip, Unit;
        public bool DefBool, HasRange, Collapsible, Collapsed;
        public List<Item> Children;          // у группы
        public float DefNum, Min, Max, Step;
        public int Decimals = -1, Parts;
        public string DefStr, Placeholder;   // у выбора (ключ варианта) и текста
        public string[] Options, OptionLabels;
        public int MaxLength = DefaultMaxLength;
        public bool CheckConflicts;         // у клавиши: подсвечивать конфликты (мод включил .CheckConflicts())

        public bool IsSetting { get { return Kind == "toggle" || Kind == "number" || Kind == "choice" || Kind == "text" || Kind == "hotkey"; } }

        public string DefaultString
        {
            get { return Kind == "toggle" ? (DefBool ? "true" : "false") : Kind == "number" ? Num.Str(DefNum) : DefStr; }
        }

        // Подпись варианта выбора по ключу (нет подписи — сам ключ).
        public string OptionLabel(string key)
        {
            int i = Options != null ? Array.IndexOf(Options, key) : -1;
            if (i < 0)
                return key ?? "";
            return OptionLabels != null && i < OptionLabels.Length && OptionLabels[i] != null ? OptionLabels[i] : key;
        }

        // Текст для поля: одна строка, не длиннее MaxLength.
        public string CleanText(string s)
        {
            s = (s ?? "").Replace("\r", "").Replace('\n', ' ');
            return s.Length > MaxLength ? s.Substring(0, MaxLength) : s;
        }

        // Разбор описания меню; null + текст ошибки, если формат не тот.
        public static List<Item> ParseList(object[] desc, out string error)
        {
            error = null;
            var list = new List<Item>();
            if (desc == null)
            {
                error = "нет описания меню";
                return null;
            }
            for (int i = 0; i < desc.Length; i++)
            {
                var a = desc[i] as object[];
                string kind = a != null && a.Length > 0 ? a[0] as string : null;
                if (kind == null)
                {
                    error = "элемент " + i + ": неверный формат";
                    return null;
                }
                try
                {
                    var it = new Item { Kind = kind };
                    switch (kind)
                    {
                        case "toggle":      // toggle, key, label, tip, default
                            it.Key = (string)a[1]; it.Label = (string)a[2]; it.Tip = a[3] as string;
                            it.DefBool = (bool)a[4];
                            break;
                        case "number":      // number, key, label, tip, default, hasRange, min, max, step, decimals, unit, parts
                            it.Key = (string)a[1]; it.Label = (string)a[2]; it.Tip = a[3] as string;
                            it.DefNum = (float)a[4]; it.HasRange = (bool)a[5]; it.Min = (float)a[6]; it.Max = (float)a[7];
                            it.Step = (float)a[8]; it.Decimals = (int)a[9]; it.Unit = a[10] as string; it.Parts = (int)a[11];
                            if (!it.HasRange)
                                it.Parts &= ~PartSlider;
                            if (it.Parts == 0)
                                it.Parts = it.HasRange ? PartSlider | PartField : PartField | PartButtons;
                            it.DefNum = Num.Normalize(it, it.DefNum);
                            break;
                        case "choice":      // choice, key, label, tip, default, keys[], labels[]
                            it.Key = (string)a[1]; it.Label = (string)a[2]; it.Tip = a[3] as string;
                            it.DefStr = a[4] as string;
                            it.Options = a[5] as string[] ?? new string[0];
                            it.OptionLabels = a[6] as string[];
                            if (Array.IndexOf(it.Options, it.DefStr) < 0)
                                it.DefStr = it.Options.Length > 0 ? it.Options[0] : "";
                            break;
                        case "text":        // text, key, label, tip, default, maxLength, placeholder
                            it.Key = (string)a[1]; it.Label = (string)a[2]; it.Tip = a[3] as string;
                            it.MaxLength = (int)a[5] > 0 ? (int)a[5] : DefaultMaxLength;
                            it.Placeholder = a[6] as string;
                            it.DefStr = it.CleanText(a[4] as string);
                            break;
                        case "hotkey":      // hotkey, key, label, tip, default ("Ctrl+Shift+Alt+R", "" — нет клавиши), [checkConflicts]
                            it.Key = (string)a[1]; it.Label = (string)a[2]; it.Tip = a[3] as string;
                            it.DefStr = a[4] as string;
                            it.CheckConflicts = a.Length > 5 && a[5] is bool && (bool)a[5];
                            if (!Hk.Normalize(ref it.DefStr))
                                it.DefStr = "";
                            break;
                        case "label":       // label, text, key
                            it.Label = (string)a[1];
                            it.Key = a.Length > 2 ? a[2] as string : null;
                            break;
                        case "separator":   // separator, key
                            it.Key = a.Length > 1 ? a[1] as string : null;
                            break;
                        case "button":      // button, key, label, tip
                            it.Key = (string)a[1]; it.Label = (string)a[2]; it.Tip = a[3] as string;
                            break;
                        case "group":       // group, title, collapsible, collapsed, items[], key
                            it.Label = a[1] as string ?? ""; it.Collapsible = (bool)a[2]; it.Collapsed = (bool)a[3];
                            it.Key = a.Length > 5 ? a[5] as string : null;
                            it.Children = ParseList(a[4] as object[], out error);
                            if (it.Children == null)
                            {
                                error = "группа \"" + it.Label + "\": " + error;
                                return null;
                            }
                            break;
                        default:
                            continue;
                    }
                    if ((it.IsSetting || kind == "button") && string.IsNullOrEmpty(it.Key))
                    {
                        error = "элемент " + i + " (" + kind + "): пустой ключ";
                        return null;
                    }
                    list.Add(it);
                }
                catch (Exception e)
                {
                    error = "элемент " + i + " (" + kind + "): " + e.GetType().Name;
                    return null;
                }
            }
            return list;
        }
    }

    // Горячие клавиши: строка протокола "Ctrl+Shift+Alt+<имя MyKeys>" (модификаторы в этом порядке), "" — нет клавиши.
    public static class Hk
    {
        static Dictionary<string, MyKeys> byName;

        static Dictionary<string, MyKeys> Names
        {
            get
            {
                if (byName == null)
                {
                    byName = new Dictionary<string, MyKeys>();
                    for (int i = 1; i < 256; i++)
                    {
                        var k = (MyKeys)i;
                        string n = k.ToString();
                        if (!byName.ContainsKey(n))
                            byName[n] = k;
                    }
                }
                return byName;
            }
        }

        public static bool IsModifier(MyKeys k)
        {
            return k == MyKeys.Control || k == MyKeys.LeftControl || k == MyKeys.RightControl || k == MyKeys.Shift ||
                   k == MyKeys.LeftShift || k == MyKeys.RightShift || k == MyKeys.Alt || k == MyKeys.LeftAlt || k == MyKeys.RightAlt;
        }

        // Левый / правый модификатор — общий.
        public static MyKeys Generic(MyKeys k)
        {
            if (k == MyKeys.LeftControl || k == MyKeys.RightControl) return MyKeys.Control;
            if (k == MyKeys.LeftShift || k == MyKeys.RightShift) return MyKeys.Shift;
            if (k == MyKeys.LeftAlt || k == MyKeys.RightAlt) return MyKeys.Alt;
            return k;
        }

        public static string Format(MyKeys key, bool ctrl, bool shift, bool alt)
        {
            if (key == MyKeys.None)
                return "";
            key = Generic(key);
            if (key == MyKeys.Control) ctrl = false;
            if (key == MyKeys.Shift) shift = false;
            if (key == MyKeys.Alt) alt = false;
            return (ctrl ? "Ctrl+" : "") + (shift ? "Shift+" : "") + (alt ? "Alt+" : "") + key;
        }

        public static bool TryParse(string s, out MyKeys key, out bool ctrl, out bool shift, out bool alt)
        {
            key = MyKeys.None;
            ctrl = shift = alt = false;
            if (string.IsNullOrEmpty(s))
                return true;
            var parts = s.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "Ctrl") ctrl = true;
                else if (parts[i] == "Shift") shift = true;
                else if (parts[i] == "Alt") alt = true;
                else return false;
            }
            MyKeys k;
            if (!Names.TryGetValue(parts[parts.Length - 1], out k))
                return false;
            key = Generic(k);
            return true;
        }

        // Привести к виду протокола; false — строка не клавиша.
        public static bool Normalize(ref string s)
        {
            MyKeys k;
            bool c, sh, a;
            if (!TryParse(s, out k, out c, out sh, out a))
                return false;
            s = Format(k, c, sh, a);
            return true;
        }

        static bool Mouse(MyKeys k, out MyMouseButtonsEnum b)
        {
            b = k == MyKeys.LeftButton ? MyMouseButtonsEnum.Left : k == MyKeys.RightButton ? MyMouseButtonsEnum.Right :
                k == MyKeys.MiddleButton ? MyMouseButtonsEnum.Middle : k == MyKeys.ExtraButton1 ? MyMouseButtonsEnum.XButton1 :
                k == MyKeys.ExtraButton2 ? MyMouseButtonsEnum.XButton2 : MyMouseButtonsEnum.None;
            return b != MyMouseButtonsEnum.None;
        }

        static string KeyName(MyKeys k)
        {
            string n = null;
            try
            {
                MyMouseButtonsEnum b;
                n = Mouse(k, out b) ? MyAPIGateway.Input.GetName(b) : MyAPIGateway.Input.GetKeyName(k);
            }
            catch (Exception) { }
            return string.IsNullOrEmpty(n) ? k.ToString() : n;
        }

        // "Ctrl + R" названиями игры; пусто — как у ванили для неназначенной клавиши.
        public static string Display(string s)
        {
            MyKeys k;
            bool c, sh, a;
            if (!TryParse(s, out k, out c, out sh, out a) || k == MyKeys.None)
                return MyAPIGateway.Input != null ? MyAPIGateway.Input.GetUnassignedName() : "-";
            return (c ? KeyName(MyKeys.Control) + " + " : "") + (sh ? KeyName(MyKeys.Shift) + " + " : "") +
                   (a ? KeyName(MyKeys.Alt) + " + " : "") + KeyName(k);
        }

        // Действие игры на этой комбинации (IMyInput.GetControl) или null.
        public static string GameControl(string s)
        {
            MyKeys k;
            bool c, sh, a;
            if (!TryParse(s, out k, out c, out sh, out a) || k == MyKeys.None)
                return null;
            try
            {
                IMyControl ctl;
                MyMouseButtonsEnum b;
                if (Mouse(k, out b))
                    ctl = c || sh || a ? null : MyAPIGateway.Input.GetControl(b);
                else
                {
                    var m = MyKeyboardModifiers.None;
                    if (c) m |= MyKeyboardModifiers.Control;
                    if (sh) m |= MyKeyboardModifiers.Shift;
                    if (a) m |= MyKeyboardModifiers.Alt;
                    ctl = MyAPIGateway.Input.GetControl(k, m);
                }
                return ctl != null ? MyTexts.GetString(ctl.GetControlName()) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // Числа: строки протокола — инвариантная культура; округление к шагу и диапазон — как в клиентском файле.
    public static class Num
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Str(float v) { return v.ToString("R", Inv); }

        public static bool TryParse(string s, out float v)
        {
            v = 0f;
            if (s == null)
                return false;
            s = s.Trim().Replace(',', '.');
            return float.TryParse(s, NumberStyles.Float, Inv, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public static int StepDecimals(float step)
        {
            int d = 0;
            double s = step;
            while (d < 6 && Math.Abs(s - Math.Round(s)) > 1e-4)
            {
                s *= 10;
                d++;
            }
            return d;
        }

        public static int Decimals(Item it)
        {
            return it.Decimals >= 0 ? Math.Min(it.Decimals, 6) : it.Step > 0 ? StepDecimals(it.Step) : -1;
        }

        public static float Normalize(Item it, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                v = it.DefNum;
            if (it.Step > 0)
            {
                float b = it.HasRange ? it.Min : 0f;
                v = b + (float)Math.Round((v - b) / it.Step) * it.Step;
            }
            if (it.HasRange)
                v = Math.Max(it.Min, Math.Min(it.Max, v));
            int d = Decimals(it);
            if (d >= 0)
                v = (float)Math.Round(v, d);
            return v;
        }

        public static string Show(Item it, float v)
        {
            int d = Decimals(it);
            return d >= 0 ? v.ToString("F" + d, Inv) : v.ToString("0.###", Inv);
        }
    }

    // Закладка окна (список слева). Title = null — «Основные настройки» (элементы, добавленные прямо в меню).
    public class Tab
    {
        public string Title;
        public object Info;                  // string, Func<string> или null
        public List<Item> Items;

        // { "tab", заголовок или null, info, items[] }
        public static List<Tab> ParseTabs(object[] desc, out string error)
        {
            error = null;
            var tabs = new List<Tab>();
            if (desc == null)
            {
                error = "нет описания меню";
                return null;
            }
            for (int i = 0; i < desc.Length; i++)
            {
                var a = desc[i] as object[];
                if (a == null || a.Length < 4 || (a[0] as string) != "tab")
                {
                    error = "закладка " + i + ": неверный формат";
                    return null;
                }
                var items = Item.ParseList(a[3] as object[], out error);
                if (items == null)
                {
                    error = "закладка " + i + ": " + error;
                    return null;
                }
                tabs.Add(new Tab { Title = a[1] as string, Info = a[2], Items = items });
            }
            return tabs;
        }
    }

    // Меню мода, зарегистрированное через SendModMessage.
    public class MenuEntry
    {
        public string Id, Name, Version;
        public List<Tab> Tabs;
        public object Info;                  // общий текст правой колонки: string, Func<string> или null

        // Все элементы всех закладок и групп.
        public IEnumerable<Item> All()
        {
            foreach (var t in Tabs)
                foreach (var it in Walk(t.Items))
                    yield return it;
        }

        static IEnumerable<Item> Walk(List<Item> items)
        {
            foreach (var it in items)
            {
                yield return it;
                if (it.Children != null)
                    foreach (var c in Walk(it.Children))
                        yield return c;
            }
        }

        public bool HasInfo
        {
            get
            {
                if (Info != null)
                    return true;
                foreach (var t in Tabs)
                    if (t.Info != null)
                        return true;
                return false;
            }
        }

        public Func<Dictionary<string, string>> GetValues;
        public Func<Dictionary<string, string>, string[]> Apply;
        public Action<string> Command;
        // Изменение в окне: (черновик окна, ключ изменённого | "" открытие | "*" несколько сразу | null опрос)
        // -> { Dictionary<string,string> новые значения черновика или null, string[] недоступные ключи, string[] скрытые ключи }.
        public Func<Dictionary<string, string>, string, object[]> Edit;
        public bool Custom;
    }
}
