using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace Fd32.ModUI
{
    // Окно настроек мода — как терминал: слева список закладок, посередине настройки закладки, справа текст мода
    // (Menu.Info / tab.Info). Панель — ванильная MyModScreen во весь экран (как в Ore config). Внизу «По умолчанию»,
    // «Отмена», «ОК». До «ОК» моду ничего не уходит; Esc, × и «Отмена» всё откатывают.
    // Крестик рисуем сами: ванильный — кнопка с фокусом, Enter и Пробел её нажимают (окно закрывалось по Enter).
    public class SettingsWindow : MyModScreen
    {
        static readonly RectangleF FullWin = new RectangleF(0.02f, 0.05f, 0.96f, 0.90f);
        // Раскладка: у окна настроек — постоянная, у окна ввода (форма) — по числу значений (FormLayout).
        readonly RectangleF Win;
        readonly float CaptionY, CaptionLine, ColTop, FooterTop, ColBottom, LX, RX;
        const float ListW = 0.20f, MidW = 0.42f, Gap = 0.015f, ScrollW = 0.008f;
        const float TabRowH = 0.038f, CtrlH = 0.04f, BtnW = 0.176f, BtnH = 0.052f, FieldW = 0.085f, Indent = 0.015f;
        static readonly MyStringId TexTriangle = MyStringId.GetOrCompute("Triangle");

        public readonly MenuEntry Menu;
        readonly Gx g = new Gx();
        readonly Dictionary<string, string> edit = new Dictionary<string, string>();
        readonly HashSet<string> touched = new HashSet<string>();
        readonly Dictionary<string, string> modValues = new Dictionary<string, string>();   // что действует в моде
        readonly HashSet<Item> collapsed = new HashSet<Item>();
        readonly HashSet<string> disabled = new HashSet<string>(), hidden = new HashSet<string>();   // решает мод (Menu.Editing)
        bool closedByOk;
        readonly List<Item> settings = new List<Item>();
        string errorKey;
        int frame, tab;

        // прокрутка средней и правой колонок
        readonly Scroller mid = new Scroller(), info = new Scroller();
        string infoText;
        int infoTab = -1;

        // поле ввода (число или текст)
        string fieldKey, fieldText;
        Item fieldItem;
        int caret, blink;
        int anchor;                         // второй конец выделения в поле; anchor == caret — выделения нет
        Drag fieldDrag;                     // протаскивание мышью по полю (выделение)
        readonly Dictionary<MyKeys, long> repeatAt = new Dictionary<MyKeys, long>();

        // раскрытый список выбора
        const float ItemH = 0.03f;           // строка списка, как ITEM_HEIGHT у ванили
        const int ListRows = 10;
        Item openChoice;
        RectangleF openHead;
        readonly Scroller list = new Scroller();

        // назначение горячей клавиши
        Item capture;
        readonly List<MyKeys> pressed = new List<MyKeys>();
        readonly Dictionary<string, string> conflicts = new Dictionary<string, string>();   // ключ -> где ещё занята

        // колонки
        readonly float midX, midW, infoX, infoW;
        readonly bool hasInfo;

        class Scroller
        {
            public float Offset, ContentH;
            public void Wheel(int delta, float viewH) { Set(Offset - Math.Sign(delta) * 0.06f, viewH); }
            public void Set(float v, float viewH) { Offset = Math.Max(0f, Math.Min(Math.Max(0f, ContentH - viewH), v)); }
        }

        // Окно ввода значений (Set-действие тулбара): null — обычное окно настроек.
        readonly FormRequest form;
        bool labelManual;                   // игрок сам правил подпись — по шаблону её больше не пересчитываем
        bool labelWarned;

        public SettingsWindow(MenuEntry menu) : this(menu, null, FullWin) { }

        public SettingsWindow(FormRequest request) : this(request.AsMenu(), request, FormLayout(request)) { }

        SettingsWindow(MenuEntry menu, FormRequest request, RectangleF win)
            : base("ModUI_Settings", new Vector2(win.X + win.Width / 2f, win.Y + win.Height / 2f), new Vector2(win.Width, win.Height), Vector4.One)
        {
            Menu = menu;
            form = request;
            Win = win;
            if (form == null)
            {
                CaptionY = 0.10f; CaptionLine = 0.13f; ColTop = 0.15f; FooterTop = 0.862f; ColBottom = 0.847f; LX = 0.045f; RX = 0.955f;
            }
            else
            {
                CaptionY = win.Y + FormCaptionY; CaptionLine = win.Y + FormCaptionLine; ColTop = win.Y + FormColTop;
                ColBottom = win.Y + win.Height - FormFooterH; FooterTop = ColBottom + 0.005f;
                LX = win.X + FormPad; RX = win.X + win.Width - FormPad;
            }
            CloseButtonEnabled = false;
            EnabledBackgroundFade = true;
            g.OnPress = keep =>
            {
                if (!keep)
                    CommitField();
            };
            foreach (var it in menu.All())
            {
                if (it.IsSetting)
                    settings.Add(it);
                if (it.Kind == "group" && it.Collapsible && it.Collapsed)
                    collapsed.Add(it);
            }
            hasInfo = form == null && menu.HasInfo;
            midX = form == null ? LX + ListW + Gap : LX;
            midW = hasInfo ? MidW : RX - midX;
            infoX = midX + midW + Gap;
            infoW = RX - infoX;
            Pull(true);
            CallEdit("");                   // моду: окно открыто (Opened, Editing с Changed = null)
            UpdateConflicts();
            if (form != null)
                FormOpened();
        }

        static RectangleF R(float x, float y, float w, float h) { return new RectangleF(x, y, w, h); }

        // ---- значения ----

        // Текущие значения мода; all = false — в окне меняются только строки, которые игрок не трогал.
        void Pull(bool all)
        {
            Dictionary<string, string> v = null;
            try
            {
                if (Menu.GetValues != null)
                    v = Menu.GetValues();
            }
            catch (Exception e)
            {
                Log.Write("GetValues " + Menu.Id + ": " + e.Message);
            }
            foreach (var it in settings)
            {
                string s;
                if (v == null || !v.TryGetValue(it.Key, out s) || !Valid(it, ref s))
                    s = it.DefaultString;
                modValues[it.Key] = s;
                if (all || !touched.Contains(it.Key))
                    edit[it.Key] = s;
            }
        }

        static bool Valid(Item it, ref string s)
        {
            if (it.Kind == "toggle")
                return s == "true" || s == "false";
            if (it.Kind == "choice")
                return Array.IndexOf(it.Options, s) >= 0;
            if (it.Kind == "text")
            {
                s = it.CleanText(s);
                return true;
            }
            if (it.Kind == "hotkey")
                return Hk.Normalize(ref s);
            float f;
            if (!Num.TryParse(s, out f))
                return false;
            s = Num.Str(Num.Normalize(it, f));
            return true;
        }

        bool B(Item it) { return edit[it.Key] == "true"; }

        float N(Item it)
        {
            float f;
            return Num.TryParse(edit[it.Key], out f) ? f : it.DefNum;
        }

        void Set(Item it, string s)
        {
            edit[it.Key] = s;
            touched.Add(it.Key);
            if (errorKey == it.Key)
                errorKey = null;
        }

        // Изменение игроком: в черновик и сразу моду (Menu.Editing), если значение правда другое.
        void Change(Item it, string s)
        {
            bool differs = edit[it.Key] != s;
            Set(it, s);
            if (form != null)
                FormChanged(it);
            if (differs)
                CallEdit(it.Key);
        }

        void SetNum(Item it, float v) { Change(it, Num.Str(Num.Normalize(it, v))); }

        // Отдать моду черновик окна и взять ответ: новые значения черновика, недоступные и скрытые элементы.
        // key: ключ изменённого, "" — открытие, "*" — несколько сразу («По умолчанию»), null — просто опрос состояния.
        void CallEdit(string key)
        {
            if (Menu.Edit == null)
                return;
            object[] r;
            try
            {
                r = Menu.Edit(new Dictionary<string, string>(edit), key);
            }
            catch (Exception e)
            {
                Log.Write("Edit " + Menu.Id + ": " + e.Message);
                return;
            }
            if (r == null)
                return;
            var values = r.Length > 0 ? r[0] as Dictionary<string, string> : null;
            if (values != null)
                foreach (var kv in values)
                {
                    var it = settings.Find(x => x.Key == kv.Key);
                    string s = kv.Value;
                    if (it != null && Valid(it, ref s))
                        Set(it, s);
                }
            disabled.Clear();
            hidden.Clear();
            var dis = r.Length > 1 ? r[1] as string[] : null;
            var hid = r.Length > 2 ? r[2] as string[] : null;
            if (dis != null)
                disabled.UnionWith(dis);
            if (hid != null)
                hidden.UnionWith(hid);
            if (fieldItem != null && !Usable(fieldItem))
            {
                fieldKey = null;            // поле стало недоступным — ввод отменяется
                fieldItem = null;
            }
            if (openChoice != null && !Usable(openChoice))
                openChoice = null;
            if (capture != null && !Usable(capture))
                capture = null;
        }

        // Элемент доступен: ни он, ни группы над ним не скрыты и не выключены модом.
        bool Usable(Item it)
        {
            foreach (var t in Menu.Tabs)
            {
                bool found;
                bool r = UsableIn(t.Items, it, true, out found);
                if (found)
                    return r;
            }
            return true;
        }

        bool UsableIn(List<Item> items, Item target, bool ok, out bool found)
        {
            found = false;
            foreach (var it in items)
            {
                bool here = ok && (it.Key == null || (!disabled.Contains(it.Key) && !hidden.Contains(it.Key)));
                if (it == target)
                {
                    found = true;
                    return here;
                }
                if (it.Children != null)
                {
                    bool r = UsableIn(it.Children, target, here, out found);
                    if (found)
                        return r;
                }
            }
            return false;
        }

        // Есть что применять: значение в окне отличается от действующего в моде.
        bool Changed
        {
            get
            {
                foreach (var kv in edit)
                {
                    string m;
                    if (!modValues.TryGetValue(kv.Key, out m) || m != kv.Value)
                        return true;
                }
                return false;
            }
        }

        // Есть что сбрасывать: хоть одно значение не по умолчанию.
        bool NotDefault
        {
            get
            {
                foreach (var it in settings)
                    if (edit[it.Key] != it.DefaultString)
                        return true;
                return false;
            }
        }

        // ---- поле ввода ----

        void StartField(Item it)
        {
            if (fieldKey == it.Key)
                return;
            CommitField();
            fieldKey = it.Key;
            fieldItem = it;
            fieldText = it.Kind == "number" ? Num.Show(it, N(it)) : edit[it.Key];
            caret = anchor = fieldText.Length;
            blink = 0;
        }

        void CommitField()
        {
            if (fieldKey == null)
                return;
            var it = fieldItem;
            float f;
            if (it.Kind == "text")
                Change(it, it.CleanText(fieldText));
            else if (Num.TryParse(fieldText, out f))
                SetNum(it, f);
            fieldKey = null;
            fieldItem = null;
        }

        // Щелчок по полю: начать ввод и поставить курсор под мышь; протаскивание выделяет, Shift + щелчок — выделить
        // до щелчка (как у ванили). shown — текст, который был виден в поле.
        void FieldTarget(Item it, RectangleF box, string shown, bool focus)
        {
            float origin = g.TextBoxOrigin(box, shown, focus, caret);
            g.Target(box, null, c =>
            {
                bool extend = fieldKey == it.Key && MyAPIGateway.Input.IsAnyShiftKeyPressed();
                StartField(it);
                caret = Gx.CaretAt(fieldText, origin, c.X);
                if (!extend)
                    anchor = caret;
                blink = 0;
                // Раскладка текста — та, что была при нажатии: иначе сдвиг длинного текста за курсором сам гонит выделение.
                float start = g.TextBoxOrigin(box, fieldText, true, caret);
                fieldDrag = new Drag
                {
                    Move = p =>
                    {
                        if (fieldKey != it.Key)
                            return;
                        int to = Gx.CaretAt(fieldText, start, p.X);
                        if (to != caret)
                            blink = 0;
                        caret = to;
                    }
                };
                return fieldDrag;
            }, it.Tip, true);
        }

        // Наведение на поле: пока по нему тянут мышью (выделение), подсветка не гаснет — как у ванили.
        bool FieldHover(RectangleF box)
        {
            if (g.Dragging != null && g.Dragging == fieldDrag)
                return g.HasCursor && Gx.Inside(box, g.Cursor);
            return g.Hover(box);
        }

        int SelStart { get { return Math.Min(anchor, caret); } }
        int SelEnd { get { return Math.Max(anchor, caret); } }
        bool HasSelection { get { return anchor != caret; } }

        // Курсор в позицию; select — Shift: второй конец выделения остаётся на месте.
        void MoveCaret(int to, bool select)
        {
            caret = Math.Max(0, Math.Min(fieldText.Length, to));
            if (!select)
                anchor = caret;
            blink = 0;
        }

        void DeleteSelection()
        {
            int a = SelStart, b = SelEnd;
            fieldText = fieldText.Remove(a, b - a);
            caret = anchor = a;
            blink = 0;
        }

        // Вставить строку вместо выделения; лишние символы (сверх длины, не цифры в числе) отбрасываются.
        void InsertText(string add)
        {
            bool text = fieldItem.Kind == "text";
            int max = text ? fieldItem.MaxLength : 16;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in add)
                if (!char.IsControl(ch) && (text || char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-'))
                    sb.Append(ch);
            if (sb.Length == 0)
                return;
            if (HasSelection)
                DeleteSelection();
            int room = max - fieldText.Length;
            if (room <= 0)
                return;
            string ins = sb.Length > room ? sb.ToString(0, room) : sb.ToString();
            fieldText = fieldText.Insert(caret, ins);
            caret = anchor = caret + ins.Length;
            blink = 0;
        }

        // Слова — как у ванили (MyGuiControlTextbox.GetPreviousSpace / GetNextSpace): граница — только пробел.
        int PrevSpace()
        {
            if (caret == 0)
                return 0;
            int i = fieldText.Substring(0, caret).LastIndexOf(' ');
            return i < 0 ? 0 : i;
        }

        int NextSpace()
        {
            if (caret >= fieldText.Length)
                return fieldText.Length;
            int i = fieldText.Substring(caret + 1).IndexOf(' ');
            return i < 0 ? fieldText.Length : caret + i + 1;
        }

        // Клавиша нажата или держится: сразу, через 0.5 с и дальше каждые 30 мс (как MyKeyThrottler ванили с
        // настройками Windows по умолчанию; сами настройки модам не прочитать).
        bool Repeat(VRage.ModAPI.IMyInput input, MyKeys k)
        {
            if (!input.IsKeyPress(k))
                return false;
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (input.IsNewKeyPressed(k))
            {
                repeatAt[k] = now + 500;
                return true;
            }
            long t;
            if (repeatAt.TryGetValue(k, out t) && now >= t)
            {
                repeatAt[k] = now + 30;
                return true;
            }
            return false;
        }

        // Ввод в поле — как у ванильного поля: Shift + стрелки / Home / End и мышь выделяют, Ctrl + стрелки — по словам,
        // Ctrl+Backspace / Ctrl+Delete стирают слово, Ctrl+A / C / X; набранное заменяет выделенное. Ctrl+C без выделения
        // ничего не копирует (как у ванили). Ctrl+V нет: читать буфер обмена моды не могут (MyVRage закрыт whitelist,
        // проверено в игре 2026-11-20). Enter только принимает введённое
        // (окно не закрывает и «ОК» не нажимает).
        void FieldInput()
        {
            var input = MyAPIGateway.Input;
            bool ctrl = input.IsAnyCtrlKeyPressed(), shift = input.IsAnyShiftKeyPressed();
            if (ctrl && input.IsNewKeyPressed(MyKeys.A))
            {
                anchor = 0;
                caret = fieldText.Length;
                blink = 0;
            }
            if (ctrl && (input.IsNewKeyPressed(MyKeys.C) || input.IsNewKeyPressed(MyKeys.X)) && HasSelection)
            {
                MyClipboardHelper.SetClipboard(fieldText.Substring(SelStart, SelEnd - SelStart));
                if (input.IsNewKeyPressed(MyKeys.X))
                    DeleteSelection();
            }
            if (!ctrl || input.IsAnyAltKeyPressed())        // Ctrl+буква — не текст; Ctrl+Alt (AltGr) печатает символы
            {
                var chars = input.TextInput;
                for (int i = 0; i < chars.Count; i++)
                {
                    char ch = chars[i];
                    if (ch == '\b')
                    {
                        if (HasSelection)
                            DeleteSelection();
                        else if (caret > 0)
                        {
                            fieldText = fieldText.Remove(caret - 1, 1);
                            caret = anchor = caret - 1;
                        }
                    }
                    else if (ch == '\r' || ch == '\n')
                    {
                        var from = fieldItem;
                        CommitField();
                        if (form != null)
                            FormEnter(from);  // окно ввода: следующее поле, на последнем — «ОК»
                        return;
                    }
                    else if (!char.IsControl(ch))
                        InsertText(ch.ToString());
                    blink = 0;
                }
            }
            if (Repeat(input, MyKeys.Left))
                MoveCaret(ctrl ? PrevSpace() : caret - 1, shift);
            if (Repeat(input, MyKeys.Right))
                MoveCaret(ctrl ? NextSpace() : caret + 1, shift);
            if (input.IsNewKeyPressed(MyKeys.Home))
                MoveCaret(0, shift);
            if (input.IsNewKeyPressed(MyKeys.End))
                MoveCaret(fieldText.Length, shift);
            if (ctrl && Repeat(input, MyKeys.Back))
            {
                if (!HasSelection)
                    anchor = PrevSpace();
                DeleteSelection();
            }
            if (Repeat(input, MyKeys.Delete))
            {
                if (!HasSelection)
                    anchor = ctrl ? NextSpace() : Math.Min(fieldText.Length, caret + 1);
                DeleteSelection();
            }
        }

        // ---- кнопки внизу ----

        void Defaults()
        {
            fieldKey = null;
            fieldItem = null;
            foreach (var it in settings)
                Set(it, it.DefaultString);
            errorKey = null;
            CallEdit("*");
        }

        void Ok()
        {
            CommitField();
            if (form != null)
            {
                FormOk();
                return;
            }
            if (!Changed)
                return;
            string[] result;
            try
            {
                result = Menu.Apply != null ? Menu.Apply(new Dictionary<string, string>(edit)) : null;
            }
            catch (Exception e)
            {
                Log.Write("Apply " + Menu.Id + ": " + e);
                new MessageWindow(Loc.ErrorCaption, Loc.ModFailed).Open();
                return;
            }
            if (result == null || result.Length == 0 || result[0] == null)
            {
                closedByOk = true;
                CloseNow();
                return;
            }
            errorKey = result.Length > 1 ? result[1] : null;
            new MessageWindow(Loc.ErrorCaption, result[0]).Open();
        }

        void SelectTab(int i)
        {
            if (i == tab)
                return;
            tab = i;
            mid.Offset = 0f;
            info.Offset = 0f;
        }

        // ---- окно ввода значений (Set-действие тулбара) ----

        const float FormW = 0.5f, FormPad = 0.025f, FormCaptionY = 0.045f, FormCaptionLine = 0.075f, FormColTop = 0.09f;
        const float FormBtnW = 0.176f * 0.75f, FormBtnH = 0.052f * 0.85f;
        const float FormFooterH = 0.005f + 0.013f + FormBtnH + 0.025f;

        // Размер окна ввода: ширина как у ванильного «Количества», высота — по значениям (не выше 0.9, дальше прокрутка).
        static RectangleF FormLayout(FormRequest f)
        {
            float content = 0.006f + 0.01f;
            foreach (var it in f.Items)
                content += FormItemHeight(it);
            float h = Math.Min(0.9f, FormColTop + content + FormFooterH);
            return new RectangleF(0.5f - FormW / 2f, 0.5f - h / 2f, FormW, h);
        }

        // Высота строки — как её рисует DrawItemBody.
        static float FormItemHeight(Item it)
        {
            switch (it.Kind)
            {
                case "separator": return 0.02f;
                case "toggle": return 0.051f;
                case "number":
                case "choice":
                case "text": return 0.03f + CtrlH + 0.014f;
            }
            return 0.04f;
        }

        // Поля ввода по порядку (Enter переходит по ним); подпись — не в цепочке.
        List<Item> FormFields()
        {
            var r = new List<Item>();
            foreach (var it in form.Values)
                if ((it.Kind == "number" && (it.Parts & Item.PartField) != 0) || it.Kind == "text")
                    r.Add(it);
            return r;
        }

        void FocusField(Item it)
        {
            StartField(it);
            anchor = 0;                     // всё выделено — набранное заменит значение (как у ванили)
            caret = fieldText.Length;
        }

        void FormOpened()
        {
            if (form.AskLabel)
                edit[FormRequest.LabelKey] = FormLabel();
            var fields = FormFields();
            if (fields.Count > 0)
                FocusField(fields[0]);
        }

        // Enter в поле: следующее поле ввода; на последнем (и в подписи) — «ОК».
        void FormEnter(Item from)
        {
            if (from != null && from.Key != FormRequest.LabelKey)
            {
                var fields = FormFields();
                int i = fields.IndexOf(from);
                if (i >= 0 && i + 1 < fields.Count)
                {
                    FocusField(fields[i + 1]);
                    return;
                }
            }
            Ok();
        }

        // Игрок поменял значение: подпись пересчитывается по шаблону, пока её не правили руками.
        void FormChanged(Item it)
        {
            if (!form.AskLabel)
                return;
            if (it.Key == FormRequest.LabelKey)
                labelManual = edit[FormRequest.LabelKey] != FormLabel();
            else if (!labelManual)
                edit[FormRequest.LabelKey] = FormLabel();
        }

        // Подпись по шаблону мода ({0}, {1}… — значения по порядку) или, без шаблона, значения через пробел (число с единицей).
        // Не длиннее 9 символов — дальше игра всё равно обрежет.
        string FormLabel()
        {
            var parts = new string[form.Values.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                var it = form.Values[i];
                string s = edit[it.Key];
                if (it.Kind == "number")
                {
                    s = Num.Show(it, N(it));
                    if (form.Template == null && !string.IsNullOrEmpty(it.Unit))
                        s += " " + it.Unit;
                }
                else if (it.Kind == "choice")
                    s = it.OptionLabel(s);
                parts[i] = s ?? "";
            }
            string label;
            if (form.Template == null)
                label = string.Join(" ", parts);
            else
            {
                try
                {
                    label = string.Format(form.Template, parts);
                }
                catch (FormatException)
                {
                    if (!labelWarned)
                        Log.Write(form.Owner + ": неверный шаблон подписи \"" + form.Template + "\" — показан как есть");
                    labelWarned = true;
                    label = form.Template;
                }
            }
            label = label.Replace("\r", "").Replace('\n', ' ');
            if (label.Length > FormRequest.LabelMax)
            {
                if (form.Template != null && !labelWarned)
                    Log.Write(form.Owner + ": подпись \"" + label + "\" длиннее " + FormRequest.LabelMax + " символов — обрезана (предел игры)");
                labelWarned = true;
                label = label.Substring(0, FormRequest.LabelMax);
            }
            return label;
        }

        // «ОК»: проверка мода (отказ — сообщение, окно остаётся), потом значения и подпись — моду.
        void FormOk()
        {
            var values = new Dictionary<string, string>();
            foreach (var it in form.Values)
                values[it.Key] = edit[it.Key];
            string label = form.AskLabel ? edit[FormRequest.LabelKey] : FormLabel();
            string[] result;
            try
            {
                result = form.Check != null ? form.Check(new Dictionary<string, string>(values)) : null;
            }
            catch (Exception e)
            {
                Log.Write("проверка " + form.Owner + ": " + e);
                new MessageWindow(Loc.ErrorCaption, Loc.ModFailed).Open();
                return;
            }
            if (result != null && result.Length > 0 && result[0] != null)
            {
                errorKey = result.Length > 1 ? result[1] : null;
                new MessageWindow(Loc.ErrorCaption, result[0]).Open();
                return;
            }
            closedByOk = true;
            CloseNow();
            form.SafeOk(values, label);
        }

        // ---- MyModScreen ----

        protected override void OnClosed(bool isUnloading)
        {
            if (form != null)
            {
                if (!closedByOk && !isUnloading)
                    form.SafeCancel();
                return;
            }
            if (!isUnloading)
                HubSession.SafeCommand(Menu, closedByOk ? "closed:ok" : "closed:cancel");
        }

        protected override bool OnCancelling()
        {
            if (capture != null)
            {
                capture = null;             // Esc при назначении клавиши — отмена назначения
                return false;
            }
            if (openChoice != null)
            {
                openChoice = null;          // Esc в раскрытом списке — закрыть только список
                return false;
            }
            if (fieldKey != null)
            {
                fieldKey = null;            // Esc в поле — отменить ввод, окно не закрывать
                fieldItem = null;
                return false;
            }
            return true;
        }

        protected override void OnHandleInput()
        {
            DrawMouseCursor = capture == null;      // пока ждём клавишу, курсора нет — как у ванили
            if (capture != null)
            {
                CaptureInput();
                return;
            }
            bool hadField = fieldKey != null;
            if (hadField)
                FieldInput();
            if (!IsOpen)
                return;                     // Enter в последнем поле нажал «ОК» и закрыл окно
            // окно ввода: Enter вне поля — «ОК», как у ванильного окна «Количество»
            if (form != null && !hadField && openChoice == null && IsOpen && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Enter))
            {
                Ok();
                return;
            }
            g.Input();
        }

        protected override void OnDraw(float alpha)
        {
            frame++;
            blink++;
            if (frame % 30 == 0 && form == null)
            {
                Pull(false);                // мод мог сам поменять значения,
                CallEdit(null);             // доступность и видимость
                UpdateConflicts();
                infoTab = -1;               // и текст справа (Func<string>)
            }
            DrawMouseCursor = capture == null;
            g.Begin(alpha);
            g.Blocked = openChoice != null || capture != null;
            g.Text(Menu.Name, 0.5f, CaptionY, 0.8f, Color.White, Gx.Center, Win.Width - (form == null ? 0.2f : 0.08f), "ScreenCaption");
            if (form == null)
                g.Text(Menu.Version, LX, CaptionY, 0.6f, Gx.DimColor, Gx.LeftCenter);
            g.HLine(LX, CaptionLine, RX - LX, Gx.Line);

            if (form == null)
                DrawTabs();
            DrawMiddle();
            if (hasInfo)
                DrawInfo();

            // низ
            g.HLine(LX, FooterTop, RX - LX, Gx.Line);
            float by = FooterTop + 0.013f;
            if (form == null)
            {
                float sx = RX - BtnW, cx = sx - 0.008f - BtnW;
                g.Button(R(sx, by, BtnW, BtnH), Loc.Ok, Ok, false, Changed || fieldKey != null);
                g.Button(R(cx, by, BtnW, BtnH), Loc.Cancel, () => CloseNow());
                g.Button(R(LX, by, BtnW, BtnH), Loc.Defaults, Defaults, false, NotDefault, Gx.TextScale, Loc.DefaultsTip);
            }
            else
            {
                // как у ванильного окна «Количество»: «ОК» слева от середины, «Отмена» справа
                g.Button(R(0.5f - 0.004f - FormBtnW, by, FormBtnW, FormBtnH), Loc.Ok, Ok);
                g.Button(R(0.5f + 0.004f, by, FormBtnW, FormBtnH), Loc.Cancel, () => CloseNow());
            }

            // крестик как у ванили: стиль Close 34x31 px, правый верхний угол окна + (-0.013, 0.015)
            const float xw = 34f / 1600f, xh = 31f / 1200f;
            g.IconButton(R(Win.X + Win.Width - 0.013f - xw, Win.Y + 0.015f, xw, xh), Gx.TexClose, Gx.TexCloseHover, () => CloseNow());

            g.Blocked = false;
            if (openChoice != null)
                DrawChoiceList();
            if (capture != null)
                DrawCapture();

            g.DrawTooltip(Win.X + Win.Width, Win.Y + Win.Height);
            g.End();
        }

        // ---- слева: закладки (как список блоков терминала) ----

        void DrawTabs()
        {
            var box = R(LX, ColTop, ListW, ColBottom - ColTop);
            g.Panel(box);
            float y = box.Y + 0.004f;
            for (int i = 0; i < Menu.Tabs.Count; i++)
            {
                var row = R(box.X + 0.003f, y, box.Width - 0.006f, TabRowH);
                bool hover = g.Hover(row), sel = i == tab;
                if (sel)
                    g.Rect(row, Gx.RowSelected);
                else if (hover)
                    g.Rect(row, Gx.RowHover);
                string title = Menu.Tabs[i].Title ?? Loc.MainTab;
                g.Text(Gx.Fit(title, Gx.TextScale, row.Width - 0.016f), row.X + 0.008f, row.Y + row.Height / 2f, Gx.TextScale,
                       Gx.TextColor, Gx.LeftCenter, float.PositiveInfinity, sel || hover ? Gx.FontHover : Gx.Font);
                int index = i;
                g.Target(row, () => SelectTab(index));
                y += TabRowH;
            }
        }

        // ---- посередине: настройки закладки ----

        void DrawMiddle()
        {
            var view = R(midX, ColTop, midW, ColBottom - ColTop);
            float contentW = midW - ScrollW - 0.012f;
            g.Target(view, null, null, null, false, d => mid.Wheel(d, view.Height));
            var items = tab < Menu.Tabs.Count ? Menu.Tabs[tab].Items : new List<Item>();
            float y;
            using (g.Clip(view))
            {
                y = view.Y + 0.006f - mid.Offset;
                foreach (var it in items)
                    y = DrawItem(it, midX, contentW, y);
                if (settings.Count == 0 && items.Count == 0)
                    g.Text(Loc.Empty, midX, y, Gx.TextScale, Gx.DimColor);
            }
            mid.ContentH = y + mid.Offset - view.Y + 0.01f;
            mid.Set(mid.Offset, view.Height);
            DrawScrollBar(mid, R(view.X + view.Width - ScrollW, view.Y, ScrollW, view.Height));
        }

        // Скрытые модом элементы не рисуются и места не занимают; недоступные — серые и не нажимаются (вместе с содержимым группы).
        float DrawItem(Item it, float x, float w, float y)
        {
            if (it.Key != null && hidden.Contains(it.Key))
                return y;
            bool was = g.Disabled;
            if (it.Key != null && disabled.Contains(it.Key))
                g.Disabled = true;
            try
            {
                return DrawItemBody(it, x, w, y);
            }
            finally
            {
                g.Disabled = was;
            }
        }

        float DrawItemBody(Item it, float x, float w, float y)
        {
            switch (it.Kind)
            {
                case "separator":
                    g.HLine(x, y + 0.01f, w, Gx.Line);
                    return y + 0.02f;
                case "label":
                {
                    string text = Gx.Wrap(it.Label ?? "", Gx.TipScale, w);
                    g.Lines(text, x, y, Gx.TipScale, Gx.DimColor);
                    return y + text.Split('\n').Length * Gx.LineHeight(Gx.TipScale) + 0.012f;
                }
                case "button":
                {
                    var key = it.Key;
                    g.Button(R(x, y, w, BtnH * 0.85f), it.Label, () => HubSession.SafeCommand(Menu, "button:" + key), false, true,
                             Gx.TextScale, it.Tip);
                    return y + BtnH * 0.85f + 0.012f;
                }
                case "group":
                    return DrawGroup(it, x, w, y);
                case "toggle":
                    return DrawToggle(it, x, w, y);
                case "number":
                    return DrawNumber(it, x, w, y);
                case "choice":
                    return DrawChoice(it, x, w, y);
                case "text":
                    return DrawTextField(it, x, w, y);
                case "hotkey":
                    return DrawHotkey(it, x, w, y);
            }
            return y;
        }

        float DrawGroup(Item it, float x, float w, float y)
        {
            const float h = 0.036f;
            bool open = !collapsed.Contains(it);
            var head = R(x, y, w, h);
            bool hover = it.Collapsible && g.Hover(head);
            float tx = x;
            if (it.Collapsible)
            {
                // треугольник: вниз — раскрыта, вправо — свёрнута (LCD-текстура Triangle смотрит вверх)
                float s = 0.011f;
                MyScreenSpaceSprite.Draw(TexTriangle, new Vector2(x + s * 0.75f / 2f + 0.002f, y + h / 2f), new Vector2(s * 0.75f, s),
                                         (hover ? Color.White : Gx.DimColor) * g.Fade, Gx.Center, open ? MathHelper.Pi : MathHelper.PiOver2, g.Layer);
                tx += 0.016f;
                g.Target(head, () =>
                {
                    if (!collapsed.Remove(it))
                        collapsed.Add(it);
                });
            }
            g.Text(Gx.Fit(it.Label, Gx.TextScale, w - (tx - x)), tx, y + h / 2f, Gx.TextScale, Gx.TextColor, Gx.LeftCenter,
                   float.PositiveInfinity, Gx.FontHover);
            g.HLine(x, y + h - 0.002f, w, Gx.Line);
            y += h + 0.008f;
            if (open && it.Children != null)
                foreach (var c in it.Children)
                    y = DrawItem(c, x + Indent, w - Indent, y);
            return y + 0.006f;
        }

        void DrawLabel(Item it, float x, float w, float cy, RectangleF tipArea)
        {
            g.Text(Gx.Fit(it.Label, Gx.TextScale, w), x, cy, Gx.TextScale, Gx.TextColor, Gx.LeftCenter, float.PositiveInfinity,
                   errorKey == it.Key ? "Red" : Gx.Font);
        }

        // Галочка: подпись слева, квадрат справа; щёлкнуть можно по всей строке.
        float DrawToggle(Item it, float x, float w, float y)
        {
            const float h = 0.045f;
            float bh = 54f / 1200f * 0.85f, bw = 57f / 1600f * 0.85f;
            var row = R(x, y, w, h);
            var box = R(x + w - bw, y + (h - bh) / 2f, bw, bh);
            bool hover = g.Hover(row);
            DrawLabel(it, x, w - bw - 0.01f, y + h / 2f, row);
            g.Checkbox(box, B(it), hover);
            g.Target(row, () => Change(it, B(it) ? "false" : "true"), null, it.Tip);
            return y + h + 0.006f;
        }

        // Число: строка подписи (значение справа, если нет поля), под ней [−] ползунок [поле] [+] единица.
        float DrawNumber(Item it, float x, float w, float y)
        {
            const float lineH = 0.03f;
            bool slider = (it.Parts & Item.PartSlider) != 0, field = (it.Parts & Item.PartField) != 0,
                 buttons = (it.Parts & Item.PartButtons) != 0;
            float v = N(it);
            string unit = it.Unit ?? "";
            string valueText = field ? "" : Num.Show(it, v) + (unit.Length > 0 ? " " + unit : "");
            float vw = valueText.Length > 0 ? Gx.TextWidth(valueText, Gx.TextScale) + 0.01f : 0f;
            DrawLabel(it, x, w - vw, y + lineH / 2f, R(x, y, w, lineH));
            if (vw > 0)
                g.Text(valueText, x + w, y + lineH / 2f, Gx.TextScale, Gx.TextColor, Gx.RightCenter);
            if (!string.IsNullOrEmpty(it.Tip))
                g.Target(R(x, y, w, lineH), null, null, it.Tip);

            float cy = y + lineH;
            float sq = CtrlH * g.Px.X / g.Px.Y;              // квадрат на экране
            float unitW = field && unit.Length > 0 ? Gx.TextWidth(unit, Gx.TextScale) + 0.01f : 0f;
            float cx = x, right = x + w - unitW - (buttons ? sq + 0.005f : 0f);
            if (buttons)
            {
                g.IconButton(R(cx, cy, sq, CtrlH), Gx.TexDecrease, Gx.TexDecreaseHover, () => SetNum(it, N(it) - StepOf(it)),
                             !it.HasRange || v > it.Min, it.Tip);
                cx += sq + 0.005f;
            }
            if (slider)
            {
                float sw = right - cx - (field ? FieldW + 0.006f : 0f);
                var rail = R(cx, cy, sw, CtrlH);
                float span = Math.Max(1e-6f, it.Max - it.Min);
                g.SliderBar(rail, (v - it.Min) / span, g.Hover(rail));
                Action<Vector2> move = c => SetNum(it, it.Min + g.SliderT(rail, c.X) * span);
                g.Target(rail, null, c =>
                {
                    move(c);
                    return new Drag { Move = move };
                }, it.Tip);
                cx += sw + 0.006f;
            }
            if (field)
            {
                var box = R(cx, cy, slider ? FieldW : right - cx, CtrlH);
                bool focus = fieldKey == it.Key;
                string shown = focus ? fieldText : Num.Show(it, v);
                g.TextBox(box, shown, FieldHover(box), focus, caret, blink % 60 < 45, selStart: focus ? SelStart : 0, selEnd: focus ? SelEnd : 0);
                FieldTarget(it, box, shown, focus);
                cx = box.X + box.Width + 0.005f;
            }
            if (buttons)
            {
                g.IconButton(R(cx, cy, sq, CtrlH), Gx.TexIncrease, Gx.TexIncreaseHover, () => SetNum(it, N(it) + StepOf(it)),
                             !it.HasRange || v < it.Max, it.Tip);
            }
            if (unitW > 0)
                g.Text(unit, x + w, cy + CtrlH / 2f, Gx.TextScale, Gx.TextColor, Gx.RightCenter);
            return cy + CtrlH + 0.014f;
        }

        // Строка подписи над контролом (выбор, текст): подпись слева, подсказка при наведении.
        float LabelLine(Item it, float x, float w, float y)
        {
            const float lineH = 0.03f;
            DrawLabel(it, x, w, y + lineH / 2f, R(x, y, w, lineH));
            if (!string.IsNullOrEmpty(it.Tip))
                g.Target(R(x, y, w, lineH), null, null, it.Tip);
            return y + lineH;
        }

        // Выбор: подпись строкой, под ней ванильный комбобокс; щелчок раскрывает список (рисуется поверх всего, DrawChoiceList).
        float DrawChoice(Item it, float x, float w, float y)
        {
            float cy = LabelLine(it, x, w, y);
            var box = R(x, cy, w, CtrlH);
            bool open = openChoice == it;
            if (open)
                openHead = box;
            g.Combo(box, it.OptionLabel(edit[it.Key]), g.Hover(box), open);
            g.Target(box, () => OpenChoice(it, box), null, it.Tip);
            return cy + CtrlH + 0.014f;
        }

        void OpenChoice(Item it, RectangleF head)
        {
            if (it.Options.Length == 0)
                return;
            openChoice = it;
            openHead = head;
            float view = Math.Min(it.Options.Length, ListRows) * ItemH;
            list.ContentH = it.Options.Length * ItemH;
            int i = Math.Max(0, Array.IndexOf(it.Options, edit[it.Key]));
            list.Set(i * ItemH - (view - ItemH) / 2f, view);          // текущий вариант — посередине
        }

        // Раскрытый список (как у ванили): фон и рамка scrollable_list_border, строки 0.03, до 10 строк, дальше прокрутка;
        // вниз от комбобокса, а если до низа колонки не помещается — вверх. Щелчок мимо закрывает список и больше ничего не делает.
        void DrawChoiceList()
        {
            var it = openChoice;
            int n = it.Options.Length, rows = Math.Min(n, ListRows);
            float pad = 4f / 1200f;
            float h = rows * ItemH + 2 * pad;
            var head = openHead;
            bool up = head.Y + head.Height + h > ColBottom && head.Y - h >= Win.Y;
            var box = up ? R(head.X, head.Y - h, head.Width, h) : R(head.X, head.Y + head.Height, head.Width, h);

            g.Target(R(-1f, -1f, 3f, 3f), () => openChoice = null, null, null, false, d => { });
            g.Target(head, () => openChoice = null);
            g.Rect(box, Gx.ButtonBg);
            g.Rect(box.X, box.Y, g.Px.X, box.Height, Gx.ListBorder);
            g.Rect(box.X + box.Width - g.Px.X, box.Y, g.Px.X, box.Height, Gx.ListBorder);
            g.Rect(box.X, up ? box.Y : box.Y + box.Height - g.Px.Y, box.Width, g.Px.Y, Gx.ListBorder);

            var view = R(box.X, box.Y + pad, box.Width, rows * ItemH);
            bool scroll = n > rows;
            list.ContentH = n * ItemH;
            list.Set(list.Offset, view.Height);
            g.Target(view, null, null, null, false, d => list.Set(list.Offset - Math.Sign(d) * ItemH, view.Height));
            float rowW = view.Width - 0.006f - (scroll ? ScrollW + 0.004f : 0f);
            string current = edit[it.Key];
            using (g.Clip(view))
            {
                float y = view.Y - list.Offset;
                for (int i = 0; i < n; i++, y += ItemH)
                {
                    if (y + ItemH < view.Y || y > view.Y + view.Height)
                        continue;
                    var row = R(view.X + 0.003f, y, rowW, ItemH);
                    bool hover = g.Hover(row);
                    if (hover)
                        g.Rect(row, Gx.RowHover);
                    string label = it.OptionLabel(it.Options[i]);
                    g.Text(Gx.Fit(label, Gx.ComboScale, rowW - 0.01f), view.X + 0.01f, y + ItemH / 2f, Gx.ComboScale, Gx.TextColor,
                           Gx.LeftCenter, float.PositiveInfinity, hover || it.Options[i] == current ? Gx.FontHover : Gx.Font);
                    string key = it.Options[i];
                    g.Target(row, () =>
                    {
                        openChoice = null;
                        Change(it, key);
                    });
                }
            }
            if (scroll)
                DrawScrollBar(list, R(box.X + box.Width - ScrollW - 0.003f, view.Y, ScrollW, view.Height));
        }

        // Текст: подпись строкой, под ней ванильное поле во всю ширину; пустое — серая подсказка мода.
        float DrawTextField(Item it, float x, float w, float y)
        {
            float cy = LabelLine(it, x, w, y);
            var box = R(x, cy, w, CtrlH);
            bool focus = fieldKey == it.Key;
            string shown = focus ? fieldText : edit[it.Key];
            g.TextBox(box, shown, FieldHover(box), focus, caret, blink % 60 < 45, false, it.Placeholder, focus ? SelStart : 0, focus ? SelEnd : 0);
            FieldTarget(it, box, shown, focus);
            return cy + CtrlH + 0.014f;
        }

        // ---- горячая клавиша ----

        // Как в ванильном «Управлении»: название слева, справа кнопка с клавишей. Занятая где-то ещё — красная, в подсказке где.
        float DrawHotkey(Item it, float x, float w, float y)
        {
            const float h = 0.045f;
            float bw = w * 0.4f;
            var btn = R(x + w - bw, y + (h - CtrlH) / 2f, bw, CtrlH);
            DrawLabel(it, x, w - bw - 0.01f, y + h / 2f, R(x, y, w, h));
            if (!string.IsNullOrEmpty(it.Tip))
                g.Target(R(x, y, w - bw - 0.01f, h), null, null, it.Tip);
            string conflict;
            conflicts.TryGetValue(it.Key, out conflict);
            bool cap = capture == it;
            string tip = conflict == null ? it.Tip : (string.IsNullOrEmpty(it.Tip) ? "" : it.Tip + "\n") + Loc.KeyBusy + conflict;
            g.Button(btn, cap ? Loc.PressKey : Hk.Display(edit[it.Key]), () => capture = it, cap, true, Gx.TextScale, tip, false,
                     conflict != null && !cap ? "Red" : null);
            return y + h + 0.006f;
        }

        // Ждём клавишу: обычная — сразу (с зажатыми Ctrl / Shift / Alt); сам модификатор — когда его отпустили, ничего не нажав.
        // Delete — очистить, Esc (OnCancelling) — отмена. Кнопки мыши назначаются все, и левая с правой (как в ванильном
        // «Управлении»); курсор, пока ждём, спрятан. Щелчок, которым открыли ожидание, — не новое нажатие и не считается.
        void CaptureInput()
        {
            var input = MyAPIGateway.Input;
            var it = capture;
            if (input.IsNewKeyPressed(MyKeys.Delete))
            {
                capture = null;
                Change(it, "");
                UpdateConflicts();
                return;
            }
            bool c = input.IsAnyCtrlKeyPressed(), s = input.IsAnyShiftKeyPressed(), a = input.IsAnyAltKeyPressed();
            MyKeys got = MyKeys.None;
            if (input.IsNewLeftMousePressed()) got = MyKeys.LeftButton;
            else if (input.IsNewRightMousePressed()) got = MyKeys.RightButton;
            else if (input.IsNewMiddleMousePressed()) got = MyKeys.MiddleButton;
            else if (input.IsNewXButton1MousePressed()) got = MyKeys.ExtraButton1;
            else if (input.IsNewXButton2MousePressed()) got = MyKeys.ExtraButton2;
            else
            {
                pressed.Clear();
                input.GetPressedKeys(pressed);
                foreach (var k in pressed)
                    if (k != MyKeys.None && k != MyKeys.LeftButton && k != MyKeys.RightButton && k != MyKeys.Escape &&
                        !Hk.IsModifier(k) && input.IsNewKeyPressed(k))
                    {
                        got = k;
                        break;
                    }
            }
            if (got == MyKeys.None)
            {
                if (Released(input, MyKeys.Control, MyKeys.LeftControl, MyKeys.RightControl)) got = MyKeys.Control;
                else if (Released(input, MyKeys.Shift, MyKeys.LeftShift, MyKeys.RightShift)) got = MyKeys.Shift;
                else if (Released(input, MyKeys.Alt, MyKeys.LeftAlt, MyKeys.RightAlt)) got = MyKeys.Alt;
            }
            if (got == MyKeys.None)
                return;
            capture = null;
            Change(it, Hk.Format(got, c, s, a));
            UpdateConflicts();
        }

        static bool Released(VRage.ModAPI.IMyInput input, MyKeys a, MyKeys b, MyKeys c)
        {
            return input.IsNewKeyReleased(a) || input.IsNewKeyReleased(b) || input.IsNewKeyReleased(c);
        }

        // Надпись поверх окна, пока ждём клавишу.
        void DrawCapture()
        {
            g.Target(R(-1f, -1f, 3f, 3f), null, null, null, false, d => { });
            float w = 0.4f, h = 0.12f;
            var box = R(0.5f - w / 2f, 0.5f - h / 2f, w, h);
            g.Panel(box);
            g.Text(Loc.PressKeyFor, 0.5f, box.Y + 0.03f, Gx.TextScale, Gx.TextColor, Gx.Center, w - 0.02f);
            g.Text("«" + capture.Label + "»", 0.5f, box.Y + 0.06f, Gx.TextScale, Gx.TextColor, Gx.Center, w - 0.02f, Gx.FontHover);
            g.Text(Loc.PressKeyHint, 0.5f, box.Y + 0.092f, Gx.TipScale, Gx.DimColor, Gx.Center, w - 0.02f);
        }

        // Где ещё занята каждая клавиша окна: управление игры, другие клавиши этого мода (в окне), клавиши других модов центра.
        // Только у клавиш, где мод включил .CheckConflicts(); сравниваются со всеми клавишами (и без флага).
        void UpdateConflicts()
        {
            conflicts.Clear();
            var mine = settings.FindAll(x => x.Kind == "hotkey");
            if (!mine.Exists(x => x.CheckConflicts))
                return;
            var others = new List<KeyValuePair<string, string>>();
            var hub = HubSession.Instance;
            if (hub != null)
                foreach (var m in hub.Menus)
                {
                    if (m == Menu || m.Tabs == null)
                        continue;
                    Dictionary<string, string> v = null;
                    try
                    {
                        if (m.GetValues != null)
                            v = m.GetValues();
                    }
                    catch (Exception) { }
                    foreach (var it in m.All())
                    {
                        if (it.Kind != "hotkey")
                            continue;
                        string s;
                        if (v == null || !v.TryGetValue(it.Key, out s) || !Hk.Normalize(ref s))
                            s = it.DefStr;
                        if (s != "")
                            others.Add(new KeyValuePair<string, string>(s, m.Name + ": " + it.Label));
                    }
                }
            foreach (var it in mine)
            {
                string s = edit[it.Key];
                if (s == "" || !it.CheckConflicts)
                    continue;
                var found = new List<string>();
                string game = Hk.GameControl(s);
                if (game != null)
                    found.Add(Loc.InGame + " (" + game + ")");
                foreach (var o in mine)
                    if (o != it && edit[o.Key] == s)
                        found.Add(o.Label);
                foreach (var o in others)
                    if (o.Key == s)
                        found.Add(o.Value);
                if (found.Count > 0)
                    conflicts[it.Key] = string.Join(", ", found);
            }
        }

        // Шаг для −/+: заданный, иначе 1/100 диапазона, иначе 1.
        static float StepOf(Item it)
        {
            if (it.Step > 0)
                return it.Step;
            return it.HasRange ? (it.Max - it.Min) / 100f : 1f;
        }

        // ---- справа: текст мода ----

        string InfoText()
        {
            if (infoTab == tab && infoText != null)
                return infoText;
            object src = tab < Menu.Tabs.Count && Menu.Tabs[tab].Info != null ? Menu.Tabs[tab].Info : Menu.Info;
            string s = src as string;
            var f = src as Func<string>;
            if (f != null)
            {
                try { s = f(); }
                catch (Exception e)
                {
                    Log.Write("Info " + Menu.Id + ": " + e.Message);
                    s = "";
                }
            }
            infoText = s ?? "";
            infoTab = tab;
            return infoText;
        }

        // Текст с переносом; строка, начинающаяся с "# ", — заголовок (белый шрифт, крупнее).
        void DrawInfo()
        {
            var box = R(infoX, ColTop, infoW, ColBottom - ColTop);
            g.Panel(box);
            var view = R(box.X + 0.002f, box.Y + 0.002f, box.Width - 0.004f, box.Height - 0.004f);
            g.Target(view, null, null, null, false, d => info.Wheel(d, view.Height));
            float x = box.X + 0.01f, w = box.Width - 0.02f - ScrollW;
            float y;
            using (g.Clip(view))
            {
                y = box.Y + 0.01f - info.Offset;
                foreach (var para in InfoText().Split('\n'))
                {
                    bool head = para.StartsWith("# ");
                    float scale = head ? Gx.TextScale : Gx.TipScale;
                    string text = Gx.Wrap(head ? para.Substring(2) : para, scale, w);
                    g.Lines(text, x, y, scale, Gx.TextColor, Gx.TopLeft, head ? Gx.FontHover : Gx.Font);
                    y += text.Split('\n').Length * Gx.LineHeight(scale) + (head ? 0.006f : 0f);
                }
            }
            info.ContentH = y + info.Offset - box.Y + 0.01f;
            info.Set(info.Offset, view.Height);
            DrawScrollBar(info, R(box.X + box.Width - ScrollW - 0.003f, view.Y, ScrollW, view.Height));
        }

        // Ванильная полоса прокрутки (только бегунок), тянется мышью.
        void DrawScrollBar(Scroller s, RectangleF track)
        {
            if (s.ContentH <= track.Height + 1e-4f)
                return;
            float th = Math.Max(0.03f, track.Height * track.Height / s.ContentH);
            float max = s.ContentH - track.Height;
            var thumb = R(track.X, track.Y + (track.Height - th) * (s.Offset / max), track.Width, th);
            g.ScrollThumb(thumb, g.Hover(thumb) || (g.Dragging != null && dragScroll == s));
            g.Target(track, null, c =>
            {
                float startOffset = s.Offset, startY = c.Y;
                if (!Gx.Inside(thumb, c))            // щелчок по полосе — бегунок под курсор
                {
                    s.Set((c.Y - track.Y - th / 2f) / (track.Height - th) * max, track.Height);
                    startOffset = s.Offset;
                }
                dragScroll = s;
                return new Drag
                {
                    Move = p => s.Set(startOffset + (p.Y - startY) * max / Math.Max(1e-4f, track.Height - th), track.Height),
                    End = (p, moved) => dragScroll = null,
                };
            });
        }

        Scroller dragScroll;
    }

    // Сообщение поверх окна: ванильная панель, заголовок, текст, кнопка «ОК» — или вопрос с «Да» / «Нет» (answer != null;
    // Esc = «Нет»). Отказ мода на «ОК» и Menu.ShowMessage / ShowConfirm.
    public class MessageWindow : MyModScreen
    {
        const float W = 0.42f;
        readonly string caption, text, owner;
        readonly Gx g = new Gx();
        readonly float h;
        Action<bool> answer;

        public MessageWindow(string caption, string message, Action<bool> answer = null, string owner = null)
            : base("ModUI_Message", new Vector2(0.5f, 0.5f), new Vector2(W, Height(message)), Vector4.One)
        {
            this.caption = caption;
            this.answer = answer;
            this.owner = owner;
            text = Gx.Wrap(message ?? "", Gx.TextScale, W - 0.06f);
            h = Height(message);
            EnabledBackgroundFade = true;
        }

        // Ответ уходит моду один раз.
        void Answer(bool yes)
        {
            var a = answer;
            answer = null;
            CloseNow();
            if (a == null)
                return;
            try
            {
                a(yes);
            }
            catch (Exception e)
            {
                Log.Write("ответ на вопрос мода " + owner + ": " + e.Message);
            }
        }

        protected override bool OnCancelling()
        {
            var a = answer;                 // Esc = «Нет»; окно закроет сама игра
            answer = null;
            if (a != null)
                try
                {
                    a(false);
                }
                catch (Exception e)
                {
                    Log.Write("ответ на вопрос мода " + owner + ": " + e.Message);
                }
            return true;
        }

        static float Height(string message)
        {
            int lines = Gx.Wrap(message ?? "", Gx.TextScale, W - 0.06f).Split('\n').Length;
            return 0.17f + lines * Gx.LineHeight(Gx.TextScale);
        }

        protected override void OnHandleInput()
        {
            g.Input();
        }

        protected override void OnDraw(float alpha)
        {
            g.Begin(alpha);
            float top = 0.5f - h / 2f;
            g.Text(caption, 0.5f, top + 0.05f, 0.8f, Color.White, Gx.Center, W - 0.04f, "ScreenCaption");
            g.Lines(text, 0.5f, top + 0.085f, Gx.TextScale, Gx.TextColor, Gx.TopCenter);
            float bw = 0.176f * 0.75f, bh = 0.052f * 0.85f, by = top + h - bh - 0.025f;
            if (answer != null)
            {
                g.Button(new RectangleF(0.5f - bw - 0.004f, by, bw, bh), Loc.Yes, () => Answer(true));
                g.Button(new RectangleF(0.5f + 0.004f, by, bw, bh), Loc.No, () => Answer(false));
            }
            else
                g.Button(new RectangleF(0.5f - bw / 2f, by, bw, bh), Loc.Ok, () => CloseNow());
            g.End();
        }
    }
}
