using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AjazzManager
{
    internal sealed class MainForm : Form
    {
        private readonly Color background = Color.FromArgb(24, 25, 30);
        private readonly Color panel = Color.FromArgb(34, 36, 43);
        private readonly Color panelLight = Color.FromArgb(45, 48, 57);
        private readonly Color text = Color.FromArgb(238, 240, 244);
        private readonly Color muted = Color.FromArgb(166, 171, 183);
        private readonly Color accent = Color.FromArgb(116, 77, 230);
        private readonly Color green = Color.FromArgb(63, 190, 124);
        private readonly Color orange = Color.FromArgb(235, 166, 71);

        private readonly AppPaths paths = new AppPaths();
        private ManagerLog log;
        private PluginService plugins;
        private IconService icons;

        private Label ajazzStatus;
        private Label elgatoStatus;
        private Label footerStatus;
        private TabControl tabs;
        private ListView pluginList;
        private Button installButton;
        private Button uninstallButton;
        private Label pluginDetails;
        private ListView backupList;
        private Button restoreButton;
        private ListView iconList;
        private PictureBox iconPreview;
        private Label iconDetails;
        private TextBox iconSearch;
        private TextBox diagnostics;
        private Panel busyOverlay;
        private Label busyOverlayText;
        private ProgressBar busyProgress;
        private readonly List<PluginEntry> currentPlugins = new List<PluginEntry>();
        private readonly List<BackupEntry> currentBackups = new List<BackupEntry>();
        private readonly List<IconEntry> currentIcons = new List<IconEntry>();
        private readonly List<FluentNavigationButton> navigationButtons = new List<FluentNavigationButton>();
        private bool busy;

        public MainForm()
        {
            paths.EnsureManagerFolders();
            log = new ManagerLog(paths);
            plugins = new PluginService(paths, log);
            icons = new IconService(paths, log);

            Text = "Ajazz Plugin Manager";
            Width = 1120;
            Height = 760;
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = background;
            ForeColor = text;
            Font = new Font("Segoe UI", 9.5f);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            BuildInterface();
            Shown += delegate { RefreshEverything(); };
        }

        private void BuildInterface()
        {
            footerStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Padding = new Padding(18, 8, 0, 0),
                Text = "Готово",
                BackColor = Color.FromArgb(19, 20, 24),
                ForeColor = muted
            };

            tabs = new TablessTabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildPluginsTab());
            tabs.TabPages.Add(BuildIconsTab());
            tabs.TabPages.Add(BuildBackupsTab());
            tabs.TabPages.Add(BuildDiagnosticsTab());

            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 214, BackColor = panel, Padding = new Padding(12) };

            FlowLayoutPanel navigation = new FlowLayoutPanel
            {
                Left = 10,
                Top = 14,
                Width = 194,
                Height = 230,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = panel
            };
            navigation.Controls.Add(CreateNavigationButton("Плагины", "\uEA86", 0));
            navigation.Controls.Add(CreateNavigationButton("Иконки", "\uE7AA", 1));
            navigation.Controls.Add(CreateNavigationButton("Резервные копии", "\uE81C", 2));
            navigation.Controls.Add(CreateNavigationButton("Диагностика", "\uE9D9", 3));
            sidebar.Controls.Add(navigation);

            Panel connectionPanel = new Panel { Dock = DockStyle.Bottom, Height = 116, BackColor = Color.FromArgb(29, 31, 37), Padding = new Padding(14, 12, 10, 10) };
            Label connectionTitle = new Label
            {
                Text = "СОСТОЯНИЕ",
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.FromArgb(119, 124, 137),
                Font = new Font("Segoe UI Semibold", 8f)
            };
            ajazzStatus = new Label { Dock = DockStyle.Top, Height = 28, Text = "●  AJAZZ · проверка", ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft };
            elgatoStatus = new Label { Dock = DockStyle.Top, Height = 28, Text = "●  Elgato · проверка", ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft };
            connectionPanel.Controls.Add(elgatoStatus);
            connectionPanel.Controls.Add(ajazzStatus);
            connectionPanel.Controls.Add(connectionTitle);
            sidebar.Controls.Add(connectionPanel);

            Panel content = new Panel { Dock = DockStyle.Fill, BackColor = background };
            content.Controls.Add(tabs);
            content.Controls.Add(footerStatus);
            busyOverlay = new Panel { BackColor = Color.FromArgb(27, 29, 35), Visible = false };
            busyOverlayText = new Label
            {
                Text = "Выполняется операция…",
                Height = 28,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = text,
                Font = new Font("Segoe UI Semibold", 11f)
            };
            busyProgress = new ProgressBar { Width = 260, Height = 5, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 22 };
            busyOverlay.Controls.Add(busyOverlayText);
            busyOverlay.Controls.Add(busyProgress);
            content.Controls.Add(busyOverlay);
            content.Resize += delegate { LayoutBusyOverlay(content); };
            Controls.Add(content);
            Controls.Add(sidebar);
            LayoutBusyOverlay(content);
            SelectPage(0);
        }

        private void LayoutBusyOverlay(Panel content)
        {
            if (busyOverlay == null) return;
            busyOverlay.SetBounds(0, 0, content.ClientSize.Width, Math.Max(0, content.ClientSize.Height - footerStatus.Height));
            busyOverlayText.SetBounds(0, Math.Max(20, busyOverlay.Height / 2 - 34), busyOverlay.Width, 28);
            busyProgress.Left = Math.Max(0, (busyOverlay.Width - busyProgress.Width) / 2);
            busyProgress.Top = busyOverlayText.Bottom + 12;
        }

        private FluentNavigationButton CreateNavigationButton(string caption, string glyph, int pageIndex)
        {
            FluentNavigationButton button = new FluentNavigationButton
            {
                Caption = caption,
                Glyph = glyph,
                Text = caption,
                AccessibleName = caption,
                Width = 194,
                Height = 45,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                BackColor = panel,
                ForeColor = muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                Margin = new Padding(0, 0, 0, 5),
                Cursor = Cursors.Hand,
                Tag = pageIndex
            };
            button.Click += delegate { SelectPage((int)button.Tag); };
            navigationButtons.Add(button);
            return button;
        }

        private void SelectPage(int index)
        {
            tabs.SelectedIndex = index;
            for (int i = 0; i < navigationButtons.Count; i++)
            {
                bool selected = i == index;
                navigationButtons[i].Active = selected;
                navigationButtons[i].BackColor = selected ? Color.FromArgb(61, 49, 97) : panel;
                navigationButtons[i].ForeColor = selected ? Color.White : muted;
                navigationButtons[i].Invalidate();
            }
        }

        private TabPage BuildPluginsTab()
        {
            TabPage page = NewTab("Плагины");
            Panel commands = new Panel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(12, 10, 12, 5), BackColor = background };
            Button refresh = NewButton("Обновить список", 135);
            installButton = NewButton("Установить / обновить", 175);
            Button importPackage = NewButton("Выбрать .sdPlugin", 150);
            uninstallButton = NewButton("Удалить", 105);
            Button openFolder = NewButton("Открыть папку", 135);
            refresh.Click += delegate { RefreshEverything(); };
            installButton.Click += InstallSelected;
            importPackage.Click += ImportPluginFolder;
            uninstallButton.Click += UninstallSelected;
            openFolder.Click += delegate { OpenFolder(paths.AjazzPlugins); };
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            flow.Controls.Add(refresh);
            flow.Controls.Add(installButton);
            flow.Controls.Add(importPackage);
            flow.Controls.Add(uninstallButton);
            flow.Controls.Add(openFolder);
            commands.Controls.Add(flow);

            pluginDetails = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 82,
                Padding = new Padding(14, 10, 14, 8),
                ForeColor = muted,
                BackColor = panel,
                Text = "Выберите плагин. Проверенные рецепты можно ставить одним нажатием; остальные доступны в экспериментальном режиме."
            };

            pluginList = NewListView();
            pluginList.Columns.Add("Плагин", 205);
            pluginList.Columns.Add("Версия", 105);
            pluginList.Columns.Add("Источник", 140);
            pluginList.Columns.Add("Совместимость", 135);
            pluginList.Columns.Add("Установлен", 135);
            pluginList.Columns.Add("Действий", 65);
            pluginList.Resize += delegate { FillLastColumn(pluginList, 65); };
            pluginList.SelectedIndexChanged += PluginSelectionChanged;
            pluginList.DoubleClick += InstallSelected;

            page.Controls.Add(pluginList);
            page.Controls.Add(pluginDetails);
            page.Controls.Add(commands);
            return page;
        }

        private TabPage BuildIconsTab()
        {
            TabPage page = NewTab("Иконки");
            Panel commands = new Panel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(12, 10, 12, 5), BackColor = background };
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            Button importFiles = NewButton("Импорт файлов", 125);
            Button importFolder = NewButton("Импорт папки", 125);
            Button openLibrary = NewButton("Открыть библиотеку", 150);
            Button copyPath = NewButton("Скопировать путь", 135);
            Label searchLabel = new Label { Text = "Поиск", AutoSize = false, Width = 48, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted };
            iconSearch = new TextBox { Width = 165, Height = 28, BackColor = panelLight, ForeColor = text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 0) };
            iconSearch.TextChanged += delegate { RenderIcons(); };
            importFiles.Click += ImportIconFiles;
            importFolder.Click += ImportIconFolder;
            openLibrary.Click += delegate { OpenFolder(paths.IconLibrary); };
            copyPath.Click += CopySelectedIconPath;
            flow.Controls.Add(importFiles);
            flow.Controls.Add(importFolder);
            flow.Controls.Add(openLibrary);
            flow.Controls.Add(copyPath);
            flow.Controls.Add(searchLabel);
            flow.Controls.Add(iconSearch);
            commands.Controls.Add(flow);

            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 500), SplitterDistance = 650, BackColor = background, Panel1MinSize = 400, Panel2MinSize = 220 };
            iconList = NewListView();
            iconList.Columns.Add("Название", 175);
            iconList.Columns.Add("Набор", 125);
            iconList.Columns.Add("Источник", 120);
            iconList.Columns.Add("Формат", 60);
            iconList.Columns.Add("Размер", 75);
            iconList.Resize += delegate { FillLastColumn(iconList, 85); };
            iconList.SelectedIndexChanged += IconSelectionChanged;
            split.Panel1.Controls.Add(iconList);

            Panel previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = panel, Padding = new Padding(18) };
            Label previewTitle = new Label { Text = "Предпросмотр", Dock = DockStyle.Top, Height = 35, Font = new Font("Segoe UI Semibold", 12f), ForeColor = text };
            iconPreview = new PictureBox { Width = 180, Height = 180, Top = 46, Left = 25, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(20, 21, 26), BorderStyle = BorderStyle.FixedSingle };
            iconDetails = new Label { Top = 245, Left = 22, Width = 240, Height = 160, ForeColor = muted, Text = "Выберите изображение." };
            previewPanel.Controls.Add(iconDetails);
            previewPanel.Controls.Add(iconPreview);
            previewPanel.Controls.Add(previewTitle);
            split.Panel2.Controls.Add(previewPanel);

            Label hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                Padding = new Padding(14, 8, 14, 6),
                BackColor = panel,
                ForeColor = muted,
                Text = "Библиотека хранится отдельно от профилей AJAZZ. Нажмите «Скопировать путь» и выберите этот файл в стандартном окне назначения иконки AJAZZ."
            };
            page.Controls.Add(split);
            page.Controls.Add(hint);
            page.Controls.Add(commands);
            return page;
        }

        private TabPage BuildBackupsTab()
        {
            TabPage page = NewTab("Резервные копии");
            Panel commands = new Panel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(12, 10, 12, 5), BackColor = background };
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            Button refresh = NewButton("Обновить", 110);
            restoreButton = NewButton("Восстановить", 135);
            Button open = NewButton("Открыть папку", 135);
            refresh.Click += delegate { RefreshBackups(); };
            restoreButton.Click += RestoreSelected;
            open.Click += delegate { OpenFolder(paths.BackupRoot); };
            flow.Controls.Add(refresh);
            flow.Controls.Add(restoreButton);
            flow.Controls.Add(open);
            commands.Controls.Add(flow);

            backupList = NewListView();
            backupList.Columns.Add("Плагин", 215);
            backupList.Columns.Add("Версия", 105);
            backupList.Columns.Add("Дата", 145);
            backupList.Columns.Add("Путь", 390);
            backupList.Resize += delegate { FillLastColumn(backupList, 220); };
            backupList.SelectedIndexChanged += delegate { restoreButton.Enabled = backupList.SelectedItems.Count == 1 && !busy; };
            page.Controls.Add(backupList);
            page.Controls.Add(commands);
            return page;
        }

        private TabPage BuildDiagnosticsTab()
        {
            TabPage page = NewTab("Диагностика");
            Panel commands = new Panel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(12, 10, 12, 5), BackColor = background };
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            Button refresh = NewButton("Повторить проверку", 155);
            Button export = NewButton("Сохранить отчёт", 145);
            Button logs = NewButton("Открыть журнал", 140);
            refresh.Click += delegate { RefreshDiagnostics(); };
            export.Click += ExportDiagnostics;
            logs.Click += delegate { SelectFile(paths.LogPath); };
            flow.Controls.Add(refresh);
            flow.Controls.Add(export);
            flow.Controls.Add(logs);
            commands.Controls.Add(flow);
            diagnostics = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(20, 21, 26),
                ForeColor = text,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10f),
                Padding = new Padding(10)
            };
            page.Controls.Add(diagnostics);
            page.Controls.Add(commands);
            return page;
        }

        private TabPage NewTab(string name)
        {
            return new TabPage(name) { BackColor = background, ForeColor = text, Padding = new Padding(8) };
        }

        private Button NewButton(string caption, int width)
        {
            Button button = new Button
            {
                Text = caption,
                Width = width,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = panelLight,
                ForeColor = text,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 9, 0)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 72, 84);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(57, 60, 72);
            return button;
        }

        private ListView NewListView()
        {
            return new DarkListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                BackColor = Color.FromArgb(27, 29, 35),
                ForeColor = text,
                BorderStyle = BorderStyle.None,
                GridLines = false
            };
        }

        private void RefreshEverything()
        {
            try
            {
                currentPlugins.Clear();
                currentPlugins.AddRange(plugins.Discover());
                pluginList.BeginUpdate();
                pluginList.Items.Clear();
                foreach (PluginEntry entry in currentPlugins)
                {
                    ListViewItem item = new ListViewItem(entry.Name);
                    item.SubItems.Add(entry.Version);
                    item.SubItems.Add(entry.SourceLabel);
                    item.SubItems.Add(entry.CompatibilityText);
                    item.SubItems.Add(entry.InstalledText);
                    item.SubItems.Add(entry.ActionCount.ToString());
                    item.Tag = entry;
                    if (entry.Compatibility == CompatibilityLevel.Supported) item.ForeColor = green;
                    else if (entry.Compatibility == CompatibilityLevel.Experimental) item.ForeColor = orange;
                    else if (entry.Compatibility == CompatibilityLevel.Protected) item.ForeColor = Color.FromArgb(205, 151, 255);
                    pluginList.Items.Add(item);
                }
                FillLastColumn(pluginList, 65);
                pluginList.EndUpdate();
                RefreshBackups();
                RefreshIcons();
                RefreshDiagnostics();
                SetStatus("Список обновлён: " + currentPlugins.Count + " плагинов");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private PluginEntry SelectedPlugin()
        {
            return pluginList.SelectedItems.Count == 1 ? pluginList.SelectedItems[0].Tag as PluginEntry : null;
        }

        private void PluginSelectionChanged(object sender, EventArgs e)
        {
            PluginEntry entry = SelectedPlugin();
            installButton.Enabled = entry != null && !String.IsNullOrWhiteSpace(entry.SourcePath)
                && entry.Compatibility != CompatibilityLevel.InstalledOnly
                && entry.Compatibility != CompatibilityLevel.Protected
                && entry.Compatibility != CompatibilityLevel.Unsupported && !busy;
            uninstallButton.Enabled = entry != null && entry.IsInstalled && !busy;
            if (entry == null) return;
            string compatibilityExplanation = entry.Compatibility == CompatibilityLevel.Supported
                ? "Для этого плагина есть проверенный рецепт совместимости с AJAZZ."
                : entry.Compatibility == CompatibilityLevel.Unsupported && entry.IsIconEditor
                    ? "Это редактор иконок Elgato без действий Stream Deck. Для изображений используйте вкладку «Иконки»."
                    : entry.Compatibility == CompatibilityLevel.Protected
                        ? "Манифест защищён DRM Elgato. Пакет обнаружен, но для AJAZZ требуется отдельный рецепт адаптации."
                    : entry.Compatibility == CompatibilityLevel.Unsupported
                        ? "В пакете нет действий Stream Deck, поэтому установить его как плагин AJAZZ нельзя."
                        : "Автоматический рецепт пока отсутствует. Установка доступна для тестирования и может работать не полностью.";
            pluginDetails.Text = entry.Name + "  •  " + entry.Id + Environment.NewLine
                + "Источник: " + entry.SourceLabel + "    Исполняемый файл: " + (String.IsNullOrEmpty(entry.CodePath) ? "не указан" : entry.CodePath) + Environment.NewLine
                + compatibilityExplanation;
        }

        private async void InstallSelected(object sender, EventArgs e)
        {
            PluginEntry entry = SelectedPlugin();
            if (entry == null || entry.Compatibility == CompatibilityLevel.InstalledOnly) return;
            if (entry.Compatibility == CompatibilityLevel.Protected)
            {
                MessageBox.Show("Манифест этого плагина защищён Elgato и не читается AJAZZ. Обычное копирование не сработает — сначала нужно подготовить отдельный рецепт адаптации.",
                    "Требуется адаптация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (entry.Compatibility == CompatibilityLevel.Unsupported)
            {
                MessageBox.Show(entry.IsIconEditor
                    ? "Это редактор иконок Elgato, а не плагин с действиями. Используйте вкладку «Иконки»."
                    : "В этом пакете нет действий Stream Deck, которые можно установить в AJAZZ.",
                    "Пакет не поддерживается", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (entry.Compatibility == CompatibilityLevel.Experimental)
            {
                DialogResult answer = MessageBox.Show(
                    "Для «" + entry.Name + "» пока нет проверенного рецепта. Менеджер сделает резервную копию, но отдельные функции, интерфейс или авторизация могут не работать.\n\nПродолжить экспериментальную установку?",
                    "Экспериментальный плагин", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
            await RunOperation("Установка " + entry.Name + "…", delegate { return plugins.Install(entry); }, delegate(string backup)
            {
                MessageBox.Show("Плагин установлен. AJAZZ перезапущен." + (String.IsNullOrEmpty(backup) ? "" : "\nПредыдущая версия сохранена."), "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private async void ImportPluginFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку плагина, содержащую manifest.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                PluginEntry entry;
                try { entry = plugins.CreateExternalEntry(dialog.SelectedPath); }
                catch (Exception ex) { ShowError(ex); return; }
                if (entry.Compatibility == CompatibilityLevel.Unsupported)
                {
                    MessageBox.Show(entry.IsIconEditor
                        ? "Выбранная папка содержит редактор иконок Elgato, а не плагин с действиями. Используйте вкладку «Иконки»."
                        : "В выбранном пакете нет действий Stream Deck.",
                        "Пакет не поддерживается", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (entry.Compatibility == CompatibilityLevel.Protected)
                {
                    MessageBox.Show("Манифест выбранного плагина защищён Elgato. Для установки в AJAZZ нужен отдельный рецепт адаптации.",
                        "Требуется адаптация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(
                    "Установить «" + entry.Name + "» в экспериментальном режиме?\n\nМенеджер проверит манифест и создаст резервную копию текущей версии, но не может гарантировать совместимость этого плагина с AJAZZ.",
                    "Импорт .sdPlugin", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                await RunOperation("Импорт " + entry.Name + "…", delegate { return plugins.Install(entry); }, delegate(string backup)
                {
                    MessageBox.Show("Плагин скопирован в AJAZZ. Проверьте его действия и настройки.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }
        }

        private async void UninstallSelected(object sender, EventArgs e)
        {
            PluginEntry entry = SelectedPlugin();
            if (entry == null || !entry.IsInstalled) return;
            if (MessageBox.Show("Удалить «" + entry.Name + "» из AJAZZ? Перед удалением будет создана резервная копия.", "Удаление плагина", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunOperation("Удаление " + entry.Name + "…", delegate { return plugins.Uninstall(entry.Id); }, delegate(string backup)
            {
                MessageBox.Show("Плагин удалён. Резервная копия сохранена.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private async void RestoreSelected(object sender, EventArgs e)
        {
            if (backupList.SelectedItems.Count != 1) return;
            BackupEntry backup = backupList.SelectedItems[0].Tag as BackupEntry;
            if (backup == null) return;
            if (MessageBox.Show("Восстановить «" + backup.PluginName + "» версии " + backup.Version + "? Текущая версия также будет сохранена.", "Восстановление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunOperation("Восстановление резервной копии…", delegate { plugins.Restore(backup); return backup.Path; }, delegate(string value)
            {
                MessageBox.Show("Резервная копия восстановлена.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private async Task RunOperation(string caption, Func<string> operation, Action<string> completed)
        {
            if (busy) return;
            SetBusy(true, caption);
            try
            {
                string result = await Task.Run(operation);
                if (completed != null) completed(result);
            }
            catch (Exception ex) { ShowError(ex); }
            finally
            {
                SetBusy(false, "Готово");
                RefreshEverything();
            }
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            UseWaitCursor = value;
            busyOverlayText.Text = status;
            busyOverlay.Visible = value;
            if (value) busyOverlay.BringToFront();
            SetStatus(status);
        }

        private void RefreshBackups()
        {
            currentBackups.Clear();
            currentBackups.AddRange(plugins.GetBackups());
            backupList.BeginUpdate();
            backupList.Items.Clear();
            foreach (BackupEntry backup in currentBackups)
            {
                ListViewItem item = new ListViewItem(backup.PluginName);
                item.SubItems.Add(backup.Version);
                item.SubItems.Add(backup.Created.ToString("dd.MM.yyyy HH:mm:ss"));
                item.SubItems.Add(backup.Path);
                item.Tag = backup;
                backupList.Items.Add(item);
            }
            FillLastColumn(backupList, 220);
            backupList.EndUpdate();
            restoreButton.Enabled = false;
        }

        private void ImportIconFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите иконки";
                dialog.Multiselect = true;
                dialog.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.svg;*.webp|Все файлы|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                int count = icons.ImportFiles(dialog.FileNames, "Импорт " + DateTime.Now.ToString("yyyy-MM-dd HH-mm"));
                RefreshIcons();
                SetStatus("Импортировано иконок: " + count);
            }
        }

        private void ImportIconFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с набором иконок";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int count = icons.ImportFolder(dialog.SelectedPath);
                    RefreshIcons();
                    SetStatus("Импортировано иконок: " + count);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void RefreshIcons()
        {
            currentIcons.Clear();
            currentIcons.AddRange(icons.GetIcons());
            RenderIcons();
        }

        private void RenderIcons()
        {
            if (iconList == null) return;
            string filter = iconSearch == null ? "" : iconSearch.Text.Trim();
            iconList.BeginUpdate();
            iconList.Items.Clear();
            foreach (IconEntry icon in currentIcons)
            {
                if (!String.IsNullOrEmpty(filter)
                    && icon.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0
                    && icon.PackName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0
                    && icon.Source.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                ListViewItem item = new ListViewItem(icon.Name);
                item.SubItems.Add(icon.PackName);
                item.SubItems.Add(icon.Source);
                item.SubItems.Add(icon.Extension);
                item.SubItems.Add(FormatSize(icon.Size));
                item.Tag = icon;
                iconList.Items.Add(item);
            }
            FillLastColumn(iconList, 85);
            iconList.EndUpdate();
        }

        private void IconSelectionChanged(object sender, EventArgs e)
        {
            if (iconPreview.Image != null) { Image old = iconPreview.Image; iconPreview.Image = null; old.Dispose(); }
            if (iconList.SelectedItems.Count != 1) { iconDetails.Text = "Выберите изображение."; return; }
            IconEntry icon = iconList.SelectedItems[0].Tag as IconEntry;
            if (icon == null) return;
            string file = icon.Path;
            FileInfo info = new FileInfo(file);
            string dimensions = "Предпросмотр недоступен";
            try
            {
                using (Image source = Image.FromFile(file))
                {
                    dimensions = source.Width + " × " + source.Height + " px";
                    iconPreview.Image = new Bitmap(source);
                }
            }
            catch { }
            iconDetails.Text = info.Name + Environment.NewLine + dimensions + Environment.NewLine + FormatSize(info.Length) + Environment.NewLine + Environment.NewLine + info.DirectoryName;
        }

        private void CopySelectedIconPath(object sender, EventArgs e)
        {
            if (iconList.SelectedItems.Count != 1) return;
            IconEntry icon = iconList.SelectedItems[0].Tag as IconEntry;
            if (icon == null) return;
            Clipboard.SetText(icon.Path);
            SetStatus("Путь к иконке скопирован");
        }

        private void RefreshDiagnostics()
        {
            bool ajazzFound = !String.IsNullOrWhiteSpace(paths.AjazzExe);
            bool elgatoFound = Directory.Exists(paths.ElgatoPlugins);
            bool ajazzRunning = Process.GetProcessesByName("Stream Dock AJAZZ").Length > 0;
            int installed = Directory.Exists(paths.AjazzPlugins) ? Directory.GetDirectories(paths.AjazzPlugins, "*.sdPlugin").Length : 0;
            int originals = Directory.Exists(paths.ElgatoPlugins) ? Directory.GetDirectories(paths.ElgatoPlugins, "*.sdPlugin").Length : 0;

            ajazzStatus.Text = ajazzFound ? "●  AJAZZ · " + (ajazzRunning ? "подключён" : "найден") : "●  AJAZZ · не найден";
            ajazzStatus.ForeColor = ajazzFound ? green : orange;
            elgatoStatus.Text = elgatoFound ? "●  Elgato · плагинов " + originals : "●  Elgato · не найден";
            elgatoStatus.ForeColor = elgatoFound ? green : muted;

            StringBuilder report = new StringBuilder();
            report.AppendLine("AJAZZ PLUGIN MANAGER — ДИАГНОСТИКА");
            report.AppendLine("Дата: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine();
            report.AppendLine("Stream Dock AJAZZ: " + (ajazzFound ? paths.AjazzExe : "НЕ НАЙДЕН"));
            report.AppendLine("Процесс AJAZZ: " + (ajazzRunning ? "запущен" : "остановлен"));
            report.AppendLine("Данные AJAZZ: " + paths.AjazzRoot);
            report.AppendLine("Плагины AJAZZ: " + installed);
            report.AppendLine("Плагины Elgato: " + originals);
            report.AppendLine("Источник Elgato: " + paths.ElgatoPlugins);
            report.AppendLine();
            report.AppendLine("Библиотека иконок: " + paths.IconLibrary);
            report.AppendLine("Резервные копии: " + paths.BackupRoot);
            report.AppendLine("Журнал менеджера: " + paths.LogPath);
            report.AppendLine();
            report.AppendLine("Проверенный рецепт: Twitch " + (currentPlugins.Any(delegate(PluginEntry p) { return p.Id == PluginService.TwitchId && p.IsPackaged; }) ? "доступен" : "не найден в packages"));
            diagnostics.Text = report.ToString();
        }

        private void ExportDiagnostics(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Сохранить отчёт";
                dialog.Filter = "Текстовый файл|*.txt";
                dialog.FileName = "ajazz-manager-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, diagnostics.Text, Encoding.UTF8);
                SetStatus("Диагностический отчёт сохранён");
            }
        }

        private void ShowError(Exception ex)
        {
            log.Write("ОШИБКА: " + ex);
            SetStatus("Ошибка: " + ex.Message);
            MessageBox.Show(ex.Message, "Ajazz Plugin Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetStatus(string value)
        {
            footerStatus.Text = value;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / 1024d / 1024d).ToString("0.0") + " МБ";
            if (bytes >= 1024) return (bytes / 1024d).ToString("0.0") + " КБ";
            return bytes + " Б";
        }

        private static void FillLastColumn(ListView list, int minimumWidth)
        {
            if (list.Columns.Count == 0 || list.ClientSize.Width <= 0) return;
            int used = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++) used += list.Columns[i].Width;
            int width = Math.Max(minimumWidth, list.ClientSize.Width - used - 2);
            if (list.Columns[list.Columns.Count - 1].Width != width) list.Columns[list.Columns.Count - 1].Width = width;
        }

        private static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        }

        private static void SelectFile(string path)
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }
    }

    internal sealed class TablessTabControl : TabControl
    {
        private const int TcmAdjustRect = 0x1328;

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == TcmAdjustRect && !DesignMode)
            {
                message.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class DarkListView : ListView
    {
        private readonly Color headerBackground = Color.FromArgb(45, 48, 57);
        private readonly Color headerForeground = Color.FromArgb(218, 221, 228);
        private readonly Color selectedBackground = Color.FromArgb(55, 72, 112);
        private readonly Color separator = Color.FromArgb(59, 62, 72);

        public DarkListView()
        {
            OwnerDraw = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(headerBackground)) e.Graphics.FillRectangle(brush, e.Bounds);
            Rectangle textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, textBounds, headerForeground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (Pen pen = new Pen(separator))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 5, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            if (View != View.Details) e.DrawDefault = true;
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            Color rowBackground = e.Item.Selected ? selectedBackground : BackColor;
            Color rowForeground = e.Item.Selected ? Color.White : e.Item.ForeColor;
            using (SolidBrush brush = new SolidBrush(rowBackground)) e.Graphics.FillRectangle(brush, e.Bounds);
            Rectangle textBounds = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, textBounds, rowForeground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class FluentNavigationButton : Button
    {
        private string caption = "";
        private string glyph = "";
        private bool active;
        private bool hovered;
        private readonly Font iconFont;
        private readonly Font captionFont;

        public FluentNavigationButton()
        {
            string iconFamily = FontFamily.Families.Any(delegate(FontFamily family) { return family.Name == "Segoe Fluent Icons"; })
                ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            iconFont = new Font(iconFamily, 14f, FontStyle.Regular, GraphicsUnit.Point);
            captionFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public string Caption
        {
            get { return caption; }
            set { caption = value ?? ""; Invalidate(); }
        }

        public string Glyph
        {
            get { return glyph; }
            set { glyph = value ?? ""; Invalidate(); }
        }

        public bool Active
        {
            get { return active; }
            set { active = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color baseColor = Active ? Color.FromArgb(61, 49, 97) : BackColor;
            if (hovered && !Active) baseColor = Color.FromArgb(45, 48, 57);
            using (SolidBrush background = new SolidBrush(baseColor)) e.Graphics.FillRectangle(background, ClientRectangle);
            if (Active)
            {
                using (SolidBrush marker = new SolidBrush(Color.FromArgb(139, 104, 244))) e.Graphics.FillRectangle(marker, 0, 8, 3, Height - 16);
            }
            Color foreground = Active ? Color.White : ForeColor;
            TextRenderer.DrawText(e.Graphics, Glyph, iconFont, new Rectangle(15, 0, 25, Height), foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, Caption, captionFont, new Rectangle(51, 0, Width - 57, Height), foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                iconFont.Dispose();
                captionFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
