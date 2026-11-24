using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;

namespace Fd32.ModUI
{
    // Положение кнопок меню Esc (MyGuiScreenMainMenu.CreateInGameMenu, 1.211). Все Y — доли высоты экрана.
    // Опорная точка кнопки k снизу (1 = «Выход в главное меню», 2 = «Настройки»): центр-низ,
    // низ = 1 - 54/1080 + 0.043 - k * 0.06; X центра = ComputeFullscreenGuiCoordinate(LEFT, 54 px * высота/1080)
    // + 15/1600 + половина ширины кнопки Default (281/1600).
    // Сама кнопка — стиль StripeLeft: размер текстуры 286x62 px (/1600, /1200), и MyGuiControlButton.Draw сдвигает её
    // на половину внутреннего отступа стиля (Left 11.5/1600, Top 6/1200): на 5.75/1600 влево и 3/1200 вверх.
    // Проверено по скриншотам V0.1/V0.2: видимая кнопка «Выход» y 949..1003, x 66..316 при 1920x1080.
    public static class EscLayout
    {
        public const float ButtonW = 286f / 1600f, ButtonH = 62f / 1200f, Step = 0.06f;
        public const float RowW = 0.24f;
        const float DefaultW = 281f / 1600f, ShiftX = 11.5f / 2f / 1600f, ShiftY = 6f / 2f / 1200f;
        // В button_stripe_left.dds (260x60) слева 6 прозрачных столбцов; сверху, снизу и справа — сразу рамка.
        public const float StripeClear = 6f / 260f;
        // Видимый квадрат в текстуре шестерёнки (button_filter_system.dds 128x128): пиксели 13..114.
        const float GearVisible = 102f / 128f, GearPad = 13f / 128f;
        // Ванильный фон кнопок меню: стиль StripeLeft, BackgroundColor = (1, 1, 1, 0.9) (× UIOpacity — в Gx.Alpha).
        public static readonly Color ButtonMask = new Color(1f, 1f, 1f, 0.9f);

        public static Vector2 Viewport()
        {
            var cam = MyAPIGateway.Session != null ? MyAPIGateway.Session.Camera : null;
            return cam != null ? cam.ViewportSize : new Vector2(1920, 1080);
        }

        // Прямоугольник текстуры ванильной кнопки k снизу (как его рисует игра).
        public static RectangleF MenuButton(int k)
        {
            float pxScale = Viewport().Y / 1080f;
            float center = MyScreenSpace.PixelsToNormalized(new Vector2(54f * pxScale, 0f)).X + 15f / 1600f + DefaultW / 2f;
            float bottom = 1f - 54f / 1080f + 0.043f - k * Step - ShiftY;
            return new RectangleF(center - ButtonW / 2f - ShiftX, bottom - ButtonH, ButtonW, ButtonH);
        }

        // Видимая часть кнопки (без прозрачных столбцов текстуры слева).
        public static RectangleF Visible(RectangleF r)
        {
            float clear = r.Width * StripeClear;
            return new RectangleF(r.X + clear, r.Y, r.Width - clear, r.Height);
        }

        // Квадрат в пикселях: ширина в нормализованных X = высота * (Px.X / Px.Y).
        static float ToX(float heightNorm)
        {
            var px = MyScreenSpace.PixelSizeToNormalized(new Vector2(1f, 1f));
            return heightNorm * px.X / px.Y;
        }

        // Видимый зазор между кнопками по вертикали, перенесённый в X.
        static float Gap { get { return ToX(Step - ButtonH); } }

        // Шестерёнка (видимый квадрат): высотой с кнопку, справа от «Настроек» с тем же зазором, что между кнопками по вертикали.
        public static RectangleF Gear()
        {
            var o = MenuButton(2);
            return new RectangleF(o.X + o.Width + Gap, o.Y, ToX(ButtonH), ButtonH);
        }

        // Прямоугольник спрайта шестерёнки: видимый квадрат + прозрачные поля текстуры.
        public static RectangleF GearSprite()
        {
            var v = Gear();
            float w = v.Width / GearVisible, h = v.Height / GearVisible;
            return new RectangleF(v.X - w * GearPad, v.Y - h * GearPad, w, h);
        }

        // Строка i из n подменю (прямоугольник текстуры, видимая часть — Visible): одна строка — на уровне шестерёнки;
        // больше — последняя на уровне «Выхода», остальные выше с шагом меню. Видимый зазор от шестерёнки — как между кнопками.
        public static RectangleF Row(int i, int n)
        {
            var gear = Gear();
            float top = n <= 1 ? gear.Y : MenuButton(1).Y - (n - 1 - i) * Step;
            return new RectangleF(gear.X + gear.Width + Gap - RowW * StripeClear, top, RowW, ButtonH);
        }
    }

    // Центр: принимает меню модов (SendModMessage), рисует шестерёнку и подменю в меню Esc, открывает окна настроек.
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class HubSession : MySessionComponentBase
    {
        public const string Version = "V0.9.1";
        public const long Channel = 3291573804L;     // канал SendModMessage центра
        public const int Protocol = 1;

        public static HubSession Instance;

        public readonly List<MenuEntry> Menus = new List<MenuEntry>();   // по алфавиту имён

        static readonly MyStringId TexGear = MyStringId.GetOrCompute("ModUI_button_filter_system");
        static readonly MyStringId TexGearHover = MyStringId.GetOrCompute("ModUI_button_filter_system_highlight");
        static readonly MyStringId TexGearActive = MyStringId.GetOrCompute("ModUI_button_filter_system_active");

        readonly Gx g = new Gx { Layer = MyScreenSpaceLayer.AboveEverything };
        readonly HashSet<object> covering = new HashSet<object>();   // окна поверх меню Esc (настройки игры, окна модов…)
        object escMenu;
        bool submenuOpen;
        bool drawErrorLogged;
        SettingsWindow window;

        public override void LoadData()
        {
            Instance = this;
            if (MyAPIGateway.Utilities.IsDedicated)
                return;
            Log.Write("Mod UI " + Version + " loaded");
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMessage);
            if (MyAPIGateway.Gui != null)
            {
                MyAPIGateway.Gui.GuiControlCreated += OnScreenAdded;
                MyAPIGateway.Gui.GuiControlRemoved += OnScreenRemoved;
            }
            // Клиенты, загрузившиеся раньше, регистрируются повторно прямо внутри SendModMessage.
            MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { "ModSettings.HubReady", Protocol });
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMessage);
            if (MyAPIGateway.Gui != null)
            {
                MyAPIGateway.Gui.GuiControlCreated -= OnScreenAdded;
                MyAPIGateway.Gui.GuiControlRemoved -= OnScreenRemoved;
            }
            if (window != null && window.IsOpen)
                window.CloseNow();
            window = null;
            escMenu = null;
            covering.Clear();
            Menus.Clear();
            Instance = null;
        }

        // ---- протокол ----

        void OnMessage(object message)
        {
            try
            {
                var a = message as object[];
                string kind = a != null && a.Length > 0 ? a[0] as string : null;
                if (kind == "ModSettings.Register")
                    OnRegister(a);
                else if (kind == "ModSettings.Unregister")
                    OnUnregister(a);
                else if (kind == "ModSettings.Show")
                    OnShow(a);
                else if (kind == "ModUI.Hello")
                    OnHello(a);
                else if (kind == "ModUI.Ask")
                    OnAsk(a);
            }
            catch (Exception e)
            {
                Log.Write("ошибка сообщения: " + e);
            }
        }

        // { "ModSettings.Register", протокол, id, имя, версия, закладки object[], GetValues, Apply, Command, своё окно, info, Edit }
        void OnRegister(object[] a)
        {
            if (a.Length < 12 || !(a[1] is int))
            {
                Log.Write("регистрация: неверный формат (" + a.Length + " полей)");
                return;
            }
            var m = new MenuEntry
            {
                Id = a[2] as string,
                Name = a[3] as string,
                Version = a[4] as string,
                GetValues = a[6] as Func<Dictionary<string, string>>,
                Apply = a[7] as Func<Dictionary<string, string>, string[]>,
                Command = a[8] as Action<string>,
                Custom = a[9] is bool && (bool)a[9],
                Info = a[10],
                Edit = a[11] as Func<Dictionary<string, string>, string, object[]>,
            };
            int proto = (int)a[1];
            if (proto > Protocol)
            {
                Log.Write("меню " + m.Id + ": протокол " + proto + " новее центра (" + Protocol + ") — нужен новый Mod UI");
                SafeCommand(m, "refused:" + Protocol);
                return;
            }
            if (string.IsNullOrEmpty(m.Id) || string.IsNullOrEmpty(m.Name))
            {
                Log.Write("регистрация: пустой id или имя");
                return;
            }
            string error;
            m.Tabs = Tab.ParseTabs(a[5] as object[], out error);
            if (m.Tabs == null)
            {
                Log.Write("меню " + m.Id + ": " + error);
                return;
            }
            var keys = new HashSet<string>();
            int count = 0;
            foreach (var it in m.All())
            {
                count++;
                if (it.Key != null && !keys.Add(it.Key))
                {
                    Log.Write("меню " + m.Id + ": ключ \"" + it.Key + "\" повторяется");
                    return;
                }
            }
            Menus.RemoveAll(x => x.Id == m.Id);
            Menus.Add(m);
            Menus.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            Log.Write("меню " + m.Id + " \"" + m.Name + "\" " + m.Version + ": закладок " + m.Tabs.Count + ", элементов " + count + (m.Custom ? ", своё окно" : ""));
            SafeCommand(m, "ack");
        }

        // { "ModSettings.Show", id, заголовок или null, текст, Action<bool> ответ или null } — сообщение (ОК) или вопрос (Да / Нет).
        void OnShow(object[] a)
        {
            if (a.Length < 5)
                return;
            string id = a[1] as string, caption = a[2] as string, text = a[3] as string;
            var answer = a[4] as Action<bool>;
            if (caption == null)
            {
                var m = Menus.Find(x => x.Id == id);
                caption = answer != null ? Loc.ConfirmCaption : m != null ? m.Name : "";
            }
            new MessageWindow(caption, text, answer, id).Open();
        }

        // { "ModUI.Hello", Action<int> ответ } — клиент проверяет, есть ли центр с окном ввода (ModUI.IsReady).
        void OnHello(object[] a)
        {
            var reply = a.Length > 1 ? a[1] as Action<int> : null;
            if (reply != null)
                reply(FormRequest.Protocol);
        }

        // Окно ввода значений для Set-действия тулбара (FormRequest.Parse).
        void OnAsk(object[] a)
        {
            string error;
            var f = FormRequest.Parse(a, out error);
            if (f == null)
            {
                Log.Write("окно ввода: " + error);
                var cancel = a.Length > 9 ? a[9] as Action : null;
                if (cancel != null)
                    cancel();
                return;
            }
            new SettingsWindow(f).Open();
        }

        void OnUnregister(object[] a)
        {
            string id = a.Length > 1 ? a[1] as string : null;
            if (Menus.RemoveAll(x => x.Id == id) > 0)
                Log.Write("меню " + id + " снято");
            if (window != null && window.IsOpen && window.Menu.Id == id)
                window.CloseNow();
        }

        public static bool SafeCommand(MenuEntry m, string command)
        {
            try
            {
                if (m.Command != null)
                    m.Command(command);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("команда " + command + " моду " + m.Id + ": " + e.Message);
                return false;
            }
        }

        // ---- меню Esc ----

        static bool IsEscMenu(object screen)
        {
            if (screen == null)
                return false;
            string type = screen.GetType().FullName;
            return type != null && type.EndsWith(".MyGuiScreenMainMenu", StringComparison.Ordinal);
        }

        void OnScreenAdded(object screen)
        {
            if (IsEscMenu(screen))
            {
                escMenu = screen;
                covering.Clear();
                submenuOpen = false;
            }
            else if (escMenu != null && screen != null)
            {
                covering.Add(screen);
                submenuOpen = false;
            }
        }

        void OnScreenRemoved(object screen)
        {
            if (screen == null)
                return;
            if (screen == escMenu)
            {
                escMenu = null;
                covering.Clear();
                submenuOpen = false;
            }
            else
                covering.Remove(screen);
        }

        // Шестерёнка видна, только пока меню Esc — верхнее окно.
        bool GearVisible
        {
            get { return escMenu != null && covering.Count == 0; }
        }

        int RowCount { get { return Math.Max(1, Menus.Count); } }

        public override void Draw()
        {
            if (!GearVisible)
                return;
            try
            {
                // Как ванильное меню Esc: прозрачность из настроек игры (MyGuiScreenMainMenu: m_guiTransition = Config.UIOpacity).
                g.Begin(MyAPIGateway.Session.Config.UIOpacity);
                var gear = EscLayout.Gear();
                bool hover = g.Hover(gear);
                g.Sprite(submenuOpen ? TexGearActive : hover ? TexGearHover : TexGear, EscLayout.GearSprite(), EscLayout.ButtonMask);
                g.Target(gear, ToggleSubmenu, null, Loc.GearTip);
                if (submenuOpen)
                {
                    int n = RowCount;
                    if (Menus.Count == 0)
                        DrawRow(EscLayout.Row(0, 1), Loc.NoMods, null, null);
                    for (int i = 0; i < Menus.Count; i++)
                    {
                        var m = Menus[i];
                        DrawRow(EscLayout.Row(i, n), m.Name, m.Version, () => OpenMenu(m));
                    }
                }
                var screen = MyScreenSpace.PixelsToNormalized(EscLayout.Viewport());
                g.DrawTooltip(screen.X, screen.Y);
                g.End();
            }
            catch (Exception e)
            {
                if (!drawErrorLogged)
                    Log.Write("DRAW error: " + e);
                drawErrorLogged = true;
            }
        }

        // Строка подменю: ванильная кнопка меню (StripeLeft), имя слева, версия справа.
        void DrawRow(RectangleF r, string name, string version, Action click)
        {
            bool hover = click != null && g.Hover(EscLayout.Visible(r));
            g.Sprite(hover ? Gx.TexStripeHover : Gx.TexStripe, r, EscLayout.ButtonMask);
            r = EscLayout.Visible(r);
            float pad = 0.014f;
            float verW = string.IsNullOrEmpty(version) ? 0f : Gx.TextWidth(version, Gx.TipScale) + 0.008f;
            g.Text(Gx.Fit(name, Gx.TextScale, r.Width - pad * 2 - verW), r.X + pad, r.Y + r.Height / 2f, Gx.TextScale,
                   click != null ? Gx.TextColor : Gx.DisabledColor, Gx.LeftCenter, float.PositiveInfinity, hover ? Gx.FontHover : Gx.Font);
            if (verW > 0)
                g.Text(version, r.X + r.Width - pad, r.Y + r.Height / 2f, Gx.TipScale, Gx.DimColor, Gx.RightCenter);
            if (click != null)
                g.Target(r, click);
        }

        public override void HandleInput()
        {
            if (!GearVisible)
                return;
            var input = MyAPIGateway.Input;
            Vector2 cursor;
            // щелчок мимо шестерёнки и подменю закрывает подменю
            if (submenuOpen && input.IsNewLeftMousePressed() && MyScreenSpace.TryGetCursorPosition(out cursor))
            {
                bool inside = Gx.Inside(EscLayout.Gear(), cursor);
                for (int i = 0; i < RowCount && !inside; i++)
                    inside = Gx.Inside(EscLayout.Visible(EscLayout.Row(i, RowCount)), cursor);
                if (!inside)
                    submenuOpen = false;
            }
            g.Input();
        }

        void ToggleSubmenu()
        {
            submenuOpen = !submenuOpen;
        }

        void OpenMenu(MenuEntry m)
        {
            submenuOpen = false;
            if (m.Custom)
            {
                SafeCommand(m, "open");
                return;
            }
            window = new SettingsWindow(m);
            window.Open();
        }
    }
}
