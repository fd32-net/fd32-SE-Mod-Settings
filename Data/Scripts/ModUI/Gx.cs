using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Fd32.ModUI
{
    // Press-and-drag in progress: Move gets the cursor every frame while the button is held, End gets it on release
    // (moved = the cursor went further than a few pixels; a release without moving is a click).
    public class Drag
    {
        public Vector2 Start;
        public bool Moved;
        public Action<Vector2> Move;
        public Action<Vector2, bool> End;
    }

    // Colours with transparency are premultiplied (Color * alpha), as the game draws sprites.
    // Tiny immediate-mode GUI on top of MyScreenSpaceText/Sprite. All coordinates are normalized GUI coordinates
    // (0..1 over the 4:3 safe area), as in the web prototype. Widgets are drawn and register their targets while
    // drawing; Input() then hit-tests the targets of the last drawn frame.
    public class Gx
    {
        public static readonly MyStringId White = MyStringId.GetOrCompute("SquareSimple");
        // Vanilla look (see notes/se-vanilla-gui-style.md): text in the "Blue" font (220,244,252), "White" on hover;
        // controls drawn with the game's own GUI textures (Data/TransparentMaterials_ModUI.sbc).
        public const string Font = "Blue";
        public const string FontHover = "White";
        public const float TextScale = 0.8f, TipScale = 0.7f;

        static MyStringId Tex(string name) { return MyStringId.GetOrCompute("ModUI_" + name); }
        public static readonly MyStringId TexButton = Tex("button_default"), TexButtonHover = Tex("button_default_highlight"),
            TexButtonActive = Tex("button_default_active"), TexStripe = Tex("button_stripe_left"),
            TexStripeHover = Tex("button_stripe_left_highlight"), TexChecked = Tex("checkbox_checked"),
            TexUnchecked = Tex("checkbox_unchecked"), TexCheckedHover = Tex("checkbox_checked_highlight"),
            TexUncheckedHover = Tex("checkbox_unchecked_highlight"), TexRailL = Tex("slider_rail_left"),
            TexRailC = Tex("slider_rail_center"), TexRailR = Tex("slider_rail_right"), TexThumb = Tex("slider_thumb"),
            TexThumbHover = Tex("slider_thumb_highlight"), TexTooltip = Tex("TooltipBackground");
        static readonly MyStringId[] TexBox = { Tex("textbox_left"), Tex("textbox_center"), Tex("textbox_right") };
        static readonly MyStringId[] TexBoxHover = { Tex("textbox_left_highlight"), Tex("textbox_center_highlight"), Tex("textbox_right_highlight") };
        static readonly MyStringId[] TexBoxFocus = { Tex("textbox_left_focus"), Tex("textbox_center_focus"), Tex("textbox_right_focus") };
        public static readonly MyStringId TexIncrease = Tex("button_increase"), TexIncreaseHover = Tex("button_increase_highlight"),
            TexDecrease = Tex("button_decrease"), TexDecreaseHover = Tex("button_decrease_highlight");
        static readonly MyStringId[] TexCombo = { Tex("combobox_default_left"), Tex("combobox_default_center"), Tex("combobox_default_right") };
        static readonly MyStringId[] TexComboHover = { Tex("combobox_default_highlight_left"), Tex("combobox_default_highlight_center"), Tex("combobox_default_highlight_right") };
        static readonly MyStringId[] TexComboActive = { Tex("combobox_default_active_left"), Tex("combobox_default_active_center"), Tex("combobox_default_active_right") };
        static readonly MyStringId[] TexHueRail = { Tex("hue_slider_rail_left"), Tex("hue_slider_rail_center"), Tex("hue_slider_rail_right") };
        static readonly MyStringId TexHueThumb = Tex("hue_slider_thumb"), TexHueThumbHover = Tex("hue_slider_thumb_highlight");
        public static readonly MyStringId TexClose = Tex("button_close_symbol"), TexCloseHover = Tex("button_close_symbol_highlight");
        static readonly MyStringId[] TexScroll = { Tex("scrollbar_v_thumb_top"), Tex("scrollbar_v_thumb_center"), Tex("scrollbar_v_thumb_bottom") };
        static readonly MyStringId[] TexScrollHover = { Tex("scrollbar_v_thumb_top_highlight"), Tex("scrollbar_v_thumb_center_highlight"), Tex("scrollbar_v_thumb_bottom_highlight") };

        // colour masks for the "Blue" font (White = the font's own colour)
        public static readonly Color TextColor = Color.White;
        public static readonly Color DimColor = new Color(160, 172, 178);
        public static readonly Color DisabledColor = new Color(0.4f, 0.47f, 0.5f, 0.8f);      // DISABLED_BUTTON_TEXT_COLOR
        public static readonly Color Accent = new Color(142, 188, 206);                         // vanilla focus colour
        public static readonly Color Warning = new Color(0xf0, 0xc8, 0x5f);
        public static readonly Color Good = new Color(0x82, 0xd2, 0x8c);
        public static readonly Color Bad = new Color(227, 65, 65);                              // "Red" font
        // vanilla fills (the colours inside the game's textures)
        public static readonly Color ButtonBg = new Color(41, 54, 62);
        public static readonly Color ButtonHover = new Color(60, 76, 82);
        public static readonly Color ButtonActive = new Color(91, 115, 123);
        public static readonly Color RowHover = new Color(60, 76, 82);                          // item_highlight_dark
        public static readonly Color RowSelected = new Color(91, 115, 123);                     // item_active_dark
        public static readonly Color PanelBg = new Color(33, 40, 45);                           // rectangle_dark
        public static readonly Color PanelBorder = new Color(63, 71, 79);                       // rectangle_dark_border
        public static readonly Color Line = new Color(77, 99, 113);                             // THEMED_GUI_LINE_COLOR
        public static readonly Color HatchA = new Color(53, 65, 74);                            // rectangle_neutral
        public static readonly Color HatchB = new Color(33, 40, 45);
        public static readonly Color ListBorder = new Color(99, 108, 115);                      // scrollable_list_border
        public const float ComboScale = 0.72f;                                                  // текст комбобокса и его списка

        public const MyGuiDrawAlignEnum TopLeft = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
        public const MyGuiDrawAlignEnum TopCenter = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP;
        public const MyGuiDrawAlignEnum TopRight = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP;
        public const MyGuiDrawAlignEnum LeftCenter = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER;
        public const MyGuiDrawAlignEnum Center = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
        public const MyGuiDrawAlignEnum RightCenter = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER;

        struct Hit
        {
            public RectangleF Rect;
            public Action Click;
            public Func<Vector2, Drag> Press;
            public Action<int> Wheel;
            public string Tip;
            public bool KeepSelection;
        }

        List<Hit> building = new List<Hit>();
        List<Hit> active = new List<Hit>();
        readonly Stack<RectangleF> clips = new Stack<RectangleF>();

        public float Alpha = 1f;
        public Vector2 Cursor;
        public bool HasCursor;
        public Vector2 Px;               // one screen pixel in normalized units
        public MyScreenSpaceLayer Layer = MyScreenSpaceLayer.BelowHud;
        public Drag Dragging;            // the drag in progress, if any
        public Action<bool> OnPress;     // any press: argument = the pressed target keeps the selection
        public bool Blocked;             // всё под раскрытым списком: наведение не подсвечивает
        public bool Disabled;            // недоступные элементы: цвет x 0.7 (как DISABLED_CONTROL_COLOR_MASK_MULTIPLIER), щелчки не ловятся

        public void Begin(float alpha)
        {
            Alpha = alpha;
            building.Clear();
            clips.Clear();
            HasCursor = MyScreenSpace.TryGetCursorPosition(out Cursor) && MyAPIGateway.Gui.IsCursorVisible;
            Px = MyScreenSpace.PixelSizeToNormalized(new Vector2(1f, 1f));
        }

        // Makes this frame's targets the clickable ones.
        public void End()
        {
            var t = active;
            active = building;
            building = t;
        }

        // Call once per frame from input handling.
        public void Input()
        {
            var input = MyAPIGateway.Input;
            Vector2 cursor;
            bool has = MyScreenSpace.TryGetCursorPosition(out cursor);
            if (Dragging != null)
            {
                var d = Dragging;
                if (input.IsLeftMousePressed())
                {
                    if (has)
                    {
                        var px = MyScreenSpace.NormalizedSizeToPixels(cursor - d.Start);
                        if (px.Length() > 3f)
                            d.Moved = true;
                        if (d.Move != null)
                            d.Move(cursor);
                    }
                }
                else
                {
                    Dragging = null;
                    if (d.End != null)
                        d.End(has ? cursor : d.Start, d.Moved);
                }
                return;
            }
            if (!has)
                return;
            if (input.IsNewLeftMousePressed())
            {
                int hit = TopAt(cursor, true);
                if (OnPress != null)
                    OnPress(hit >= 0 && active[hit].KeepSelection);
                if (hit >= 0)
                {
                    var t = active[hit];
                    if (t.Press != null)
                    {
                        var d = t.Press(cursor);
                        if (d != null)
                        {
                            d.Start = cursor;
                            Dragging = d;
                        }
                    }
                    else if (t.Click != null)
                        t.Click();
                }
            }
            int wheel = input.DeltaMouseScrollWheelValue();
            if (wheel != 0)
            {
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    if (active[i].Wheel != null && Inside(active[i].Rect, cursor))
                    {
                        active[i].Wheel(wheel);
                        break;
                    }
                }
            }
        }

        int TopAt(Vector2 p, bool interactive)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var t = active[i];
                if (interactive && t.Click == null && t.Press == null)
                    continue;
                if (Inside(t.Rect, p))
                    return i;
            }
            return -1;
        }

        public static bool Inside(RectangleF r, Vector2 p)
        {
            return p.X >= r.X && p.Y >= r.Y && p.X < r.X + r.Width && p.Y < r.Y + r.Height;
        }

        static RectangleF Intersect(RectangleF a, RectangleF b)
        {
            float x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
            float x2 = Math.Min(a.X + a.Width, b.X + b.Width), y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            return new RectangleF(x1, y1, Math.Max(0f, x2 - x1), Math.Max(0f, y2 - y1));
        }

        RectangleF Clipped(RectangleF r)
        {
            return clips.Count > 0 ? Intersect(r, clips.Peek()) : r;
        }

        public bool Hover(RectangleF r)
        {
            return HasCursor && Dragging == null && !Blocked && !Disabled && Inside(Clipped(r), Cursor);
        }

        // Registers a target. Later targets are on top of earlier ones.
        public void Target(RectangleF r, Action click = null, Func<Vector2, Drag> press = null, string tip = null,
                           bool keepSelection = false, Action<int> wheel = null)
        {
            var c = Clipped(r);
            if (c.Width <= 0 || c.Height <= 0)
                return;
            if (Disabled)
            {
                click = null;
                press = null;
                if (tip == null)
                    return;
            }
            building.Add(new Hit { Rect = c, Click = click, Press = press, Tip = tip, KeepSelection = keepSelection, Wheel = wheel });
        }

        public IDisposable Clip(RectangleF r)
        {
            clips.Push(clips.Count > 0 ? Intersect(r, clips.Peek()) : r);
            return new ClipScope(this, MyScreenSpace.UsingScissorRectangle(clips.Peek(), Layer));
        }

        class ClipScope : IDisposable
        {
            readonly Gx owner;
            readonly IDisposable scissor;
            public ClipScope(Gx owner, IDisposable scissor) { this.owner = owner; this.scissor = scissor; }
            public void Dispose()
            {
                scissor.Dispose();
                if (owner.clips.Count > 0)
                    owner.clips.Pop();
            }
        }

        // ---- drawing primitives ----

        public void Rect(RectangleF r, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0)
                return;
            MyScreenSpaceSprite.Draw(White, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c * Fade, TopLeft, 0f, Layer);
        }

        // Множитель цвета: прозрачность окна и недоступность.
        public float Fade { get { return Disabled ? Alpha * 0.7f : Alpha; } }

        public void Rect(float x, float y, float w, float h, Color c)
        {
            Rect(new RectangleF(x, y, w, h), c);
        }

        public void Sprite(MyStringId material, RectangleF r, Color c)
        {
            MyScreenSpaceSprite.Draw(material, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c * Fade, TopLeft, 0f, Layer);
        }

        public void HLine(float x, float y, float w, Color c)
        {
            Rect(new RectangleF(x, y, w, Px.Y), c);
        }

        public void VLine(float x, float y, float h, Color c)
        {
            Rect(new RectangleF(x, y, Px.X, h), c);
        }

        // Dashed horizontal line.
        public void HDash(float x, float y, float w, Color c)
        {
            float dash = Px.X * 4, gap = Px.X * 3;
            for (float cx = x; cx < x + w; cx += dash + gap)
                Rect(new RectangleF(cx, y, Math.Min(dash, x + w - cx), Px.Y), c);
        }

        // Frame `thick` pixels wide inside r.
        public void Frame(RectangleF r, Color c, float thick = 1f)
        {
            float tx = Px.X * thick, ty = Px.Y * thick;
            Rect(new RectangleF(r.X, r.Y, r.Width, ty), c);
            Rect(new RectangleF(r.X, r.Y + r.Height - ty, r.Width, ty), c);
            Rect(new RectangleF(r.X, r.Y + ty, tx, r.Height - 2 * ty), c);
            Rect(new RectangleF(r.X + r.Width - tx, r.Y + ty, tx, r.Height - 2 * ty), c);
        }

        // Dashed frame (one pixel).
        public void DashFrame(RectangleF r, Color c)
        {
            HDash(r.X, r.Y, r.Width, c);
            HDash(r.X, r.Y + r.Height - Px.Y, r.Width, c);
            float dash = Px.Y * 4, gap = Px.Y * 3;
            for (float cy = r.Y; cy < r.Y + r.Height; cy += dash + gap)
            {
                float h = Math.Min(dash, r.Y + r.Height - cy);
                Rect(new RectangleF(r.X, cy, Px.X, h), c);
                Rect(new RectangleF(r.X + r.Width - Px.X, cy, Px.X, h), c);
            }
        }

        // Diagonal stripes ("Empty").
        public void Hatch(RectangleF r, float opacity = 1f)
        {
            Rect(r, HatchB * opacity);
            if (r.Width <= Px.X * 2 || r.Height <= Px.Y * 2)
                return;
            using (Clip(r))
            {
                // 45° stripes 3 px wide, 6 px period (8.5 px along x), like the prototype's repeating gradient;
                // each stripe is a long thin sprite rotated around a point on the rect's horizontal middle line
                float wPx = r.Width / Px.X, hPx = r.Height / Px.Y;
                float lenPx = (wPx + hPx) * 1.5f;
                var size = new Vector2(3f * Px.X, lenPx * Px.Y);
                Color c = HatchA * (Alpha * opacity);
                float cy = r.Y + r.Height / 2f;
                for (float k = -hPx / 2f; k < wPx + hPx / 2f; k += 8.5f)
                    MyScreenSpaceSprite.Draw(White, new Vector2(r.X + k * Px.X, cy), size, c, Center, (float)(Math.PI / 4), Layer);
            }
        }

        public void Text(string text, float x, float y, float scale, Color c, MyGuiDrawAlignEnum align = TopLeft,
                         float maxWidth = float.PositiveInfinity, string font = Font)
        {
            if (string.IsNullOrEmpty(text))
                return;
            MyScreenSpaceText.Draw(font, text, new Vector2(x, y), scale, c * Fade, align, maxWidth, Layer);
        }

        // Dark panel with the vanilla 1-pixel border (rectangle_dark_border).
        public void Panel(RectangleF r, bool border = true)
        {
            Rect(r, PanelBg);
            if (border)
                Frame(r, PanelBorder);
        }

        // Vanilla checkbox (57x54 px texture, stretched to r).
        public void Checkbox(RectangleF r, bool on, bool hover)
        {
            Sprite(on ? (hover ? TexCheckedHover : TexChecked) : (hover ? TexUncheckedHover : TexUnchecked), r, Color.White);
        }

        // Vanilla slider rail (3 pieces, 23 / 4 / 23 x 55 px) with the thumb (32x32 px) at t (0..1).
        float SliderCap(RectangleF r) { return Math.Min(r.Width / 3f, r.Height * 23f / 55f * Px.X / Px.Y); }

        // Отступ хода бегунка от краёв рельса: ваниль — SLIDER_INSIDE_OFFSET_X = 0.017 при рельсе 55 px (0.0458) высотой.
        static float SliderInset(RectangleF r) { return Math.Min(r.Width / 3f, 0.017f * r.Height / (55f / 1200f)); }

        // Value 0..1 of a slider drawn by SliderBar for a cursor x (the thumb travels between the insets).
        public float SliderT(RectangleF r, float x)
        {
            float inset = SliderInset(r);
            return MathHelper.Clamp((x - r.X - inset) / Math.Max(1e-5f, r.Width - 2 * inset), 0f, 1f);
        }

        // x of value t on a slider drawn by SliderBar.
        public float SliderX(RectangleF r, float t)
        {
            float inset = SliderInset(r);
            return r.X + inset + (r.Width - 2 * inset) * MathHelper.Clamp(t, 0f, 1f);
        }

        public void SliderBar(RectangleF r, float t, bool hover)
        {
            DrawSlider(r, t, TexRailL, TexRailC, TexRailR, hover ? TexThumbHover : TexThumb);
        }

        // Ванильный ползунок оттенка (стиль Hue): радуга на рельсе, свой бегунок; размеры — как у обычного.
        public void HueBar(RectangleF r, float t, bool hover)
        {
            DrawSlider(r, t, TexHueRail[0], TexHueRail[1], TexHueRail[2], hover ? TexHueThumbHover : TexHueThumb);
        }

        void DrawSlider(RectangleF r, float t, MyStringId left, MyStringId center, MyStringId right, MyStringId thumb)
        {
            float capW = SliderCap(r);
            // середина — первой и на 1 px под края: игра округляет X и ширину куска по отдельности, встык бывает щель в 1 px
            Sprite(center, new RectangleF(r.X + capW - Px.X, r.Y, r.Width - 2 * capW + 2 * Px.X, r.Height), Color.White);
            Sprite(left, new RectangleF(r.X, r.Y, capW, r.Height), Color.White);
            Sprite(right, new RectangleF(r.X + r.Width - capW, r.Y, capW, r.Height), Color.White);
            float th = r.Height * 32f / 55f, tw = th * Px.X / Px.Y;
            float cx = SliderX(r, t);
            Sprite(thumb, new RectangleF(cx - tw / 2f, r.Y + (r.Height - th) / 2f, tw, th), Color.White);
        }

        // Vanilla vertical scroll bar thumb (top / centre / bottom pieces; the caps are square on screen).
        public void ScrollThumb(RectangleF r, bool hover)
        {
            var t = hover ? TexScrollHover : TexScroll;
            float cap = Math.Min(r.Height / 2f, r.Width * Px.Y / Px.X);
            Sprite(t[1], new RectangleF(r.X, r.Y + cap - Px.Y, r.Width, r.Height - 2 * cap + 2 * Px.Y), Color.White);   // под края (щель 1 px)
            Sprite(t[0], new RectangleF(r.X, r.Y, r.Width, cap), Color.White);
            Sprite(t[2], new RectangleF(r.X, r.Y + r.Height - cap, r.Width, cap), Color.White);
        }

        public void Icon(MyStringId icon, float x, float y, float h, float opacity = 1f)
        {
            MyScreenSpaceSprite.Draw(icon, new Vector2(x, y), new Vector2(h * 0.75f, h), Color.White * (Alpha * opacity), TopLeft, 0f, Layer);
        }

        public static float TextWidth(string text, float scale)
        {
            return MyScreenSpaceText.Measure(Font, text, scale).X;
        }

        public static float LineHeight(float scale)
        {
            return 0.0267f * scale;
        }

        // Несколько строк (через '\n') одна под другой; align — для каждой строки, y — верх первой.
        public void Lines(string text, float x, float y, float scale, Color c, MyGuiDrawAlignEnum align = TopLeft, string font = Font)
        {
            if (string.IsNullOrEmpty(text))
                return;
            float lh = LineHeight(scale);
            foreach (var line in text.Split('\n'))
            {
                Text(line, x, y, scale, c, align, float.PositiveInfinity, font);
                y += lh;
            }
        }

        // Обрезает строку по ширине и ставит "..." (символа «…» и стрелок в шрифтах игры может не быть).
        public static string Fit(string text, float scale, float width)
        {
            if (string.IsNullOrEmpty(text) || TextWidth(text, scale) <= width)
                return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string s = text.Substring(0, n).TrimEnd() + "...";
                if (TextWidth(s, scale) <= width)
                    return s;
            }
            return "...";
        }

        // Word wrap to a normalized width.
        public static string Wrap(string text, float scale, float width)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string paragraph in text.Split('\n'))
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                string line = "";
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && TextWidth(candidate, scale) > width)
                    {
                        sb.Append(line).Append('\n');
                        line = word;
                    }
                    else
                        line = candidate;
                }
                sb.Append(line);
            }
            return sb.ToString();
        }

        // ---- widgets ----

        // Vanilla button (style Default: button_default / _highlight / _active, text "Blue", "White" on hover).
        public void Button(RectangleF r, string label, Action click, bool active = false, bool enabled = true, float scale = TextScale,
                           string tip = null, bool keepSelection = false, string font = null)
        {
            bool hover = enabled && Hover(r);
            if (!enabled)
                Sprite(TexButton, r, Color.White * 0.7f);
            else
                Sprite(hover ? TexButtonHover : active ? TexButtonActive : TexButton, r, Color.White);
            Text(label, r.X + r.Width / 2f, r.Y + r.Height / 2f, scale, enabled ? TextColor : DisabledColor, Center, r.Width - 0.008f,
                 font ?? (hover ? FontHover : Font));
            if (enabled && click != null)
                Target(r, click, null, tip, keepSelection);
            else if (tip != null)
                Target(r, null, null, tip);
        }

        // Квадратная кнопка с картинкой (−/+: button_decrease / button_increase 60x60 px, при наведении _highlight).
        public void IconButton(RectangleF r, MyStringId normal, MyStringId hover, Action click, bool enabled = true, string tip = null)
        {
            bool h = enabled && Hover(r);
            Sprite(h ? hover : normal, r, enabled ? Color.White : Color.White * 0.7f);
            if (enabled && click != null)
                Target(r, click, null, tip);
            else if (tip != null)
                Target(r, null, null, tip);
        }

        // Ванильное поле ввода (MyGuiControlTextbox): 3 куска 8 / 4 / 8 x 48 px; при наведении _highlight,
        // при вводе _focus; шрифт Blue, при наведении и вводе White; курсор ввода — 1 px во всю высоту, мигает 45 из 60 кадров.
        // hint — серая подсказка в пустом поле (пока в нём не печатают). selStart..selEnd — выделение: белый прямоугольник
        // с прозрачностью 0.5 под текстом, как у ванили.
        public void TextBox(RectangleF r, string text, bool hover, bool focus, int caret, bool caretOn, bool rightAlign = false, string hint = null,
                            int selStart = 0, int selEnd = 0)
        {
            var t = hover ? TexBoxHover : focus ? TexBoxFocus : TexBox;
            float cap = Math.Min(r.Width / 3f, r.Height * 8f / 48f * Px.X / Px.Y);
            Sprite(t[1], new RectangleF(r.X + cap - Px.X, r.Y, r.Width - 2 * cap + 2 * Px.X, r.Height), Color.White);   // под края (щель 1 px)
            Sprite(t[0], new RectangleF(r.X, r.Y, cap, r.Height), Color.White);
            Sprite(t[2], new RectangleF(r.X + r.Width - cap, r.Y, cap, r.Height), Color.White);
            var inner = new RectangleF(r.X + BoxPad, r.Y, r.Width - 2 * BoxPad, r.Height);
            text = text ?? "";
            float x = TextBoxOrigin(r, text, focus, caret, rightAlign);
            using (Clip(inner))
            {
                selStart = Math.Max(0, Math.Min(selStart, text.Length));
                selEnd = Math.Max(selStart, Math.Min(selEnd, text.Length));
                if (focus && selEnd > selStart)
                {
                    float sx = x + TextWidth(text.Substring(0, selStart), TextScale);
                    float sw = TextWidth(text.Substring(0, selEnd), TextScale) - (sx - x);
                    Rect(new RectangleF(sx, r.Y + r.Height * 0.15f, sw + Px.X, r.Height * 0.7f), Color.White * 0.5f);
                }
                if (text.Length == 0 && !focus && !string.IsNullOrEmpty(hint))
                    Text(hint, inner.X, r.Y + r.Height / 2f, TextScale, DimColor, LeftCenter);
                Text(text, x, r.Y + r.Height / 2f, TextScale, TextColor, LeftCenter, float.PositiveInfinity, hover || focus ? FontHover : Font);
                if (focus && caretOn)
                {
                    float cx = x + TextWidth(text.Substring(0, Math.Max(0, Math.Min(caret, text.Length))), TextScale);
                    Rect(new RectangleF(cx, r.Y + r.Height * 0.15f, Px.X, r.Height * 0.7f), Color.White);
                }
            }
        }

        const float BoxPad = 0.006f;

        // x начала текста в поле TextBox: при вводе текст едет влево, если курсор ушёл за правый край.
        public float TextBoxOrigin(RectangleF r, string text, bool focus, int caret, bool rightAlign = false)
        {
            text = text ?? "";
            float innerX = r.X + BoxPad, innerW = r.Width - 2 * BoxPad;
            float x = rightAlign && !focus ? innerX + innerW - TextWidth(text, TextScale) : innerX;
            if (focus)
            {
                float cx = TextWidth(text.Substring(0, Math.Max(0, Math.Min(caret, text.Length))), TextScale);
                if (cx > innerW)
                    x -= cx - innerW;
            }
            return x;
        }

        // Позиция курсора ввода под точкой x (щелчок мышью в поле); origin — из TextBoxOrigin.
        public static int CaretAt(string text, float origin, float x)
        {
            text = text ?? "";
            float prev = 0f;
            for (int i = 1; i <= text.Length; i++)
            {
                float w = TextWidth(text.Substring(0, i), TextScale);
                if (x < origin + (prev + w) / 2f)
                    return i - 1;
                prev = w;
            }
            return text.Length;
        }

        // Ванильный комбобокс (MyGuiControlCombobox, стиль Default): 3 куска 20 / 4 / 51 x 48 px (справа — стрелка),
        // при наведении _highlight, раскрытый — _active; выбранный вариант шрифтом Blue 0.72 (White при наведении) с отступом 0.01.
        public void Combo(RectangleF r, string text, bool hover, bool open)
        {
            var t = open ? TexComboActive : hover ? TexComboHover : TexCombo;
            float k = r.Height / 48f * Px.X / Px.Y;          // ширина одного пикселя текстуры
            float l = Math.Min(r.Width / 4f, 20f * k), rw = Math.Min(r.Width / 2f, 51f * k);
            Sprite(t[1], new RectangleF(r.X + l - Px.X, r.Y, r.Width - l - rw + 2 * Px.X, r.Height), Color.White);   // под края (щель 1 px)
            Sprite(t[0], new RectangleF(r.X, r.Y, l, r.Height), Color.White);
            Sprite(t[2], new RectangleF(r.X + r.Width - rw, r.Y, rw, r.Height), Color.White);
            Text(Fit(text, ComboScale, r.Width - 0.01f - rw), r.X + 0.01f, r.Y + r.Height / 2f, ComboScale, TextColor, LeftCenter,
                 float.PositiveInfinity, hover || open ? FontHover : Font);
        }

        // Button of the Esc menu (style StripeLeft).
        public void MenuButton(RectangleF r, string label, Action click)
        {
            bool hover = Hover(r);
            Sprite(hover ? TexStripeHover : TexStripe, r, Color.White);
            Text(label, r.X + r.Width / 2f, r.Y + r.Height / 2f, TextScale, TextColor, Center, r.Width - 0.01f, hover ? FontHover : Font);
            Target(r, click);
        }

        // Row of adjacent buttons; selected = -1 shows none as active (mixed values).
        public void Segmented(float x, float y, float h, float[] widths, string[] labels, int selected, Action<int> pick, float scale = TextScale)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button(new RectangleF(x, y, widths[i], h), labels[i], () => pick(index), selected == i, true, scale);
                x += widths[i] + 0.003f;
            }
        }

        // Tooltip of the topmost target under the cursor; call at the very end of drawing.
        public void DrawTooltip(float maxRight, float maxBottom)
        {
            if (!HasCursor || Dragging != null)
                return;
            string tip = null;
            for (int i = building.Count - 1; i >= 0; i--)
                if (Inside(building[i].Rect, Cursor))
                {
                    tip = building[i].Tip;
                    break;
                }
            if (string.IsNullOrEmpty(tip))
                return;
            DrawTipBox(tip, Cursor, maxRight, maxBottom);
        }

        // Vanilla tooltip (MyToolTips): TooltipBackground.dds tinted (73,86,94,230), "Blue" text at 0.7, placed at
        // cursor + (0.025, 0.03) and kept inside maxRight / maxBottom. A first line starting with '#' is a header
        // (white font).
        public void DrawTipBox(string tip, Vector2 at, float maxRight, float maxBottom)
        {
            const float scale = TipScale;
            bool header = tip.StartsWith("#");
            if (header)
                tip = tip.Substring(1);
            string wrapped = Wrap(tip, scale, 0.34f);
            string[] lines = wrapped.Split('\n');
            float w = 0;
            foreach (var l in lines)
                w = Math.Max(w, TextWidth(l, scale));
            float lh = LineHeight(scale);
            var pad = new Vector2(0.005f, 0.002f);
            float bw = w + pad.X * 2, bh = lines.Length * lh + pad.Y * 2;
            float x = at.X + 0.025f - pad.X, y = at.Y + 0.03f;
            if (x + bw > maxRight) x = maxRight - bw;
            if (y + bh > maxBottom) y = maxBottom - bh;
            var box = new RectangleF(x, y, bw, bh);
            Sprite(TexTooltip, box, new Color(73, 86, 94, 230));
            for (int i = 0; i < lines.Length; i++)
                Text(lines[i], x + pad.X, y + pad.Y + i * lh, scale, TextColor, TopLeft, float.PositiveInfinity,
                     header && i == 0 ? FontHover : Font);
        }
    }
}
