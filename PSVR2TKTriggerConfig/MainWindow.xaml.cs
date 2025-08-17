using PSVR2Toolkit.CAPI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace PSVR2TKTriggerConfig
{
    public class ParameterDefinition
    {
        public string Label { get; }
        public int ArrayCount { get; }
        public byte MinValue { get; }
        public byte MaxValue { get; }
        public ParameterDefinition(string label, byte minValue = 0, byte maxValue = 255, int arrayCount = 1)
        {
            Label = label;
            MinValue = minValue;
            MaxValue = maxValue;
            ArrayCount = arrayCount;
        }
    }

    public class EffectTabDefinition { public string Header { get; } public List<ParameterDefinition> Parameters { get; } public Action<EVRControllerType, byte[]> SendCommandAction { get; } public EffectTabDefinition(string header, List<ParameterDefinition> parameters, Action<EVRControllerType, byte[]> sendCommandAction) { Header = header; Parameters = parameters; SendCommandAction = sendCommandAction; } }

    public partial class MainWindow : Window
    {
        private TextBlock _statusTextBlock = null!;
        private ComboBox _controllerComboBox = null!;
        private readonly CancellationTokenSource _connectionCts = new CancellationTokenSource();
        private readonly Dictionary<string, List<Slider>> _slidersByEffect = new Dictionary<string, List<Slider>>();
        private List<EffectTabDefinition> _effectDefinitions = null!;
        private readonly Dictionary<string, Timer> _debounceTimers = new Dictionary<string, Timer>();
        private const int DebounceMilliseconds = 200;

        public MainWindow()
        {
            this.Title = "PSVR2TKTriggerConfig";
            this.MinHeight = 400;
            this.SizeToContent = SizeToContent.WidthAndHeight;
            this.ResizeMode = ResizeMode.NoResize;

            InitializeUI();
            StartConnectionManager();
            this.Closing += (s, e) => {
                _connectionCts.Cancel();
                foreach (var timer in _debounceTimers.Values) { timer.Dispose(); }
                IpcClient.Instance().Stop();
            };
        }

        private void InitializeUI()
        {
            var mainDockPanel = new DockPanel();
            this.Content = mainDockPanel;

            var menuBar = CreateMenuBar();
            DockPanel.SetDock(menuBar, Dock.Top);
            mainDockPanel.Children.Add(menuBar);

            var statusBar = CreateStatusBar();
            DockPanel.SetDock(statusBar, Dock.Bottom);
            mainDockPanel.Children.Add(statusBar);

            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainDockPanel.Children.Add(contentGrid);

            var controllerGroupBox = CreateGlobalControls();
            Grid.SetRow(controllerGroupBox, 0);
            contentGrid.Children.Add(controllerGroupBox);

            var tabControl = new TabControl { Margin = new Thickness(5) };
            Grid.SetRow(tabControl, 1);
            contentGrid.Children.Add(tabControl);

            _effectDefinitions = CreateEffectDefinitions();
            foreach (var definition in _effectDefinitions)
            {
                tabControl.Items.Add(CreateTabFromDefinition(definition, tabControl));
            }

            tabControl.SelectionChanged += OnTabSelectionChanged;
        }

        private List<EffectTabDefinition> CreateEffectDefinitions()
        {
            var ipc = IpcClient.Instance();
            return new List<EffectTabDefinition>
            {
                new("Feedback", new List<ParameterDefinition> { new("Position", 0, 9), new("Strength", 0, 8) }, (ctl, p) => ipc.TriggerEffectFeedback(ctl, p[0], p[1])),
                new("Weapon", new List<ParameterDefinition> { new("Start Position", 2, 7), new("End Position", 3, 8), new("Strength", 0, 8) }, (ctl, p) => ipc.TriggerEffectWeapon(ctl, p[0], p[1], p[2])),
                new("Vibration", new List<ParameterDefinition> { new("Position", 0, 9), new("Amplitude", 0, 8), new("Frequency", 0, 255) }, (ctl, p) => ipc.TriggerEffectVibration(ctl, p[0], p[1], p[2])),
                new("Slope Feedback", new List<ParameterDefinition> { new("Start Position", 0, 8), new("End Position", 1, 9), new("Start Strength", 1, 8), new("End Strength", 1, 8) }, (ctl, p) => ipc.TriggerEffectSlopeFeedback(ctl, p[0], p[1], p[2], p[3])),
                new("Multi-Pos Feedback", new List<ParameterDefinition> { new("Strength", 0, 8, 10) }, (ctl, p) => ipc.TriggerEffectMultiplePositionFeedback(ctl, p)),
                new("Multi-Pos Vibration", new List<ParameterDefinition> { new("Frequency", 0, 255), new("Amplitude", 0, 8, 10) }, (ctl, p) => ipc.TriggerEffectMultiplePositionVibration(ctl, p[0], p.Skip(1).ToArray()))
            };
        }

        private TabItem CreateTabFromDefinition(EffectTabDefinition definition, TabControl tabControl)
        {
            var panel = new StackPanel();
            var slidersForThisTab = new List<Slider>();
            _slidersByEffect[definition.Header] = slidersForThisTab;

            Action sendEffectAction = () => {
                if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Header as string == definition.Header)
                {
                    var controller = (EVRControllerType)_controllerComboBox.SelectedItem;
                    var sliderValues = slidersForThisTab.Select(sl => (byte)sl.Value).ToArray();
                    definition.SendCommandAction(controller, sliderValues);
                }
            };

            Action debouncedSendAction = () => {
                if (_debounceTimers.TryGetValue(definition.Header, out var timer)) { timer.Change(DebounceMilliseconds, Timeout.Infinite); }
                else { _debounceTimers[definition.Header] = new Timer(_ => { Dispatcher.Invoke(sendEffectAction); }, null, DebounceMilliseconds, Timeout.Infinite); }
            };

            foreach (var param in definition.Parameters)
            {
                if (param.ArrayCount > 1)
                {
                    var grid = new UniformGrid { Columns = 5 };
                    for (int i = 0; i < param.ArrayCount; i++)
                    {
                        (var slider, var sliderPanel) = CreateEditableSlider($"{param.Label} [{i}]", param.MinValue, param.MaxValue);
                        slidersForThisTab.Add(slider);
                        slider.ValueChanged += (s, e) => debouncedSendAction();
                        grid.Children.Add(sliderPanel);
                    }
                    panel.Children.Add(new GroupBox { Header = param.Label + "s", Content = grid });
                }
                else
                {
                    (var slider, var sliderPanel) = CreateEditableSlider(param.Label, param.MinValue, param.MaxValue);
                    slidersForThisTab.Add(slider);
                    slider.ValueChanged += (s, e) => debouncedSendAction();
                    panel.Children.Add(sliderPanel);
                }
            }
            return new TabItem { Header = definition.Header, Content = panel };
        }

        #region UI Creation Helpers
        private Menu CreateMenuBar()
        {
            var menuBar = new Menu();
            var saveMenuItem = new MenuItem { Header = "Save Preset" };
            saveMenuItem.Click += SavePreset_Click;
            var loadMenuItem = new MenuItem { Header = "Load Preset" };
            loadMenuItem.Click += LoadPreset_Click;
            var resetMenuItem = new MenuItem { Header = "Reset Sliders" };
            resetMenuItem.Click += ResetSliders_Click;
            menuBar.Items.Add(saveMenuItem);
            menuBar.Items.Add(loadMenuItem);
            menuBar.Items.Add(resetMenuItem);
            return menuBar;
        }

        private StatusBar CreateStatusBar()
        {
            var statusBar = new StatusBar();
            var statusItem = new StatusBarItem();
            _statusTextBlock = new TextBlock { Text = "Initializing...", Margin = new Thickness(5, 0, 5, 0) };
            statusItem.Content = _statusTextBlock;
            statusBar.Items.Add(statusItem);
            return statusBar;
        }

        private GroupBox CreateGlobalControls()
        {
            var controllerGroupBox = new GroupBox { Header = "Global Controls", Margin = new Thickness(5) };
            var controllerPanel = new WrapPanel();
            _controllerComboBox = new ComboBox { Width = 100, Margin = new Thickness(5) };
            _controllerComboBox.ItemsSource = Enum.GetValues(typeof(EVRControllerType));
            _controllerComboBox.SelectedIndex = 2;
            var disableButton = new Button { Content = "Disable All Effects", Width = 120, Margin = new Thickness(5) };
            disableButton.Click += (s, e) => IpcClient.Instance().TriggerEffectDisable(EVRControllerType.Both);
            controllerPanel.Children.Add(new Label { Content = "Target Controller:", VerticalAlignment = VerticalAlignment.Center });
            controllerPanel.Children.Add(_controllerComboBox);
            controllerPanel.Children.Add(disableButton);
            controllerGroupBox.Content = controllerPanel;
            return controllerGroupBox;
        }

        private (Slider slider, Panel panel) CreateEditableSlider(string labelText, double min, double max)
        {
            var containerPanel = new StackPanel { Margin = new Thickness(5, 2, 5, 2) };
            containerPanel.Children.Add(new Label { Content = labelText });
            var sliderGrid = new Grid();
            sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var slider = new Slider { Minimum = min, Maximum = max, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 0);
            sliderGrid.Children.Add(slider);

            slider.PreviewMouseWheel += (s, e) =>
            {
                if (s is Slider currentSlider)
                {
                    if (e.Delta > 0)
                    {
                        currentSlider.Value += currentSlider.TickFrequency;
                    }
                    else if (e.Delta < 0)
                    {
                        currentSlider.Value -= currentSlider.TickFrequency;
                    }
                    e.Handled = true;
                }
            };

            var editorContainer = new Grid();
            Grid.SetColumn(editorContainer, 1);
            sliderGrid.Children.Add(editorContainer);
            var valueTextBlock = new TextBlock { Text = slider.Value.ToString("F0"), MinWidth = 30, TextAlignment = TextAlignment.Right, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            editorContainer.Children.Add(valueTextBlock);
            var valueTextBox = new TextBox { MinWidth = 30, TextAlignment = TextAlignment.Right, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            editorContainer.Children.Add(valueTextBox);

            slider.ValueChanged += (s, e) => valueTextBlock.Text = e.NewValue.ToString("F0");

            Action commitEdit = () => {
                if (double.TryParse(valueTextBox.Text, out double result))
                {
                    slider.Value = Math.Clamp(result, slider.Minimum, slider.Maximum);
                }
                valueTextBox.Visibility = Visibility.Collapsed;
                valueTextBlock.Visibility = Visibility.Visible;
            };

            valueTextBlock.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2)
                {
                    valueTextBlock.Visibility = Visibility.Collapsed;
                    valueTextBox.Visibility = Visibility.Visible;
                    valueTextBox.Text = slider.Value.ToString("F0");
                    valueTextBox.Focus();
                    valueTextBox.SelectAll();
                }
            };
            valueTextBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) commitEdit(); };
            valueTextBox.LostFocus += (s, e) => commitEdit();

            containerPanel.Children.Add(sliderGrid);
            return (slider, containerPanel);
        }
        #endregion

        #region Logic Handlers
        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl tabControl) return;

            foreach (var removedItem in e.RemovedItems)
            {
                if (removedItem is TabItem tab && tab.Header is string header && _debounceTimers.TryGetValue(header, out var timer))
                {
                    timer.Dispose();
                    _debounceTimers.Remove(header);
                }
            }

            IpcClient.Instance().TriggerEffectDisable(EVRControllerType.Both);

            if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Header is string selectedHeader)
            {
                if (_effectDefinitions.FirstOrDefault(def => def.Header == selectedHeader) is EffectTabDefinition definition &&
                    _slidersByEffect.TryGetValue(selectedHeader, out var sliders))
                {
                    var controller = (EVRControllerType)_controllerComboBox.SelectedItem;
                    var sliderValues = sliders.Select(sl => (byte)sl.Value).ToArray();
                    definition.SendCommandAction(controller, sliderValues);
                }
            }
        }
        private void ResetSliders_Click(object sender, RoutedEventArgs e) { foreach (var sliderList in _slidersByEffect.Values) { foreach (var slider in sliderList) { slider.Value = slider.Minimum; } } }
        private async void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json", DefaultExt = "json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var presetData = _slidersByEffect.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Select(s => (byte)s.Value)
                    );
                    var json = JsonSerializer.Serialize(presetData, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(dialog.FileName, json);
                }
                catch (Exception ex) { MessageBox.Show($"Failed to save preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        private async void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(dialog.FileName);
                    var presetData = JsonSerializer.Deserialize<Dictionary<string, byte[]>>(json);
                    if (presetData == null) return;
                    foreach (var kvp in presetData)
                    {
                        if (_slidersByEffect.TryGetValue(kvp.Key, out var sliders))
                        {
                            for (int i = 0; i < Math.Min(sliders.Count, kvp.Value.Length); i++) { sliders[i].Value = kvp.Value[i]; }
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Failed to load preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        private void StartConnectionManager()
        {
            Task.Run(async () => {
                while (!_connectionCts.IsCancellationRequested)
                {
                    if (!IpcClient.Instance().IsRunning)
                    {
                        await Dispatcher.InvokeAsync(() => {
                            _statusTextBlock.Text = "Connecting...";
                        });
                        bool success = IpcClient.Instance().Start();
                        await Dispatcher.InvokeAsync(() => {
                            if (success)
                            {
                                _statusTextBlock.Text = "Connected";
                            }
                            else
                            {
                                _statusTextBlock.Text = "Disconnected - Server not found. Retrying...";
                            }
                        });
                    }
                    await Task.Delay(2000, _connectionCts.Token);
                }
            }, _connectionCts.Token);
        }
        #endregion
    }
}