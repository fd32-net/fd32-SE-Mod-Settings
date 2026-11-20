// Mod Settings — клиентский файл (протокол 1). Скопируйте его в свой мод без изменений:
// Data\Scripts\<ВашМод>\ModSettings.cs. Центр «Mod Settings» укажите у мода обязательной зависимостью Workshop.
// Описание API — README центра: https://github.com/fd32-net/fd32-SE-Mod-Settings. Пример:
//
//   Menu = new SettingsMenu("mymod", "My Mod", "1.0");
//   Menu.Info("# My Mod\nЧто делает мод и как им пользоваться.");      // правая колонка окна
//   Speed = Menu.Number("speed", "Скорость", 20).Range(0, 100).Step(5).Unit("м/с");   // закладка «Основные настройки»
//   var hud = Menu.Tab("HUD");                                          // своя закладка слева
//   Hints = hud.Group("Подсказки").Toggle("hints", "Показывать", true);
//   Units = hud.Choice("units", "Единицы", "ms").Option("ms", "м/с").Option("kmh", "км/ч");   // выпадающий список
//   ShipName = hud.Text("name", "Имя", "").MaxLength(32).Placeholder("Введите имя...");      // поле ввода
//   Link = hud.Hotkey("link", "Связать", MyKeys.R, MyKeyboardModifiers.Control);   // в игре: if (Link.IsNewPressed()) ...
//   Menu.Editing += e => ShipName.Enabled = e.New(Hints);              // игрок что-то поменял (ещё до «ОК»): мод сам решает,
//                                                                      // что доступно (Enabled), что видно (Visible), e.Set(...) — поменять другие
//   Menu.Applying += e => { if (e.New(Speed) > 50 && e.New(Hints)) e.Cancel("Так нельзя", Speed); };
//   hud.Button("Сбросить", () => Menu.ShowConfirm("Сбросить всё?", yes => { if (yes) Reset(); }));
//   Menu.LoadFromLocalStorage();    // необязательно: значения из файла мода
//   Menu.Applied += () => Menu.SaveToLocalStorage();
//   Menu.Register();                // в LoadData;  Menu.Dispose() — в UnloadData
//
using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;

namespace ModSettings
{
    // Общее у меню, закладки и группы: элементы окна (порядок на экране = порядок вызовов).
    public abstract class SettingsContainer : Entry
    {
        internal readonly List<Entry> Entries = new List<Entry>();
        internal SettingsMenu Owner;

        public ToggleSetting Toggle(string key, string label, bool defaultValue)
        {
            return Add(new ToggleSetting(key, label, defaultValue));
        }

        public NumberSetting Number(string key, string label, float defaultValue)
        {
            return Add(new NumberSetting(key, label, defaultValue));
        }

        // Выбор из вариантов (выпадающий список); defaultKey — ключ варианта по умолчанию, варианты — .Option(...).
        public ChoiceSetting Choice(string key, string label, string defaultKey)
        {
            return Add(new ChoiceSetting(key, label, defaultKey));
        }

        // Горячая клавиша: одна клавиша (клавиатура или любая кнопка мыши) с Ctrl / Shift / Alt.
        public HotkeySetting Hotkey(string key, string label, MyKeys defaultKey, MyKeyboardModifiers modifiers = MyKeyboardModifiers.None)
        {
            return Add(new HotkeySetting(key, label, new HotkeyValue(defaultKey, modifiers)));
        }

        // Строка текста (поле ввода).
        public TextSetting Text(string key, string label, string defaultValue = "")
        {
            return Add(new TextSetting(key, label, defaultValue));
        }

        // Пояснительный текст во всю ширину колонки.
        public Entry Label(string text)
        {
            string key = Owner.NewKey("l");
            return Add(new StaticEntry(key, new object[] { "label", text ?? "", key }));
        }

        public Entry Separator()
        {
            string key = Owner.NewKey("s");
            return Add(new StaticEntry(key, new object[] { "separator", key }));
        }

        // Кнопка-действие: срабатывает сразу, без «ОК». Спросить игрока — Menu.ShowConfirm в action.
        public ButtonItem Button(string label, Action action)
        {
            return Add(new ButtonItem(Owner.NewKey("b"), label, action));
        }

        internal T Add<T>(T e) where T : Entry
        {
            Entries.Add(e);
            return e;
        }

        internal object[] DescribeEntries()
        {
            var a = new object[Entries.Count];
            for (int i = 0; i < Entries.Count; i++)
                a[i] = Entries[i].Describe();
            return a;
        }

        // Все элементы, включая вложенные в группы.
        internal IEnumerable<Entry> All()
        {
            foreach (var e in Entries)
            {
                yield return e;
                var c = e as SettingsContainer;
                if (c != null)
                    foreach (var x in c.All())
                        yield return x;
            }
        }
    }

    // Группа: заголовок раздела в средней колонке; Collapsible() — сворачивается щелчком по заголовку.
    public class SettingsGroup : SettingsContainer
    {
        public readonly string Title;
        bool collapsible, collapsed;

        readonly string key;

        internal SettingsGroup(SettingsMenu owner, string title)
        {
            Owner = owner;
            Title = title;
            key = owner.NewKey("g");
        }

        internal override string EntryKey { get { return key; } }

        public SettingsGroup Collapsible(bool startCollapsed = false)
        {
            collapsible = true;
            collapsed = startCollapsed;
            return this;
        }

        internal override object[] Describe()
        {
            return new object[] { "group", Title, collapsible, collapsed, DescribeEntries(), key };
        }
    }

    // Закладка: строка в списке слева, её настройки — посередине, её текст (Info) — справа.
    public class SettingsTab : SettingsContainer
    {
        public readonly string Title;
        object info;

        internal SettingsTab(SettingsMenu owner, string title)
        {
            Owner = owner;
            Title = title;
        }

        public SettingsGroup Group(string title)
        {
            return Add(new SettingsGroup(Owner, title));
        }

        // Текст правой колонки для этой закладки (вместо общего Menu.Info). Строка "# ..." — заголовок.
        public SettingsTab Info(string text) { info = text; return this; }
        // То же, но текст берётся заново пару раз в секунду (например, состояние мода).
        public SettingsTab Info(Func<string> text) { info = text; return this; }

        internal override string EntryKey { get { return null; } }

        internal override object[] Describe()
        {
            return new object[] { "tab", Title, info, DescribeEntries() };
        }
    }

    // Меню мода: строка в подменю шестерёнки (меню Esc) и окно настроек с заголовком = Name.
    // Элементы, добавленные прямо в меню, попадают на закладку «Основные настройки» (первая в списке).
    public class SettingsMenu : SettingsContainer
    {
        const long Channel = 3291573804L;
        const int Protocol = 1;

        public readonly string Id, Name, Version;
        // Центр принял меню (окно настроек есть). Без центра значения всё равно работают.
        public bool IsConnected { get; private set; }
        // «ОК» в окне: новые значения ещё не применены, можно отказать (e.Cancel).
        public event Action<SettingsApply> Applying;
        // «ОК» принят: новые значения уже в .Value, Changed у изменённых настроек уже вызваны. Место для сохранения.
        public event Action Applied;
        // Окно настроек открыто (сразу после него — Editing с Changed = null).
        public event Action Opened;
        // Игрок поменял значение в окне (ещё до «ОК»): e.New(...) — черновик окна, e.Changed — что поменяли
        // (null — окно только открылось или «По умолчанию»). Здесь мод решает, что доступно (Enabled), что видно (Visible),
        // и может поменять другие значения черновика (e.Set). Ползунок присылает изменения, пока его тянут; поле — когда ввод принят.
        public event Action<SettingsEdit> Editing;
        // Окно закрыто: true — через «ОК» (после Applied), false — «Отмена», Esc или крестик.
        public event Action<bool> Closed;

        int keyCount;
        internal string NewKey(string prefix) { return "#" + prefix + (keyCount++); }
        readonly List<SettingsTab> tabs = new List<SettingsTab>();
        object info;
        Action customWindow;
        bool registered, disposed;

        // id — постоянный ключ мода (не менять между версиями); name и version видны в подменю.
        public SettingsMenu(string id, string name, string version)
        {
            Owner = this;
            Id = id;
            Name = name;
            Version = version;
        }

        // Своя закладка в списке слева.
        public SettingsTab Tab(string title)
        {
            var t = new SettingsTab(this, title);
            tabs.Add(t);
            return t;
        }

        // Группа на закладке «Основные настройки».
        public SettingsGroup Group(string title)
        {
            return Add(new SettingsGroup(this, title));
        }

        // Текст правой колонки окна (для закладок без своего Info). Строка "# ..." — заголовок.
        public SettingsMenu Info(string text) { info = text; return this; }
        // То же, но текст берётся заново пару раз в секунду (например, состояние мода).
        public SettingsMenu Info(Func<string> text) { info = text; return this; }

        // Своё окно вместо стандартного: клик по строке мода в подменю вызывает open (например, new MyScreen().Open()).
        public SettingsMenu CustomWindow(Action open)
        {
            customWindow = open;
            return this;
        }

        internal override object[] Describe() { return null; }
        internal override string EntryKey { get { return null; } }

        // Сообщение игроку поверх игры (окно с «ОК»). Без центра — уведомлением.
        public void ShowMessage(string text, string caption = null)
        {
            Show(text, caption, null);
        }

        // Вопрос игроку: окно с «Да» / «Нет» (Esc = «Нет»); answer(true) — «Да». Без центра вопроса нет (строка в логе).
        public void ShowConfirm(string text, Action<bool> answer, string caption = null)
        {
            Show(text, caption, answer ?? (yes => { }));
        }

        void Show(string text, string caption, Action<bool> answer)
        {
            if (!IsConnected || disposed)
            {
                Log(Id + ": центра нет, сообщение не показано: " + text);
                if (answer == null && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.ShowNotification(text, 5000);
                return;
            }
            MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { "ModSettings.Show", Id, caption, text, answer });
        }

        // ---- сохранение (необязательно) ----
        // Все значения меню — в файл ModSettings_<id>.txt в локальном хранилище этого мода (Storage\<мод>), строки «ключ=значение».

        string StorageFile { get { return "ModSettings_" + Id + ".txt"; } }

        public void SaveToLocalStorage()
        {
            try
            {
                using (var w = MyAPIGateway.Utilities.WriteFileInLocalStorage(StorageFile, typeof(SettingsMenu)))
                {
                    w.WriteLine("# " + Name + " " + Version + ": настройки (Mod Settings), строки ключ=значение");
                    foreach (var e in Everything())
                    {
                        var s = e as Setting;
                        if (s != null)
                            w.WriteLine(s.Key + "=" + s.Write());
                    }
                }
            }
            catch (Exception ex)
            {
                Log(Id + ": не удалось сохранить " + StorageFile + ": " + ex.Message);
            }
        }

        // Вызывать после описания настроек. Changed не вызывается. Ключа нет в файле — значение остаётся;
        // лишние ключи пропускаются; испорченное значение — по умолчанию (строка в логе).
        public void LoadFromLocalStorage()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(StorageFile, typeof(SettingsMenu)))
                    return;
                string text;
                using (var r = MyAPIGateway.Utilities.ReadFileInLocalStorage(StorageFile, typeof(SettingsMenu)))
                    text = r.ReadToEnd();
                var map = new Dictionary<string, Setting>();
                foreach (var e in Everything())
                {
                    var s = e as Setting;
                    if (s != null)
                        map[s.Key] = s;
                }
                foreach (var raw in text.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    int eq = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || eq <= 0)
                        continue;
                    Setting s;
                    if (!map.TryGetValue(line.Substring(0, eq), out s))
                        continue;
                    string v = line.Substring(eq + 1);
                    s.ResetToDefault();
                    s.Commit(s.Read(v));
                    if (s.Write() != v)
                        Log(Id + ": " + StorageFile + ": " + s.Key + " = \"" + v + "\" заменено на \"" + s.Write() + "\"");
                }
            }
            catch (Exception ex)
            {
                Log(Id + ": не удалось прочитать " + StorageFile + ": " + ex.Message);
            }
        }

        IEnumerable<Entry> Everything()
        {
            foreach (var e in All())
                yield return e;
            foreach (var t in tabs)
                foreach (var e in t.All())
                    yield return e;
        }

        // ---- связь с центром ----

        // Отдать описание центру. Вызывать один раз, после описания всех элементов (обычно в LoadData).
        public void Register()
        {
            if (registered || disposed)
                return;
            registered = true;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
            Send();
        }

        // Снять меню (UnloadData мода).
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            IsConnected = false;
            if (!registered || MyAPIGateway.Utilities == null)
                return;
            MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMessage);
            MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { "ModSettings.Unregister", Id });
        }

        void OnMessage(object message)
        {
            var a = message as object[];
            if (a != null && a.Length > 0 && (a[0] as string) == "ModSettings.HubReady" && !disposed)
                Send();                     // центр загрузился позже мода
        }

        void Send()
        {
            foreach (var e in Everything())
            {
                var s = e as Setting;
                if (s != null)
                    s.Prepare();
            }
            var desc = new List<object>();
            if (Entries.Count > 0 || tabs.Count == 0)
                desc.Add(new object[] { "tab", null, null, DescribeEntries() });     // «Основные настройки»
            foreach (var t in tabs)
                desc.Add(t.Describe());
            Func<Dictionary<string, string>> getValues = GetValues;
            Func<Dictionary<string, string>, string[]> apply = Apply;
            Action<string> command = Command;
            Func<Dictionary<string, string>, string, object[]> edit = Edit;
            MyAPIGateway.Utilities.SendModMessage(Channel, new object[]
                { "ModSettings.Register", Protocol, Id, Name, Version, desc.ToArray(), getValues, apply, command, customWindow != null, info, edit });
        }

        static void Log(string text)
        {
            MyLog.Default.WriteLine("ModSettings client: " + text);
        }

        Dictionary<string, string> GetValues()
        {
            var d = new Dictionary<string, string>();
            foreach (var e in Everything())
            {
                var s = e as Setting;
                if (s != null)
                    d[s.Key] = s.Write();
            }
            return d;
        }

        // «ОК»: null — принято; иначе { текст ошибки, ключ строки или null }.
        string[] Apply(Dictionary<string, string> values)
        {
            try
            {
                var e = new SettingsApply();
                e.Fill(Everything(), values);
                if (Applying != null)
                    Applying(e);
                if (e.IsCancelled)
                    return new[] { e.Error, e.ErrorKey };
                var changed = new List<Setting>();
                foreach (var kv in e.Next)
                    if (!kv.Key.Same(kv.Value))
                    {
                        kv.Key.Commit(kv.Value);
                        changed.Add(kv.Key);
                    }
                foreach (var s in changed)
                    s.FireChanged();
                if (Applied != null)
                    Applied();
                return null;
            }
            catch (Exception ex)
            {
                Log(Id + ": ошибка при применении: " + ex);
                throw;
            }
        }

        // Окно: черновик и ключ изменённого ("" — открытие, "*" — несколько сразу, null — просто опрос).
        // Ответ: { новые значения черновика или null, недоступные ключи, скрытые ключи }.
        object[] Edit(Dictionary<string, string> draft, string changed)
        {
            Dictionary<string, string> values = null;
            try
            {
                if (changed == "" && Opened != null)
                    Opened();
                if (changed != null && Editing != null)
                {
                    var e = new SettingsEdit();
                    e.Fill(Everything(), draft);
                    foreach (var entry in Everything())
                    {
                        var s = entry as Setting;
                        if (s != null && s.Key == changed)
                            e.Changed = s;
                    }
                    Editing(e);
                    if (e.Values.Count > 0)
                        values = e.Values;
                }
            }
            catch (Exception ex)
            {
                Log(Id + ": ошибка в Opened/Editing: " + ex);
            }
            var off = new List<string>();
            var hidden = new List<string>();
            foreach (var entry in Everything())
            {
                string key = entry.EntryKey;
                if (key == null)
                    continue;
                if (!entry.Enabled)
                    off.Add(key);
                if (!entry.Visible)
                    hidden.Add(key);
            }
            return new object[] { values, off.ToArray(), hidden.ToArray() };
        }

        void Command(string command)
        {
            try
            {
                if (command == "ack")
                {
                    if (!IsConnected)
                        Log(Id + ": центр принял меню");
                    IsConnected = true;
                }
                else if (command == "open")
                {
                    if (customWindow != null)
                        customWindow();
                }
                else if (command.StartsWith("button:"))
                {
                    string key = command.Substring(7);
                    foreach (var e in Everything())
                    {
                        var b = e as ButtonItem;
                        if (b != null && b.Key == key && b.Action != null)
                            b.Action();
                    }
                }
                else if (command == "closed:ok" || command == "closed:cancel")
                {
                    if (Closed != null)
                        Closed(command == "closed:ok");
                }
                else if (command.StartsWith("refused:"))
                    Log(Id + ": центр Mod Settings устарел (его протокол " + command.Substring(8) + ", нужен " + Protocol + ") — окна настроек не будет");
            }
            catch (Exception ex)
            {
                Log(Id + ": ошибка в команде " + command + ": " + ex);
            }
        }
    }

    // Значения из окна (ещё не применённые): e.New(настройка).
    public abstract class SettingsValues
    {
        internal readonly Dictionary<Setting, object> Next = new Dictionary<Setting, object>();

        public bool New(ToggleSetting s) { return (bool)Get(s); }
        public float New(NumberSetting s) { return (float)Get(s); }
        public string New(ChoiceSetting s) { return (string)Get(s); }
        public string New(TextSetting s) { return (string)Get(s); }
        public HotkeyValue New(HotkeySetting s) { return (HotkeyValue)Get(s); }

        // В окне значение отличается от действующего (.Value).
        public bool IsChanged(Setting s) { return !s.Same(Get(s)); }

        internal void Fill(IEnumerable<Entry> all, Dictionary<string, string> values)
        {
            foreach (var entry in all)
            {
                var s = entry as Setting;
                string text;
                if (s != null)
                    Next[s] = values != null && values.TryGetValue(s.Key, out text) ? s.Read(text) : s.Boxed;
            }
        }

        internal object Get(Setting s)
        {
            object v;
            return Next.TryGetValue(s, out v) ? v : s.Boxed;
        }
    }

    // Изменение в окне (Menu.Editing).
    public class SettingsEdit : SettingsValues
    {
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>();

        // Что поменял игрок; null — окно только открылось или «По умолчанию» (поменялось сразу многое).
        public Setting Changed { get; internal set; }

        // Поменять значение в окне (ещё не применено — как будто его поменял игрок).
        public void Set(ToggleSetting s, bool value) { Put(s, value); }
        public void Set(NumberSetting s, float value) { Put(s, value); }
        public void Set(ChoiceSetting s, string value) { Put(s, value); }
        public void Set(TextSetting s, string value) { Put(s, value); }
        public void Set(HotkeySetting s, HotkeyValue value) { Put(s, value); }

        void Put(Setting s, object value)
        {
            value = s.Clean(value);
            Next[s] = value;
            Values[s.Key] = s.Format(value);
        }
    }

    // Новые значения на «ОК» (Menu.Applying).
    public class SettingsApply : SettingsValues
    {
        internal string Error, ErrorKey;

        public bool IsCancelled { get; private set; }

        // Отказать: окно остаётся открытым, игрок видит message; focus — подсветить строку этой настройки.
        public void Cancel(string message, Setting focus = null)
        {
            IsCancelled = true;
            Error = string.IsNullOrEmpty(message) ? "?" : message;
            ErrorKey = focus != null ? focus.Key : null;
        }
    }

    // ---- элементы ----

    public abstract class Entry
    {
        // Видна ли строка в окне и можно ли её менять (серая — нельзя). Менять когда угодно, обычно в Menu.Editing;
        // у группы действует на всё содержимое. Окно перечитывает их само.
        public bool Visible { get; set; } = true;
        public bool Enabled { get; set; } = true;

        internal abstract string EntryKey { get; }
        internal abstract object[] Describe();
    }

    class StaticEntry : Entry
    {
        readonly string key;
        readonly object[] desc;
        public StaticEntry(string key, object[] desc) { this.key = key; this.desc = desc; }
        internal override string EntryKey { get { return key; } }
        internal override object[] Describe() { return desc; }
    }

    public class ButtonItem : Entry
    {
        public readonly string Key, Label;
        internal readonly Action Action;
        string tip;

        internal ButtonItem(string key, string label, Action action)
        {
            Key = key;
            Label = label;
            Action = action;
        }

        public ButtonItem Tooltip(string text)
        {
            tip = text;
            return this;
        }

        internal override string EntryKey { get { return Key; } }
        internal override object[] Describe() { return new object[] { "button", Key, Label, tip }; }
    }

    // Общее у всех настроек.
    public abstract class Setting : Entry
    {
        public readonly string Key, Label;
        internal string Tip;

        protected Setting(string key, string label)
        {
            Key = key;
            Label = label;
        }

        internal abstract object Boxed { get; }
        internal abstract object Read(string text);
        internal abstract bool Same(object value);
        internal abstract void Commit(object value);
        internal abstract void FireChanged();
        internal virtual void Prepare() { }          // перед отправкой описания центру
        internal abstract object Clean(object value);  // привести к правилам настройки (шаг, диапазон, варианты, длина)
        internal abstract string Format(object value); // значение -> строка протокола
        internal abstract void ResetToDefault();
        internal override string EntryKey { get { return Key; } }
        internal string Write() { return Format(Boxed); }
    }

    public abstract class Setting<T, TSelf> : Setting where TSelf : Setting<T, TSelf>
    {
        T value;

        // Значение по умолчанию (кнопка «По умолчанию»).
        public T Default { get; protected set; }

        // Текущее значение. Присвоение из кода мода (например, загруженное) Changed не вызывает.
        public T Value
        {
            get { return value; }
            set { this.value = Normalize(value); }
        }

        // Значение поменял игрок («ОК», который мод не отменил).
        public event Action<T> Changed;

        protected Setting(string key, string label, T defaultValue) : base(key, label)
        {
            Default = defaultValue;
            value = defaultValue;
        }

        // Подсказка при наведении, как у ванили.
        public TSelf Tooltip(string text)
        {
            Tip = text;
            return (TSelf)this;
        }

        protected virtual T Normalize(T v) { return v; }

        protected void Renormalize()
        {
            Default = Normalize(Default);
            value = Normalize(value);
        }

        internal override object Boxed { get { return value; } }
        internal override bool Same(object v) { return EqualityComparer<T>.Default.Equals(value, (T)v); }
        internal override void Commit(object v) { value = (T)v; }
        internal override object Clean(object v) { return Normalize((T)v); }
        internal override void ResetToDefault() { value = Default; }

        internal override void FireChanged()
        {
            if (Changed != null)
                Changed(value);
        }
    }

    // Галочка.
    public class ToggleSetting : Setting<bool, ToggleSetting>
    {
        internal ToggleSetting(string key, string label, bool defaultValue) : base(key, label, defaultValue) { }

        internal override object[] Describe() { return new object[] { "toggle", Key, Label, Tip, Default }; }
        internal override string Format(object v) { return (bool)v ? "true" : "false"; }
        internal override object Read(string text) { return text == "true" ? true : text == "false" ? false : Value; }
    }

    // Число: ползунок, поле ввода и кнопки −/+ в любой комбинации.
    public class NumberSetting : Setting<float, NumberSetting>
    {
        const int PartSlider = 1, PartField = 2, PartButtons = 4;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        bool hasRange;
        float min, max, step;
        int decimals = -1, parts;
        string unit;

        internal NumberSetting(string key, string label, float defaultValue) : base(key, label, defaultValue) { }

        // Значение, округлённое до целого.
        public int IntValue { get { return (int)Math.Round(Value); } }

        // Диапазон: значение всегда в нём; без диапазона ползунка нет.
        public NumberSetting Range(float minValue, float maxValue)
        {
            hasRange = true;
            min = Math.Min(minValue, maxValue);
            max = Math.Max(minValue, maxValue);
            Renormalize();
            return this;
        }

        // Шаг: ползунок ходит шагами, −/+ прибавляют шаг, введённое округляется к шагу (от min).
        public NumberSetting Step(float value)
        {
            step = Math.Max(0f, value);
            Renormalize();
            return this;
        }

        // Знаков после запятой (по умолчанию — по шагу).
        public NumberSetting Decimals(int count)
        {
            decimals = Math.Max(0, Math.Min(6, count));
            Renormalize();
            return this;
        }

        public NumberSetting Unit(string text)
        {
            unit = text;
            return this;
        }

        // Элементы в строке окна. Без вызовов: с диапазоном — ползунок + поле, без — поле + кнопки.
        public NumberSetting Slider() { parts |= PartSlider; return this; }
        public NumberSetting Field() { parts |= PartField; return this; }
        public NumberSetting Buttons() { parts |= PartButtons; return this; }

        int EffectiveDecimals()
        {
            if (decimals >= 0)
                return decimals;
            if (step <= 0)
                return -1;
            int d = 0;
            double s = step;
            while (d < 6 && Math.Abs(s - Math.Round(s)) > 1e-4)
            {
                s *= 10;
                d++;
            }
            return d;
        }

        protected override float Normalize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                v = Default;
            if (step > 0)
            {
                float b = hasRange ? min : 0f;
                v = b + (float)Math.Round((v - b) / step) * step;
            }
            if (hasRange)
                v = Math.Max(min, Math.Min(max, v));
            int d = EffectiveDecimals();
            if (d >= 0)
                v = (float)Math.Round(v, d);
            return v;
        }

        internal override object[] Describe()
        {
            return new object[] { "number", Key, Label, Tip, Default, hasRange, min, max, step, decimals, unit, parts };
        }

        internal override string Format(object v) { return ((float)v).ToString("R", Inv); }

        internal override object Read(string text)
        {
            float f;
            if (text != null && float.TryParse(text.Replace(',', '.'), NumberStyles.Float, Inv, out f))
                return Normalize(f);
            return Value;
        }
    }
    // Выбор из вариантов: выпадающий список. Значение — ключ варианта (строка, хранится и передаётся как есть).
    public class ChoiceSetting : Setting<string, ChoiceSetting>
    {
        readonly List<string> keys = new List<string>(), labels = new List<string>();

        internal ChoiceSetting(string key, string label, string defaultKey) : base(key, label, defaultKey ?? "") { }

        // Вариант: key — постоянный ключ (его видит код мода и сохранение), label — подпись в списке.
        public ChoiceSetting Option(string key, string label = null)
        {
            if (key != null && !keys.Contains(key))
            {
                keys.Add(key);
                labels.Add(label ?? key);
            }
            return this;
        }

        public IList<string> Keys { get { return keys.AsReadOnly(); } }

        // Неизвестный ключ (например, вариант убрали в новой версии) — значение по умолчанию.
        protected override string Normalize(string v)
        {
            if (keys.Count == 0 || keys.Contains(v))
                return v;
            return keys.Contains(Default) ? Default : keys[0];
        }

        internal override void Prepare()
        {
            if (keys.Count > 0 && !keys.Contains(Default))
                Default = keys[0];
            Value = Value;
        }

        internal override object[] Describe()
        {
            return new object[] { "choice", Key, Label, Tip, Default, keys.ToArray(), labels.ToArray() };
        }

        internal override string Format(object v) { return (string)v; }
        internal override object Read(string text) { return text != null && keys.Contains(text) ? text : Value; }
    }

    // Строка текста: ванильное поле ввода, одна строка.
    public class TextSetting : Setting<string, TextSetting>
    {
        int maxLength = 256;
        string placeholder;

        internal TextSetting(string key, string label, string defaultValue) : base(key, label, defaultValue ?? "")
        {
            Renormalize();
        }

        // Не длиннее n символов (по умолчанию 256).
        public TextSetting MaxLength(int n)
        {
            maxLength = Math.Max(1, n);
            Renormalize();
            return this;
        }

        // Серая подсказка в пустом поле.
        public TextSetting Placeholder(string text)
        {
            placeholder = text;
            return this;
        }

        protected override string Normalize(string v)
        {
            v = (v ?? "").Replace("\r", "").Replace('\n', ' ');
            return v.Length > maxLength ? v.Substring(0, maxLength) : v;
        }

        internal override object[] Describe()
        {
            return new object[] { "text", Key, Label, Tip, Default, maxLength, placeholder };
        }

        internal override string Format(object v) { return (string)v; }
        internal override object Read(string text) { return Normalize(text); }
    }
    // Значение горячей клавиши: клавиша + Ctrl / Shift / Alt. Key = MyKeys.None — клавиши нет.
    public struct HotkeyValue : IEquatable<HotkeyValue>
    {
        public readonly MyKeys Key;
        public readonly bool Ctrl, Shift, Alt;

        public static readonly HotkeyValue None = new HotkeyValue(MyKeys.None, false, false, false);

        public HotkeyValue(MyKeys key, MyKeyboardModifiers modifiers = MyKeyboardModifiers.None)
            : this(key, (modifiers & (MyKeyboardModifiers.Control | MyKeyboardModifiers.LeftControl | MyKeyboardModifiers.RightControl)) != 0,
                   (modifiers & (MyKeyboardModifiers.Shift | MyKeyboardModifiers.LeftShift | MyKeyboardModifiers.RightShift)) != 0,
                   (modifiers & (MyKeyboardModifiers.Alt | MyKeyboardModifiers.LeftAlt | MyKeyboardModifiers.RightAlt)) != 0) { }

        public HotkeyValue(MyKeys key, bool ctrl, bool shift, bool alt)
        {
            Key = Generic(key);
            Ctrl = ctrl && Key != MyKeys.Control && Key != MyKeys.None;
            Shift = shift && Key != MyKeys.Shift && Key != MyKeys.None;
            Alt = alt && Key != MyKeys.Alt && Key != MyKeys.None;
        }

        public bool IsNone { get { return Key == MyKeys.None; } }

        // Назначена сама клавиша-модификатор (например, Ctrl).
        public bool IsModifierKey { get { return Key == MyKeys.Control || Key == MyKeys.Shift || Key == MyKeys.Alt; } }

        // "Ctrl + R" — названиями клавиш игры (для подсказок мода).
        public string DisplayText
        {
            get
            {
                if (IsNone)
                    return MyAPIGateway.Input != null ? MyAPIGateway.Input.GetUnassignedName() : "-";
                return (Ctrl ? Name(MyKeys.Control) + " + " : "") + (Shift ? Name(MyKeys.Shift) + " + " : "") +
                       (Alt ? Name(MyKeys.Alt) + " + " : "") + Name(Key);
            }
        }

        static string Name(MyKeys k)
        {
            string n = null;
            var input = MyAPIGateway.Input;
            if (input != null)
                n = k == MyKeys.LeftButton ? input.GetName(MyMouseButtonsEnum.Left)
                  : k == MyKeys.RightButton ? input.GetName(MyMouseButtonsEnum.Right)
                  : k == MyKeys.MiddleButton ? input.GetName(MyMouseButtonsEnum.Middle)
                  : k == MyKeys.ExtraButton1 ? input.GetName(MyMouseButtonsEnum.XButton1)
                  : k == MyKeys.ExtraButton2 ? input.GetName(MyMouseButtonsEnum.XButton2) : input.GetKeyName(k);
            return string.IsNullOrEmpty(n) ? k.ToString() : n;
        }

        static MyKeys Generic(MyKeys k)
        {
            if (k == MyKeys.LeftControl || k == MyKeys.RightControl) return MyKeys.Control;
            if (k == MyKeys.LeftShift || k == MyKeys.RightShift) return MyKeys.Shift;
            if (k == MyKeys.LeftAlt || k == MyKeys.RightAlt) return MyKeys.Alt;
            return k;
        }

        // Строка протокола и файла: "Ctrl+Shift+Alt+R", "" — нет клавиши.
        public override string ToString()
        {
            return IsNone ? "" : (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Key;
        }

        static Dictionary<string, MyKeys> names;

        public static bool TryParse(string text, out HotkeyValue value)
        {
            value = None;
            if (string.IsNullOrEmpty(text))
                return text != null;
            if (names == null)
            {
                names = new Dictionary<string, MyKeys>();
                for (int i = 1; i < 256; i++)
                {
                    var k = (MyKeys)i;
                    if (!names.ContainsKey(k.ToString()))
                        names[k.ToString()] = k;
                }
            }
            var parts = text.Split('+');
            bool c = false, s = false, a = false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "Ctrl") c = true;
                else if (parts[i] == "Shift") s = true;
                else if (parts[i] == "Alt") a = true;
                else return false;
            }
            MyKeys key;
            if (!names.TryGetValue(parts[parts.Length - 1], out key))
                return false;
            value = new HotkeyValue(key, c, s, a);
            return true;
        }

        public bool Equals(HotkeyValue o) { return Key == o.Key && Ctrl == o.Ctrl && Shift == o.Shift && Alt == o.Alt; }
        public override bool Equals(object obj) { return obj is HotkeyValue && Equals((HotkeyValue)obj); }
        public override int GetHashCode() { return (int)Key * 8 + (Ctrl ? 1 : 0) + (Shift ? 2 : 0) + (Alt ? 4 : 0); }
    }

    // Горячая клавиша. Проверять нажатие — здесь же, модификаторы мод сам не разбирает:
    //   R не срабатывает, пока зажат Ctrl (R и Ctrl+R — разные клавиши); назначенный сам модификатор (Ctrl)
    //   срабатывает и с другими зажатыми клавишами. Пока открыт чат или окно с курсором — false (кроме EvenInMenus).
    public class HotkeySetting : Setting<HotkeyValue, HotkeySetting>
    {
        bool evenInMenus, checkConflicts;

        internal HotkeySetting(string key, string label, HotkeyValue defaultValue) : base(key, label, defaultValue) { }

        // Срабатывать и при открытом чате / окне.
        public HotkeySetting EvenInMenus()
        {
            evenInMenus = true;
            return this;
        }

        // Подсвечивать в окне красным, если клавиша занята в управлении игры, другой клавишей этого мода или
        // клавишей другого мода центра. По умолчанию выключено: только мод знает, где его клавиша работает
        // (R в игре — только с оружием в руках и т.п.).
        public HotkeySetting CheckConflicts()
        {
            checkConflicts = true;
            return this;
        }

        public string DisplayText { get { return Value.DisplayText; } }

        // Держат сейчас.
        public bool IsPressed() { return Ready() && MyAPIGateway.Input.IsKeyPress(Value.Key) && Modifiers(); }
        // Нажали в этом кадре.
        public bool IsNewPressed() { return Ready() && MyAPIGateway.Input.IsNewKeyPressed(Value.Key) && Modifiers(); }
        // Отпустили в этом кадре (модификаторы не проверяются).
        public bool IsNewReleased() { return Ready() && MyAPIGateway.Input.IsNewKeyReleased(Value.Key); }

        bool Ready()
        {
            if (Value.IsNone || MyAPIGateway.Input == null)
                return false;
            if (evenInMenus)
                return true;
            var gui = MyAPIGateway.Gui;
            return gui == null || !(gui.ChatEntryVisible || gui.IsCursorVisible);
        }

        bool Modifiers()
        {
            var input = MyAPIGateway.Input;
            var v = Value;
            bool c = input.IsAnyCtrlKeyPressed(), s = input.IsAnyShiftKeyPressed(), a = input.IsAnyAltKeyPressed();
            if (v.IsModifierKey)
                return (!v.Ctrl || c) && (!v.Shift || s) && (!v.Alt || a);
            return c == v.Ctrl && s == v.Shift && a == v.Alt;
        }

        internal override object[] Describe() { return new object[] { "hotkey", Key, Label, Tip, Default.ToString(), checkConflicts }; }
        internal override string Format(object v) { return ((HotkeyValue)v).ToString(); }

        internal override object Read(string text)
        {
            HotkeyValue v;
            return HotkeyValue.TryParse(text, out v) ? v : Value;
        }
    }
}
