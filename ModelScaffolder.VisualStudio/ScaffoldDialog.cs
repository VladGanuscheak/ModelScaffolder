using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using WinForms = System.Windows.Forms;

namespace ModelScaffolder.VisualStudio
{
    /// <summary>
    ///     "Scaffold Solution" dialog: output folder, optional templates, target framework and the default system user.
    ///     Built in code (no XAML) to keep the VSIX build simple.
    /// </summary>
    internal sealed class ScaffoldDialog : DialogWindow
    {
        private const int MinPasswordLength = 8;

        private readonly TextBox _output = new();
        private readonly TextBox _templates = new();
        private readonly TextBox _framework = new();
        private readonly TextBox _user = new();
        private readonly PasswordBox _password = new();
        private readonly PasswordBox _repeat = new();
        private readonly CheckBox _overwrite = new() { Content = "Overwrite existing files that differ from the generated version" };
        private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly ScaffoldRequest _request;

        public ScaffoldDialog(ScaffoldRequest request)
        {
            _request = request;

            Title = "Scaffold Solution";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = false;
            HasMinimizeButton = false;
            HasMaximizeButton = false;

            _output.Text = request.OutputDirectory;
            _templates.Text = request.TemplateDirectory ?? string.Empty;
            _framework.Text = request.TargetFramework;
            _user.Text = request.AdminUser;
            _overwrite.IsChecked = request.Overwrite;

            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var row = 0;
            AddRow(grid, ref row, "Model:", new TextBox { Text = request.ModelPath, IsReadOnly = true });
            AddRow(grid, ref row, "Output folder:", _output, BrowseButton(_output, "Folder for the generated solution"));
            AddRow(grid, ref row, "Templates (optional):", _templates, BrowseButton(_templates, "Folder with manifest.json (a copy of the shipped Templates folder)"));
            AddRow(grid, ref row, "Target framework:", _framework);
            AddSection(grid, ref row, "Default system user (gets every permission; only a PBKDF2 hash of the password is written):");
            AddRow(grid, ref row, "User name:", _user);
            AddRow(grid, ref row, "Password:", _password);
            AddRow(grid, ref row, "Repeat password:", _repeat);
            AddSpanning(grid, ref row, _overwrite);
            AddSpanning(grid, ref row, _error);

            var generate = new Button { Content = "Generate", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            generate.Click += (_, _) => Accept();
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(generate);
            buttons.Children.Add(cancel);
            AddSpanning(grid, ref row, buttons);

            Content = grid;
            Loaded += (_, _) => (_user.Text.Length == 0 ? (Control)_user : _password).Focus();
        }

        private void Accept()
        {
            var output = _output.Text.Trim();
            var templates = _templates.Text.Trim();
            var user = _user.Text.Trim();

            string? error =
                output.Length == 0 ? "Choose an output folder."
                : !IsFullPath(output) ? @"The output folder must be a valid full path, e.g. C:\Source\Store."
                : File.Exists(output) ? "The output folder is an existing file."
                : templates.Length > 0 && (!IsFullPath(templates) || !File.Exists(Path.Combine(templates, "manifest.json"))) ? "The templates folder must contain manifest.json."
                : _framework.Text.Trim().Length == 0 ? "Enter a target framework, e.g. net10.0."
                : user.Length == 0 || user.Contains(":") ? "The user name must be non-empty and must not contain ':'."
                : _password.Password.Length < MinPasswordLength ? $"The password must have at least {MinPasswordLength} characters."
                : _password.Password != _repeat.Password ? "The passwords do not match."
                : null;

            if (error is not null)
            {
                _error.Text = error;
                return;
            }

            _request.OutputDirectory = output;
            _request.TemplateDirectory = templates.Length > 0 ? templates : null;
            _request.TargetFramework = _framework.Text.Trim();
            _request.AdminUser = user;
            _request.AdminPassword = _password.Password;
            _request.Overwrite = _overwrite.IsChecked == true;
            DialogResult = true;
        }

        /// <summary>Rooted and well-formed. On .NET Framework, invalid characters make Path methods throw.</summary>
        private static bool IsFullPath(string path)
        {
            try
            {
                return Path.IsPathRooted(path) && Path.GetFullPath(path).Length > 0;
            }
            catch (System.Exception ex) when (ex is System.ArgumentException || ex is System.NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        private static Button BrowseButton(TextBox target, string description)
        {
            var button = new Button { Content = "...", MinWidth = 32, Margin = new Thickness(4, 0, 0, 0) };
            button.Click += (_, _) =>
            {
                using var dialog = new WinForms.FolderBrowserDialog { Description = description, ShowNewFolderButton = true };
                if (Directory.Exists(target.Text))
                    dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                    target.Text = dialog.SelectedPath;
            };
            return button;
        }

        private static void AddRow(Grid grid, ref int row, string label, Control input, Control? extra = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new Label { Content = label, Padding = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            input.Margin = new Thickness(0, 3, 0, 3);
            Place(grid, text, row, 0);
            Place(grid, input, row, 1, extra is null ? 2 : 1);
            if (extra is not null)
                Place(grid, extra, row, 2);
            row++;
        }

        private static void AddSection(Grid grid, ref int row, string text) =>
            AddSpanning(grid, ref row, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });

        private static void AddSpanning(Grid grid, ref int row, FrameworkElement element)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (element is CheckBox)
                element.Margin = new Thickness(0, 8, 0, 0);
            Place(grid, element, row, 0, 3);
            row++;
        }

        private static void Place(Grid grid, UIElement element, int row, int column, int columnSpan = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
            grid.Children.Add(element);
        }
    }
}
