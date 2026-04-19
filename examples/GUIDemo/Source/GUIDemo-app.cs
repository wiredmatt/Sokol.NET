using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sokol;
using Sokol.GUI;
using Sokol.SFileSystem;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SLog;
using static Sokol.NanoVG;
using static Sokol.STM;

public static unsafe class GuidemoApp
{
    // ─── Sokol state ─────────────────────────────────────────────────────────
    static sg_pass_action _passAction;
    static IntPtr         _vg = IntPtr.Zero;
    static Screen?        _screen;

    // ─── Demo state ──────────────────────────────────────────────────────────
    static float   _sliderVal  = 0.5f;
    static float   _progress   = 0.3f;
    static bool    _darkTheme  = true;
    static string  _textBoxVal = "Hello, Sokol.GUI!";
    static int     _comboSel   = 0;
    static Tween?  _animTween;
    static float   _animAlpha  = 1f;

    // ─── Widgets we need to reference ────────────────────────────────────────
    static Label?      _statusLabel;
    static ProgressBar? _progressBar;
    static Image?      _baboonImg;

    // ─── Init ─────────────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func },
        });

        stm_setup();
        FileSystem.Instance.Initialize();

        // NanoVG context
        _vg = nvgCreateSokol(NVG_ANTIALIAS | NVG_STENCIL_STROKES);

        // Pass action — NanoVG requires stencil cleared to 0.
        _passAction = default;
        _passAction.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        _passAction.colors[0].clear_value = new sg_color { r = 0.18f, g = 0.18f, b = 0.20f, a = 1f };
        _passAction.depth.load_action     = sg_load_action.SG_LOADACTION_CLEAR;
        _passAction.depth.clear_value     = 1.0f;
        _passAction.stencil.load_action   = sg_load_action.SG_LOADACTION_CLEAR;
        _passAction.stencil.clear_value   = 0;

        // Sokol.GUI screen
        _screen = Screen.Initialize(_vg);

        // Load fonts asynchronously
        FontRegistry.Instance.RegisterAsync(_vg, "sans", "fonts/Roboto-Regular.ttf");
        FontRegistry.Instance.RegisterAsync(_vg, "bold", "fonts/Roboto-Bold.ttf");

        // Hebrew & Arabic fallback fonts — NanoVG will automatically use these
        // when "sans" or "bold" encounter glyphs not present in Roboto.
        var baseFonts = new[] { "sans", "bold" };
        FontRegistry.Instance.RegisterFallbackAsync(_vg, "hebrew", "fonts/NotoSansHebrew-Regular.ttf", baseFonts);
        FontRegistry.Instance.RegisterFallbackAsync(_vg, "arabic", "fonts/NotoSansArabic-Regular.ttf", baseFonts);

        // Load baboon image for the More tab image widget
        FileSystem.Instance.LoadFile("baboon.png", (path, bytes, status) =>
        {
            if (status == FileLoadStatus.Success && bytes != null)
                _baboonImg!.Source = UIImage.LoadFromMemory(_vg, bytes);
        });

        // Build the UI
        BuildUI();
    }

    // ─── Frame ────────────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Frame()
    {
        FileSystem.Instance.Update();

#if __ANDROID__
        float dpi  = 1f; // TBD ELI , unreliable on Android
#else
        float dpi  = sapp_dpi_scale();
#endif
        float winW = sapp_widthf()  / dpi;
        float winH = sapp_heightf() / dpi;

        // Animate progress bar for fun
        _progress = (_progress + 0.002f) % 1.0f;
        if (_progressBar != null) _progressBar.Value = _progress;

        _screen!.Update(winW, winH, dpi);

        sg_begin_pass(new sg_pass { action = _passAction, swapchain = sglue_swapchain() });

        if (_vg != IntPtr.Zero)
            _screen.Draw(winW, winH, dpi);

        sg_end_pass();
        sg_commit();
    }

    // ─── Event ───────────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Event(sapp_event* e) => _screen?.DispatchEvent(e);

    // ─── Cleanup ─────────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        Screen.Shutdown();
        if (_vg != IntPtr.Zero) nvgDeleteSokol(_vg);
        FileSystem.Instance.Shutdown();
        sg_shutdown();

        if (Debugger.IsAttached) Environment.Exit(0);
    }

    // ─── UI Builder ──────────────────────────────────────────────────────────

    static void BuildUI()
    {
        var tabs = new TabView(); // Screen fills root children to window size
        _screen!.AddChild(tabs);

        tabs.AddTab("Widgets",    BuildWidgetsTab());
        tabs.AddTab("Theming",    BuildThemingTab());
        tabs.AddTab("MVVM",       BuildMvvmTab());
        tabs.AddTab("MVC",        BuildMvcTab());
        tabs.AddTab("Animation",  BuildAnimationTab());
        tabs.AddTab("XML",        BuildXmlTab());
        tabs.AddTab("Extended",   BuildExtendedTab());
        tabs.AddTab("More",       BuildMoreTab());
        tabs.AddTab("ColorPicker",BuildColorPickerTab());
        tabs.AddTab("PropGrid",   BuildPropertyGridTab());
        tabs.AddTab("RichText",   BuildRichTextTab());
        tabs.AddTab("BiDi",       BuildBiDiTab());
        tabs.AddTab("HScroll",    BuildHScrollTestTab());
        tabs.AddTab("Docking",    BuildDockingTab());
        tabs.AddTab("Layout",     BuildLayoutTab());
        tabs.AddTab("DragDrop",   BuildDragDropTab());
        tabs.AddTab("KbScroll",   BuildKeyboardScrollTab());
    }

    // ── Tab 1: Widgets ────────────────────────────────────────────────────────
    static Widget BuildWidgetsTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 8),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Sokol.GUI Widget Showcase", FontSize = 20 });
        root.AddChild(new Separator());

        // Buttons row
        var btnRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 8),
                                 FixedSize = new Vector2(0, 36) };
        var btnPrimary = new Button("Click Me!") { CornerRadius = 6 };
        _statusLabel   = new Label { Text = "Status: waiting…", ForeColor = UIColor.FromHex("#888888") };
        btnPrimary.Clicked += () => { if (_statusLabel != null) _statusLabel.Text = "Status: Button clicked!"; };
        btnRow.AddChild(btnPrimary);
        btnRow.AddChild(new Button("Disabled") { Enabled = false, CornerRadius = 6 });
        btnRow.AddChild(_statusLabel);
        root.AddChild(btnRow);

        // CheckBox + RadioButtons
        var checkRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 16),
                                   FixedSize = new Vector2(0, 28) };
        var chk = new CheckBox { Label = "Enable feature", IsChecked = true };
        chk.CheckedChanged += v => { if (_statusLabel != null) _statusLabel.Text = $"CheckBox: {v}"; };
        checkRow.AddChild(chk);

        var grp = new RadioGroup();
        checkRow.AddChild(new RadioButton(grp, "Option A"));
        checkRow.AddChild(new RadioButton(grp, "Option B") { IsChecked = true });
        checkRow.AddChild(new RadioButton(grp, "Option C"));
        grp.SelectionChanged += r => { if (_statusLabel != null) _statusLabel.Text = $"Radio: {r?.Label}"; };
        root.AddChild(checkRow);

        // Slider
        var sliderRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12),
                                    FixedSize = new Vector2(0, 30) };
        var slider = new Slider { FixedSize = new Vector2(200, 24), Value = _sliderVal };
        var sliderLbl = new Label { Text = $"{_sliderVal:P0}", FixedSize = new Vector2(50, 24) };
        slider.ValueChanged += v => { _sliderVal = v; sliderLbl.Text = $"{v:P0}"; };
        sliderRow.AddChild(new Label { Text = "Slider:", FixedSize = new Vector2(55, 24) });
        sliderRow.AddChild(slider);
        sliderRow.AddChild(sliderLbl);
        root.AddChild(sliderRow);

        // Progress bar
        var pbRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12),
                                FixedSize = new Vector2(0, 30) };
        _progressBar = new ProgressBar { FixedSize = new Vector2(200, 16), Value = _progress, ShowLabel = true };
        pbRow.AddChild(new Label { Text = "Progress:", FixedSize = new Vector2(70, 24) });
        pbRow.AddChild(_progressBar);
        root.AddChild(pbRow);

        // TextBox
        var tbRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12),
                                FixedSize = new Vector2(0, 34) };
        var tb = new TextBox { Text = _textBoxVal, FixedSize = new Vector2(260, 30), Placeholder = "Type here…" };
        tb.TextChanged += t => _textBoxVal = t;
        tbRow.AddChild(new Label { Text = "TextBox:", FixedSize = new Vector2(65, 30) });
        tbRow.AddChild(tb);
        root.AddChild(tbRow);

        // ComboBox
        var cbRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12),
                                FixedSize = new Vector2(0, 34) };
        var cb = new ComboBox { FixedSize = new Vector2(180, 30) };
        cb.SetItems(["Option A", "Option B", "Option C", "Option D"]);
        cb.SelectedIndex = _comboSel;
        cb.SelectionChanged += (i, s) => {
            _comboSel = i;
            if (_statusLabel != null) _statusLabel.Text = $"ComboBox: {s}";
        };
        cbRow.AddChild(new Label { Text = "ComboBox:", FixedSize = new Vector2(75, 30) });
        cbRow.AddChild(cb);
        root.AddChild(cbRow);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 2: Theming ────────────────────────────────────────────────────────
    static Widget BuildThemingTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Theme Switcher", FontSize = 20 });
        root.AddChild(new Separator());

        var themeRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 10),
                                   FixedSize = new Vector2(0, 36) };

        var btnDark  = new Button("Dark Theme")  { CornerRadius = 6, FixedSize = new Vector2(120, 32) };
        var btnLight = new Button("Light Theme") { CornerRadius = 6, FixedSize = new Vector2(120, 32) };
        var previewLabel = new Label { Text = "Current: Dark", ForeColor = UIColor.FromHex("#AAAAAA") };

        btnDark.Clicked += () =>
        {
            ThemeManager.Apply(new DarkTheme());
            previewLabel.Text = "Current: Dark";
            _passAction.colors[0].clear_value = new sg_color { r = 0.18f, g = 0.18f, b = 0.20f, a = 1f };
        };
        btnLight.Clicked += () =>
        {
            ThemeManager.Apply(new LightTheme());
            previewLabel.Text = "Current: Light";
            _passAction.colors[0].clear_value = new sg_color { r = 0.95f, g = 0.95f, b = 0.97f, a = 1f };
        };

        themeRow.AddChild(btnDark);
        themeRow.AddChild(btnLight);
        themeRow.AddChild(previewLabel);
        root.AddChild(themeRow);

        // Custom skin demo
        root.AddChild(new Label { Text = "Custom Skin" });
        var skinBtn = new Button("Apply Skin") { CornerRadius = 6, FixedSize = new Vector2(120, 32) };
        var skinPreview = new Panel { FixedSize = new Vector2(240, 60), CornerRadius = new CornerRadius(8), DrawShadow = true };
        skinPreview.AddChild(new Label { Text = "Skin Preview", Align = TextAlign.Center,
                                         FixedSize = new Vector2(240, 60) });
        skinBtn.Clicked += () =>
        {
            var skin = new Skin(ThemeManager.Current);
            skin.Set("Primary",              UIColor.FromHex("#FF6B35"));  // AccentColor, CheckBox, Slider, ProgressBar
            skin.Set("ButtonGradientTop",    UIColor.FromHex("#FF6B35"));
            skin.Set("ButtonGradientBottom", UIColor.FromHex("#CC5520"));
            skin.Set("ButtonHoverTop",       UIColor.FromHex("#FF8C5A"));
            skin.Set("ButtonHoverBottom",    UIColor.FromHex("#FF6B35"));
            skin.Set("ButtonPressedTop",     UIColor.FromHex("#CC5520"));
            skin.Set("ButtonPressedBottom",  UIColor.FromHex("#99400F"));
            skin.Set("CheckMark",            UIColor.FromHex("#FF6B35"));
            skin.Set("SliderFill",           UIColor.FromHex("#FF6B35"));
            skin.Set("BorderFocus",          UIColor.FromHex("#FF6B35"));
            skinPreview.BackgroundColor = UIColor.FromHex("#FF6B35").WithAlpha(0.15f);
            ThemeManager.Apply(skin);
        };
        root.AddChild(skinBtn);
        root.AddChild(skinPreview);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 3: MVVM ───────────────────────────────────────────────────────────
    static Widget BuildMvvmTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "MVVM Data Binding", FontSize = 20 });
        root.AddChild(new Separator());

        // Register binding properties for the VM.
        BindingRegistry.Register<CounterVM, int>(  "Count",   vm => vm.Count,   (vm, v) => vm.SetCount(v));
        BindingRegistry.Register<CounterVM, string>("Display", vm => vm.Display, null);

        var vm = new CounterVM();
        var ctx = new BindingContext { DataObject = vm };

        var displayLabel = new Label { Text = vm.Display, FontSize = 18 };
        ctx.Bind(vm, "Display", v => displayLabel.Text = (string?)v ?? "");

        var incBtn = new Button("+1")  { CornerRadius = 6, FixedSize = new Vector2(80, 32) };
        var decBtn = new Button("-1")  { CornerRadius = 6, FixedSize = new Vector2(80, 32) };
        var rstBtn = new Button("Reset") { CornerRadius = 6, FixedSize = new Vector2(80, 32) };

        incBtn.Clicked += () => vm.Increment();
        decBtn.Clicked += () => vm.Decrement();
        rstBtn.Clicked += () => vm.Reset();

        var btnRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 8),
                                 FixedSize = new Vector2(0, 36) };
        btnRow.AddChild(incBtn);
        btnRow.AddChild(decBtn);
        btnRow.AddChild(rstBtn);

        root.AddChild(new Label { Text = "Counter (changes reflected live):" });
        root.AddChild(displayLabel);
        root.AddChild(btnRow);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 4: Animation ─────────────────────────────────────────────────────
    static Widget BuildAnimationTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Animation System", FontSize = 20 });
        root.AddChild(new Separator());

        // Animated box — tracks `_animAlpha` driven by a Tween.
        var animBox = new Panel
        {
            FixedSize = new Vector2(120, 80),
            BackgroundColor = UIColor.FromHex("#7BAFD4"),
            CornerRadius = new CornerRadius(8),
        };

        var easingLabel = new Label { Text = "Idle" };

        var btnEaseOut = new Button("EaseOutElastic") { CornerRadius = 6, FixedSize = new Vector2(160, 32) };
        var btnBounce  = new Button("EaseOutBounce")  { CornerRadius = 6, FixedSize = new Vector2(160, 32) };

        btnEaseOut.Clicked += () =>
        {
            _animTween?.Stop();
            _animTween = AnimationManager.Instance!.Animate(
                from: 0.2f, to: 1f, duration: 1.2f,
                onUpdate: v => animBox.BackgroundColor = UIColor.FromHex("#7BAFD4").WithAlpha(v),
                onComplete: () => easingLabel.Text = "Done",
                easing: Easing.EaseOutElastic);
            easingLabel.Text = "EaseOutElastic running…";
        };
        btnBounce.Clicked += () =>
        {
            _animTween?.Stop();
            _animTween = AnimationManager.Instance!.Animate(
                from: 0f, to: 1f, duration: 1.0f,
                onUpdate: v => animBox.BackgroundColor = UIColor.FromHex("#F28B82").WithAlpha(v),
                onComplete: () => easingLabel.Text = "Done",
                easing: Easing.EaseOutBounce);
            easingLabel.Text = "EaseOutBounce running…";
        };

        var btnRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 8),
                                 FixedSize = new Vector2(0, 36) };
        btnRow.AddChild(btnEaseOut);
        btnRow.AddChild(btnBounce);

        root.AddChild(animBox);
        root.AddChild(easingLabel);
        root.AddChild(btnRow);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 5: XML Markup ─────────────────────────────────────────────────────
    static Widget BuildXmlTab()
    {
        const string xml = """
            <Panel Layout="stack" Spacing="10" Padding="12">
              <Label Text="XML Markup Demo" />
              <Separator />
              <Label Text="This panel was constructed entirely from XML." Wrap="Wrap" />
              <Button Id="xml_btn" Text="XML Button" CornerRadius="6" Width="160" Height="32" />
              <CheckBox Label="XML CheckBox" IsChecked="true" />
              <TextBox Placeholder="XML TextBox" Width="240" Height="30" />
            </Panel>
            """;

        Widget root;
        try
        {
            root = XmlLoader.Load(xml);
            // Wire up XML button if found.
            var btn = root.FindById("xml_btn") as Button;
            var lbl = new Label { Text = "" };
            if (btn != null) btn.Clicked += () => lbl.Text = "XML button clicked!";
            root.AddChild(lbl);
        }
        catch (Exception ex)
        {
            root = new Label { Text = $"XML error: {ex.Message}" };
        }

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 6: Extended Widgets ───────────────────────────────────────────────
    static Widget BuildExtendedTab()
    {
        var inner = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                                Padding = new Thickness(12) };

        inner.AddChild(new Label { Text = "Extended Widget Library", FontSize = 20 });
        inner.AddChild(new Separator());

        // ── Event log (created first so events can reference it) ──────────────
        var logBox = new ListBox { FixedSize = new Vector2(0, 90), MultiSelect = true };
        void Log(string msg)
        {
            string ts = System.DateTime.Now.ToString("HH:mm:ss");
            logBox.AddItem($"[{ts}] {msg}");
            logBox.ScrollToBottom();  // scroll without clearing multi-selection
        }

        // ── Row 1: SpinBox + NumberInput ──────────────────────────────────────
        var row1 = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12),
                               FixedSize = new Vector2(0, 32) };
        var spinLbl = new Label { Text = "Value: 5.0", FixedSize = new Vector2(90, 28) };
        var spin    = new SpinBox { Min = 0, Max = 100, Step = 0.5f, Value = 5f,
                                    FixedSize = new Vector2(120, 28) };
        spin.ValueChanged += v => { spinLbl.Text = $"Value: {v:F1}"; Log($"SpinBox: {v:F1}"); };

        var numInput = new NumberInput { Value = 42f, Min = 0f, Max = 999f, DecimalPlaces = 1,
                                          FixedSize = new Vector2(100, 28), Placeholder = "0.0" };
        numInput.ValueCommitted += v => Log($"NumberInput committed: {v:F1}");
        row1.AddChild(new Label { Text = "SpinBox:", FixedSize = new Vector2(60, 28) });
        row1.AddChild(spin);
        row1.AddChild(spinLbl);
        row1.AddChild(new Label { Text = "  Number:", FixedSize = new Vector2(60, 28) });
        row1.AddChild(numInput);
        inner.AddChild(row1);

        // ── Row 2: ColorButton ────────────────────────────────────────────────
        var row2 = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12),
                               FixedSize = new Vector2(0, 32) };
        var colorLbl = new Label { Text = "#FF6B35FF", FixedSize = new Vector2(100, 28) };
        var colorBtn = new ColorButton(UIColor.FromHex("#FF6B35")) { FixedSize = new Vector2(72, 28) };
        colorBtn.ColorChanged += c => { colorLbl.Text = c.ToString(); Log($"ColorButton: {c}"); };
        row2.AddChild(new Label { Text = "Color:", FixedSize = new Vector2(50, 28) });
        row2.AddChild(colorBtn);
        row2.AddChild(colorLbl);
        inner.AddChild(row2);

        // ── GroupBox with CheckBoxes ───────────────────────────────────────────
        var group = new GroupBox { Title = "Options Group", FixedSize = new Vector2(320, 80) };
        var gbStack = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12) };
        var cbA = new CheckBox { Label = "Option A", IsChecked = true };
        var cbB = new CheckBox { Label = "Option B" };
        var cbC = new CheckBox { Label = "Option C" };
        cbA.CheckedChanged += v => Log($"CheckBox A: {v}");
        cbB.CheckedChanged += v => Log($"CheckBox B: {v}");
        cbC.CheckedChanged += v => Log($"CheckBox C: {v}");
        gbStack.AddChild(cbA);
        gbStack.AddChild(cbB);
        gbStack.AddChild(cbC);
        group.AddChild(gbStack);
        inner.AddChild(group);

        // ── Accordion ─────────────────────────────────────────────────────────
        inner.AddChild(new Label { Text = "Accordion (click headers to expand/collapse):" });
        var accordion = new Accordion { FixedSize = new Vector2(320, 0), Exclusive = true };
        string[] accordionTitles = ["Section One", "Section Two", "Section Three"];

        var sec1 = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 6),
                               Padding = new Thickness(8) };
        sec1.AddChild(new Label { Text = "This is the first section content." });
        var actionBtn = new Button("Action") { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        actionBtn.Clicked += () => Log("Accordion Button: Action clicked");
        sec1.AddChild(actionBtn);
        accordion.AddSection("Section One", sec1, expanded: true);

        var sec2 = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 6),
                               Padding = new Thickness(8) };
        sec2.AddChild(new Label { Text = "Second section with a slider." });
        var accSlider = new Slider { FixedSize = new Vector2(200, 22), Value = 0.3f };
        accSlider.ValueChanged += v => Log($"Accordion Slider: {v:F2}");
        sec2.AddChild(accSlider);
        accordion.AddSection("Section Two", sec2);

        var sec3 = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 6),
                               Padding = new Thickness(8) };
        sec3.AddChild(new Label { Text = "Third section — click to expand." });
        accordion.AddSection("Section Three", sec3);

        accordion.SectionToggled += i =>
        {
            string title = i >= 0 && i < accordionTitles.Length ? accordionTitles[i] : $"#{i}";
            Log($"Accordion: {title} toggled");
        };

        inner.AddChild(accordion);

        // ── SplitView: TreeView (left) + ListBox (right) ──────────────────────
        inner.AddChild(new Label { Text = "SplitView (double-click or ▸ to expand, Option+→ recursive):" });

        var tree = new TreeView { FixedSize = new Vector2(0, 0) };
        var rootNode = new TreeNode { Label = "Root" };
        var childA   = new TreeNode { Label = "Folder A" };
        childA.Children.Add(new TreeNode { Label = "Item A1" });
        childA.Children.Add(new TreeNode { Label = "Item A2" });
        // Widget content node: a Button inside the tree
        childA.Children.Add(new TreeNode { Label = "Widget Item", Content = new Button("Click Me") { CornerRadius = 3, FixedSize = new Vector2(80, 18) } });
        rootNode.Children.Add(childA);
        var childB = new TreeNode { Label = "Folder B" };
        childB.Children.Add(new TreeNode { Label = "Item B1" });
        // Widget content node: a CheckBox inside the tree
        childB.Children.Add(new TreeNode { Label = "Check Option", Content = new CheckBox { Label = "Opt-in", FixedSize = new Vector2(100, 18) } });
        rootNode.Children.Add(childB);
        rootNode.Children.Add(new TreeNode { Label = "File C" });
        rootNode.IsExpanded = true;
        tree.Root = rootNode;

        var listBox = new ListBox { FixedSize = new Vector2(0, 0) };
        // Mix of string + widget items in ListBox
        listBox.SetItems(["Apple", "Banana", "Cherry", "Dragonfruit", "Elderberry",
                          "Fig", "Grape", "Honeydew", "Kiwi", "Lemon"]);
        tree.SelectionChanged += n => { listBox.AddItem(n.Label + " →"); Log($"TreeView selected: {n.Label}"); };
        listBox.SelectionChanged += i =>
        {
            if (i >= 0 && i < listBox.Items.Count) Log($"ListBox selected: {listBox.Items[i]}");
        };

        var split = new SplitView
        {
            First      = tree,
            Second     = listBox,
            SplitRatio = 0.4f,
            FixedSize  = new Vector2(0, 160),
        };
        inner.AddChild(split);

        // ── Notification triggers ─────────────────────────────────────────────
        inner.AddChild(new Separator());
        inner.AddChild(new Label { Text = "Toast Notifications:" });
        var notifRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 8),
                                   FixedSize = new Vector2(0, 34) };
        var btnInfo    = new Button("Info")    { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        var btnSuccess = new Button("Success") { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        var btnWarning = new Button("Warning") { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        var btnError   = new Button("Error")   { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        btnInfo   .Clicked += () => { Notification.Show("This is an info message.",    2.5f, NotificationType.Info);    Log("Toast: Info"); };
        btnSuccess.Clicked += () => { Notification.Show("Operation succeeded!",        2.5f, NotificationType.Success); Log("Toast: Success"); };
        btnWarning.Clicked += () => { Notification.Show("Warning: check your input.",  2.5f, NotificationType.Warning); Log("Toast: Warning"); };
        btnError  .Clicked += () => { Notification.Show("Error: something went wrong!", 3f,  NotificationType.Error);   Log("Toast: Error"); };
        notifRow.AddChild(btnInfo);
        notifRow.AddChild(btnSuccess);
        notifRow.AddChild(btnWarning);
        notifRow.AddChild(btnError);
        inner.AddChild(notifRow);

        // ── Event Log display ─────────────────────────────────────────────────
        inner.AddChild(new Separator());
        inner.AddChild(new Label { Text = "Event Log (select row + Ctrl+C to copy):" });
        inner.AddChild(logBox);

        return new ScrollView
        {
            Content = inner,
            CanScrollVertical = true,
        };
    }

    // ── Tab 7: More Widgets ───────────────────────────────────────────────────
    static Widget BuildMoreTab()
    {
        // root: MenuBar + ToolBar at top, scrollable content in middle, StatusBar at bottom
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 0),
                               Padding = new Thickness(0) };

        // ── MenuBar ───────────────────────────────────────────────────────────
        var menuBar = new MenuBar { FixedSize = new Vector2(0, MenuBar.DefaultHeight) };
        var fileMenu = menuBar.AddMenu("File");
        fileMenu.Items.Add(new MenuItem { Label = "New",  Shortcut = "Ctrl+N", OnClick = () => Notification.Show("File → New",  1.5f, NotificationType.Info) });
        fileMenu.Items.Add(new MenuItem { Label = "Open", Shortcut = "Ctrl+O", OnClick = () => Notification.Show("File → Open", 1.5f, NotificationType.Info) });
        fileMenu.Items.Add(new MenuItem { IsSep = true });
        fileMenu.Items.Add(new MenuItem { Label = "Exit", OnClick = () => Notification.Show("File → Exit", 1.5f, NotificationType.Warning) });
        var editMenu = menuBar.AddMenu("Edit");
        editMenu.Items.Add(new MenuItem { Label = "Undo",  Shortcut = "Ctrl+Z" });
        editMenu.Items.Add(new MenuItem { Label = "Redo",  Shortcut = "Ctrl+Y" });
        editMenu.Items.Add(new MenuItem { IsSep = true });
        editMenu.Items.Add(new MenuItem { Label = "Cut",   Shortcut = "Ctrl+X" });
        editMenu.Items.Add(new MenuItem { Label = "Copy",  Shortcut = "Ctrl+C" });
        editMenu.Items.Add(new MenuItem { Label = "Paste", Shortcut = "Ctrl+V" });
        var viewMenu = menuBar.AddMenu("View");
        viewMenu.Items.Add(new MenuItem { Label = "Zoom In",     Shortcut = "Ctrl++" });
        viewMenu.Items.Add(new MenuItem { Label = "Zoom Out",    Shortcut = "Ctrl+-" });
        viewMenu.Items.Add(new MenuItem { IsSep = true });
        viewMenu.Items.Add(new MenuItem { Label = "Full Screen", IsChecked = false });
        root.AddChild(menuBar);

        // ── ToolBar ───────────────────────────────────────────────────────────
        var toolBar = new ToolBar { FixedSize = new Vector2(0, ToolBar.DefaultItemSize + 8f) };
        toolBar.AddButton("New",  onClick: () => Notification.Show("New",    1.5f, NotificationType.Info),    tooltip: "Create new file");
        toolBar.AddButton("Open", onClick: () => Notification.Show("Open",   1.5f, NotificationType.Info),    tooltip: "Open file");
        toolBar.AddButton("Save", onClick: () => Notification.Show("Saved",  1.5f, NotificationType.Success), tooltip: "Save");
        toolBar.AddSeparator();
        var boldToggle   = toolBar.AddToggle("Bold",   tooltip: "Toggle bold");
        var italicToggle = toolBar.AddToggle("Italic", tooltip: "Toggle italic");
        toolBar.AddSeparator();
        toolBar.AddButton("Undo", onClick: () => Notification.Show("Undo",  1.5f, NotificationType.Info), tooltip: "Undo last action");
        toolBar.AddButton("Redo", onClick: () => Notification.Show("Redo",  1.5f, NotificationType.Info), tooltip: "Redo");
        root.AddChild(toolBar);

        // ── Scrollable content area ───────────────────────────────────────────
        // Window = 640, tab header ~36, menubar 26, toolbar 36, statusbar 22 => ~520 available
        var inner = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                                Padding = new Thickness(12) };

        // Section: TextArea + RichLabel side by side
        inner.AddChild(new Label { Text = "TextArea  /  RichLabel" });
        var textAreaSplit = new SplitView
        {
            First = new TextArea
            {
                Text = "TextArea is a read-only multi-line text widget.\n\n" +
                       "It uses renderer.DrawTextBox() to wrap long lines automatically.\n\n" +
                       "Useful for logs, documentation, or any read-only content.",
                Padding = new Thickness(6),
            },
            Second = new RichLabel
            {
                Text = "RichLabel supports [b]bold[/b] text and\n" +
                       "[color=#4CAAFF]colored[/color] spans.\n\n" +
                       "Font size: [size=18]large[/size] or [size=10]small[/size].\n\n" +
                       "Click a [link=https://github.com]link[/link] to fire an event.",
                Padding = new Thickness(6),
            },
            SplitRatio = 0.5f,
            FixedSize   = new Vector2(0, 130),
        };
        if (textAreaSplit.Second is RichLabel rl)
            rl.LinkClicked += url => Notification.Show($"Link: {url}", 2.5f, NotificationType.Info);
        inner.AddChild(textAreaSplit);

        // Section: ScrollView
        inner.AddChild(new Separator());
        inner.AddChild(new Label { Text = "ScrollView (scroll inside)" });
        var scrollContent = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 4),
                                        Padding = new Thickness(6) };
        for (int i = 1; i <= 20; i++)
            scrollContent.AddChild(new Label { Text = $"Scrollable row {i}" });
        inner.AddChild(new ScrollView
        {
            Content = scrollContent,
            CanScrollVertical = true,
            FixedSize = new Vector2(0, 110),
        });

        // Section: VirtualList
        inner.AddChild(new Separator());
        inner.AddChild(new Label { Text = "VirtualList (100 items, virtualized)" });
        var vlistData = new List<object>();
        for (int i = 1; i <= 100; i++) vlistData.Add((object)$"VirtualList item #{i:D3}");
        var vlist = new VirtualList
        {
            ItemsSource  = vlistData,
            ItemTemplate = obj =>
            {
                var row = new Panel
                {
                    Layout    = new BoxLayout(Orientation.Horizontal, Alignment.Center, 6),
                    Padding   = new Thickness(4, 0, 4, 0),
                    FixedSize = new Vector2(0, 26),
                };
                row.AddChild(new Label { Text = obj.ToString() ?? "" });
                return row;
            },
            FixedSize = new Vector2(0, 110),
        };
        vlist.SelectionChanged += i => Notification.Show($"VirtualList → item {i + 1}", 1.5f, NotificationType.Info);
        inner.AddChild(vlist);

        // Section: Multi-select TreeView + ListBox
        inner.AddChild(new Separator());
        inner.AddChild(new Label { Text = "Multi-select TreeView / ListBox (Ctrl+Click, dbl-click expand)" });
        var msTree = new TreeView { MultiSelect = true };
        var msRoot = new TreeNode { Label = "Projects" };
        var proj1  = msRoot.Add("Project Alpha");
        proj1.Add("main.cs"); proj1.Add("app.json");
        var proj2  = msRoot.Add("Project Beta");
        proj2.Add("index.html"); proj2.Add("style.css"); proj2.Add("app.js");
        msRoot.Add("README.md");
        msRoot.IsExpanded = true; proj1.IsExpanded = true; proj2.IsExpanded = true;
        msTree.Root = msRoot;
        msTree.SelectionChanged += n => Notification.Show($"TreeView (multi) → {n.Label}", 1.5f, NotificationType.Info);

        var msListBox = new ListBox { MultiSelect = true };
        msListBox.SetItems(["Alpha", "Beta", "Gamma", "Delta", "Epsilon",
                             "Zeta", "Eta", "Theta", "Iota", "Kappa"]);
        msListBox.SelectionChanged += i =>
        {
            if (i >= 0 && i < msListBox.Items.Count)
                Notification.Show($"ListBox (multi) → {msListBox.Items[i]}", 1.5f, NotificationType.Info);
        };

        inner.AddChild(new SplitView
        {
            First      = msTree,
            Second     = msListBox,
            SplitRatio = 0.5f,
            FixedSize  = new Vector2(0, 150),
        });

        // Section: Tooltip / ContextMenu / Image
        inner.AddChild(new Separator());
        inner.AddChild(new Label { Text = "Tooltip  /  ContextMenu  /  Image" });
        var demoRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 10),
                                   FixedSize = new Vector2(0, 130) };

        var tooltipBtn = new Button("Hover me")
        {
            CornerRadius = 4, FixedSize = new Vector2(100, 28),
            Tooltip = "This is a tooltip shown on hover.",
        };

        var ctxBtn = new Button("Context Menu") { CornerRadius = 4, FixedSize = new Vector2(120, 28) };
        ctxBtn.Clicked += () =>
        {
            var items = new[]
            {
                new MenuItem { Label = "Copy",   OnClick = () => Notification.Show("Context: Copy",   1.5f, NotificationType.Info) },
                new MenuItem { Label = "Paste",  OnClick = () => Notification.Show("Context: Paste",  1.5f, NotificationType.Info) },
                new MenuItem { IsSep = true },
                new MenuItem { Label = "Delete", OnClick = () => Notification.Show("Context: Delete", 1.5f, NotificationType.Warning) },
            };
            ContextMenu.Show(items, ctxBtn.ScreenPosition + new Vector2(0, ctxBtn.Bounds.Height));
        };

        _baboonImg = new Image
        {
            FixedSize = new Vector2(120, 120), KeepAspect = true,
            Tooltip = "baboon.png loaded via NanoVG",
        };
        var imgWidget = _baboonImg;

        demoRow.AddChild(tooltipBtn);
        demoRow.AddChild(ctxBtn);
        demoRow.AddChild(imgWidget);
        inner.AddChild(demoRow);

        // Wrap inner in a ScrollView so content doesn't overflow the tab area
        root.AddChild(new ScrollView
        {
            Content = inner,
            CanScrollVertical = true,
            Expand = true,
        });

        // ── StatusBar ─────────────────────────────────────────────────────────
        var statusBar  = new StatusBar { FixedSize = new Vector2(0, StatusBar.DefaultHeight) };
        var statusItem = statusBar.AddItem("Ready", weight: 1f);
        boldToggle.OnClick   = () => { statusItem.Text = boldToggle.Pressed   ? "Bold ON"   : "Bold OFF";   };
        italicToggle.OnClick = () => { statusItem.Text = italicToggle.Pressed ? "Italic ON" : "Italic OFF"; };
        statusBar.AddItem("Ln 1, Col 1", weight: null);
        statusBar.AddItem("UTF-8",       weight: null);
        root.AddChild(statusBar);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 4b: MVC ───────────────────────────────────────────────────────────
    static Widget BuildMvcTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "MVC Pattern Demo", FontSize = 20 });
        root.AddChild(new Separator());

        // ─── Model ────────────────────────────────────────────────────────────
        var model = new TodoListModel();

        // ─── View ─────────────────────────────────────────────────────────────
        var listBox = new ListBox { FixedSize = new Vector2(0, 180) };
        var statusLabel = new Label { Text = $"0 items  |  0 done" };

        void RefreshView()
        {
            listBox.SetItems([]);
            foreach (var item in model.Items)
                listBox.AddItem((item.Done ? "☑ " : "☐ ") + item.Text);
            int done = 0;
            foreach (var i in model.Items) if (i.Done) done++;
            statusLabel.Text = $"{model.Items.Count} items  |  {done} done";
        }

        // ─── Controller actions ────────────────────────────────────────────────
        var inputBox = new TextBox { Placeholder = "New task…", FixedSize = new Vector2(220, 30) };

        var addBtn    = new Button("Add")    { CornerRadius = 6, FixedSize = new Vector2(70, 30) };
        var toggleBtn = new Button("Toggle") { CornerRadius = 6, FixedSize = new Vector2(70, 30) };
        var removeBtn = new Button("Remove") { CornerRadius = 6, FixedSize = new Vector2(80, 30) };
        var clearBtn  = new Button("Clear")  { CornerRadius = 6, FixedSize = new Vector2(70, 30) };

        addBtn.Clicked += () =>
        {
            var text = inputBox.Text.Trim();
            if (text.Length == 0) return;
            model.Add(text);
            inputBox.Text = "";
            RefreshView();
        };
        toggleBtn.Clicked += () =>
        {
            int sel = listBox.SelectedIndex;
            if (sel < 0 || sel >= model.Items.Count) return;
            model.Toggle(sel);
            RefreshView();
        };
        removeBtn.Clicked += () =>
        {
            int sel = listBox.SelectedIndex;
            if (sel < 0 || sel >= model.Items.Count) return;
            model.Remove(sel);
            RefreshView();
        };
        clearBtn.Clicked += () => { model.Clear(); RefreshView(); };

        // Seed a few items
        model.Add("Buy groceries");
        model.Add("Read a book");
        model.Add("Ship it");
        model.Toggle(2);
        RefreshView();

        var inputRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 8),
                                   FixedSize = new Vector2(0, 36) };
        inputRow.AddChild(inputBox);
        inputRow.AddChild(addBtn);

        var actionRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 8),
                                    FixedSize = new Vector2(0, 36) };
        actionRow.AddChild(toggleBtn);
        actionRow.AddChild(removeBtn);
        actionRow.AddChild(clearBtn);

        root.AddChild(new Label { Text = "Todo list (Model ↔ Controller ↔ View):" });
        root.AddChild(listBox);
        root.AddChild(statusLabel);
        root.AddChild(inputRow);
        root.AddChild(actionRow);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 9: Color Picker ───────────────────────────────────────────────────
    static Widget BuildColorPickerTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Color Picker", FontSize = 20 });
        root.AddChild(new Separator());

        var hexLabel  = new Label { Text = "Selected: #FF0000FF" };
        var previewBox = new Panel
        {
            FixedSize       = new Vector2(0, 28),
            BackgroundColor = UIColor.Red,
            CornerRadius    = new CornerRadius(6),
        };

        var picker = new ColorPicker
        {
            Color     = UIColor.Red,
            ShowAlpha = true,
        };
        picker.ColorChanged += c =>
        {
            hexLabel.Text = $"Selected: {c}";
            previewBox.BackgroundColor = c;
        };

        root.AddChild(new Label { Text = "Pick a color:" });
        root.AddChild(picker);
        root.AddChild(previewBox);
        root.AddChild(hexLabel);

        // Preset swatches
        root.AddChild(new Separator());
        root.AddChild(new Label { Text = "Preset swatches (click to load):" });
        var swatchRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 8),
                                    FixedSize = new Vector2(0, 36) };
        UIColor[] presets = [
            UIColor.Red, UIColor.FromHex("#00B050"), UIColor.FromHex("#0070C0"),
            UIColor.FromHex("#FF6B35"), UIColor.FromHex("#9B59B6"), UIColor.FromHex("#1ABC9C"),
        ];
        foreach (var c in presets)
        {
            var col = c;
            var btn = new Button { FixedSize = new Vector2(32, 28), BackColor = col, CornerRadius = 6f };
            btn.Clicked += () => { picker.Color = col; hexLabel.Text = $"Selected: {col}"; previewBox.BackgroundColor = col; };
            swatchRow.AddChild(btn);
        }
        root.AddChild(swatchRow);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 10: Property Grid ─────────────────────────────────────────────────
    static Widget BuildPropertyGridTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Property Grid", FontSize = 20 });
        root.AddChild(new Separator());

        // Live-preview panel driven by property grid edits
        var previewLabel = new Label { Text = "Hello, PropertyGrid!", FontSize = 16 };
        var previewPanel = new Panel
        {
            BackgroundColor = UIColor.FromHex("#E8F4FD"),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(10),
            FixedSize       = new Vector2(0, 60),
        };
        previewPanel.AddChild(previewLabel);

        // A demo object to edit
        var demo = new DemoObject
        {
            Title       = "Hello, PropertyGrid!",
            FontSize    = 16f,
            BackColor   = UIColor.FromHex("#E8F4FD"),
            Visible     = true,
            BorderWidth = 0f,
        };

        var grid = new PropertyGrid { Expand = true };
        grid.AddProperty<string>("Title",       () => demo.Title,
            v => { demo.Title = v ?? ""; previewLabel.Text = demo.Title; }, "Appearance");
        grid.AddProperty<float>("Font Size",    () => demo.FontSize,
            v => { demo.FontSize = v; previewLabel.FontSize = v; }, "Appearance");
        grid.AddProperty<UIColor>("Back Color", () => demo.BackColor,
            v => { demo.BackColor = v; previewPanel.BackgroundColor = v; }, "Appearance");
        grid.AddProperty<bool>("Visible",       () => demo.Visible,
            v => { demo.Visible = v; previewPanel.Visible = v; }, "Behaviour");
        grid.AddProperty<float>("Border Width", () => demo.BorderWidth,
            v => { demo.BorderWidth = v; previewPanel.BorderWidth = v; }, "Behaviour");

        root.AddChild(new Label { Text = "Live preview (edit properties below):" });
        root.AddChild(previewPanel);
        root.AddChild(new Separator());
        root.AddChild(new Label { Text = "Properties:" });
        root.AddChild(grid);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 11: Rich Text & Notifications ─────────────────────────────────────
    static Widget BuildRichTextTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Rich Text & Notifications", FontSize = 20 });
        root.AddChild(new Separator());

        // ── Rich Label demo ────────────────────────────────────────────────────
        root.AddChild(new Label { Text = "Inline markup rendering:", FontSize = 14 });

        var rl1 = new RichLabel
        {
            Text      = "Normal text, [b]bold text[/b], and [color=#E53935]red colored[/color] text.",
            Wrap      = TextWrap.Wrap,
            FontSize  = 14,
        };
        var rl2 = new RichLabel
        {
            Text     = "[size=20]Large title[/size]  [size=11]small caption[/size]",
            FontSize = 14,
        };
        var rl3 = new RichLabel
        {
            Text     = "Click a link: [link=https://github.com]GitHub[/link] or [link=https://dotnet.microsoft.com].NET[/link]",
            FontSize = 14,
        };
        var linkStatus = new Label { Text = "Link: (none clicked)", FontSize = 12 };
        rl3.LinkClicked += url => linkStatus.Text = $"Link clicked: {url}";

        root.AddChild(rl1);
        root.AddChild(rl2);
        root.AddChild(rl3);
        root.AddChild(linkStatus);

        root.AddChild(new Separator());

        // ── Notification triggers ──────────────────────────────────────────────
        root.AddChild(new Label { Text = "Toast notifications:", FontSize = 14 });

        var durationSlider = new Slider { Value = 0.5f, FixedSize = new Vector2(180, 20) };
        var durationLabel  = new Label  { Text = "Duration: 3.0s" };
        durationSlider.ValueChanged += v => durationLabel.Text = $"Duration: {1f + v * 4f:F1}s";

        var dRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 8),
                               FixedSize = new Vector2(0, 28) };
        dRow.AddChild(new Label { Text = "Duration:", FixedSize = new Vector2(65, 24) });
        dRow.AddChild(durationSlider);
        dRow.AddChild(durationLabel);
        root.AddChild(dRow);

        var notifRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 8),
                                   FixedSize = new Vector2(0, 34) };
        var btnInfo    = new Button("Info")    { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        var btnSuccess = new Button("Success") { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        var btnWarning = new Button("Warning") { CornerRadius = 4, FixedSize = new Vector2(80, 28) };
        var btnError   = new Button("Error")   { CornerRadius = 4, FixedSize = new Vector2(80, 28) };

        btnInfo   .Clicked += () => Notification.Show("Info toast message.",              1f + durationSlider.Value * 4f, NotificationType.Info);
        btnSuccess.Clicked += () => Notification.Show("Success! Operation completed.",    1f + durationSlider.Value * 4f, NotificationType.Success);
        btnWarning.Clicked += () => Notification.Show("Warning: please review your work.",1f + durationSlider.Value * 4f, NotificationType.Warning);
        btnError  .Clicked += () => Notification.Show("Error: an unexpected failure.",    1f + durationSlider.Value * 4f, NotificationType.Error);

        notifRow.AddChild(btnInfo);
        notifRow.AddChild(btnSuccess);
        notifRow.AddChild(btnWarning);
        notifRow.AddChild(btnError);
        root.AddChild(notifRow);

        root.AddChild(new Separator());

        // ── Multi-line rich text ───────────────────────────────────────────────
        root.AddChild(new Label { Text = "Multi-line rich text:", FontSize = 14 });
        var rl4 = new RichLabel
        {
            Text     = "[b]Sokol.GUI[/b] is a [color=#0070C0]cross-platform[/color] immediate-mode inspired retained GUI toolkit.\n" +
                       "It supports [b]bold[/b], [color=#E53935]colored[/color], [size=18]sized[/size] and [link=url]linked[/link] text inline.\n" +
                       "Runs on [color=#2E7D32]macOS[/color], [color=#1565C0]Windows[/color], [color=#F57F17]iOS[/color], [color=#4A148C]Android[/color] and [color=#BF360C]WebAssembly[/color].",
            Wrap     = TextWrap.Wrap,
            FontSize = 14,
        };
        rl4.LinkClicked += url => Notification.Show($"Link: {url}", 2f, NotificationType.Info);
        root.AddChild(rl4);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab 12: BiDi ──────────────────────────────────────────────────────────
    static Widget BuildBiDiTab()
    {
        var root = new Panel
        {
            Layout  = new BoxLayout(Orientation.Vertical, Alignment.Start, 8),
            Padding = new Thickness(12)
        };

        root.AddChild(new Label { Text = "Editable Multi-line TextArea", FontSize = 20 });

        var textArea = new TextArea
        {
            IsEditable = true,
            Text = "Hello World!\nשלום עולם\nمرحبا بالعالم\nMixed: Hello שלום World عالم\n\nType here to experiment with BiDi text...",
            Padding    = new Thickness(6),
            FixedSize  = new Vector2(0, 300),
            FontSize   = 18
        };
        root.AddChild(textArea);

        root.AddChild(new Label { Text = "Read-only TextArea", FontSize = 16 });
        root.AddChild(new TextArea
        {
            Text = "This TextArea is read-only (IsEditable = false).\nשלום — Hebrew\nمرحبا — Arabic\nLTR and RTL text.",
            Padding   = new Thickness(6),
            FixedSize = new Vector2(0, 120),
            FontSize  = 16
        });

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab: Horizontal Scroll Test ────────────────────────────────────────────
    static Widget BuildHScrollTestTab()
    {
        var root = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
                               Padding = new Thickness(12) };

        root.AddChild(new Label { Text = "Horizontal Scroll Test", FontSize = 20 });
        root.AddChild(new Label { Text = "Resize the window narrower to see the horizontal scrollbar appear." });
        root.AddChild(new Separator());

        // Wide row of buttons
        var btnRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 8) };
        for (int i = 1; i <= 8; i++)
            btnRow.AddChild(new Button($"Button {i}") { FixedSize = new Vector2(120, 32), CornerRadius = 6 });
        root.AddChild(btnRow);

        root.AddChild(new Separator());

        // Wide text box
        root.AddChild(new Label { Text = "Wide TextBox (800px):" });
        root.AddChild(new TextBox { Text = "This is a very wide text box to test horizontal scrolling.",
                                    FixedSize = new Vector2(800, 30) });

        root.AddChild(new Separator());

        // Wide slider row
        var sliderRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 12) };
        sliderRow.AddChild(new Label { Text = "Wide Slider:" });
        sliderRow.AddChild(new Slider { FixedSize = new Vector2(600, 24), Value = 0.5f });
        sliderRow.AddChild(new Label { Text = "50%" });
        root.AddChild(sliderRow);

        root.AddChild(new Separator());

        // Wide progress bar
        root.AddChild(new Label { Text = "Wide ProgressBar (700px):" });
        root.AddChild(new ProgressBar { FixedSize = new Vector2(700, 18), Value = 0.65f, ShowLabel = true });

        root.AddChild(new Separator());

        // Row of color buttons
        var colorRow = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Center, 6) };
        colorRow.AddChild(new Label { Text = "Colors:" });
        UIColor[] colors = [ UIColor.Red, UIColor.Green, UIColor.Blue,
                             UIColor.FromHex("#FF8800"), UIColor.FromHex("#8800FF"),
                             UIColor.FromHex("#00CCCC"), UIColor.FromHex("#CC0066"),
                             UIColor.FromHex("#66CC00"), UIColor.FromHex("#0066CC"),
                             UIColor.FromHex("#CC6600") ];
        foreach (var c in colors)
            colorRow.AddChild(new Button { BackColor = c, FixedSize = new Vector2(60, 28), CornerRadius = 4 });
        root.AddChild(colorRow);

        return new ScrollView { Content = root, CanScrollVertical = true };
    }

    // ── Tab: Docking ─────────────────────────────────────────────────────────
    static Widget BuildDockingTab()
    {
        var host = new Panel { Layout = new DockLayout(), Padding = new Thickness(8) };

        var help = new Label
        {
            Text = "Drag tabs between panels to split. Drag divider bars to resize.",
            FontSize = 13,
            FixedSize = new Vector2(0, 22),
        };
        DockLayout.SetDock(help, DockPosition.Top);
        host.AddChild(help);

        var dock = new DockSpace();
        host.AddChild(dock);

        var floatingHost = new FloatingPanelHost();
        Screen.Instance.AddChild(floatingHost);
        var dm = new DockManager(dock, floatingHost);

        var outline = new ListBox();
        outline.AddItem("App"); outline.AddItem("├─ MainWindow"); outline.AddItem("└─ StatusBar");

        var props = new PropertyGrid();
        props.Target = new DemoObject { Title = "Docked", FontSize = 14f, Visible = true };

        var log = new TextArea
        {
            Text = "INFO: docking demo ready\nINFO: try dragging tabs around\n",
        };

        var scene = new ListBox();
        scene.AddItem("Scene"); scene.AddItem("├─ Camera"); scene.AddItem("├─ DirectionalLight");
        scene.AddItem("└─ Mesh");

        var inspector = new PropertyGrid();
        inspector.Target = new DemoObject { Title = "Selected", FontSize = 12f, Visible = true };

        var console = new TextArea
        {
            Text = "[INFO]  Application started\n[DEBUG] Renderer: Metal\n[INFO]  Scene loaded: 4 nodes\n",
        };

        var searchBox = new TextBox { Placeholder = "Search assets...", FixedSize = new Vector2(0, 28) };

        var assetList = new ListBox();
        assetList.AddItem("texture_albedo.png");
        assetList.AddItem("texture_normal.png");
        assetList.AddItem("mesh_cube.glb");
        assetList.AddItem("mesh_sphere.glb");
        assetList.AddItem("material_pbr.json");
        assetList.AddItem("shader_default.glsl");

        var assetPanel = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Stretch, 4), Padding = new Thickness(4) };
        assetPanel.AddChild(searchBox);
        assetPanel.AddChild(assetList);

        dm.CreatePanel("outline",   "Outline",    outline);
        dm.CreatePanel("scene",     "Scene",      scene,     target: dock.Root, zone: DockDropZone.Center);
        dm.CreatePanel("assets",    "Assets",     assetPanel, target: dock.Root, zone: DockDropZone.Bottom);
        dm.CreatePanel("props",     "Properties", props,     target: dock.Root, zone: DockDropZone.Right);
        dm.CreatePanel("inspector", "Inspector",  inspector, target: dock.Root, zone: DockDropZone.Center);
        dm.CreatePanel("log",       "Output",     log,       target: dock.Root, zone: DockDropZone.Bottom);
        dm.CreatePanel("console",   "Console",    console,   target: dock.Root, zone: DockDropZone.Center);

        return host;
    }

    // ── Tab: Layout Save/Load ────────────────────────────────────────────────
    static Widget BuildLayoutTab()
    {
        var root = new Panel
        {
            Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 8),
            Padding = new Thickness(12),
        };

        root.AddChild(new Label { Text = "Layout Persistence", FontSize = 20 });
        root.AddChild(new Label
        {
            Text = "Capture the current dock layout to JSON, then restore it.",
            FontSize = 13,
        });

        var dockHost = new Panel { Layout = new DockLayout() };
        dockHost.FixedSize = new Vector2(0, 380);
        var dock = new DockSpace();
        dockHost.AddChild(dock);
        var floatingHost = new FloatingPanelHost();
        Screen.Instance.AddChild(floatingHost);
        var dm = new DockManager(dock, floatingHost);

        dm.CreatePanel("a", "Alpha",   new Label { Text = "Alpha content" });
        dm.CreatePanel("b", "Beta",    new Label { Text = "Beta content"   }, target: dock.Root, zone: DockDropZone.Right);
        dm.CreatePanel("c", "Gamma",   new Label { Text = "Gamma content"  }, target: dock.Root, zone: DockDropZone.Center);
        dm.CreatePanel("d", "Delta",   new Label { Text = "Delta content"  }, target: dock.Root, zone: DockDropZone.Bottom);
        dm.CreatePanel("e", "Epsilon", new Label { Text = "Epsilon content" }, target: dock.Root, zone: DockDropZone.Center);
        dm.CreatePanel("f", "Zeta",    new Label { Text = "Zeta content"   }, target: dock.Root, zone: DockDropZone.Right);

        root.AddChild(dockHost);

        var json = new TextArea { FixedSize = new Vector2(0, 140) };
        var mgr  = new LayoutManager(dm, id => id switch
        {
            "a" => new Label { Text = "Alpha (restored)"   },
            "b" => new Label { Text = "Beta (restored)"    },
            "c" => new Label { Text = "Gamma (restored)"   },
            "d" => new Label { Text = "Delta (restored)"   },
            "e" => new Label { Text = "Epsilon (restored)" },
            "f" => new Label { Text = "Zeta (restored)"    },
            _   => new Panel(),
        });

        var row = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 8) };
        var saveBtn = new Button("Capture") { Tooltip = "Serialize the current dock layout to JSON and display it below." };
        saveBtn.Clicked += () =>
        {
            var data = mgr.Capture();
            json.Text = LayoutSerializer.Save(data);
        };
        var loadBtn = new Button("Restore") { Tooltip = "Restore the dock layout from the JSON shown below." };
        loadBtn.Clicked += () =>
        {
            var data = LayoutSerializer.Load(json.Text);
            if (data != null) mgr.Apply(data);
        };
        var saveFileBtn = new Button("Save to user prefs") { Tooltip = "Capture the layout and persist it to the user preferences file." };
        saveFileBtn.Clicked += () =>
        {
            mgr.SaveToUserPrefs("GUIDemo");
        };
        var loadFileBtn = new Button("Load from user prefs") { Tooltip = "Load the previously saved layout from the user preferences file." };
        loadFileBtn.Clicked += () =>
        {
            mgr.LoadFromUserPrefs("GUIDemo");
        };

        row.AddChild(saveBtn);
        row.AddChild(loadBtn);
        row.AddChild(saveFileBtn);
        row.AddChild(loadFileBtn);
        root.AddChild(row);
        root.AddChild(new Label { Text = "Serialized layout:" });
        root.AddChild(json);

        return root;
    }

    // ── Tab: Drag & Drop ─────────────────────────────────────────────────────
    static Widget BuildDragDropTab()
    {
        var root = new Panel
        {
            Layout  = new BoxLayout(Orientation.Vertical, Alignment.Start, 10),
            Padding = new Thickness(12),
        };

        root.AddChild(new Label { Text = "Drag & Drop", FontSize = 20 });
        root.AddChild(new Label
        {
            Text = "Left list: drag items to reorder. Middle list: drag items to the right list.",
            FontSize = 13,
        });

        var log = new TextArea { FixedSize = new Vector2(0, 90) };

        // ── Row of three lists: reorderable, source, target ──
        var row = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 12) };

        // Reorderable list.
        var reorder = new ListBox { AllowReorder = true, FixedSize = new Vector2(220, 220) };
        reorder.SetItems(new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig" });

        var colA = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 4) };
        colA.AddChild(new Label { Text = "Reorderable" });
        colA.AddChild(reorder);

        // Cross-list source + target.
        var source = new CrossListSource { FixedSize = new Vector2(220, 220) };
        source.SetItems(new[] { "Task #1", "Task #2", "Task #3", "Task #4", "Task #5" });

        var target = new CrossListTarget { FixedSize = new Vector2(220, 220), Log = log };
        target.SetItems(new[] { "Done task" });

        var colB = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 4) };
        colB.AddChild(new Label { Text = "Source (drag out)" });
        colB.AddChild(source);

        var colC = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 4) };
        colC.AddChild(new Label { Text = "Target (drop here)" });
        colC.AddChild(target);

        row.AddChild(colA);
        row.AddChild(colB);
        row.AddChild(colC);
        root.AddChild(row);

        // ── Tree-reparent + TabView reorder demo ──
        var row2 = new Panel { Layout = new BoxLayout(Orientation.Horizontal, Alignment.Start, 12) };

        var tree = new TreeView { AllowDragDrop = true, FixedSize = new Vector2(260, 200) };
        var treeRoot = new TreeNode("root");
        var docs     = treeRoot.Add("Documents"); docs.IsExpanded = true;
        docs.Add("Report.pdf");
        docs.Add("Notes.txt");
        var imgs     = treeRoot.Add("Images");    imgs.IsExpanded = true;
        imgs.Add("Photo.jpg");
        imgs.Add("Screenshot.png");
        treeRoot.Add("Projects");
        tree.Root = treeRoot;

        var colD = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 4) };
        colD.AddChild(new Label { Text = "Reparent tree (drag a node onto another)" });
        colD.AddChild(tree);

        var subTabs = new TabView { AllowReorder = true, FixedSize = new Vector2(320, 200) };
        subTabs.AddTab("One",   new Label { Text = "Tab One content" });
        subTabs.AddTab("Two",   new Label { Text = "Tab Two content" });
        subTabs.AddTab("Three", new Label { Text = "Tab Three content" });
        subTabs.AddTab("Four",  new Label { Text = "Tab Four content" });

        var colE = new Panel { Layout = new BoxLayout(Orientation.Vertical, Alignment.Start, 4) };
        colE.AddChild(new Label { Text = "Reorder tabs (drag a tab header)" });
        colE.AddChild(subTabs);

        row2.AddChild(colD);
        row2.AddChild(colE);
        root.AddChild(row2);

        root.AddChild(new Label { Text = "Event log:" });
        root.AddChild(log);
        return root;
    }

    // ── Tab: Keyboard Scroll ─────────────────────────────────────────────────
    static Widget BuildKeyboardScrollTab()
    {
        var root = new Panel { Layout = new DockLayout() };

        var header = new Label
        {
            Text      = "Tap any field — it scrolls above the virtual keyboard.",
            FontSize  = 13,
            Padding   = new Thickness(12, 8, 12, 8),
            FixedSize = new Vector2(0, 34),
        };
        DockLayout.SetDock(header, DockPosition.Top);
        root.AddChild(header);

        // Scrollable content with many fields so some are off-screen on mobile.
        var content = new Panel
        {
            Layout  = new BoxLayout(Orientation.Vertical, Alignment.Stretch, 12),
            Padding = new Thickness(16),
        };

        string[] labels =
        [
            "First name", "Last name", "Email", "Phone",
            "Address", "City", "State", "Postal code",
            "Country", "Company", "Job title", "Website",
        ];

        foreach (var lbl in labels)
        {
            var row = new Panel
            {
                Layout    = new BoxLayout(Orientation.Vertical, Alignment.Stretch, 4),
                FixedSize = new Vector2(0, 58),
            };
            row.AddChild(new Label { Text = lbl, FontSize = 12 });
            row.AddChild(new TextBox { Placeholder = lbl, FixedSize = new Vector2(0, 34) });
            content.AddChild(row);
        }

        // Multi-line notes field at the bottom.
        content.AddChild(new Label { Text = "Notes", FontSize = 12 });
        content.AddChild(new TextArea
        {
            IsEditable = true,
            FixedSize  = new Vector2(0, 120),
            Text       = "",
        });

        root.AddChild(new ScrollView
        {
            Content             = content,
            CanScrollVertical   = true,
            CanScrollHorizontal = false,
        });

        return root;
    }

    // ─── sokol_main ──────────────────────────────────────────────────────────
    public static sapp_desc sokol_main() => new sapp_desc
    {
        init_cb    = &Init,
        frame_cb   = &Frame,
        event_cb   = &Event,
        cleanup_cb = &Cleanup,
        width      = 960,
        height     = 640,
        sample_count = 4,
        window_title = "Sokol.GUI Demo",
        icon         = { sokol_default = true },
        enable_clipboard = true,
        clipboard_size   = 4096,
        logger = { func = &slog_func },
        ios = { keyboard_resizes_canvas = true },
    };
}

// ─── Sample ViewModel ────────────────────────────────────────────────────────
public sealed class CounterVM : Sokol.GUI.ObservableObject
{
    private int _count;

    public int    Count   => _count;
    public string Display => $"Count = {_count}";

    public void Increment() { _count++; RaisePropertyChanged(nameof(Count)); RaisePropertyChanged(nameof(Display)); }
    public void Decrement() { _count--; RaisePropertyChanged(nameof(Count)); RaisePropertyChanged(nameof(Display)); }
    public void Reset()     { _count = 0; RaisePropertyChanged(nameof(Count)); RaisePropertyChanged(nameof(Display)); }

    public void SetCount(int v) { _count = v; RaisePropertyChanged(nameof(Count)); RaisePropertyChanged(nameof(Display)); }
}

// ─── MVC helpers ─────────────────────────────────────────────────────────────

public sealed class TodoItem
{
    public string Text { get; set; } = string.Empty;
    public bool   Done { get; set; }
}

public sealed class TodoListModel
{
    public System.Collections.Generic.List<TodoItem> Items { get; } = [];

    public void Add(string text) => Items.Add(new TodoItem { Text = text });
    public void Toggle(int idx)  { if (idx >= 0 && idx < Items.Count) Items[idx].Done = !Items[idx].Done; }
    public void Remove(int idx)  { if (idx >= 0 && idx < Items.Count) Items.RemoveAt(idx); }
    public void Clear()          => Items.Clear();
}

// ─── PropertyGrid demo object ─────────────────────────────────────────────────

public sealed class DemoObject
{
    public string  Title       { get; set; } = string.Empty;
    public float   FontSize    { get; set; } = 14f;
    public UIColor BackColor   { get; set; } = UIColor.White;
    public bool    Visible     { get; set; } = true;
    public float   BorderWidth { get; set; } = 0f;
}

// ─── Drag & Drop demo helpers ────────────────────────────────────────────────

/// <summary>ListBox that acts as a cross-list drag source.</summary>
public sealed class CrossListSource : Sokol.GUI.ListBox
{
    public const string Format = "guidemo/crosslist";

    public CrossListSource() { IsDragSource = true; }

    public override Sokol.GUI.DragDropData? OnDragBegin(Sokol.GUI.Vector2 localPos)
    {
        int idx = SelectedIndex;
        if (idx < 0 || idx >= Items.Count) return null;
        string item = Items[idx];
        return new Sokol.GUI.DragDropData
        {
            Format         = Format,
            Payload        = item,
            Source         = this,
            DragLabel      = item,
            AllowedEffects = Sokol.GUI.DragDropEffect.Move,
        };
    }

    public override void OnDragEnd(Sokol.GUI.DragDropEffect effect)
    {
        if (effect != Sokol.GUI.DragDropEffect.Move) return;
        int idx = SelectedIndex;
        if (idx < 0 || idx >= Items.Count) return;
        var list = new System.Collections.Generic.List<string>(Items);
        list.RemoveAt(idx);
        SetItems(list);
    }
}

/// <summary>ListBox that accepts items dragged from a <see cref="CrossListSource"/>.</summary>
public sealed class CrossListTarget : Sokol.GUI.ListBox
{
    public Sokol.GUI.TextArea? Log { get; set; }

    public CrossListTarget() { IsDropTarget = true; }

    public override void OnDragOver(Sokol.GUI.DragDropEventArgs e)
    {
        if (e.Data.Format == CrossListSource.Format)
            e.Effect = Sokol.GUI.DragDropEffect.Move;
    }

    public override void OnDrop(Sokol.GUI.DragDropEventArgs e)
    {
        if (e.Data.Format != CrossListSource.Format) return;
        if (e.Data.Payload is not string item) return;
        var list = new System.Collections.Generic.List<string>(Items) { item };
        SetItems(list);
        e.Handled = true;
        e.Effect  = Sokol.GUI.DragDropEffect.Move;
        if (Log != null) Log.Text += $"Dropped \"{item}\"\n";
    }
}

